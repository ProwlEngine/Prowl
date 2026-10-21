using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Draw programs built from each pass's shared compiled variants: the global offsets, the renderer's layouts and the
/// pipeline state of the draw put on top. A variant not ready yet, or failed, draws with the pass's default variant, every axis at its first
/// value, else with the matching pass of the shader's declared fallback. Compiles follow the policy, blocking in deterministic mode. Only compiles run on workers,
/// programs are created on the render thread. Failures are reported once.
/// </summary>
internal sealed class ProgramCache(GraphicsDevice device, IShaderCompiler? compiler, GlobalRegistry globals, DrawPrograms programs, bool deterministic, Action<RendererError> report, CompilePolicy policy = CompilePolicy.Async, Action<IDisposable>? retire = null) : IDisposable
{
    private sealed class Entry
    {
        public required DrawProgram Program;
        public required bool Mirrored;
        public required PassVariants Variants;
        public required int Key;
        public required int Revision;
        public GraphicsProgram? Built;
        public bool Failed;
    }

    private readonly record struct Family(Shader Shader, int PassIndex, PassState? StateOverride, bool Mirrored);

    private readonly CompilePolicy _policy = compiler == null ? CompilePolicy.BakedOnly : deterministic ? CompilePolicy.Sync : policy;
    private readonly Dictionary<(ushort Program, bool Mirrored, bool Debug, ulong Globals), Entry> _entries = new();
    private readonly Dictionary<Family, int> _families = new();
    private readonly Dictionary<Family, Entry> _defaults = new();
    private readonly Dictionary<Family, Entry?> _declared = new();

    /// <summary>The hardware constant and slope offsets shadow casters draw with.</summary>
    public (float Constant, float Slope) ShadowBias { get; set; } = (0, 1.5f);

    public static readonly ResourceLayoutDescription FrameLayout = GraphiteBindings.FrameTable(RendererTextures.Textures2D, RendererTextures.OtherTextures);
    public static readonly ResourceLayoutDescription ViewLayout = GraphiteBindings.ViewTable(RendererTextures.Samplers);

    /// <summary>Drops every variant of a freed draw program id, before the id is reused for another program.</summary>
    public void Drop(ushort id)
    {
        List<(ushort, bool, bool, ulong)>? keys = null;
        foreach (((ushort Program, bool Mirrored, bool Debug, ulong Globals) key, Entry entry) in _entries)
        {
            if (key.Program != id)
                continue;
            (keys ??= new()).Add(key);
            Forget(entry);
            Discard(entry.Built);
            Family family = FamilyOf(entry);
            if (--_families[family] > 0)
                continue;
            _families.Remove(family);
            if (_defaults.Remove(family, out Entry? fallback))
            {
                Forget(fallback);
                Discard(fallback.Built);
            }
            if (_declared.Remove(family, out Entry? declared) && declared != null)
            {
                Forget(declared);
                Discard(declared.Built);
            }
        }
        if (keys == null)
            return;
        foreach ((ushort, bool, bool, ulong) key in keys)
            _entries.Remove(key);
    }

    /// <summary>Variants known to the cache, ready, compiling or failed.</summary>
    internal int EntryCount => _entries.Count;

    private int _compiling;

    /// <summary>Programs not built yet and not failed. Any thread.</summary>
    public int Compiling => Volatile.Read(ref _compiling);

    /// <summary>Programs not built yet and not failed.</summary>
    public int Pending
    {
        get
        {
            int pending = 0;
            foreach (Entry entry in _entries.Values)
            {
                if (entry.Built == null && !entry.Failed)
                    pending++;
            }
            return pending;
        }
    }

    /// <summary>The program to draw with now: the wanted variant when it is built, else a fallback, else null. Render thread.</summary>
    public GraphicsProgram? Get(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords)
    {
        Entry entry = Wanted(id, mirrored, keywords);
        return entry.Built ?? Fallback(entry);
    }

    private readonly Dictionary<GraphicsProgram, PropertyID[]> _userBuffers = new();

    /// <summary>The buffers a program reads that the host sets through Globals.SetBuffer.</summary>
    public PropertyID[] UserBuffers(GraphicsProgram program)
    {
        lock (_userBuffers)
            return _userBuffers.TryGetValue(program, out PropertyID[]? buffers) ? buffers : [];
    }

    /// <summary>Whether the wanted variant itself is built, rather than a fallback standing in. Render thread.</summary>
    public bool Ready(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords).Built != null;

    /// <summary>Whether the wanted variant failed to compile, so it never becomes ready. Render thread.</summary>
    public bool Failed(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords).Failed;

    private Entry Wanted(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords)
    {
        DrawProgram program = programs[id];
        bool debug = DebugProgram(ref program, keywords);
        IReadOnlyList<VariantSpace> axes = program.Shader.GlobalAxes;
        ulong key = 0, stride = 1;
        Span<int> globalValues = stackalloc int[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            globalValues[i] = ValueIndex(axes[i], keywords.GetValueOrDefault(axes[i].Name));
            if (!program.Shader.PassHasAxis(program.PassIndex, axes[i].Name))
                continue;
            key += (ulong)globalValues[i] * stride;
            stride *= (ulong)axes[i].Values.Count;
        }

        if (!_entries.TryGetValue((id, mirrored, debug, key), out Entry? entry))
        {
            entry = Create(program, mirrored, globalValues);
            _entries[(id, mirrored, debug, key)] = entry;
        }
        Complete(entry);
        return entry;
    }

    // A Forward pass draws through its shader's Debug pass while the view sets the debug axis
    private static bool DebugProgram(ref DrawProgram program, IReadOnlyDictionary<string, string> keywords)
    {
        if (!keywords.ContainsKey(Shader.DebugAxis))
            return false;
        Shader shader = program.Shader;
        int debug = shader.PassIndex(PassType.Debug);
        if (debug < 0 || !ShaderDefinition.PassHasTag(shader.Definition.Passes![program.PassIndex], PassType.Tag, PassType.Forward.Name))
            return false;
        program = program with { PassIndex = debug, Variant = shader.PassVariant(debug, program.Keywords) };
        return true;
    }

    public void Dispose()
    {
        HashSet<PassVariants> variants = new();
        foreach (Entry entry in Every())
        {
            variants.Add(entry.Variants);
            Discard(entry.Built);
        }
        foreach (PassVariants pass in variants)
            pass.WaitPending();
        _entries.Clear();
        _families.Clear();
        _defaults.Clear();
        _declared.Clear();
    }

    private IEnumerable<Entry> Every()
    {
        foreach (Entry entry in _entries.Values)
            yield return entry;
        foreach (Entry entry in _defaults.Values)
            yield return entry;
        foreach (Entry? entry in _declared.Values)
        {
            if (entry != null)
                yield return entry;
        }
    }

    private static Family FamilyOf(Entry entry) => new(entry.Program.Shader, entry.Program.PassIndex, entry.Program.StateOverride, entry.Mirrored);

    private Entry Create(DrawProgram program, bool mirrored, ReadOnlySpan<int> globalValues)
    {
        IReadOnlyList<VariantSpace> materialAxes = program.Shader.MaterialAxes;
        IReadOnlyList<VariantSpace> globalAxes = program.Shader.GlobalAxes;
        Keyword[] combo = new Keyword[materialAxes.Count + globalAxes.Count];
        for (int i = 0; i < materialAxes.Count; i++)
            combo[i] = program.Keywords[i];
        for (int i = 0; i < globalAxes.Count; i++)
            combo[materialAxes.Count + i] = new Keyword(globalAxes[i].Name, globalAxes[i].Values[globalValues[i]]);

        PassVariants variants = program.Shader.Definition.Passes![program.PassIndex].Variants(compiler, device.BackendType);
        if (!variants.TryGetKey(combo, out int key))
            key = 0;
        Entry entry = new() { Program = program, Mirrored = mirrored, Variants = variants, Key = key, Revision = variants.Revision };
        Family family = FamilyOf(entry);
        _families[family] = _families.GetValueOrDefault(family) + 1;
        Interlocked.Increment(ref _compiling);
        return entry;
    }

    // What stands in for a variant not built: the pass's default variant, else the declared fallback shader's pass of the same kind
    private GraphicsProgram? Fallback(Entry wanted)
    {
        Family family = FamilyOf(wanted);
        if (wanted.Key != 0)
        {
            if (!_defaults.TryGetValue(family, out Entry? fallback))
            {
                fallback = new Entry { Program = wanted.Program, Mirrored = wanted.Mirrored, Variants = wanted.Variants, Key = 0, Revision = wanted.Variants.Revision };
                Interlocked.Increment(ref _compiling);
                _defaults[family] = fallback;
            }
            Complete(fallback);
            if (fallback.Built != null)
                return fallback.Built;
        }

        if (!_declared.TryGetValue(family, out Entry? declared))
            _declared[family] = declared = Declared(wanted);
        if (declared == null)
            return null;
        Complete(declared);
        return declared.Built;
    }

    private Entry? Declared(Entry wanted)
    {
        Shader shader = wanted.Program.Shader;
        if (shader.Fallback is not { } fallback || fallback == shader)
            return null;
        string? kind = shader.Definition.Passes![wanted.Program.PassIndex].Tags?.GetValueOrDefault(PassType.Tag);
        ShaderPass[] passes = fallback.Definition.Passes ?? [];
        for (int i = 0; i < passes.Length; i++)
        {
            if (passes[i].Tags?.GetValueOrDefault(PassType.Tag) != kind)
                continue;
            Keyword[] keywords = new Keyword[fallback.MaterialAxes.Count];
            for (int a = 0; a < keywords.Length; a++)
                keywords[a] = new Keyword(fallback.MaterialAxes[a].Name, fallback.MaterialAxes[a].Values[0]);
            DrawProgram program = new(fallback, i, 0, wanted.Program.StateOverride, keywords);
            Interlocked.Increment(ref _compiling);
            PassVariants variants = passes[i].Variants(compiler, device.BackendType);
            return new Entry { Program = program, Mirrored = wanted.Mirrored, Variants = variants, Key = 0, Revision = variants.Revision };
        }
        return null;
    }

    // The shared variant once compiled, its globals declared and its layouts made the renderer's. A variant that declared
    // new globals waits until the next extraction has grown the regions to hold them.
    private void Complete(Entry entry)
    {
        int revision = entry.Variants.Revision;
        if (revision != entry.Revision)
        {
            entry.Revision = revision;
            if (entry.Built != null)
            {
                Retire(entry.Built);
                entry.Built = null;
                Interlocked.Increment(ref _compiling);
            }
            else if (entry.Failed)
            {
                entry.Failed = false;
                Interlocked.Increment(ref _compiling);
            }
        }
        if (entry.Built != null || entry.Failed)
            return;
        VariantState state = entry.Variants.Request(entry.Key, _policy, out ShaderDescription compiled, out Exception? error);
        if (state == VariantState.Pending)
            return;
        if (state == VariantState.Failed)
        {
            Fail(entry, error!);
            return;
        }

        DrawProgram program = entry.Program;
        try
        {
            compiled.Specializations = MaterialLayout.GlobalOffsets(entry.Variants.Globals, globals);
            if (globals.NeedsGrowth)
                return;
            compiled.ResourceLayouts = MaterialLayout.Layouts(program.Shader.Name, compiled.ResourceLayouts ?? [], FrameLayout, ViewLayout);
            entry.Built = Build(program, compiled, entry.Mirrored);
            Interlocked.Decrement(ref _compiling);
        }
        catch (Exception e)
        {
            Fail(entry, e);
        }
    }

    private void Fail(Entry entry, Exception e)
    {
        entry.Failed = true;
        Interlocked.Decrement(ref _compiling);
        DrawProgram program = entry.Program;
        report(new RendererError("shader.compile", $"{program.Shader.Name}: {e.Message}", RendererErrorSeverity.Error, program.Shader) { Key = $"program.{program.Shader.Name}.{program.PassIndex}.{entry.Key}" });
    }

    // An entry that never finished no longer counts as compiling
    private void Forget(Entry entry)
    {
        if (entry.Built == null && !entry.Failed)
            Interlocked.Decrement(ref _compiling);
    }

    private void Retire(GraphicsProgram program)
    {
        lock (_userBuffers)
            _userBuffers.Remove(program);
        if (retire != null)
            retire(program);
        else
            program.Dispose();
    }

    private void Discard(GraphicsProgram? program)
    {
        if (program == null)
            return;
        lock (_userBuffers)
            _userBuffers.Remove(program);
        program.Dispose();
    }

    private static int ValueIndex(VariantSpace axis, string? value)
    {
        for (int v = 0; v < axis.Values.Count; v++)
        {
            if (axis.Values[v] == value)
                return v;
        }
        return 0;
    }

    // Colour takes the pass's blend. An opaque forward pass writes the G-buffer targets its fragment stage stores, so they
    // match the surface that won the depth test; ones it does not store keep their clear, and transparent, blended and
    // other passes leave them. The prepass writes no colour at all. Views with fewer targets ignore the extra states.
    internal static BlendStateDescription SceneBlend(PassState state, bool forward, bool prepass, Func<uint, bool> stores)
    {
        BlendAttachmentDescription color = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool gbuffer = WritesGBufferTargets(state, forward, prepass);
        BlendAttachmentDescription target = BlendAttachmentDescription.OverrideBlend;
        BlendAttachmentDescription masked = target with { ColorWriteMask = ColorWriteMask.None };
        if (prepass)
            color.ColorWriteMask = ColorWriteMask.None;

        BlendStateDescription blend = state.ToBlendState(BlendStateDescription.SingleOverrideBlend);
        blend.AttachmentStates = [color, gbuffer && stores(1) ? target : masked, gbuffer && stores(2) ? target : masked, gbuffer && stores(3) ? target : masked];
        return blend;
    }

    /// <summary>Whether a pass draws opaque surfaces, so it writes the G-buffer targets it stores.</summary>
    internal static bool WritesGBufferTargets(PassState state, bool forward, bool prepass)
    {
        BlendAttachmentDescription color = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool blended = color.BlendEnabled && !(color.SourceColorFactor == BlendFactor.One && color.DestinationColorFactor == BlendFactor.Zero);
        return forward && !prepass && !blended && color.ColorWriteMask != ColorWriteMask.None;
    }

    private GraphicsProgram Build(DrawProgram program, ShaderDescription description, bool mirrored)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
        PassState state = program.StateOverride is { } over ? over.Apply(pass.State) : pass.State;
        bool forward = ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Forward.Name) || ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Debug.Name);
        bool prepass = ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Prepass.Name);
        ShaderDescription compiled = description;
        description.BlendState = SceneBlend(state, forward, prepass, location => MaterialLayout.Stores(compiled, location));
        description.DepthStencilState = state.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyGreaterEqual);
        RasterizerStateDescription raster = state.ToRasterizerState(RasterizerStateDescription.Default);
        if (mirrored)
            raster.FrontFace = raster.FrontFace == FrontFace.Clockwise ? FrontFace.CounterClockwise : FrontFace.Clockwise;
        if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Sky.Name))
        {
            description.DepthStencilState = DepthStencilStateDescription.DepthOnlyGreaterEqualRead;
            raster = RasterizerStateDescription.CullNone;
        }

        // Casters draw depth alone, clamped rather than clipped so casters toward a directional light still land, offset away from the light
        if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.ShadowCaster.Name))
        {
            description.BlendState = BlendStateDescription.Empty;
            description.DepthStencilState = DepthStencilStateDescription.DepthOnlyGreaterEqual;
            raster.DepthClipEnabled = !device.Features.DepthClipDisable;
            raster.DepthBiasEnabled = true;
            raster.DepthBiasConstantFactor = -ShadowBias.Constant;
            raster.DepthBiasSlopeFactor = -ShadowBias.Slope;
        }
        description.RasterizerState = raster;

        GraphicsProgram built = device.ResourceFactory.CreateGraphicsProgram(description);
        built.Name = $"{program.Shader.Name} {pass.Name}";
        List<PropertyID> buffers = new();
        foreach (ResourceLayoutDescription layout in description.ResourceLayouts ?? [])
        {
            if (layout.Set != GraphiteBindings.UserSet)
                continue;
            foreach (ResourceLayoutElementDescription element in layout.Elements)
                buffers.Add(element.Name);
        }
        lock (_userBuffers)
            _userBuffers[built] = [.. buffers];
        return built;
    }
}

/// <summary>
/// A device local buffer the CPU fills every frame through the frame's scatter, like instance lists and indirect
/// commands. It grows by doubling, and a grown buffer changes the frame's buffer set.
/// </summary>
internal sealed class GpuArray(GraphicsDevice device, BufferUsage usage, string name) : IDisposable
{
    public DeviceBuffer Buffer { get; private set; } = Create(device, usage, name, 256);

    public void Write<T>(Uploader uploader, ReadOnlySpan<T> data) where T : unmanaged
    {
        uint bytes = (uint)(data.Length * Unsafe.SizeOf<T>());
        if (bytes > Buffer.SizeInBytes)
        {
            uint size = Buffer.SizeInBytes;
            Buffer.Dispose();
            while (size < bytes)
                size *= 2;
            Buffer = Create(device, usage, name, size);
        }
        if (bytes > 0)
            uploader.Write(Buffer, 0, MemoryMarshal.Cast<T, uint>(data));
    }

    public void Dispose() => Buffer.Dispose();

    private static DeviceBuffer Create(GraphicsDevice device, BufferUsage usage, string name, uint size)
    {
        DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(size, usage | BufferUsage.StructuredBufferReadWrite));
        buffer.Name = name;
        return buffer;
    }
}

/// <summary>The id and render size of a camera's view. Kept per camera so a frame allocates nothing.</summary>
internal sealed class CameraView
{
    public uint PixelWidth { get; set; } = 1;
    public uint PixelHeight { get; set; } = 1;
    public int ViewId { get; set; }
}

/// <summary>
/// Fills the frame's new deformed copies from their meshes, then deforms the copies a view sees this frame whose bones
/// or blend shape weights changed: morphing copies take their rest pose, each slot of active frames adds its deltas
/// across every copy, then skinning reads the result. Barriers fall between those steps only, as copies never share
/// words. A copy deformed the frame it is made skips the streams the deform writes. After the upload and before the
/// fence every view follows.
/// </summary>
internal sealed class DeformPass(ComputeProgram program, GeometryPool geometry) : IExecutionPass, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public uint Mode, First, Count, Streams;
        public uint SourceWord, TargetWord, Normal, TargetNormal;
        public uint Tangent, TargetTangent, BoneIndices, BoneWeights;
        public uint BoneBase, BoneCount, Deltas;
        public float Weight;
    }

    private const uint MaxPerDispatch = 65535 * 64;
    private const uint SkinnedStreams = (1u << (int)VertexSemantic.Position) | (1u << (int)VertexSemantic.Normal) | (1u << (int)VertexSemantic.Tangent);

    private readonly PropertySet _properties = new();
    private readonly List<(RenderWorld.RenderSide World, int Index)> _jobs = new();

    public DeviceBuffer[] Buffers = [];
    public readonly List<RenderWorld.RenderSide> Worlds = new();

    public string Name => "Graphite.Deform";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < Buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), Buffers[i], BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        _jobs.Clear();
        bool bound = false, morphs = false;
        int slots = 0;
        foreach (RenderWorld.RenderSide world in Worlds)
        {
            if (world.NewCopies.Count == 0 && world.Deforming.Count == 0)
                continue;
            if (!bound)
            {
                cmd.SetComputeShader(program);
                bound = true;
            }

            foreach ((int index, MeshPlacement from, MeshPlacement to) in world.NewCopies)
            {
                uint skipped = world.DeformNow[index] ? SkinnedStreams : 0;
                DeviceBuffer source = geometry.Pages[from.Page].Vertices, target = geometry.Pages[to.Page].Vertices;
                for (int s = 0; s < MeshLayout.StreamCount; s++)
                {
                    if ((from.StreamsPresent & (1u << s) & ~skipped) != 0)
                        Copy(cmd, world, source, target, from.StreamOffsets[s], to.StreamOffsets[s], MeshLayout.WordsPerVertex((VertexSemantic)s) * from.VertexCount);
                }
                Copy(cmd, world, geometry.Pages[from.Page].Indices, geometry.Pages[to.Page].Indices, from.IndexBase, to.IndexBase, from.Indices.Size);
            }

            foreach (int index in world.Deforming)
            {
                if (!world.DeformNow[index])
                    continue;
                _jobs.Add((world, index));
                MeshPlacement from = world.Mesh[index]!.Placement!, to = world.Deformed[index]!;
                if (from.BlendFrames.Length == 0)
                    continue;
                morphs = true;
                slots = Math.Max(slots, world.ActiveFrames[index].Length);
                DeviceBuffer source = geometry.Pages[from.Page].Vertices, target = geometry.Pages[to.Page].Vertices;
                for (int s = 0; s < MeshLayout.StreamCount; s++)
                {
                    if ((from.StreamsPresent & (1u << s) & SkinnedStreams) != 0)
                        Copy(cmd, world, source, target, from.StreamOffsets[s], s == (int)VertexSemantic.Position ? world.PoseTarget[index] : to.StreamOffsets[s], MeshLayout.WordsPerVertex((VertexSemantic)s) * from.VertexCount);
                }
            }
        }
        if (morphs)
        {
            cmd.ComputeBarrier();
            for (int slot = 0; slot < slots; slot++)
            {
                foreach ((RenderWorld.RenderSide world, int index) in _jobs)
                {
                    if (slot < world.ActiveFrames[index].Length)
                        Morph(cmd, world, index, world.ActiveFrames[index][slot]);
                }
                cmd.ComputeBarrier();
            }
        }
        foreach ((RenderWorld.RenderSide world, int index) in _jobs)
            Skin(cmd, world, index);
        foreach (RenderWorld.RenderSide world in Worlds)
            world.CommitDeformation();
    }

    // Adds one frame's weighted deltas to the copy's pose
    private void Morph(CommandBuffer cmd, RenderWorld.RenderSide world, int index, (int Frame, float Weight) active)
    {
        MeshPlacement from = world.Mesh[index]!.Placement!, to = world.Deformed[index]!;
        BlendFrame frame = from.BlendFrames[active.Frame];
        bool normals = frame.Normals != BlendFrame.None, tangents = frame.Tangents != BlendFrame.None;
        Bind(cmd, world, geometry.Pages[from.Page].Vertices, geometry.Pages[to.Page].Vertices);
        Dispatch(cmd, new Push
        {
            Mode = 2,
            Count = frame.Count,
            Streams = (normals ? 1u : 0) | (tangents ? 2u : 0),
            SourceWord = frame.Vertices,
            Deltas = frame.Positions,
            Normal = frame.Normals,
            Tangent = frame.Tangents,
            TargetWord = world.PoseTarget[index],
            TargetNormal = to.StreamOffsets[(int)VertexSemantic.Normal],
            TargetTangent = to.StreamOffsets[(int)VertexSemantic.Tangent],
            Weight = active.Weight,
        });
    }

    // Skins the copy from the mesh's rest pose, or from its own pose when it morphs
    private void Skin(CommandBuffer cmd, RenderWorld.RenderSide world, int index)
    {
        MeshPlacement from = world.Mesh[index]!.Placement!, to = world.Deformed[index]!;
        if (!world.Skins(index, from))
            return;
        (uint first, uint count) = world.BonesOf(index);
        uint ownPose = from.BlendFrames.Length > 0 ? 4u : 0;
        Bind(cmd, world, geometry.Pages[from.Page].Vertices, geometry.Pages[to.Page].Vertices);
        Dispatch(cmd, new Push
        {
            Mode = 1,
            Count = from.VertexCount,
            Streams = ((from.StreamsPresent >> (int)VertexSemantic.Normal) & 1) | (((from.StreamsPresent >> (int)VertexSemantic.Tangent) & 1) << 1) | ownPose,
            SourceWord = from.StreamOffsets[(int)VertexSemantic.Position],
            TargetWord = world.PoseTarget[index],
            Normal = from.StreamOffsets[(int)VertexSemantic.Normal],
            TargetNormal = to.StreamOffsets[(int)VertexSemantic.Normal],
            Tangent = from.StreamOffsets[(int)VertexSemantic.Tangent],
            TargetTangent = to.StreamOffsets[(int)VertexSemantic.Tangent],
            BoneIndices = from.StreamOffsets[(int)VertexSemantic.BoneIndices],
            BoneWeights = from.StreamOffsets[(int)VertexSemantic.BoneWeights],
            BoneBase = first,
            BoneCount = count,
        });
    }


    private void Copy(CommandBuffer cmd, RenderWorld.RenderSide world, DeviceBuffer source, DeviceBuffer target, uint sourceWord, uint targetWord, uint words)
    {
        Bind(cmd, world, source, target);
        Dispatch(cmd, new Push { Mode = 0, Count = words, SourceWord = sourceWord, TargetWord = targetWord });
    }

    private void Bind(CommandBuffer cmd, RenderWorld.RenderSide world, DeviceBuffer source, DeviceBuffer target)
    {
        _properties.SetBuffer("Graphite_DeformSource", source);
        _properties.SetBuffer("Graphite_DeformTarget", target);
        _properties.SetBuffer("Graphite_SkinMatrices", world.SkinMatrices.Buffer);
        cmd.SetProperties(_properties);
    }

    // In runs of at most 65535 groups
    private static void Dispatch(CommandBuffer cmd, Push push)
    {
        for (uint first = 0; first < push.Count; first += MaxPerDispatch)
        {
            push.First = first;
            cmd.SetPushConstants(push);
            cmd.Dispatch((Math.Min(MaxPerDispatch, push.Count - first) + 63) / 64, 1, 1);
        }
    }

    public void Dispose() => program.Dispose();
}

/// <summary>The FrameStart pass: the frame's single scatter into every persistent buffer, with each declared as written.</summary>
internal sealed class UploadPass(Uploader uploader) : IExecutionPass
{
    public DeviceBuffer[] Buffers = [];

    public string Name => "Graphite.FrameStart";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < Buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), Buffers[i], BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

/// <summary>Everything a camera pipeline draws in one frame, prepared before dispatch.</summary>
internal sealed class CameraFrame : IDisposable
{
    public readonly DrawList Prepass = new();
    public readonly DrawList Forward = new();
    public readonly DrawList Transparent = new();
    public readonly SceneDrawer Drawer;

    /// <summary>The features this frame's pick runs on the view.</summary>
    public readonly List<RenderFeature> Selected = new();

    /// <summary>The colours and scene a feature drew onto this frame, so the first to draw one clears it.</summary>
    public readonly HashSet<RenderResourceID> Written = new();

    /// <summary>Whether a feature before the one recording encoded the colour for display, as tonemappers do.</summary>
    public bool DisplayEncoded;

    /// <summary>
    /// The opaque colour with its mips that passes tagged "SceneColor" = "Read" sample, made the first time the view's
    /// transparent list holds one. Used says whether this frame's list holds one.
    /// </summary>
    public Texture? SceneColor;
    public bool SceneColorUsed;
    private TextureView? _sceneColorView;

    /// <summary>The cube a capture view presents into.</summary>
    public RenderTexture? CaptureCube;

    /// <summary>The persistent screen lighting a producer in the view's shape writes, resolved before the graph runs.</summary>
    public RenderTexture? ScreenVisibility, ScreenIrradiance;
    private (RenderTexture? Texture, TextureView? View) _visibilityView, _irradianceView;

    public TextureView? ScreenVisibilityView() => ArrayView(ref _visibilityView, ScreenVisibility);

    public TextureView? ScreenIrradianceView() => ArrayView(ref _irradianceView, ScreenIrradiance);

    private TextureView? ArrayView(ref (RenderTexture? Texture, TextureView? View) cache, RenderTexture? texture)
    {
        if (cache.Texture == texture)
            return cache.View;
        if (cache.View != null)
            _frame.Retired.Add(cache.View);
        cache = (texture, texture == null ? null : _device.ResourceFactory.CreateTextureView(new TextureViewDescription(texture.ColorTextures[0]) { Arrayed = true }));
        return cache.View;
    }

    /// <summary>The host's buffers the view's graph declares, from the world's globals.</summary>
    public DeviceBuffer[] HostBuffers = [];

    /// <summary>Where the world's providers add this view's items.</summary>
    public readonly DrawItemSink Sink = new();

    /// <summary>The view's directional cascades, and the records its shaders read them through.</summary>
    public readonly ViewShadows Shadows = new();
    public readonly GpuArray Cascades;

    /// <summary>What a shadow view culls its casters against, and which of them it draws, set before it is prepared.</summary>
    public CullFrustum ShadowCull;
    public CasterFilter ShadowFilter;

    /// <summary>Whether this is one of the renderer's own shadow views, drawing its casters alone.</summary>
    public bool IsShadowView { get; init; }

    /// <summary>This frame's instance counts copied from providers' buffers into the view's commands, as byte offsets.</summary>
    public readonly List<(DeviceBuffer Source, uint Offset, uint Destination)> Counts = new();

    public void PlanCounts()
    {
        Counts.Clear();
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        foreach ((BucketSet.TransparentItem item, DrawList list, DeviceBuffer source, uint offset) in Sink.Counts)
        {
            int at = Lists.IndexOf(list);
            if (at < 0 || item.FirstCommand < 0)
                continue;
            for (int c = 0; c < item.Chunks.Length; c++)
                Counts.Add((source, offset, (uint)(CommandBases[at] + item.FirstCommand + c) * stride + 4));
        }
    }

    /// <summary>What host pipelines draw an external view through.</summary>
    public ViewDraws? Draws;
    public FeatureView Info;

    /// <summary>This frame's time as the host gave it.</summary>
    public FrameTime Time;
    public readonly TableRing ViewTable;
    public readonly GpuArray Instances;
    public readonly GpuArray Records;
    public readonly GpuArray Commands;
    public readonly GpuArray ViewBlocks;

    /// <summary>What the view builds each spatial set's trees into, by the set's slot, made the first time it sees one.</summary>
    public readonly SpatialView?[] Spatial = new SpatialView?[SpatialIndex.MaxSets];

    /// <summary>Each set's roots in this view, all empty for sets the world lacks.</summary>
    public readonly GpuArray SpatialRoots;
    public readonly SpatialRoots[] Roots = new SpatialRoots[SpatialIndex.MaxSets];

    /// <summary>Whether the view's basis flips handedness, as cube captures do, so every draw's winding flips too.</summary>
    public bool Mirrored;
    public ViewDesc Desc;
    public WorldFrame World = null!;

    /// <summary>The world's global keywords with the view's own over them, what its draws pick their variants by.</summary>
    public IReadOnlyDictionary<string, string> Keywords = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string>? _worldKeywords;
    private Keyword[] _viewKeywords = [];

    /// <summary>Brings <see cref="Keywords"/> up to date with the world's snapshot and the view's keywords. Render thread.</summary>
    public void ResolveKeywords()
    {
        IReadOnlyDictionary<string, string> world = World.World.Globals.KeywordSnapshot;
        Keyword[] view = Desc.Keywords ?? [];
        if (ReferenceEquals(world, _worldKeywords) && view.AsSpan().SequenceEqual(_viewKeywords))
            return;
        _worldKeywords = world;
        _viewKeywords = view;
        if (view.Length == 0)
        {
            Keywords = world;
            return;
        }
        Dictionary<string, string> merged = new(world, StringComparer.Ordinal);
        foreach (Keyword keyword in view)
            merged[keyword.Name] = keyword.Value;
        Keywords = merged;
    }
    public int LastFrame;
    public readonly CameraView View = new();

    /// <summary>Last frame's view block per layer, for motion: its view projection and camera position.</summary>
    public readonly ViewBlock[] Previous = new ViewBlock[6];
    public readonly ViewBlock[] Blocks = new ViewBlock[6];
    public bool HasPrevious;

    /// <summary>Whether the view had drawn before this frame, as features see it.</summary>
    public bool HadPrevious;

    /// <summary>The Halton phase of the view's jitter, and this and last frame's jitter in clip space.</summary>
    public int JitterPhase;
    public Float2 Jitter, PreviousJitter;
    public int LayerCount = 1;

    private IndirectDrawIndexedArguments[] _commands = new IndirectDrawIndexedArguments[64];
    private DrawRecord[] _records = new DrawRecord[64];
    private uint[] _instances = new uint[256];
    private readonly FramePipeline _frame;
    private readonly GraphicsDevice _device;

    /// <summary>The view's buffers for a set's trees.</summary>
    public SpatialView SpatialFor(int slot) => Spatial[slot] ??= new SpatialView(_device, $"View {View.ViewId} set {slot}");

    public CameraFrame(GraphicsDevice device, FramePipeline frame, int viewId)
    {
        _frame = frame;
        Drawer = new SceneDrawer(frame, this);
        View.ViewId = viewId;
        Lists = [Prepass, Forward, Transparent];
        ViewTable = new TableRing(device, ProgramCache.ViewLayout, $"View table {viewId}");
        Instances = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Instances {viewId}");
        Records = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Draw records {viewId}");
        Commands = new GpuArray(device, BufferUsage.IndirectBuffer, $"Indirect commands {viewId}");
        ViewBlocks = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View blocks {viewId}");
        SpatialRoots = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Spatial roots {viewId}");
        Cascades = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Cascades {viewId}");
        _device = device;
    }

    /// <summary>Picks this frame's features.</summary>
    public void SelectFeatures(RenderFeature[] world, RenderFeature[] view)
    {
        FeatureSelection.Select(Selected, world, view, Info);
    }

    /// <summary>The scene targets the selected features read.</summary>
    public SceneTargets SceneReads
    {
        get
        {
            SceneTargets targets = SceneTargets.None;
            foreach (RenderFeature feature in Selected)
                targets |= feature.SceneReadsSnapshot;
            return targets;
        }
    }

    /// <summary>Makes the scene colour for the view's size. Returns whether it did.</summary>
    public bool EnsureSceneColor(uint width, uint height, uint layers, PixelFormat format)
    {
        layers = Math.Max(layers, 1u);
        if (SceneColor is { } existing && existing.Width == width && existing.Height == height && existing.ArrayLayers == layers && existing.Format == format)
            return false;
        ReleaseSceneColor();
        uint mips = (uint)Math.Log2(Math.Max(width, height)) + 1;
        SceneColor = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layers, format, TextureUsage.Sampled));
        SceneColor.Name = $"Graphite.SceneColor {View.ViewId}";
        return true;
    }

    /// <summary>The scene colour viewed as an array whatever its layer count.</summary>
    public TextureView SceneColorView()
        => _sceneColorView ??= _device.ResourceFactory.CreateTextureView(new TextureViewDescription(SceneColor!) { Arrayed = true });

    public void ReleaseSceneColor()
    {
        if (_sceneColorView != null)
            _frame.Retired.Add(_sceneColorView);
        if (SceneColor != null)
            _frame.Retired.Add(SceneColor);
        (SceneColor, _sceneColorView) = (null, null);
    }

    // States are kept while their feature is in the world's or view's set, so history survives toggles
    public void PruneStates(RenderFeature[] world, RenderFeature[] view)
    {
        bool Known(RenderFeature feature) => Array.IndexOf(world, feature) >= 0 || Array.IndexOf(view, feature) >= 0 || Array.IndexOf(FeatureSelection.BuiltIn, feature) >= 0;
        List<(RenderFeature, Type)>? stale = null;
        foreach ((RenderFeature feature, Type type) in States.Keys)
        {
            if (!Known(feature))
                (stale ??= new()).Add((feature, type));
        }
        foreach ((RenderFeature, Type) key in stale ?? [])
        {
            (States[key] as IDisposable)?.Dispose();
            States.Remove(key);
        }
    }

    public T State<T>(RenderFeature feature) where T : class, new()
    {
        if (!States.TryGetValue((feature, typeof(T)), out object? state))
            States[(feature, typeof(T))] = state = new T();
        return (T)state;
    }

    public void AddBuffers(List<DeviceBuffer> buffers)
    {
        buffers.Add(Instances.Buffer);
        buffers.Add(Records.Buffer);
        buffers.Add(Commands.Buffer);
        buffers.Add(ViewBlocks.Buffer);
        buffers.Add(SpatialRoots.Buffer);
        buffers.Add(Cascades.Buffer);
        foreach (SpatialView? view in Spatial)
            view?.AddBuffers(buffers);
    }

    /// <summary>The frame's draw lists: prepass, opaque, transparent, then one per pass type a feature draws.</summary>
    public List<DrawList> Lists { get; }

    /// <summary>The pass types features of the view draw, each with its list, as of the last graph build.</summary>
    public readonly List<(PassType Type, DrawList List)> PassLists = new();
    private readonly Dictionary<PassType, DrawList> _passListPool = new();

    /// <summary>Where each list's commands start in the uploaded command buffer.</summary>
    public int[] CommandBases { get; private set; } = new int[3];

    /// <summary>State features keep for this view, made the first time each asks.</summary>
    public readonly Dictionary<(RenderFeature Feature, Type Type), object> States = new();

    /// <summary>The list of a pass type a feature draws, made when the view's graph is built.</summary>
    public DrawList ListFor(PassType type)
    {
        if (type == PassType.Prepass)
            return Prepass;
        if (type == PassType.Forward)
            return Forward;
        foreach ((PassType known, DrawList list) in PassLists)
        {
            if (known == type)
                return list;
        }
        if (!_passListPool.TryGetValue(type, out DrawList? created))
            _passListPool[type] = created = new DrawList();
        PassLists.Add((type, created));
        Lists.Add(created);
        return created;
    }

    /// <summary>The list of a pass type the view's features asked for, or null.</summary>
    public DrawList? FindList(PassType type)
    {
        if (type == PassType.Prepass)
            return Prepass;
        if (type == PassType.Forward)
            return Forward;
        foreach ((PassType known, DrawList list) in PassLists)
        {
            if (known == type)
                return list;
        }
        return null;
    }

    /// <summary>Forgets which pass lists the view draws before a graph build asks for them again, keeping what they hold.</summary>
    public void ResetPassLists()
    {
        PassLists.Clear();
        Lists.RemoveRange(3, Lists.Count - 3);
    }

    /// <summary>Queues the frame's commands, draw records, instances and view blocks, the lists one after another.</summary>
    public void Upload(Uploader uploader, ReadOnlySpan<ViewBlock> blocks)
    {
        int commands = 0, instances = 0;
        foreach (DrawList list in Lists)
        {
            commands += list.CommandCount;
            instances += list.InstanceCount;
        }
        if (CommandBases.Length < Lists.Count)
            CommandBases = new int[Lists.Count];
        if (_commands.Length < commands)
        {
            Array.Resize(ref _commands, commands * 2);
            Array.Resize(ref _records, commands * 2);
        }
        if (_instances.Length < instances)
            Array.Resize(ref _instances, instances * 2);

        int commandBase = 0, instanceBase = 0;
        for (int l = 0; l < Lists.Count; l++)
        {
            DrawList list = Lists[l];
            CommandBases[l] = commandBase;
            for (int i = 0; i < list.CommandCount; i++)
            {
                IndirectDrawIndexedArguments command = list.Commands[i];
                DrawRecord record = list.Records[i];
                command.FirstInstance += (uint)commandBase;
                if ((record.Flags & DrawRecord.SharedObject) == 0)
                    record.FirstInstance += (uint)instanceBase;
                _commands[commandBase + i] = command;
                _records[commandBase + i] = record;
            }
            Array.Copy(list.Instances, 0, _instances, instanceBase, list.InstanceCount);
            commandBase += list.CommandCount;
            instanceBase += list.InstanceCount;
        }

        Commands.Write<IndirectDrawIndexedArguments>(uploader, _commands.AsSpan(0, commands));
        Records.Write<DrawRecord>(uploader, _records.AsSpan(0, commands));
        Instances.Write<uint>(uploader, _instances.AsSpan(0, instances));
        ViewBlocks.Write<ViewBlock>(uploader, blocks);
    }

    public void Dispose()
    {
        _sceneColorView?.Dispose();
        SceneColor?.Dispose();
        foreach (object state in States.Values)
            (state as IDisposable)?.Dispose();
        ViewTable.Dispose();
        Instances.Dispose();
        Records.Dispose();
        Commands.Dispose();
        ViewBlocks.Dispose();
        SpatialRoots.Dispose();
        Cascades.Dispose();
        Shadows.Dispose();
        foreach (SpatialView? view in Spatial)
            view?.Dispose();
    }
}

/// <summary>Records a view's built in draws for the core features: its table, its draw lists and its sky.</summary>
internal sealed class SceneDrawer(FramePipeline frame, CameraFrame camera)
{
    private readonly PropertySet _properties = new();
    private (DeviceBuffer? Buffer, uint Offset, uint Size) _globals;
    private DescriptorTable _viewTable = null!;

    /// <summary>Fills the view's table for the frame. Recorded first in the view's graph.</summary>
    public void Prepare(uint ring)
    {
        GlobalRegistry registry = frame.Globals;
        (DeviceBuffer Buffer, uint Offset, uint Size) globals = (registry.Buffer, (uint)camera.World.GlobalRegion * registry.RegionWords * 4, registry.RegionWords * 4);
        if (globals != _globals)
        {
            _globals = globals;
            camera.ViewTable.Set("Graphite_Globals", 0, new DeviceBufferRange(globals.Buffer, globals.Offset, globals.Size));
        }
        camera.ViewTable.Set("Graphite_Views", 0, camera.ViewBlocks.Buffer);
        camera.ViewTable.Set("Graphite_SpatialRoots", 0, camera.SpatialRoots.Buffer);
        for (int slot = 0; slot < camera.Spatial.Length; slot++)
        {
            if (camera.Spatial[slot] is not { } view)
                continue;
            camera.ViewTable.Set("Graphite_SpatialRecords", (uint)(slot * 2 + 1), view.Records.Buffer);
            camera.ViewTable.Set("Graphite_SpatialNodes", (uint)(slot * 3 + 1), view.Nodes.Buffer);
            camera.ViewTable.Set("Graphite_SpatialNodes", (uint)(slot * 3 + 2), view.RegionNodes.Buffer);
            camera.ViewTable.Set("Graphite_SpatialRegions", (uint)slot, view.RegionEntries.Buffer);
        }
        SetWorldBuffers(camera.ViewTable, camera.World.World);
        camera.ViewTable.Set("Graphite_ShadowSampler", 0, frame.ShadowSampler);
        camera.ViewTable.Set("Graphite_ShadowFaces", 0, camera.World.Shadows!.Faces.Buffer);
        camera.ViewTable.Set("Graphite_Cascades", 0, camera.Cascades.Buffer);
        camera.ViewTable.Set("Graphite_ScreenVisibility", 0, camera.ScreenVisibilityView() ?? frame.NoScreenVisibility);
        camera.ViewTable.Set("Graphite_ScreenIrradiance", 0, camera.ScreenIrradianceView() ?? frame.NoScreenIrradiance);
        camera.ViewTable.Set("Graphite_ScreenSampler", 0, frame.TrilinearClamp);
        camera.ViewTable.Set("Graphite_SceneColor", 0, camera.SceneColor != null ? camera.SceneColorView() : frame.NoSceneColor);
        camera.ViewTable.Set("Graphite_SceneColorSampler", 0, frame.TrilinearClamp);
        _viewTable = camera.ViewTable.Get(ring);
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);
    }

    /// <summary>Binds the frame's texture tables and the view's table for a program drawn without draw records.</summary>
    public void BindTables(CommandBuffer cmd)
    {
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.TextureTable);
        cmd.SetDescriptorTable(GraphiteBindings.ViewSet, _viewTable);
        cmd.SetPushConstants(new FramePipeline.Push());
    }

    /// <summary>Records one of the view's draw lists.</summary>
    public void DrawList(CommandBuffer cmd, DrawList list) => Draw(cmd, list, camera.CommandBases[camera.Lists.IndexOf(list)]);

    // The host's buffers a program reads, from the world's globals over the renderer's. A missing one skips the draw.
    private bool BindUserBuffers(GraphicsProgram program, Shader shader)
    {
        IReadOnlyDictionary<PropertyID, DeviceBuffer> buffers = camera.World.World.Globals.BufferSnapshot;
        foreach (PropertyID name in frame.Programs.UserBuffers(program))
        {
            if (!buffers.TryGetValue(name, out DeviceBuffer? buffer) || buffer.IsDisposed)
            {
                frame.Report(new RendererError("global.buffer.missing", $"{shader.Name} reads the buffer {name.Text()}, which no Globals.SetBuffer provides, so it does not draw.", RendererErrorSeverity.Error, shader) { Key = $"global.buffer.{name.Text()}" });
                return false;
            }
            _properties.SetBuffer(name, buffer);
        }
        return true;
    }

    // The world's buffers sit in every view's table, which a frame in flight's table never sees change
    private void SetWorldBuffers(TableRing table, RenderWorld world)
    {
        RenderWorld.RenderSide render = world.Render;
        WorldLighting lighting = world.Lighting;
        table.Set("Graphite_Chunks", 0, frame.Geometry.Chunks.Buffer);
        table.Set("Graphite_Transforms", 0, render.Transforms.Buffer);
        table.Set("Graphite_PreviousTransforms", 0, render.PreviousTransforms.Buffer);
        table.Set("Graphite_Objects", 0, render.Attributes.Buffer);
        table.Set("Graphite_MaterialSlots", 0, render.MaterialSlots.Buffer);
        foreach (SpatialIndex set in world.SpatialSets)
        {
            table.Set("Graphite_SpatialRecords", (uint)(set.Slot * 2), set.StaticRecords.Buffer);
            table.Set("Graphite_SpatialNodes", (uint)(set.Slot * 3), set.StaticNodes.Buffer);
        }
        table.Set("Graphite_WorldLighting", 0, lighting.World.Buffer);
        table.Set("Graphite_ObjectProbes", 0, world.Probes.ObjectSh);
        table.Set("Graphite_SkyAmbientSH", 0, world.Reflections.SkyAmbient);
        table.Set("Graphite_Directionals", 0, lighting.Directionals.Buffer);
    }

    // A fullscreen triangle at the far plane with the sky material's slot in the push constants
    public void DrawSky(CommandBuffer cmd)
    {
        WorldFrame world = camera.World;
        if (!camera.Desc.Sky || camera.Desc.Overlay || world.SkyProgram is not ushort id || world.World.Lighting.Environment.Sky is not { IsDisposed: false, Table: { } table } sky)
            return;
        GraphicsProgram? program = frame.Programs.Get(id, false, camera.Keywords);
        if (program == null || !BindUserBuffers(program, sky.Shader))
            return;

        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.TextureTable);
        cmd.SetDescriptorTable(GraphiteBindings.ViewSet, _viewTable);
        _properties.SetBuffer(GraphiteBindings.MaterialsName, table.Records.Buffer);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new FramePipeline.Push { InstanceBase = sky.Slot });
        cmd.Draw(3);
    }

    private void Draw(CommandBuffer cmd, DrawList list, int commandBase)
    {
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        bool shadow = camera.Desc.Kind == ViewKind.Shadow;
        foreach (DrawCall call in list.Calls)
        {
            frame.CountDraw(shadow, call.CommandCount);
            GraphicsProgram? program = frame.Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.Keywords);
            if (program == null)
                continue;

            Shader shader = frame.DrawPrograms[call.Program].Shader;
            if (!frame.Materials.Tables.TryGetValue(shader, out MaterialTable? table) || !BindUserBuffers(program, shader))
                continue;

            GeometryPage page = frame.Geometry.Pages[call.Page];
            cmd.SetShader(program);
            cmd.SetVertexSource(frame.VertexSource(call.Page, call.Topology));
            cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.TextureTable);
            cmd.SetDescriptorTable(GraphiteBindings.ViewSet, _viewTable);
            _properties.SetBuffer(GraphiteBindings.MaterialsName, table.Records.Buffer);
            _properties.SetBuffer(GraphiteBindings.VerticesName, page.Vertices);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(new FramePipeline.Push { DrawRecordBase = (uint)(commandBase + call.FirstCommand) });
            cmd.DrawIndexedIndirect(camera.Commands.Buffer, (uint)(commandBase + call.FirstCommand) * stride, (uint)call.CommandCount, stride);
        }
    }
}

/// <summary>A world as the frame pipeline draws it: its buckets, its globals region and its frame table.</summary>
internal sealed class WorldFrame(RenderWorld world, BucketSet buckets, int globalRegion, DrawPrograms programs) : IDisposable
{
    /// <summary>The draw programs of the materials the world's providers draw with.</summary>
    public ProviderPrograms Programs { get; } = new(programs);

    private static int s_nextCapture;

    public RenderWorld World { get; } = world;
    public BucketSet Buckets { get; } = buckets;
    public int GlobalRegion { get; } = globalRegion;

    /// <summary>The draw program of the sky material's Sky pass, held while the world draws it.</summary>
    public ushort? SkyProgram;

    /// <summary>The world's local light shadows, made at its first frame.</summary>
    public WorldShadows? Shadows;

    /// <summary>The views capturing the sky and the reflection probes: six faces at once or one, reused across frames.</summary>
    public CameraFrame? SkyCamera;
    public readonly List<CameraFrame> ProbeCameras = new();
    public readonly List<CameraFrame> ProbeFaceCameras = new();
    public int ProbeCamerasUsed, ProbeFaceCamerasUsed;

    /// <summary>A new capture view id, below any a host may use.</summary>
    public static int NextCaptureViewId() => int.MinValue + Interlocked.Increment(ref s_nextCapture);

    public IEnumerable<CameraFrame> Captures()
    {
        if (SkyCamera != null)
            yield return SkyCamera;
        foreach (CameraFrame camera in ProbeCameras)
            yield return camera;
        foreach (CameraFrame camera in ProbeFaceCameras)
            yield return camera;
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in Captures())
            camera.Dispose();
        Shadows?.Dispose();
    }
}

internal readonly record struct PlannedInstance(GraphKind Kind, int Id, WorldFrame World, GraphExtent Extent, Framebuffer? Target, object Data);

/// <summary>
/// The frame's pipelines: the FrameStart pipeline uploading every change, then one camera pipeline per view, all in
/// one execution. Graphs rebuild only when the set of persistent buffers changes.
/// </summary>
internal sealed partial class FramePipeline : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Push
    {
        public uint DrawRecordBase;
        public uint InstanceBase;
        public uint ViewBase;
        public uint Flags;
    }

    private static readonly List<RenderResourceID> s_bufferIds = new();

    private readonly GraphicsDevice _device;
    private readonly IShaderCompiler? _compiler;
    private readonly UploadPass _upload;
    private readonly ProbePass _probes;
    private readonly DeformPass _deform;
    private readonly Dictionary<int, CameraFrame> _cameras = new();
    private readonly Dictionary<(int Page, PrimitiveTopology Topology), VertexSource> _vertexSources = new();
    private readonly List<GraphDispatch> _dispatches = new();
    private readonly List<PlannedInstance> _planned = new();
    private readonly Dictionary<WorldFrame, GraphInstance> _worldInstances = new();
    private readonly Dictionary<GraphKind, FeatureShape> _shapes = new();
    private readonly GraphStore _store = new();
    private readonly IPass[] _fillPasses;

    internal GraphStore Store => _store;
    private readonly ReadbackTexturePass _readbackTexture = new();
    private readonly ReadbackBufferPass _readbackBuffer = new();
    private readonly List<GraphImport> _imports = new();
    private RenderGraph? _graph;
    private bool _graphDirty = true;
    private readonly List<DeviceBuffer> _buffers = new();
    private readonly List<CameraFrame> _captures = new();
    private readonly List<Texture> _disposeAfterDispatch = new();
    private readonly List<IDisposable> _retired = new();
    private readonly List<DeviceBuffer> _replaced = new();
    private readonly RenderPipeline _providerPipeline = new() { Name = "Graphite.Providers" };
    private readonly ProviderWork _providerWork = new();
    private readonly ReadbackStaging _readback;
    private readonly List<int> _sortedViews = new();
    private bool[] _placed = [];
    private readonly HashSet<RenderFeature> _prepared = new();
    private readonly List<int> _viewOrder = new();
    private readonly List<WorldFrame> _frameWorlds = new();
    private int _frame;

    private readonly RendererTextures _textures;
    private readonly bool _deterministic;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, IShaderCompiler? compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, bool deterministic, Action<RendererError> report, CompilePolicy policy = CompilePolicy.Async)
    {
        Report = report;
        _device = device;
        _compiler = compiler;
        _textures = textures;
        _uploader = uploader;
        _deterministic = deterministic;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, deterministic, report, policy, _retired.Add);
        programs.Freed += Programs.Drop;
        _upload = new UploadPass(uploader);
        _probes = new ProbePass(InternalShaders.Compute(device, compiler, "Internal/Probes.glsl"));
        _deform = new DeformPass(InternalShaders.Compute(device, compiler, "Internal/Deform.glsl"), geometry);
        _readback = new ReadbackStaging(device, report);

        const string present = InternalShaders.Present, decode = InternalShaders.Decode, encode = InternalShaders.Encode;
        BlendStateDescription over = new(default, BlendAttachmentDescription.AlphaBlend with { SourceColorFactor = BlendFactor.One, SourceAlphaFactor = BlendFactor.One });
        _present =
        [
            FullscreenProgram.Create(device, compiler, present, "Graphite/Present"),
            FullscreenProgram.Create(device, compiler, present, "Graphite/PresentOver", blend: over),
            FullscreenProgram.Create(device, compiler, decode, "Graphite/PresentDecode"),
            FullscreenProgram.Create(device, compiler, decode, "Graphite/PresentDecodeOver", blend: over),
            FullscreenProgram.Create(device, compiler, encode, "Graphite/PresentEncode"),
            FullscreenProgram.Create(device, compiler, encode, "Graphite/PresentEncodeOver", blend: over),
        ];
        Noise = LoadData(device, "Noise.bin", 256, 256, mipmapped: true);
        SmaaArea = LoadData(device, "SmaaArea.bin", 160, 560, mipmapped: false);
        SmaaSearch = LoadData(device, "SmaaSearch.bin", 64, 16, mipmapped: false);
        SmaaAreaLayered = LoadData(device, "SmaaArea.bin", 160, 560, mipmapped: false, layers: 6);
        SmaaSearchLayered = LoadData(device, "SmaaSearch.bin", 64, 16, mipmapped: false, layers: 6);
        textures.RegisterDefault("noise", Noise);
        NoScreenVisibility = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled));
        NoScreenIrradiance = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        NoSceneColor = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        for (uint layer = 0; layer < 6; layer++)
        {
            device.UpdateTexture<byte>(NoScreenVisibility, [255], TextureRegion.Whole(NoScreenVisibility, 0, layer));
            device.UpdateTexture<ushort>(NoScreenIrradiance, [0, 0, 0, 0], TextureRegion.Whole(NoScreenIrradiance, 0, layer));
            device.UpdateTexture<ushort>(NoSceneColor, [0, 0, 0, 0], TextureRegion.Whole(NoSceneColor, 0, layer));
        }
        ClearProgram = FullscreenProgram.Create(device, compiler, InternalShaders.Clear, "Graphite/Clear", source: false);
        BlendAttachmentDescription untouched = BlendAttachmentDescription.OverrideBlend with { ColorWriteMask = ColorWriteMask.None };
        CopyBackProgram = FullscreenProgram.Create(device, compiler, InternalShaders.CopyBack, "Graphite/CopyBack",
            blend: new BlendStateDescription(default, BlendAttachmentDescription.OverrideBlend, untouched, untouched));
        _fillPasses = CaptureFillPasses.Of(this);
        PrefilterProgram = Fullscreen(device, compiler, "Internal/Prefilter.glsl");
        SkyAmbientProgram = InternalShaders.Compute(device, compiler, "Internal/SkyAmbient.glsl");
        TrilinearClamp = device.ResourceFactory.CreateSampler(SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        });
        LinearSampler = device.ResourceFactory.CreateSampler(SamplerDescription.Linear);
    }

    public GraphicsDevice Device => _device;
    public GeometryPool Geometry { get; }
    public MaterialSystem Materials { get; }
    public GlobalRegistry Globals { get; }
    public DrawPrograms DrawPrograms { get; }
    public ProgramCache Programs { get; }
    private readonly FullscreenProgram[] _present;

    /// <summary>
    /// Copies the view's colour to its target with its rows reversed, the first row at the bottom of clip space. Decoding
    /// undoes a tonemapper's encode for sRGB and float targets so theirs applies once, encoding sRGB encodes linear colour for
    /// targets that store display values, and over blends it as premultiplied by its alpha.
    /// </summary>
    public FullscreenProgram Present(bool decode, bool encode, bool over) => _present[(decode ? 2 : encode ? 4 : 0) + (over ? 1 : 0)];

    /// <summary>Prowl's noise: blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise { get; }

    /// <summary>The SMAA reference's area and search tables, RGBA8 in its row order.</summary>
    public Texture SmaaArea { get; }
    public Texture SmaaSearch { get; }

    /// <summary>The SMAA tables in each of six layers, for layered views.</summary>
    public Texture SmaaAreaLayered { get; }
    public Texture SmaaSearchLayered { get; }

    /// <summary>The screen lighting a view without a producer reads: full visibility, no irradiance, six layers.</summary>
    public Texture NoScreenVisibility { get; }
    public Texture NoScreenIrradiance { get; }

    /// <summary>The scene colour a view without a copy reads: black, six layers.</summary>
    public Texture NoSceneColor { get; }

    // An RGBA8 texture from the library's data, in its stored row order, in every layer, mipmapped on the GPU when asked
    private static Texture LoadData(GraphicsDevice device, string file, uint width, uint height, bool mipmapped, uint layers = 1)
    {
        using System.IO.Stream stream = typeof(FramePipeline).Assembly.GetManifestResourceStream("Graphite/Data/" + file)
            ?? throw new InvalidOperationException($"The library has no Data/{file}.");
        byte[] pixels = new byte[width * height * 4];
        stream.ReadExactly(pixels);
        uint mips = mipmapped ? (uint)Math.Log2(Math.Max(width, height)) + 1 : 1;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layers, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        texture.Name = "Graphite " + System.IO.Path.GetFileNameWithoutExtension(file);
        for (uint layer = 0; layer < layers; layer++)
            device.UpdateTexture<byte>(texture, pixels, TextureRegion.Whole(texture, 0, layer));
        if (mipmapped)
            device.Record(cmd => cmd.GenerateMipmaps(texture)).Wait();
        return texture;
    }

    /// <summary>Fills a view's viewport with its clear colour when nothing drew.</summary>
    public FullscreenProgram ClearProgram { get; }

    /// <summary>Copies a colour features wrote back into the scene's colour, leaving its G-buffer.</summary>
    public FullscreenProgram CopyBackProgram { get; }

    /// <summary>Resources the frame stopped using, disposed once it is dispatched.</summary>
    public List<IDisposable> Retired => _retired;

    public GraphicsProgram PrefilterProgram { get; }

    /// <summary>Reports a problem once per key.</summary>
    public Action<RendererError> Report { get; }
    public ComputeProgram SkyAmbientProgram { get; }

    /// <summary>Frames a view keeps its per view state after it was last drawn.</summary>
    public int StaleViewFrames { get; init; } = 8;

    /// <summary>The renderer's bindless texture table, set 0 of every material program.</summary>
    public DescriptorTable TextureTable => _textures.Table;
    public Sampler TrilinearClamp { get; }

    private static GraphicsProgram Fullscreen(GraphicsDevice device, IShaderCompiler? compiler, string file)
    {
        ShaderDescription description = InternalShaders.FilePass(file).Variants(compiler, device.BackendType).Require();
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
    public Sampler LinearSampler { get; }

    /// <summary>Every persistent buffer the frame writes and its cameras read, in a stable order.</summary>
    public DeviceBuffer[] Buffers { get; private set; } = [];

    public static RenderResourceID BufferId(int index)
    {
        lock (s_bufferIds)
        {
            while (s_bufferIds.Count <= index)
                s_bufferIds.Add(RenderResourceID.Intern("Graphite.Buffer" + s_bufferIds.Count));
            return s_bufferIds[index];
        }
    }

    public VertexSource VertexSource(int page, PrimitiveTopology topology)
    {
        DeviceBuffer indices = Geometry.Pages[page].Indices;
        if (!_vertexSources.TryGetValue((page, topology), out VertexSource? source))
        {
            source = new VertexSource(topology).SetIndexBuffer(indices, IndexFormat.UInt32, indices.SizeInBytes / 4);
            _vertexSources[(page, topology)] = source;
        }
        return source;
    }

    /// <summary>Culls and compacts every view, then dispatches the frame. Returns the execution. Render thread.</summary>
    private int _drawCalls, _drawCommands, _shadowDrawCalls, _shadowDrawCommands;

    /// <summary>The counts of the last frame recorded. Any thread.</summary>
    public FrameStats LastStats { get; private set; } = new(0, 0, 0, 0, 0);

    // Render thread, while the frame records
    internal void CountDraw(bool shadow, int commands)
    {
        if (shadow) { _shadowDrawCalls++; _shadowDrawCommands += commands; }
        else { _drawCalls++; _drawCommands += commands; }
    }

    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, ReadOnlySpan<RenderFeature[]> viewFeatures, GraphDispatch[] pipelines, List<ReadbackRequest> readbacks, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds, ulong lastDispatched, in FrameTime time, IProfiler[]? profilers = null)
    {
        LastStats = new FrameStats(views.Length, _drawCalls, _drawCommands, _shadowDrawCalls, _shadowDrawCommands, ShadowsAdded, ShadowsGrown, ShadowsShrunk);
        _drawCalls = _drawCommands = _shadowDrawCalls = _shadowDrawCommands = 0;
        _frame++;
        _dispatches.Clear();
        _planned.Clear();
        _frameWorlds.Clear();
        _prepared.Clear();
        foreach (WorldFrame world in allWorlds)
            PrepareFeatures(world.World.FeatureSnapshot);
        foreach (RenderFeature[] features in viewFeatures)
            PrepareFeatures(features);

        DrawPrograms.Sweep();
        foreach (WorldFrame world in allWorlds)
        {
            world.World.Render.BeginFrame();
            world.Programs.Sweep();
        }

        foreach (WorldFrame world in allWorlds)
        {
            _frameWorlds.Add(world);
            world.ProbeCamerasUsed = 0;
            world.ProbeFaceCamerasUsed = 0;
            UpdateSkyProgram(world);
            WorldReflections reflections = world.World.Reflections;
            if (reflections.CaptureDue)
            {
                CaptureSky(world, time);
                readbacks.Add(new ReadbackRequest { Buffer = reflections.SkyAmbient, Size = reflections.SkyAmbient.SizeInBytes, BufferDone = world.World.ReceiveSkyAmbient });
            }
            foreach (ProbeCapture capture in reflections.Captures)
                CaptureProbe(world, capture, time);
        }

        OrderViews(views);
        foreach (int index in _viewOrder)
        {
            ViewDesc view = views[index];
            if (!worlds.TryGetValue(view.World, out WorldFrame? world))
                continue;

            if (!_cameras.TryGetValue(view.ViewId, out CameraFrame? camera))
            {
                camera = new CameraFrame(_device, this, view.ViewId);
                _cameras[view.ViewId] = camera;
                _textures.TrackView(camera.ViewTable);
            }
            else if (camera.LastFrame == _frame)
            {
                throw new InvalidOperationException($"Two views share the ViewId {view.ViewId} in one frame.");
            }

            Draw(camera, view, viewFeatures[index], world, time);
        }

        bool shadows = PrepareShadows();

        // Providers' objects, written while the views were prepared, the deforming copies' poses, and the shadows' faces,
        // after the frame's earlier writes, as they can rewrite a record those wrote
        _uploader.Fence();
        foreach (WorldFrame world in _frameWorlds)
        {
            world.World.Render.PrepareDeformation();
            _replaced.Clear();
            world.World.Render.Flush(_uploader, _replaced);
            _retired.AddRange(_replaced);
            if (world.Shadows!.Faces.Flush(_uploader) is { } faces)
                _retired.Add(faces);
        }

        ResolvePrograms();
        if (_deterministic && Globals.NeedsGrowth)
        {
            Globals.Extract();
            Globals.Flush(_uploader, lastDispatched);
            ResolvePrograms();
        }

        bool providers = _providerWork.Update(_frameWorlds, Active(), _frame, _providerPipeline, Report);
        CollectBuffers(allWorlds);

        int first = 1;
        if (providers)
            _dispatches.Insert(first++, new GraphDispatch(_providerPipeline, _frameStartViews));
        if (shadows)
            _dispatches.Insert(first + 1, new GraphDispatch(_shadowPipeline, _frameStartViews));
        _dispatches.AddRange(pipelines);
        _readback.Prepare(readbacks);
        if (_graphDirty)
            BuildGraph();
        ExecutionTask task = _device.DispatchGraph(_graph!, CreateInstances(_graph!), profilers ?? []);
        foreach (Texture texture in _disposeAfterDispatch)
            texture.Dispose();
        _disposeAfterDispatch.Clear();
        ReturnShadowCameras();
        foreach (IDisposable retired in _shadowRetired)
            retired.Dispose();
        _shadowRetired.Clear();
        foreach (ShadowTarget target in _shadowRetiredTargets)
            target.Release();
        _shadowRetiredTargets.Clear();
        foreach (IDisposable retired in _retired)
            retired.Dispose();
        _retired.Clear();
        ReleaseStaleCameras();
        ReleaseIdleCaptures();
        ReleaseStaleShapes();
        return task;
    }

    private void Draw(CameraFrame camera, in ViewDesc view, RenderFeature[] features, WorldFrame world, in FrameTime time)
    {
        camera.LastFrame = _frame;
        camera.Desc = view;
        if (camera.LayerCount != view.LayerCount)
        {
            camera.LayerCount = view.LayerCount;
            camera.HasPrevious = false;
        }
        if (view.Cut)
            camera.HasPrevious = false;
        camera.World = world;
        camera.ResolveKeywords();
        camera.HostBuffers = world.World.Globals.BufferList;
        uint width = Math.Max(1u, (uint)MathF.Round(view.Target.Width * view.Viewport.Z * view.RenderScale));
        uint height = Math.Max(1u, (uint)MathF.Round(view.Target.Height * view.Viewport.W * view.RenderScale));
        camera.Info = new FeatureView(view, width, height);
        camera.SelectFeatures(world.World.FeatureSnapshot, features);
        camera.PruneStates(world.World.FeatureSnapshot, features);
        FeatureShape shape = ShapeOf(camera, view);
        camera.ResetPassLists();
        foreach (RenderFeature feature in camera.Selected)
        {
            foreach (PassType type in shape.PassOf(feature).Types)
                camera.ListFor(type);
        }
        ResolveScreenLighting(camera, shape, width, height);
        Prepare(camera, view, world, time, width, height);
        UpdateSceneColor(camera, shape, width, height);
        camera.View.PixelWidth = width;
        camera.View.PixelHeight = height;
        _planned.Add(new PlannedInstance(shape.Kind, view.ViewId, world, new GraphExtent(width, height), shape.Captures ? null : view.Target, camera));
    }

    private void ReleaseStaleShapes()
    {
        List<GraphKind>? stale = null;
        foreach ((GraphKind kind, FeatureShape shape) in _shapes)
        {
            if (_frame - shape.LastFrame > StaleViewFrames)
                (stale ??= new()).Add(kind);
        }
        foreach (GraphKind kind in stale ?? [])
        {
            _shapes[kind].Dispose();
            _shapes.Remove(kind);
            _graphDirty = true;
        }
    }

    private FeatureShape ShapeOf(CameraFrame camera, in ViewDesc view)
    {
        CameraShape key = CameraShape.Of(view, camera.Selected, camera.HostBuffers.Length);
        GraphKind kind = RenderKinds.Of(view.Kind, key);
        if (!_shapes.TryGetValue(kind, out FeatureShape? shape))
        {
            _shapes[kind] = shape = new FeatureShape(kind, view.Kind, key) { Frame = this };
            _graphDirty = true;
        }
        shape.LastFrame = _frame;
        foreach (RenderFeature feature in camera.Selected)
        {
            if (shape.Add(feature) || shape.PassOf(feature).BuiltVersion != feature.Version)
                _graphDirty = true;
        }
        if (_graphDirty)
            BuildGraph();
        return shape;
    }

    private void BuildGraph()
    {
        List<IPass> passes = [_upload, _probes, _deform, _readbackTexture, _readbackBuffer, .. _fillPasses];
        List<GraphKind> kinds = [];
        foreach (FeatureShape shape in _shapes.Values)
        {
            passes.AddRange(shape.Passes());
            kinds.Add(shape.Kind);
        }
        _graph = RenderGraph.Build(passes, RenderKinds.Scopes(kinds), "Graphite", _store);
        _graphDirty = false;
    }

    private void ResolveScreenLighting(CameraFrame camera, FeatureShape shape, uint width, uint height)
    {
        if (!shape.Shape.HasScreenLighting)
        {
            camera.ScreenVisibility = camera.ScreenIrradiance = null;
            return;
        }
        GraphInstanceKey key = new(shape.Kind, camera.View.ViewId);
        GraphExtent extent = new(width, height);
        camera.ScreenVisibility = _store.ResolvePersistent(_device, shape.ScreenVisibilityId, shape.ScreenDesc(PixelFormat.R8_UNorm), key, extent);
        camera.ScreenIrradiance = shape.Shape.ScreenIrradiance
            ? _store.ResolvePersistent(_device, shape.ScreenIrradianceId, shape.ScreenDesc(PixelFormat.R16_G16_B16_A16_Float), key, extent)
            : null;
    }

    private GraphInstances CreateInstances(RenderGraph graph)
    {
        GraphInstances instances = graph.CreateInstances(data: this);
        _worldInstances.Clear();
        foreach (WorldFrame world in _frameWorlds)
            _worldInstances[world] = instances.Add(RenderKinds.World, world.World.Id, data: world, name: $"World {world.World.Id}");
        foreach (PlannedInstance planned in _planned)
        {
            _imports.Clear();
            if (planned.Data is CameraFrame camera)
            {
                FeatureShape shape = _shapes[planned.Kind];
                for (int i = 0; i < camera.HostBuffers.Length; i++)
                    _imports.Add(GraphImport.Of(shape.HostId(i), camera.HostBuffers[i]));
                if (camera.CaptureCube != null)
                    _imports.Add(GraphImport.Of(shape.CaptureId, camera.CaptureCube));
            }
            else if (planned.Data is CaptureFill fill)
            {
                _imports.Add(GraphImport.Of(RenderKinds.FillSource, fill.Source));
                _imports.Add(GraphImport.Of(RenderKinds.FillTarget, fill.Target.Persistent.Texture!));
                if (fill.SkyAmbient != null)
                    _imports.Add(GraphImport.Of(RenderKinds.FillAmbient, fill.SkyAmbient));
            }
            instances.Add(planned.Kind, planned.Id, _worldInstances[planned.World], planned.Extent, planned.Target, planned.Data, $"{planned.Kind.Name} {planned.Id}", _imports);
        }
        for (int i = 0; i < _readback.Requests.Count; i++)
        {
            ReadbackRequest request = _readback.Requests[i];
            GraphImport import = request.Texture is { } texture ? GraphImport.Of(RenderKinds.ReadbackTexture, texture) : GraphImport.Of(RenderKinds.ReadbackBuffer, request.Buffer!);
            instances.Add(RenderKinds.Readback, i, data: request, name: $"Readback {i}", imports: [import]);
        }
        return instances;
    }

    // Whether the view's transparent list reads the scene colour this frame. Returns whether its copy was just made, which
    // adds the copy to the view's graph; the lists keep their order through that rebuild, so what Prepare uploaded holds.
    private void UpdateSceneColor(CameraFrame camera, FeatureShape shape, uint width, uint height)
    {
        bool used = false;
        foreach (DrawCall call in camera.Transparent.Calls)
        {
            DrawProgram program = DrawPrograms[call.Program];
            if (program.Shader.PassReadsSceneColor(program.PassIndex))
            {
                used = true;
                break;
            }
        }
        camera.SceneColorUsed = used;
        if (used)
            camera.EnsureSceneColor(width, height, (uint)shape.Shape.Layers, shape.ColorFormat);
    }

    /// <summary>The draws of an external view this frame. Render thread.</summary>
    public ViewDraws Draws(int viewId)
    {
        if (!_cameras.TryGetValue(viewId, out CameraFrame? camera) || camera.LastFrame != _frame || !camera.Desc.External)
            throw new InvalidOperationException($"No external view {viewId} was submitted this frame.");
        return camera.Draws ??= new ViewDraws(this, camera);
    }

    private void PrepareFeatures(RenderFeature[] features)
    {
        foreach (RenderFeature feature in features)
        {
            if (!feature.EnabledSnapshot || !_prepared.Add(feature))
                continue;
            try
            {
                feature.PrepareFrame();
            }
            catch (Exception e)
            {
                Report(new RendererError("feature.prepare", $"{feature.GetType().Name}.PrepareFrame threw: {e}", RendererErrorSeverity.Error, feature) { Key = $"feature.prepare.{feature.GetType().FullName}" });
            }
        }
    }

    // The world's providers add this view's items to its lists, transparent ones kept for the sort
    private void CullProviders(CameraFrame camera, in ViewDesc view, WorldFrame world, in CullFrustum frustum)
    {
        camera.Sink.Begin(camera, world, frustum, view.CullingMask, camera.IsShadowView);
        ViewKinds kind = (ViewKinds)(1 << (int)view.Kind);
        ProviderView providerView = new(camera.Info, frustum);
        foreach (RenderProvider provider in world.World.ProviderSnapshot)
        {
            if (!provider.EnabledSnapshot || (provider.RunsOnSnapshot & kind) == 0)
                continue;
            try
            {
                provider.Cull(providerView, camera.Sink);
            }
            catch (Exception e)
            {
                Report(new RendererError("provider.cull", $"{provider.GetType().Name}.Cull threw: {e}", RendererErrorSeverity.Error, provider) { Key = $"provider.cull.{provider.GetType().FullName}" });
            }
        }
    }

    // Views by Order, each after the views it depends on. A cycle is reported and drawn in Order.
    private void OrderViews(ReadOnlySpan<ViewDesc> views)
    {
        _viewOrder.Clear();
        for (int i = 0; i < views.Length; i++)
        {
            int j = _viewOrder.Count - 1;
            while (j >= 0 && views[_viewOrder[j]].Order > views[i].Order)
                j--;
            _viewOrder.Insert(j + 1, i);
        }

        bool dependencies = false;
        foreach (ViewDesc view in views)
            dependencies |= view.DependsOn is { Length: > 0 };
        if (!dependencies)
            return;

        List<int> sorted = _sortedViews;
        sorted.Clear();
        sorted.AddRange(_viewOrder);
        if (_placed.Length < views.Length)
            _placed = new bool[views.Length];
        bool[] placed = _placed;
        Array.Clear(placed);
        _viewOrder.Clear();
        while (_viewOrder.Count < sorted.Count)
        {
            int next = -1;
            foreach (int index in sorted)
            {
                if (!placed[index] && Ready(views, index, placed))
                {
                    next = index;
                    break;
                }
            }
            if (next < 0)
            {
                Report(new RendererError("view.dependency.cycle", "Views depend on each other in a cycle, so the rest draw in Order.", RendererErrorSeverity.Warning));
                foreach (int index in sorted)
                {
                    if (!placed[index])
                        _viewOrder.Add(index);
                }
                return;
            }
            placed[next] = true;
            _viewOrder.Add(next);
        }
    }

    // Whether every view a view depends on is placed, ones not submitted this frame counting as placed
    private static bool Ready(ReadOnlySpan<ViewDesc> views, int index, bool[] placed)
    {
        if (views[index].DependsOn is not { } dependencies)
            return true;
        foreach (int id in dependencies)
        {
            for (int i = 0; i < views.Length; i++)
            {
                if (i != index && views[i].ViewId == id && !placed[i])
                    return false;
            }
        }
        return true;
    }

    // Holds the draw program of the environment's sky material while it has a Sky pass
    private void UpdateSkyProgram(WorldFrame world)
    {
        ushort? program = null;
        if (world.World.Lighting.Environment.Sky is { IsDisposed: false, Table: not null } sky && DrawPrograms.TryGet(sky.Draw, PassType.Sky, out ushort id))
            program = id;
        if (program == world.SkyProgram)
            return;
        if (program is ushort acquired)
            DrawPrograms.Acquire(acquired);
        if (world.SkyProgram is ushort released)
            DrawPrograms.Release(released);
        world.SkyProgram = program;
    }

    private void CaptureSky(WorldFrame world, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        world.SkyCamera ??= Capturing();
        WorldReflections.Scratch scratch = reflections.SkyScratch!;
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.SkyIncomplete();

        ViewDesc capture = new()
        {
            World = world.World,
            Kind = ViewKind.SkyCapture,
            ViewId = world.SkyCamera.View.ViewId,
            LayerCount = 6,
            Target = scratch.All,
            CullingMask = 0,
            ClearColor = world.World.Lighting.Environment.SkyColor,
            Near = 0.05f,
        };
        for (int face = 0; face < 6; face++)
            capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = WorldReflections.FaceProjection };
        world.SkyCamera.CaptureCube = scratch.AllTexture;
        Draw(world.SkyCamera, capture, [], world, time);
        Fill(world, reflections.Sky!, scratch, null, reflections.SkyAmbient);
    }

    private void Fill(WorldFrame world, WorldReflections.Target target, WorldReflections.Scratch scratch, DeviceBuffer? readback, DeviceBuffer? ambient)
    {
        CaptureFill fill = new() { Source = scratch.AllTexture, Target = target, Samples = world.World.Reflections.PrefilterSamples, Readback = readback, SkyAmbient = ambient };
        _planned.Add(new PlannedInstance(RenderKinds.CaptureFill, _planned.Count, world, new GraphExtent(target.Resolution, target.Resolution), null, fill));
    }

    private void CaptureProbe(WorldFrame world, ProbeCapture work, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        ReflectionProbeDesc probe = reflections.Probe(work.Index);
        WorldReflections.Target target = reflections.ProbeTarget(work.Index);
        WorldReflections.Scratch scratch = work.Scratch;
        Float4x4 projection = WorldReflections.FaceProjectionBetween(probe.NearClip, probe.FarClip);
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot) && !Programs.Failed(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.Request(work.Index);
        int faces = work.EndFace - work.FirstFace;
        if (faces == 6)
        {
            CameraFrame camera = Pooled(world.ProbeCameras, ref world.ProbeCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                Kind = ViewKind.ReflectionProbe,
                ViewId = camera.View.ViewId,
                LayerCount = 6,
                Target = scratch.All,
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            for (int face = 0; face < 6; face++)
                capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = projection, Position = probe.Position };
            camera.CaptureCube = scratch.AllTexture;
            Draw(camera, capture, [], world, time);
        }
        else if (faces == 1)
        {
            CameraFrame camera = Pooled(world.ProbeFaceCameras, ref world.ProbeFaceCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                Kind = ViewKind.ReflectionProbe,
                ViewId = camera.View.ViewId,
                Target = scratch.Faces[work.FirstFace],
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            capture.Layers[0] = new ViewLayer { View = WorldReflections.FaceView(work.FirstFace), Projection = projection, Position = probe.Position };
            camera.CaptureCube = scratch.FaceTextures[work.FirstFace];
            Draw(camera, capture, [], world, time);
        }

        if (work.Prefilter)
            Fill(world, target, scratch, work.Readback, null);
    }

    private CameraFrame Capturing()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    private CameraFrame Pooled(List<CameraFrame> pool, ref int used)
    {
        if (used == pool.Count)
            pool.Add(Capturing());
        return pool[used++];
    }

    // Capture views keep their textures while drawing now and then, and let them go once unused for a while
    private void ReleaseIdleCaptures()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
            {
                if (_frame - camera.LastFrame > StaleViewFrames)
                    camera.ReleaseSceneColor();
            }
        }
    }

    /// <summary>Releases what the frame pipeline keeps for a removed world. Render thread idle.</summary>
    public void ForgetWorld(WorldFrame world)
    {
        world.Programs.Clear();
        if (world.SkyProgram is ushort program)
            DrawPrograms.Release(program);
        world.SkyProgram = null;
        foreach (CameraFrame camera in world.Captures())
            Untrack(camera);
    }

    // A view's table and its cascades' tables leave the sampler tables they follow
    private void Untrack(CameraFrame camera)
    {
        _textures.Untrack(camera.ViewTable);
        foreach ((_, _, ViewShadows.Cascade[] cascades) in camera.Shadows.Lights)
        {
            foreach (ViewShadows.Cascade cascade in cascades)
            {
                if (cascade.Camera != null)
                    _textures.Untrack(cascade.Camera.ViewTable);
            }
        }
    }

    // Asks for every call's program before recording, so compiles start early and deterministic ones finish here.
    private void ResolvePrograms()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            if (world.SkyProgram is ushort sky)
                Programs.Get(sky, false, world.World.Globals.KeywordSnapshot);
        }
        foreach (CameraFrame camera in Active())
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach (DrawList list in camera.Lists)
            {
                foreach (DrawCall call in list.Calls)
                    Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.Keywords);
            }
        }
    }

    private void Prepare(CameraFrame camera, in ViewDesc view, WorldFrame world, in FrameTime time, uint width, uint height)
    {
        camera.Time = time;
        int layers = view.LayerCount;
        bool shadow = camera.IsShadowView;
        CullFrustum frustum = shadow ? camera.ShadowCull : new(view.Layers, layers);
        Array.Fill(camera.Roots, SpatialRoots.None);
        if (shadow)
        {
            world.Buckets.Cull(frustum, view.CullingMask, camera.ShadowFilter);
            if (camera.ShadowFilter == CasterFilter.StaticOnly)
                camera.Sink.Begin(camera, world, frustum, 0);
            else
                CullProviders(camera, view, world, frustum);
            camera.Prepass.Clear();
            camera.Forward.Clear();
            camera.Transparent.Clear();
            foreach ((PassType type, DrawList list) in camera.PassLists)
                world.Buckets.Build(world.Buckets.Pass(type), frustum, list, camera.Sink.For(list));
        }
        else if (view.CullingMask == 0)
        {
            camera.Sink.Begin(camera, world, frustum, 0);
            foreach (DrawList list in camera.Lists)
                list.Clear();
        }
        else
        {
            world.Buckets.Cull(frustum, view.CullingMask);
            CullProviders(camera, view, world, frustum);
            DrawItemSink sink = camera.Sink;
            world.Buckets.Build(0, frustum, camera.Prepass, sink.For(camera.Prepass));
            world.Buckets.Build(1, frustum, camera.Forward, sink.For(camera.Forward));
            foreach ((PassType type, DrawList list) in camera.PassLists)
                world.Buckets.Build(world.Buckets.Pass(type), frustum, list, sink.For(list));
            world.Buckets.BuildTransparent(frustum, camera.Transparent, sink.For(camera.Transparent)?.Sorted);

            foreach (SpatialIndex set in world.World.SpatialSets)
                camera.Roots[set.Slot] = set.BuildView(frustum, camera.SpatialFor(set.Slot), _uploader);
        }
        camera.SpatialRoots.Write<SpatialRoots>(_uploader, camera.Roots);
        ViewLayer first = view.Layers[0];
        float handedness = Determinant3(first.View) * first.Projection.c0.X * first.Projection.c1.Y;
        camera.Mirrored = handedness < 0;

        // A feature asking for jitter offsets the projection by under a pixel, through Halton(2, 3)
        int phases = 0;
        float mipBias = 0;
        foreach (RenderFeature feature in camera.Features)
        {
            if (feature.JitterPhasesSnapshot > phases)
                (phases, mipBias) = (feature.JitterPhasesSnapshot, feature.JitterMipBiasSnapshot);
        }
        camera.HadPrevious = camera.HasPrevious;
        camera.PreviousJitter = camera.Jitter;
        camera.Jitter = default;
        if (phases > 0 && !camera.IsShadowView)
        {
            int phase = camera.JitterPhase++ % phases + 1;
            camera.Jitter = new Float2((Halton(phase, 2) - 0.5f) * 2f / width, (Halton(phase, 3) - 0.5f) * 2f / height);
        }
        _textures.SetViewBias(camera.ViewTable, phases > 0 ? mipBias : 0);

        for (int l = 0; l < layers; l++)
        {
            ViewLayer layer = view.Layers[l];
            Float4x4 projection = layer.Projection;
            if (projection.c3.W > 0.5f)
                (projection.c3.X, projection.c3.Y) = (projection.c3.X + camera.Jitter.X, projection.c3.Y + camera.Jitter.Y);
            else
                (projection.c2.X, projection.c2.Y) = (projection.c2.X + camera.Jitter.X, projection.c2.Y + camera.Jitter.Y);
            ViewBlock block = ViewBlock.Create(layer.View, projection, layer.Position, width, height, view.Near, view.FarDistance);
            block.Jitter = new Float4(camera.Jitter.X, camera.Jitter.Y, camera.PreviousJitter.X, camera.PreviousJitter.Y);
            float timeHigh = (float)time.Time;
            block.Time = new Float4(timeHigh, time.DeltaTime, time.FrameIndex, (float)(time.Time - timeHigh));
            Float3 offset = frustum.Relative(layer.Position);
            block.TreeOffset = new Float4(offset.X, offset.Y, offset.Z, 0);
            if (camera.HasPrevious)
            {
                block.PreviousViewProjection = camera.Previous[l].ViewProjection;
                block.PreviousCameraHigh = camera.Previous[l].CameraHigh;
                block.PreviousCameraLow = camera.Previous[l].CameraLow;
            }
            camera.Previous[l] = block with { ViewProjection = layer.Projection * layer.View };
            camera.Blocks[l] = block;
        }
        camera.HasPrevious = true;
        camera.Upload(_uploader, camera.Blocks.AsSpan(0, layers));
        camera.PlanCounts();
    }

    /// <summary>A host view's frame state, for tests.</summary>
    internal CameraFrame? Camera(int viewId) => _cameras.GetValueOrDefault(viewId);

    private static float Halton(int index, int radix)
    {
        float result = 0, fraction = 1f / radix;
        for (int i = index; i > 0; i /= radix)
        {
            result += i % radix * fraction;
            fraction /= radix;
        }
        return result;
    }

    // The hosts' views drawn this frame and every capture view, drawn this frame or not
    private IEnumerable<CameraFrame> Active()
    {
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                yield return camera;
        }
        foreach (CameraFrame camera in _captures)
            yield return camera;
        foreach (CameraFrame camera in ShadowCameras())
            yield return camera;
    }

    private static float Determinant3(in Float4x4 m)
        => m.c0.X * (m.c1.Y * m.c2.Z - m.c2.Y * m.c1.Z)
         - m.c1.X * (m.c0.Y * m.c2.Z - m.c2.Y * m.c0.Z)
         + m.c2.X * (m.c0.Y * m.c1.Z - m.c1.Y * m.c0.Z);

    private void CollectBuffers(IEnumerable<WorldFrame> worlds)
    {
        _captures.Clear();
        foreach (WorldFrame world in worlds)
            _captures.AddRange(world.Captures());
        _buffers.Clear();
        _buffers.Add(Geometry.Chunks.Buffer);
        _buffers.Add(Geometry.Meshes.Buffer);
        foreach (GeometryPage page in Geometry.Pages)
        {
            _buffers.Add(page.Vertices);
            _buffers.Add(page.Indices);
        }
        foreach (MaterialTable table in Materials.Tables.Values)
            _buffers.Add(table.Records.Buffer);
        _buffers.Add(Globals.Buffer);
        _probes.Worlds.Clear();
        _deform.Worlds.Clear();
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            _deform.Worlds.Add(render);
            WorldProbes probes = world.World.Probes;
            _probes.Worlds.Add(probes);
            _buffers.Add(probes.ProbeRecords.Buffer);
            _buffers.Add(probes.TetRecords.Buffer);
            _buffers.Add(probes.Requests.Buffer);
            _buffers.Add(probes.ObjectSh);
            foreach (SpatialIndex set in world.World.SpatialSets)
            {
                if (set.Slot < SpatialIndex.ProbesSet)
                    continue;
                _buffers.Add(set.StaticRecords.Buffer);
                _buffers.Add(set.StaticNodes.Buffer);
            }
            _buffers.Add(world.World.Reflections.SkyAmbient);

            foreach (GpuTable table in render.Tables)
                _buffers.Add(table.Buffer);
            WorldLighting lighting = world.World.Lighting;
            foreach (GpuTable table in lighting.Tables)
                _buffers.Add(table.Buffer);

        }
        foreach (WorldFrame world in worlds)
        {
            if (world.Shadows != null)
                _buffers.Add(world.Shadows.Faces.Buffer);
        }
        foreach (DeviceBuffer buffer in _providerWork.ComputeBuffers)
        {
            if (!_buffers.Contains(buffer))
                _buffers.Add(buffer);
        }
        foreach (CameraFrame camera in Active())
            camera.AddBuffers(_buffers);

        if (Same(Buffers, _buffers))
            return;

        Buffers = [.. _buffers];
        _graphDirty = true;
        _upload.Buffers = Buffers;
        _probes.Buffers = Buffers;
        _deform.Buffers = Buffers;
    }

    private static bool Same(DeviceBuffer[] a, List<DeviceBuffer> b)
    {
        if (a.Length != b.Count)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    // Per view state is released after a view has not been submitted for a few frames.
    private void ReleaseStaleCameras()
    {
        List<int>? stale = null;
        foreach ((int id, CameraFrame camera) in _cameras)
        {
            if (_frame - camera.LastFrame > StaleViewFrames)
                (stale ??= new()).Add(id);
        }
        if (stale == null)
            return;
        foreach (int id in stale)
        {
            Untrack(_cameras[id]);
            _cameras[id].Dispose();
            _cameras.Remove(id);
        }
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in _cameras.Values)
            camera.Dispose();
        _cameras.Clear();
        ReturnShadowCameras();
        foreach (CameraFrame camera in _shadowCameraPool)
            camera.Dispose();
        _shadowCameraPool.Clear();
        _providerProbe?.Dispose();
        _graph?.Dispose();
        _store.Dispose();
        foreach (FeatureShape shape in _shapes.Values)
            shape.Dispose();
        _providerPipeline.Dispose();
        _shadowPipeline.Dispose();
        _shadowSampler?.Dispose();
        _shrinkScratch?.Dispose();
        _tileClear?.Dispose();
        _probes.Dispose();
        _deform.Dispose();
        Programs.Dispose();
        foreach (FullscreenProgram program in _present)
            program.Dispose();
        Noise.Dispose();
        SmaaArea.Dispose();
        SmaaSearch.Dispose();
        SmaaAreaLayered.Dispose();
        SmaaSearchLayered.Dispose();
        NoScreenVisibility.Dispose();
        NoScreenIrradiance.Dispose();
        NoSceneColor.Dispose();
        ClearProgram.Dispose();
        CopyBackProgram.Dispose();
        foreach (IDisposable retired in _retired)
            retired.Dispose();
        PrefilterProgram.Dispose();
        SkyAmbientProgram.Dispose();
        TrilinearClamp.Dispose();
        LinearSampler.Dispose();
    }
}

/// <summary>
/// The draws the renderer prepared this frame for an external view: its culled lists, its tables and the frame's
/// buffers, which a pass before the host pipelines makes ready for them. Host passes bind a target and record. Render thread.
/// </summary>
public sealed class ViewDraws
{
    private readonly FramePipeline _frame;
    private readonly CameraFrame _camera;
    private readonly PropertySet _properties = new();
    private ulong _preparedExecution;

    internal ViewDraws(FramePipeline frame, CameraFrame camera)
    {
        _frame = frame;
        _camera = camera;
    }

    /// <summary>The view as submitted.</summary>
    public ViewDesc Desc => _camera.Desc;

    /// <summary>Records the visible renderables of a pass type onto the bound target: the prepass, opaque forward, or one in ViewDesc.Passes.</summary>
    public void Record(RenderContext context, CommandBuffer cmd, PassType pass)
    {
        DrawList list = _camera.FindList(pass) ?? throw new InvalidOperationException($"View {_camera.Desc.ViewId} did not prepare the {pass.Name} pass, list it in ViewDesc.Passes.");
        Prepare(context);
        _camera.Drawer.DrawList(cmd, list);
    }

    /// <summary>Records the visible transparent renderables back to front onto the bound target.</summary>
    public void RecordTransparent(RenderContext context, CommandBuffer cmd)
    {
        Prepare(context);
        _camera.Drawer.DrawList(cmd, _camera.Transparent);
    }

    /// <summary>Records the world's sky onto the bound target.</summary>
    public void RecordSky(RenderContext context, CommandBuffer cmd)
    {
        Prepare(context);
        _camera.Drawer.DrawSky(cmd);
    }

    /// <summary>
    /// Draws a fullscreen program onto the bound target with the view's layers, and the view's lights, shadows and probes
    /// when it is lit, such as the lighting pass of a deferred pipeline. Its properties bind last.
    /// </summary>
    public void DrawFullscreen(RenderContext context, CommandBuffer cmd, FullscreenProgram program, PropertySet? properties = null)
    {
        Prepare(context);
        cmd.SetShader(program.For(_camera.LayerCount));
        cmd.SetVertexSource(VertexSource.None);
        if (program.Lit)
            _camera.Drawer.BindTables(cmd);
        _properties.SetBuffer("Graphite_PostViews", _camera.ViewBlocks.Buffer);
        cmd.SetProperties(_properties);
        if (properties != null)
            cmd.SetProperties(properties);
        cmd.Draw(3);
    }

    private void Prepare(RenderContext context)
    {
        if (_preparedExecution == context.Task.Id)
            return;
        _camera.Drawer.Prepare(context.Task.RingSlot);
        _preparedExecution = context.Task.Id;
    }
}

/// <summary>A copy to the host the renderer records after a frame's views.</summary>
internal sealed class ReadbackRequest
{
    public Texture? Texture;
    public TextureRegion Region;
    public DeviceBuffer? Buffer;
    public uint Offset, Size;
    public Action<TextureReadback>? TextureDone;
    public Action<BufferReadback>? BufferDone;
    public DeviceBuffer? Staging;
    public ulong Execution;
    public int Attempts;
    public string? Error;
}

/// <summary>Makes the staging buffers of a frame's readbacks and drops the requests whose source was disposed.</summary>
internal sealed class ReadbackStaging(GraphicsDevice device, Action<RendererError> report)
{
    public readonly List<ReadbackRequest> Requests = new();

    public void Prepare(List<ReadbackRequest> requests)
    {
        Requests.Clear();
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            ReadbackRequest request = requests[i];
            if (request.Texture?.IsDisposed == true || request.Buffer?.IsDisposed == true)
            {
                request.Error = "The texture or buffer was disposed before it was copied.";
                report(new RendererError("readback.disposed", "A readback's texture or buffer was disposed before it was copied.", RendererErrorSeverity.Warning, (object?)request.Texture ?? request.Buffer));
                continue;
            }
            uint bytes = request.Texture is { } texture ? request.Region.Width * request.Region.Height * request.Region.Depth * texture.Format.GetSizeInBytes() : request.Size;
            request.Staging ??= device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
            Requests.Add(request);
        }
    }
}

/// <summary>Copies the texture a readback instance binds into its staging buffer.</summary>
internal sealed class ReadbackTexturePass : IInstancePass
{
    private TextureHandle _source;

    public string Name => "Graphite.ReadbackTexture";

    public GraphKind Kind => RenderKinds.Readback;

    public bool IsEnabled(GraphInstance instance) => instance.Data is ReadbackRequest { Texture: not null };

    public void Setup(InstanceContextBuilder builder) => _source = builder.DeclareImportSlotTexture(RenderKinds.ReadbackTexture, TextureState.TransferSrc);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        ReadbackRequest request = (ReadbackRequest)context.Instance.Data!;
        cmd.CopyTextureToBuffer(context.GetRenderTexture(_source).ColorTextures[0], request.Staging!, 0, request.Region);
    }
}

/// <summary>Copies the buffer a readback instance binds into its staging buffer.</summary>
internal sealed class ReadbackBufferPass : IInstancePass
{
    private BufferHandle _source;

    public string Name => "Graphite.ReadbackBuffer";

    public GraphKind Kind => RenderKinds.Readback;

    public bool IsEnabled(GraphInstance instance) => instance.Data is ReadbackRequest { Buffer: not null };

    public void Setup(InstanceContextBuilder builder) => _source = builder.DeclareImportSlotBuffer(RenderKinds.ReadbackBuffer, BufferAccess.TransferRead);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        ReadbackRequest request = (ReadbackRequest)context.Instance.Data!;
        cmd.CopyBuffer(context.GetRenderBuffer(_source), request.Offset, request.Staging!, 0, request.Size);
    }
}

/// <summary>
/// Providers' GPU work once a frame, after FrameStart and before the fence every view follows: their compute, then the
/// instance counts it wrote copied into every view's commands, so indirect items draw what this frame made.
/// </summary>
internal sealed class ProviderWork
{
    private static readonly List<RenderResourceID> s_ids = new();
    private readonly List<RenderProvider> _computing = new();
    private readonly List<DeviceBuffer> _computeBuffers = new();

    /// <summary>This frame's buffers providers' compute writes, which the fence orders before every reader.</summary>
    public IReadOnlyList<DeviceBuffer> ComputeBuffers => _computeBuffers;
    private readonly List<(DeviceBuffer Source, uint Offset, DeviceBuffer Target, uint Destination)> _copies = new();
    private readonly List<DeviceBuffer> _buffers = new();
    private readonly List<DeviceBuffer> _scratch = new();
    private DeviceBuffer[] _declared = [];
    private RenderProvider[] _declaredProviders = [];
    private readonly ComputeStep _compute;
    private readonly CopyStep _copy;
    private Action<RendererError> _report = _ => { };

    public ProviderWork()
    {
        _compute = new ComputeStep(this);
        _copy = new CopyStep(this);
    }

    /// <summary>Gathers this frame's compute and copies, rebuilding the pipeline when what they touch changes. Returns whether there is any.</summary>
    public bool Update(IEnumerable<WorldFrame> worlds, IEnumerable<CameraFrame> cameras, int frame, RenderPipeline pipeline, Action<RendererError> report)
    {
        _report = report;
        _computing.Clear();
        _computeBuffers.Clear();
        _copies.Clear();
        _buffers.Clear();
        foreach (WorldFrame world in worlds)
        {
            foreach (RenderProvider provider in world.World.ProviderSnapshot)
            {
                if (!provider.EnabledSnapshot)
                    continue;
                _scratch.Clear();
                try
                {
                    provider.ComputeBuffers(_scratch);
                }
                catch (Exception e)
                {
                    Report(provider, "ComputeBuffers", e);
                    continue;
                }
                foreach (DeviceBuffer buffer in _scratch)
                {
                    if (buffer != null && !buffer.IsDisposed && !_computeBuffers.Contains(buffer))
                        _computeBuffers.Add(buffer);
                }
                if (_scratch.Count > 0)
                    _computing.Add(provider);
            }
        }
        foreach (CameraFrame camera in cameras)
        {
            if (camera.LastFrame != frame)
                continue;
            foreach ((DeviceBuffer source, uint offset, uint destination) in camera.Counts)
            {
                if (source.IsDisposed)
                {
                    report(new RendererError("provider.count", "An indirect item's instance count buffer was disposed before its frame drew, so it draws its last count.", RendererErrorSeverity.Warning) { Key = "provider.count.disposed" });
                    continue;
                }
                _copies.Add((source, offset, camera.Commands.Buffer, destination));
            }
        }
        if (_computing.Count == 0 && _copies.Count == 0)
            return false;

        _buffers.AddRange(_computeBuffers);
        foreach ((DeviceBuffer source, _, DeviceBuffer target, _) in _copies)
        {
            if (!_buffers.Contains(source))
                _buffers.Add(source);
            if (!_buffers.Contains(target))
                _buffers.Add(target);
        }
        if (!CollectionsMarshal.AsSpan(_buffers).SequenceEqual(_declared) || !CollectionsMarshal.AsSpan(_computing).SequenceEqual(_declaredProviders))
        {
            _declared = [.. _buffers];
            _declaredProviders = [.. _computing];
            pipeline.SetPasses([_compute, _copy]);
        }
        return true;
    }

    private void Report(RenderProvider provider, string method, Exception e)
        => _report(new RendererError("provider.compute", $"{provider.GetType().Name}.{method} threw: {e}", RendererErrorSeverity.Error, provider) { Key = $"provider.compute.{provider.GetType().FullName}" });

    private static RenderResourceID Id(int index)
    {
        lock (s_ids)
        {
            while (s_ids.Count <= index)
                s_ids.Add(RenderResourceID.Intern("Graphite.Provider" + s_ids.Count));
            return s_ids[index];
        }
    }

    private sealed class ComputeStep(ProviderWork work) : IPass
    {
        public string Name => "Graphite.ProviderCompute";

        public void Setup(RenderContextBuilder builder)
        {
            for (int i = 0; i < work._declared.Length; i++)
            {
                if (work._computeBuffers.Contains(work._declared[i]))
                    builder.DeclareImportedBuffer(Id(i), work._declared[i], BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
            }
        }

        public void Render(RenderContext context, CommandBuffer cmd)
        {
            foreach (RenderProvider provider in work._computing)
            {
                try
                {
                    provider.Compute(cmd);
                }
                catch (Exception e)
                {
                    work.Report(provider, "Compute", e);
                }
            }
        }
    }

    private sealed class CopyStep(ProviderWork work) : IPass
    {
        public string Name => "Graphite.ProviderCounts";

        public void Setup(RenderContextBuilder builder)
        {
            for (int i = 0; i < work._declared.Length; i++)
            {
                DeviceBuffer buffer = work._declared[i];
                BufferAccess access = BufferAccess.None;
                foreach ((DeviceBuffer source, _, DeviceBuffer target, _) in work._copies)
                {
                    if (source == buffer)
                        access |= BufferAccess.TransferRead;
                    if (target == buffer)
                        access |= BufferAccess.TransferWrite;
                }
                if (access != BufferAccess.None)
                    builder.DeclareImportedBuffer(Id(i), buffer, access);
            }
        }

        public void Render(RenderContext context, CommandBuffer cmd)
        {
            foreach ((DeviceBuffer source, uint offset, DeviceBuffer target, uint destination) in work._copies)
                cmd.CopyBuffer(source, offset, target, destination, 4);
        }
    }
}

/// <summary>
/// What a frame drew: the views asked for, and the indirect draw calls recorded with the draws inside them (each
/// command one mesh piece, instanced or not), split between camera views and shadow views, and the local light shadows
/// that were given tiles, grew or shrank.
/// </summary>
public sealed record FrameStats(int Views, int DrawCalls, int DrawCommands, int ShadowDrawCalls, int ShadowDrawCommands, int ShadowsAdded = 0, int ShadowsGrown = 0, int ShadowsShrunk = 0);
