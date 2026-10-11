using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The Level 0 programs of draw programs: the variant compiled with the view's global keywords, put through the
/// material rewrite, with its pass state, the material's state override and the winding of mirrored objects.
/// A variant compiles on a worker the first time it is asked for, and until it is ready draws use the nearest
/// compiled variant of the same pass: the one differing in the fewest axes, ties going to axis defaults. Only the
/// compile and rewrite run on the worker, programs are created on the render thread. In deterministic mode compiles
/// block instead. Programs that fail to compile are reported once and draw nothing.
/// </summary>
internal sealed class ProgramCache(GraphicsDevice device, IShaderCompiler compiler, GlobalRegistry globals, DrawPrograms programs, bool deterministic, Action<RendererError> report) : IDisposable
{
    private sealed class Entry
    {
        public required DrawProgram Program;
        public required bool Mirrored;
        public required int[] Values;
        public Task<ShaderDescription>? Pending;
        public GraphicsProgram? Built;
        public bool Failed;
    }

    private readonly record struct Family(Shader Shader, int PassIndex, PassState? StateOverride, bool Mirrored);

    private readonly Dictionary<(ushort Program, bool Mirrored, ulong Globals), Entry> _entries = new();
    private readonly Dictionary<Family, List<Entry>> _families = new();

    public static readonly ResourceLayoutDescription FrameLayout = GraphiteBindings.FrameTable(RendererTextures.Textures2D, RendererTextures.OtherTextures);
    public static readonly ResourceLayoutDescription ViewLayout = GraphiteBindings.ViewTable(RendererTextures.Samplers);

    /// <summary>Drops every variant of a freed draw program id, before the id is reused for another program.</summary>
    public void Drop(ushort id)
    {
        List<(ushort, bool, ulong)>? keys = null;
        foreach (((ushort Program, bool Mirrored, ulong Globals) key, Entry entry) in _entries)
        {
            if (key.Program != id)
                continue;
            (keys ??= new()).Add(key);
            entry.Built?.Dispose();
            Family family = new(entry.Program.Shader, entry.Program.PassIndex, entry.Program.StateOverride, entry.Mirrored);
            if (_families.TryGetValue(family, out List<Entry>? members))
            {
                members.Remove(entry);
                if (members.Count == 0)
                    _families.Remove(family);
            }
        }
        if (keys == null)
            return;
        foreach ((ushort, bool, ulong) key in keys)
            _entries.Remove(key);
    }

    /// <summary>Variants known to the cache, ready, compiling or failed.</summary>
    internal int EntryCount => _entries.Count;

    /// <summary>Variants still compiling on a worker.</summary>
    public int Pending
    {
        get
        {
            int pending = 0;
            foreach (Entry entry in _entries.Values)
            {
                if (entry.Pending != null)
                    pending++;
            }
            return pending;
        }
    }

    /// <summary>The program to draw with now: the wanted variant when it is ready, else the nearest ready one, else null. Render thread.</summary>
    public GraphicsProgram? Get(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords)
    {
        DrawProgram program = programs[id];
        IReadOnlyList<VariantSpace> axes = program.Shader.GlobalAxes;
        ulong key = 0, stride = 1;
        Span<int> globalValues = stackalloc int[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            globalValues[i] = ValueIndex(axes[i], keywords.GetValueOrDefault(axes[i].Name));
            key += (ulong)globalValues[i] * stride;
            stride *= (ulong)axes[i].Values.Count;
        }

        if (!_entries.TryGetValue((id, mirrored, key), out Entry? entry))
        {
            entry = Create(program, mirrored, globalValues);
            _entries[(id, mirrored, key)] = entry;
        }

        Complete(entry);
        return entry.Built ?? (entry.Failed ? null : Nearest(entry));
    }

    private readonly Dictionary<GraphicsProgram, PropertyID[]> _userBuffers = new();

    /// <summary>The buffers a program reads that the host sets through Globals.SetBuffer.</summary>
    public PropertyID[] UserBuffers(GraphicsProgram program)
    {
        lock (_userBuffers)
            return _userBuffers.TryGetValue(program, out PropertyID[]? buffers) ? buffers : [];
    }

    /// <summary>Whether the wanted variant itself is built, rather than a nearby one standing in. Render thread.</summary>
    public bool Ready(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords)?.Built != null;

    /// <summary>Whether the wanted variant failed to compile, so it never becomes ready. Render thread.</summary>
    public bool Failed(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords)?.Failed == true;

    private Entry? Wanted(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords)
    {
        Get(id, mirrored, keywords);
        DrawProgram program = programs[id];
        IReadOnlyList<VariantSpace> axes = program.Shader.GlobalAxes;
        ulong key = 0, stride = 1;
        for (int i = 0; i < axes.Count; i++)
        {
            key += (ulong)ValueIndex(axes[i], keywords.GetValueOrDefault(axes[i].Name)) * stride;
            stride *= (ulong)axes[i].Values.Count;
        }
        return _entries.TryGetValue((id, mirrored, key), out Entry? entry) ? entry : null;
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries.Values)
        {
            try
            {
                entry.Pending?.Wait();
            }
            catch (AggregateException)
            {
            }
            entry.Built?.Dispose();
        }
        _entries.Clear();
        _families.Clear();
    }

    private Entry Create(DrawProgram program, bool mirrored, ReadOnlySpan<int> globalValues)
    {
        IReadOnlyList<VariantSpace> materialAxes = program.Shader.MaterialAxes;
        IReadOnlyList<VariantSpace> globalAxes = program.Shader.GlobalAxes;
        int[] values = new int[materialAxes.Count + globalAxes.Count];
        Keyword[] combo = new Keyword[values.Length];
        for (int i = 0; i < materialAxes.Count; i++)
        {
            values[i] = ValueIndex(materialAxes[i], program.Keywords[i].Value);
            combo[i] = program.Keywords[i];
        }
        for (int i = 0; i < globalAxes.Count; i++)
        {
            values[materialAxes.Count + i] = globalValues[i];
            combo[materialAxes.Count + i] = new Keyword(globalAxes[i].Name, globalAxes[i].Values[globalValues[i]]);
        }

        Entry entry = new() { Program = program, Mirrored = mirrored, Values = values };
        Family family = new(program.Shader, program.PassIndex, program.StateOverride, mirrored);
        if (!_families.TryGetValue(family, out List<Entry>? members))
        {
            members = new List<Entry>();
            _families[family] = members;
        }
        members.Add(entry);

        entry.Pending = deterministic ? DescribeNow(program, combo) : Task.Run(() => Describe(program, combo));
        return entry;
    }

    private Task<ShaderDescription> DescribeNow(DrawProgram program, Keyword[] combo)
    {
        try
        {
            return Task.FromResult(Describe(program, combo));
        }
        catch (Exception e)
        {
            return Task.FromException<ShaderDescription>(e);
        }
    }

    // A variant that declared new globals waits until the next extraction has grown the regions to hold them.
    private void Complete(Entry entry)
    {
        if (entry.Pending is not { IsCompleted: true } pending || globals.NeedsGrowth)
            return;

        entry.Pending = null;
        DrawProgram program = entry.Program;
        try
        {
            ShaderDescription description = pending.GetAwaiter().GetResult();
            entry.Built = Build(program, description, entry.Mirrored);
        }
        catch (Exception e)
        {
            entry.Failed = true;
            report(new RendererError(e is GBufferMissingException ? "shader.gbuffer.missing" : "shader.compile", $"{program.Shader.Name}: {e.Message}", RendererErrorSeverity.Error, program.Shader) { Key = $"program.{program.Shader.Name}.{program.PassIndex}.{string.Join(",", entry.Values)}" });
        }
    }

    // The ready variant of the same pass differing in the fewest axes, ties going to the one whose differences are defaults.
    private GraphicsProgram? Nearest(Entry wanted)
    {
        GraphicsProgram? best = null;
        (int Differences, int NonDefault) bestScore = (int.MaxValue, int.MaxValue);
        foreach (Entry candidate in _families[new Family(wanted.Program.Shader, wanted.Program.PassIndex, wanted.Program.StateOverride, wanted.Mirrored)])
        {
            Complete(candidate);
            if (candidate.Built == null)
                continue;

            int differences = 0, nonDefault = 0;
            for (int i = 0; i < wanted.Values.Length; i++)
            {
                if (candidate.Values[i] == wanted.Values[i])
                    continue;
                differences++;
                if (candidate.Values[i] != 0)
                    nonDefault++;
            }

            if ((differences, nonDefault).CompareTo(bestScore) < 0)
            {
                bestScore = (differences, nonDefault);
                best = candidate.Built;
            }
        }
        return best;
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

    // Worker thread: the compile and the material rewrite, nothing that touches the device.
    private ShaderDescription Describe(DrawProgram program, Keyword[] combo)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
        ShaderDescription description = compiler.Compile(pass, combo, device.BackendType);
        return MaterialRewrite.Rewrite(program.Shader.Name, description, program.Shader.Schema, compiler.GetGlobals(pass), globals, FrameLayout, ViewLayout);
    }

    // Colour takes the pass's blend. Every opaque forward pass writes the G-buffer, so it always matches the surface that
    // won the depth test; transparent, blended and other passes leave it. The prepass writes no colour at all.
    internal static BlendStateDescription SceneBlend(PassState state, bool forward, bool prepass)
    {
        BlendAttachmentDescription color = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool gbuffer = WritesGBufferTargets(state, forward, prepass);
        BlendAttachmentDescription target = BlendAttachmentDescription.OverrideBlend;
        BlendAttachmentDescription masked = target with { ColorWriteMask = ColorWriteMask.None };
        if (prepass)
            color.ColorWriteMask = ColorWriteMask.None;

        BlendStateDescription blend = state.ToBlendState(BlendStateDescription.SingleOverrideBlend);
        blend.AttachmentStates = [color, gbuffer ? target : masked, gbuffer ? target : masked];
        return blend;
    }

    /// <summary>Whether a pass draws opaque surfaces, so it must write the G-buffer.</summary>
    internal static bool WritesGBufferTargets(PassState state, bool forward, bool prepass)
    {
        BlendAttachmentDescription color = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool blended = color.BlendEnabled && !(color.SourceColorFactor == BlendFactor.One && color.DestinationColorFactor == BlendFactor.Zero);
        return forward && !prepass && !blended;
    }

    internal static bool StoresGBuffer(in ShaderDescription description)
    {
        foreach (ShaderStageDescription stage in description.Stages)
        {
            if (stage.Stage != ShaderStages.Fragment)
                continue;
            SpirvModule module = new(stage.ShaderBytes);
            uint normal = module.FindVariableAtLocation(SpirvModule.Output, 1);
            uint material = module.FindVariableAtLocation(SpirvModule.Output, 2);
            return normal != 0 && material != 0 && module.Stores(normal) && module.Stores(material);
        }
        return false;
    }

    private GraphicsProgram Build(DrawProgram program, ShaderDescription description, bool mirrored)
    {
        ShaderPass pass = program.Shader.Definition.Passes![program.PassIndex];
        PassState state = program.StateOverride is { } over ? over.Apply(pass.State) : pass.State;
        bool forward = ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Forward.Name);
        bool prepass = ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Prepass.Name);
        if (WritesGBufferTargets(state, forward, prepass) && !StoresGBuffer(description))
            throw new GBufferMissingException("An opaque Forward pass must write the G-buffer: include Graphite/Passes.glsl and call Graphite_WriteGBuffer.");
        description.BlendState = SceneBlend(state, forward, prepass);
        description.DepthStencilState = state.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyGreaterEqual);
        RasterizerStateDescription raster = state.ToRasterizerState(RasterizerStateDescription.Default);
        if (mirrored)
            raster.FrontFace = raster.FrontFace == FrontFace.Clockwise ? FrontFace.CounterClockwise : FrontFace.Clockwise;
        if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Sky.Name))
        {
            description.DepthStencilState = DepthStencilStateDescription.DepthOnlyGreaterEqualRead;
            raster = RasterizerStateDescription.CullNone;
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
/// <summary>An opaque forward pass whose fragment stage never writes the G-buffer.</summary>
internal sealed class GBufferMissingException(string message) : Exception(message);

internal sealed class GpuArray(GraphicsDevice device, BufferUsage usage, string name) : IDisposable
{
    public DeviceBuffer Buffer { get; private set; } = Create(device, usage, name, 256);

    public void Write<T>(Uploader uploader, ReadOnlySpan<T> data) where T : unmanaged
    {
        uint bytes = (uint)(data.Length * Marshal.SizeOf<T>());
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

/// <summary>The view a camera pipeline draws for: its target and size. Kept per camera so a frame allocates nothing.</summary>
internal sealed class CameraView : IRenderView
{
    public Framebuffer? Target { get; set; }
    public uint PixelWidth { get; set; } = 1;
    public uint PixelHeight { get; set; } = 1;
    public int ViewId { get; set; }
}

/// <summary>The FrameStart pass: the frame's single scatter into every persistent buffer, with each declared as written.</summary>
internal sealed class UploadPass(Uploader uploader) : IPass
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
    public readonly RenderPipeline Pipeline = new();
    public readonly SceneDrawer Drawer;
    public readonly FeatureChain Chain;
    public readonly SetupPass Setup;
    public readonly PresentPass Present;

    /// <summary>The features the view's graph runs, and this frame's pick.</summary>
    public readonly List<RenderFeature> Features = new();
    public readonly List<RenderFeature> Selected = new();

    /// <summary>Whether the view's graph rebuilds before it next draws.</summary>
    public bool Rebuild = true;

    /// <summary>The host's buffers the view's graph declares, from the world's globals.</summary>
    public DeviceBuffer[] HostBuffers = [];

    /// <summary>What host pipelines draw an external view through.</summary>
    public ViewDraws? Draws;
    public FeatureView Info;
    public readonly TableRing ViewTable;
    public readonly GpuArray Instances;
    public readonly GpuArray Records;
    public readonly GpuArray Commands;
    public readonly GpuArray ViewBlocks;
    public readonly GpuArray ViewLights;
    public readonly GpuArray ViewLightNodes;
    public readonly GpuArray ViewDecals;
    public readonly GpuArray ViewDecalNodes;
    public readonly GpuArray LightRegions;
    public readonly GpuArray LightRegionNodes;
    public readonly GpuArray DecalRegions;
    public readonly GpuArray DecalRegionNodes;
    public readonly GpuArray ProbeRegions;
    public readonly GpuArray ProbeRegionNodes;
    public readonly LightTreeBuilder LightTree = new();
    public readonly LightTreeBuilder DecalTree = new();
    public readonly LightTreeBuilder LightRegionTree = new();
    public readonly LightTreeBuilder DecalRegionTree = new();
    public readonly LightTreeBuilder ProbeRegionTree = new();

    /// <summary>Whether the view's basis flips handedness, as cube captures do, so every draw's winding flips too.</summary>
    public bool Mirrored;
    public ViewDesc Desc;
    public WorldFrame World = null!;
    public int LastFrame;
    public readonly CameraView View = new();

    /// <summary>Last frame's view block per layer, for motion: its view projection and camera position.</summary>
    public readonly ViewBlock[] Previous = new ViewBlock[6];
    public readonly ViewBlock[] Blocks = new ViewBlock[6];
    public bool HasPrevious;
    public int LayerCount = 1;
    public readonly IRenderView[] Views;

    private IndirectDrawIndexedArguments[] _commands = new IndirectDrawIndexedArguments[64];
    private DrawRecord[] _records = new DrawRecord[64];
    private uint[] _instances = new uint[256];
    private readonly FramePipeline _frame;
    private readonly Dictionary<RenderFeature, FeaturePass> _passes = new();
    private readonly List<IPass> _passList = new();

    /// <summary>Leaves a feature out of this view from the next frame, until it or the view's features change.</summary>
    public void Fail(RenderFeature feature)
    {
        if (Array.IndexOf(FeatureSelection.BuiltIn, feature) >= 0)
            return;
        _failed[feature] = feature.Version;
        Rebuild = true;
    }

    // Features that failed on this view with their version, left out until they or the view's features change
    private readonly Dictionary<RenderFeature, int> _failed = new();
    private readonly List<RenderFeature> _unfiltered = new();
    private readonly List<RenderFeature> _lastUnfiltered = new();
    private RenderFeature[] _worldFeatures = [], _viewFeatures = [];
    private Action<RendererError> _report = _ => { };

    public CameraFrame(GraphicsDevice device, FramePipeline frame, int viewId)
    {
        _frame = frame;
        Drawer = new SceneDrawer(frame, this);
        Chain = new FeatureChain(device, viewId, frame.Retired);
        Setup = new SetupPass(frame, this);
        Present = new PresentPass(frame, this);
        Views = [View];
        View.ViewId = viewId;
        Lists = [Prepass, Forward, Transparent];
        Pipeline.Name = $"Graphite.Camera {viewId}";
        ViewTable = new TableRing(device, ProgramCache.ViewLayout, $"View table {viewId}");
        Instances = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Instances {viewId}");
        Records = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Draw records {viewId}");
        Commands = new GpuArray(device, BufferUsage.IndirectBuffer, $"Indirect commands {viewId}");
        ViewBlocks = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View blocks {viewId}");
        ViewLights = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View lights {viewId}");
        ViewLightNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View light nodes {viewId}");
        ViewDecals = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View decals {viewId}");
        ViewDecalNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"View decal nodes {viewId}");
        LightRegions = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Light regions {viewId}");
        LightRegionNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Light region nodes {viewId}");
        DecalRegions = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Decal regions {viewId}");
        DecalRegionNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Decal region nodes {viewId}");
        ProbeRegions = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Probe regions {viewId}");
        ProbeRegionNodes = new GpuArray(device, BufferUsage.StructuredBufferReadOnly, $"Probe region nodes {viewId}");
    }

    /// <summary>Takes this frame's pick of features, rebuilding the graph when it or the graph's resources changed.</summary>
    /// <summary>Picks this frame's features, leaving out ones that failed until they or the view's other features change.</summary>
    public void SelectFeatures(RenderFeature[] world, RenderFeature[] view, Action<RendererError> report)
    {
        (_worldFeatures, _viewFeatures, _report) = (world, view, report);
        FeatureSelection.Select(_unfiltered, world, view, Info, report);
        bool changed = !Same(_unfiltered, _lastUnfiltered);
        foreach ((RenderFeature feature, int version) in _failed)
            changed |= feature.Version != version;
        if (changed)
        {
            _failed.Clear();
            _lastUnfiltered.Clear();
            _lastUnfiltered.AddRange(_unfiltered);
        }
        if (_failed.Count == 0)
        {
            Selected.Clear();
            Selected.AddRange(_unfiltered);
        }
        else
        {
            FeatureSelection.Select(Selected, world, view, Info, report, _failed);
        }
    }

    /// <summary>Takes this frame's pick of features, rebuilding the graph when it or the graph's resources changed.</summary>
    public void ApplyFeatures()
    {
        if (!Rebuild && Same(Features, Selected) && !Invalidated())
            return;

        // Built now, so a feature that breaks the graph is left out of this view instead of failing the frame
        while (TryBuild(Selected) is { } error)
        {
            RenderFeature? blamed = (error as FeatureSetupException)?.Feature;
            if (blamed != null && Array.IndexOf(FeatureSelection.BuiltIn, blamed) >= 0)
                throw error;
            RenderFeature culprit = blamed ?? FindCulprit() ?? throw error;
            _failed[culprit] = culprit.Version;
            _report(new RendererError("feature.setup", $"{culprit.GetType().Name} broke the graph of view {View.ViewId}, which runs without it: {error.InnerException ?? error}", RendererErrorSeverity.Error, culprit) { Key = $"feature.setup.{View.ViewId}.{culprit.GetType().FullName}" });
            FeatureSelection.Select(Selected, _worldFeatures, _viewFeatures, Info, _report, _failed);
        }

        Features.Clear();
        Features.AddRange(Selected);
        PruneFeatures();
        Chain.ReleaseUnwritten();
        Rebuild = false;
    }

    private Exception? TryBuild(List<RenderFeature> features)
    {
        _passList.Clear();
        _passList.Add(Setup);
        foreach (RenderFeature feature in features)
        {
            if (!_passes.TryGetValue(feature, out FeaturePass? pass))
                _passes[feature] = pass = new FeaturePass(feature, this, _frame);
            _passList.Add(pass);
        }
        _passList.Add(Present);
        Pipeline.SetPasses(_passList);
        try
        {
            _ = Pipeline.Graph;
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    // The first host feature whose joining the built in ones and the host features before it breaks the graph
    private RenderFeature? FindCulprit()
    {
        List<RenderFeature> kept = new(), trial = new();
        foreach (RenderFeature feature in Selected)
        {
            if (Array.IndexOf(FeatureSelection.BuiltIn, feature) >= 0)
                kept.Add(feature);
        }
        if (TryBuild(kept) != null)
            return null;
        foreach (RenderFeature feature in Selected)
        {
            if (kept.Contains(feature))
                continue;
            trial.Clear();
            foreach (RenderFeature other in Selected)
            {
                if (other == feature || kept.Contains(other))
                    trial.Add(other);
            }
            if (TryBuild(trial) != null)
                return feature;
            kept.Add(feature);
        }
        return null;
    }

    // Passes and states are kept while their feature is in the world's or view's set, so ids and history survive toggles
    private void PruneFeatures()
    {
        bool Known(RenderFeature feature) => Array.IndexOf(_worldFeatures, feature) >= 0 || Array.IndexOf(_viewFeatures, feature) >= 0 || Array.IndexOf(FeatureSelection.BuiltIn, feature) >= 0;
        List<RenderFeature>? gone = null;
        foreach (RenderFeature feature in _passes.Keys)
        {
            if (!Known(feature))
                (gone ??= new()).Add(feature);
        }
        foreach (RenderFeature feature in gone ?? [])
            _passes.Remove(feature);
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

    private bool Invalidated()
    {
        foreach (RenderFeature feature in Features)
        {
            if (_passes.TryGetValue(feature, out FeaturePass? pass) && pass.BuiltVersion != feature.Version)
                return true;
        }
        return false;
    }

    private static bool Same(List<RenderFeature> a, List<RenderFeature> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    public void AddBuffers(List<DeviceBuffer> buffers)
    {
        buffers.Add(Instances.Buffer);
        buffers.Add(Records.Buffer);
        buffers.Add(Commands.Buffer);
        buffers.Add(ViewBlocks.Buffer);
        buffers.Add(ViewLights.Buffer);
        buffers.Add(ViewLightNodes.Buffer);
        buffers.Add(ViewDecals.Buffer);
        buffers.Add(ViewDecalNodes.Buffer);
        buffers.Add(LightRegions.Buffer);
        buffers.Add(LightRegionNodes.Buffer);
        buffers.Add(DecalRegions.Buffer);
        buffers.Add(DecalRegionNodes.Buffer);
        buffers.Add(ProbeRegions.Buffer);
        buffers.Add(ProbeRegionNodes.Buffer);
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
                command.FirstInstance += (uint)instanceBase;
                _commands[commandBase + i] = command;
            }
            Array.Copy(list.Records, 0, _records, commandBase, list.CommandCount);
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
        Pipeline.Dispose();
        Chain.Dispose();
        foreach (object state in States.Values)
            (state as IDisposable)?.Dispose();
        ViewTable.Dispose();
        Instances.Dispose();
        Records.Dispose();
        Commands.Dispose();
        ViewBlocks.Dispose();
        ViewLights.Dispose();
        ViewLightNodes.Dispose();
        ViewDecals.Dispose();
        ViewDecalNodes.Dispose();
        LightRegions.Dispose();
        LightRegionNodes.Dispose();
        DecalRegions.Dispose();
        DecalRegionNodes.Dispose();
        ProbeRegions.Dispose();
        ProbeRegionNodes.Dispose();
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
        camera.ViewTable.Set("Graphite_ViewLights", 0, camera.ViewLights.Buffer);
        camera.ViewTable.Set("Graphite_ViewLightNodes", 0, camera.ViewLightNodes.Buffer);
        camera.ViewTable.Set("Graphite_ViewDecals", 0, camera.ViewDecals.Buffer);
        camera.ViewTable.Set("Graphite_ViewDecalNodes", 0, camera.ViewDecalNodes.Buffer);
        camera.ViewTable.Set("Graphite_LightRegions", 0, camera.LightRegions.Buffer);
        camera.ViewTable.Set("Graphite_LightRegionNodes", 0, camera.LightRegionNodes.Buffer);
        camera.ViewTable.Set("Graphite_DecalRegions", 0, camera.DecalRegions.Buffer);
        camera.ViewTable.Set("Graphite_DecalRegionNodes", 0, camera.DecalRegionNodes.Buffer);
        camera.ViewTable.Set("Graphite_ProbeRegions", 0, camera.ProbeRegions.Buffer);
        camera.ViewTable.Set("Graphite_ProbeRegionNodes", 0, camera.ProbeRegionNodes.Buffer);
        SetWorldBuffers(camera.ViewTable, camera.World.World);
        _viewTable = camera.ViewTable.Get(ring);
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);
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
        table.Set("Graphite_StaticLights", 0, lighting.StaticLights.Buffer);
        table.Set("Graphite_StaticLightNodes", 0, lighting.StaticLightNodes.Buffer);
        table.Set("Graphite_StaticDecals", 0, lighting.StaticDecals.Buffer);
        table.Set("Graphite_StaticDecalNodes", 0, lighting.StaticDecalNodes.Buffer);
        table.Set("Graphite_WorldLighting", 0, lighting.World.Buffer);
        table.Set("Graphite_ObjectProbes", 0, world.Probes.ObjectSh);
        table.Set("Graphite_ReflectionProbes", 0, world.Reflections.Regions.Records.Buffer);
        table.Set("Graphite_ReflectionProbeNodes", 0, world.Reflections.Regions.Nodes.Buffer);
        table.Set("Graphite_SkyAmbientSH", 0, world.Reflections.SkyAmbient);
        table.Set("Graphite_Directionals", 0, lighting.Directionals.Buffer);
    }

    // A fullscreen triangle at the far plane with the sky material's slot in the push constants
    public void DrawSky(CommandBuffer cmd)
    {
        WorldFrame world = camera.World;
        if (!camera.Desc.Sky || world.SkyProgram is not ushort id || world.World.Lighting.Environment.Sky is not { IsDisposed: false, Table: { } table } sky)
            return;
        GraphicsProgram? program = frame.Programs.Get(id, false, world.World.Globals.KeywordSnapshot);
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
        foreach (DrawCall call in list.Calls)
        {
            GraphicsProgram? program = frame.Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.World.World.Globals.KeywordSnapshot);
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
internal sealed class WorldFrame(RenderWorld world, BucketSet buckets, int globalRegion) : IDisposable
{
    private static int s_nextCapture;

    public RenderWorld World { get; } = world;
    public BucketSet Buckets { get; } = buckets;
    public int GlobalRegion { get; } = globalRegion;

    /// <summary>The draw program of the sky material's Sky pass, held while the world draws it.</summary>
    public ushort? SkyProgram;

    /// <summary>The views capturing the sky and the reflection probes: six faces at once or one, reused across frames.</summary>
    public CameraFrame? SkyCamera;
    public readonly List<CameraFrame> ProbeCameras = new();
    public readonly List<CameraFrame> ProbeFaceCameras = new();
    public int ProbeCamerasUsed, ProbeFaceCamerasUsed;

    /// <summary>The pipeline projecting the sky onto harmonics for the sky ambient mode.</summary>
    public RenderPipeline? SkyAmbient;
    public SkyAmbientPass? SkyAmbientPass;

    /// <summary>Filter pipelines, one per filling of a probe layer this frame, reused across frames.</summary>
    public readonly List<(RenderPipeline Pipeline, PrefilterPass Pass)> Filters = new();
    public int FiltersUsed;

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
        foreach ((RenderPipeline pipeline, _) in Filters)
            pipeline.Dispose();
        SkyAmbient?.Dispose();
    }
}

/// <summary>
/// The frame's pipelines: the FrameStart pipeline uploading every change, then one camera pipeline per view, all in
/// one execution. Graphs rebuild only when the set of persistent buffers changes.
/// </summary>
internal sealed class FramePipeline : IDisposable
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
    private readonly RenderPipeline _frameStart = new();
    private readonly UploadPass _upload;
    private readonly ProbePass _probes;
    private readonly Dictionary<int, CameraFrame> _cameras = new();
    private readonly Dictionary<(int Page, PrimitiveTopology Topology), VertexSource> _vertexSources = new();
    private readonly List<GraphDispatch> _dispatches = new();
    private readonly List<DeviceBuffer> _buffers = new();
    private readonly List<CameraFrame> _captures = new();
    private readonly List<Texture> _disposeAfterDispatch = new();
    private readonly List<IDisposable> _retired = new();
    private readonly RenderPipeline _readbackPipeline = new() { Name = "Graphite.Readback" };
    private readonly RenderPipeline _fencePipeline = new() { Name = "Graphite.Fence" };
    private readonly FencePass _fence = new();
    private readonly List<int> _fenceAt = new();
    private readonly ReadbackPass _readback;
    private readonly List<int> _sortedViews = new();
    private bool[] _placed = [];
    private readonly HashSet<RenderFeature> _prepared = new();
    private readonly List<int> _viewOrder = new();
    private readonly List<WorldFrame> _frameWorlds = new();
    private readonly IRenderView[] _frameStartViews = [new CameraView { ViewId = -1 }];
    private int _frame;

    private readonly RendererTextures _textures;
    private readonly bool _deterministic;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, IShaderCompiler compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, bool deterministic, Action<RendererError> report)
    {
        Report = report;
        _device = device;
        _textures = textures;
        _uploader = uploader;
        _deterministic = deterministic;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, deterministic, report);
        programs.Freed += Programs.Drop;
        _upload = new UploadPass(uploader);
        _probes = new ProbePass(InternalShaders.Compute(device, compiler, "Internal/Probes.glsl"));
        _frameStart.Name = "Graphite.FrameStart";
        _frameStart.SetPasses([_upload, _probes]);
        _readback = new ReadbackPass(device, report);
        _readbackPipeline.SetPasses([_readback]);

        PresentProgram = FullscreenProgram.Create(device, compiler, "void main() { outColor = Graphite_SampleSource(vUV); }", "Graphite/Present");
        ClearProgram = FullscreenProgram.Create(device, compiler, "uniform vec4 Graphite_ClearColor; void main() { outColor = Graphite_ClearColor; }", "Graphite/Clear", source: false);
        BlendAttachmentDescription untouched = BlendAttachmentDescription.OverrideBlend with { ColorWriteMask = ColorWriteMask.None };
        CopyBackProgram = FullscreenProgram.Create(device, compiler, "void main() { outColor = Graphite_SampleSource(vUV); }", "Graphite/CopyBack",
            blend: new BlendStateDescription(default, BlendAttachmentDescription.OverrideBlend, untouched, untouched));
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

    public GeometryPool Geometry { get; }
    public MaterialSystem Materials { get; }
    public GlobalRegistry Globals { get; }
    public DrawPrograms DrawPrograms { get; }
    public ProgramCache Programs { get; }
    public FullscreenProgram PresentProgram { get; }

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

    private static GraphicsProgram Fullscreen(GraphicsDevice device, IShaderCompiler compiler, string file)
    {
        ShaderPass pass = new() { State = new PassState(), Source = ShaderLibrary.Get(ShaderLibrary.Prefix + file)!, SourceName = "Graphite/" + file };
        ShaderDescription description = compiler.Compile(pass, [], device.BackendType);
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
    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, ReadOnlySpan<RenderFeature[]> viewFeatures, GraphDispatch[] pipelines, List<ReadbackRequest> readbacks, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds, ulong lastDispatched, in FrameTime time)
    {
        _frame++;
        _dispatches.Clear();
        _dispatches.Add(new GraphDispatch(_frameStart, _frameStartViews));
        _fenceAt.Clear();
        _fenceAt.Add(_dispatches.Count);
        _frameWorlds.Clear();
        _prepared.Clear();
        foreach (WorldFrame world in allWorlds)
            PrepareFeatures(world.World.FeatureSnapshot);
        foreach (RenderFeature[] features in viewFeatures)
            PrepareFeatures(features);

        foreach (WorldFrame world in allWorlds)
        {
            _frameWorlds.Add(world);
            world.FiltersUsed = 0;
            world.ProbeCamerasUsed = 0;
            world.ProbeFaceCamerasUsed = 0;
            UpdateSkyProgram(world);
            WorldReflections reflections = world.World.Reflections;
            if (reflections.CaptureDue)
                CaptureSky(world, time);
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

        ResolvePrograms();
        if (_deterministic && Globals.NeedsGrowth)
        {
            Globals.Extract();
            Globals.Flush(_uploader, lastDispatched);
            ResolvePrograms();
        }

        CollectBuffers(allWorlds);
        _fence.Update(Buffers, Active(), _frame, _fencePipeline);
        GraphDispatch fence = new(_fencePipeline, _frameStartViews);
        for (int i = _fenceAt.Count - 1; i >= 0; i--)
            _dispatches.Insert(_fenceAt[i], fence);
        if (pipelines.Length > 0)
        {
            _dispatches.Add(fence);
            _dispatches.AddRange(pipelines);
        }
        if (readbacks.Count > 0)
        {
            _readback.Prepare(readbacks);
            _readbackPipeline.SetPasses([_readback]);
            _dispatches.Add(new GraphDispatch(_readbackPipeline, _frameStartViews));
        }
        ExecutionTask task = _device.DispatchGraph(CollectionsMarshal.AsSpan(_dispatches));
        foreach (Texture texture in _disposeAfterDispatch)
            texture.Dispose();
        _disposeAfterDispatch.Clear();
        foreach (IDisposable retired in _retired)
            retired.Dispose();
        _retired.Clear();
        ReleaseStaleCameras();
        ReleaseIdleCaptures();
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
        camera.World = world;
        if (camera.HostBuffers != world.World.Globals.BufferList)
        {
            camera.HostBuffers = world.World.Globals.BufferList;
            camera.Rebuild = true;
        }
        uint width = Math.Max(1u, (uint)MathF.Round(view.Target.Width * view.Viewport.Z * view.RenderScale));
        uint height = Math.Max(1u, (uint)MathF.Round(view.Target.Height * view.Viewport.W * view.RenderScale));
        if (!view.External && camera.Chain.Ensure(width, height, view.LayerCount))
            camera.Rebuild = true;
        camera.Info = new FeatureView(view, width, height);
        if (!view.External)
        {
            camera.SelectFeatures(world.World.FeatureSnapshot, features, Report);
            camera.ApplyFeatures();
        }
        if (view.External)
        {
            if (camera.Chain.Scene.Texture != null)
            {
                camera.Chain.Release();
                camera.Rebuild = true;
            }
            camera.ResetPassLists();
            foreach (PassType pass in view.Passes ?? [])
                camera.ListFor(pass);
        }
        Prepare(camera, view, world, time, width, height);
        camera.View.Target = view.Target;
        camera.View.PixelWidth = width;
        camera.View.PixelHeight = height;
        if (!view.External)
            _dispatches.Add(new GraphDispatch(camera.Pipeline, camera.Views));
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

    // Draws only the sky into the six faces of the world's capture cube, then filters it into the sky probe's mips
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
        Draw(world.SkyCamera, capture, [], world, time);
        Fill(world, reflections.Sky!, scratch.Cube, null);

        if (world.SkyAmbient == null)
        {
            world.SkyAmbientPass = new SkyAmbientPass(SkyAmbientProgram, TrilinearClamp);
            world.SkyAmbient = new RenderPipeline { Name = "Graphite.SkyAmbient" };
            world.SkyAmbient.SetPasses([world.SkyAmbientPass]);
        }
        world.SkyAmbientPass!.Source = scratch.Cube;
        world.SkyAmbientPass.Output = reflections.SkyAmbient;
        _dispatches.Add(new GraphDispatch(world.SkyAmbient, _frameStartViews));
        _fenceAt.Add(_dispatches.Count);
    }

    // Filters a captured cube into every mip of a prefiltered one
    private void Fill(WorldFrame world, WorldReflections.Target target, Texture source, DeviceBuffer? readback)
    {
        if (world.FiltersUsed == world.Filters.Count)
        {
            PrefilterPass created = new(PrefilterProgram, TrilinearClamp, world.World.Reflections.PrefilterSamples);
            RenderPipeline pipeline = new() { Name = "Graphite.Prefilter" };
            pipeline.SetPasses([created]);
            world.Filters.Add((pipeline, created));
        }
        (RenderPipeline filter, PrefilterPass pass) = world.Filters[world.FiltersUsed++];
        WorldReflections reflections = world.World.Reflections;
        pass.Source = source;
        pass.Destination = target.Cube;
        pass.Readback = readback;
        _dispatches.Add(new GraphDispatch(filter, target.Views));
    }

    // Draws the world around a probe into its capture cube, all faces or the next one, then fills its layer
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
            CameraFrame camera = Pooled(world.ProbeCameras, ref world.ProbeCamerasUsed, scratch.Cube.Width);
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
            Draw(camera, capture, [], world, time);
        }
        else if (faces == 1)
        {
            CameraFrame camera = Pooled(world.ProbeFaceCameras, ref world.ProbeFaceCamerasUsed, scratch.Cube.Width);
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
            Draw(camera, capture, [], world, time);
        }

        if (work.Prefilter)
            Fill(world, target, scratch.Cube, work.Readback);
    }

    private CameraFrame Capturing()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    // The next capture camera of a pool this frame, one last drawing at this resolution if any so its textures stay
    private CameraFrame Pooled(List<CameraFrame> pool, ref int used, uint resolution)
    {
        int pick = -1;
        for (int i = used; i < pool.Count && pick < 0; i++)
        {
            if (pool[i].Chain.Scene.Texture?.Desc.Width == resolution)
                pick = i;
        }
        for (int i = used; i < pool.Count && pick < 0; i++)
        {
            if (pool[i].Chain.Scene.Texture == null)
                pick = i;
        }
        if (pick < 0)
        {
            pool.Add(Capturing());
            pick = pool.Count - 1;
        }
        (pool[used], pool[pick]) = (pool[pick], pool[used]);
        return pool[used++];
    }

    // Capture views keep their textures while drawing now and then, and let them go once unused for a while
    private void ReleaseIdleCaptures()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
            {
                if (_frame - camera.LastFrame > StaleViewFrames && camera.Chain.Scene.Texture != null)
                {
                    camera.Chain.Release();
                    camera.Rebuild = true;
                }
            }
        }
    }

    /// <summary>Releases what the frame pipeline keeps for a removed world. Render thread idle.</summary>
    public void ForgetWorld(WorldFrame world)
    {
        if (world.SkyProgram is ushort program)
            DrawPrograms.Release(program);
        world.SkyProgram = null;
        foreach (CameraFrame camera in world.Captures())
            _textures.Untrack(camera.ViewTable);
    }

    // Asks for every call's program before recording, so compiles start early and deterministic ones finish here.
    private void ResolvePrograms()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            if (world.SkyProgram is ushort sky)
                Programs.Get(sky, false, world.World.Globals.KeywordSnapshot);
        }
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach (DrawList list in camera.Lists)
            {
                foreach (DrawCall call in list.Calls)
                    Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.World.World.Globals.KeywordSnapshot);
            }
        }
    }

    private void Prepare(CameraFrame camera, in ViewDesc view, WorldFrame world, in FrameTime time, uint width, uint height)
    {
        int layers = view.LayerCount;
        CullFrustum frustum = new(view.Layers, layers);
        uint lightRegions = LightTreeBuilder.Invalid, decalRegions = LightTreeBuilder.Invalid, viewLights = LightTreeBuilder.Invalid;
        uint viewDecals = LightTreeBuilder.Invalid, probeRegions = LightTreeBuilder.Invalid;
        if (view.CullingMask == 0)
        {
            foreach (DrawList list in camera.Lists)
                list.Clear();
        }
        else
        {
            world.Buckets.Cull(frustum, view.CullingMask);
            world.Buckets.Build(0, frustum, camera.Prepass);
            world.Buckets.Build(1, frustum, camera.Forward);
            world.Buckets.BuildTransparent(frustum, camera.Transparent);
            foreach ((PassType type, DrawList list) in camera.PassLists)
                world.Buckets.Build(world.Buckets.Pass(type), frustum, list);

            WorldLighting lighting = world.World.Lighting;
            lightRegions = lighting.BuildViewLightRegions(frustum, camera.LightRegionTree, _uploader, camera.LightRegions, camera.LightRegionNodes);
            decalRegions = lighting.BuildViewDecalRegions(frustum, camera.DecalRegionTree, _uploader, camera.DecalRegions, camera.DecalRegionNodes);
            viewLights = lighting.BuildViewLights(frustum, camera.LightTree, _uploader, camera.ViewLights, camera.ViewLightNodes);
            viewDecals = lighting.BuildViewDecals(frustum, camera.DecalTree, _uploader, camera.ViewDecals, camera.ViewDecalNodes);
            probeRegions = world.World.Reflections.Regions.BuildView(frustum, camera.ProbeRegionTree, _uploader, camera.ProbeRegions, camera.ProbeRegionNodes);
        }
        ViewLayer first = view.Layers[0];
        float handedness = Determinant3(first.View) * first.Projection.c0.X * first.Projection.c1.Y;
        camera.Mirrored = handedness < 0;

        for (int l = 0; l < layers; l++)
        {
            ViewLayer layer = view.Layers[l];
            ViewBlock block = ViewBlock.Create(layer.View, layer.Projection, layer.Position, width, height, view.Near, view.FarDistance);
            float timeHigh = (float)time.Time;
            block.Time = new Float4(timeHigh, time.DeltaTime, time.FrameIndex, (float)(time.Time - timeHigh));
            Float3 offset = frustum.Relative(layer.Position);
            block.TreeOffset = new Float4(offset.X, offset.Y, offset.Z, 0);
            block.LightRegionRoot = lightRegions;
            block.DecalRegionRoot = decalRegions;
            block.ViewLightRoot = viewLights;
            block.ViewDecalRoot = viewDecals;
            block.ProbeRegionRoot = probeRegions;
            if (camera.HasPrevious)
            {
                block.PreviousViewProjection = camera.Previous[l].ViewProjection;
                block.PreviousCameraHigh = camera.Previous[l].CameraHigh;
                block.PreviousCameraLow = camera.Previous[l].CameraLow;
            }
            camera.Previous[l] = block;
            camera.Blocks[l] = block;
        }
        camera.HasPrevious = true;
        camera.Upload(_uploader, camera.Blocks.AsSpan(0, layers));
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
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            WorldProbes probes = world.World.Probes;
            _probes.Worlds.Add(probes);
            _buffers.Add(probes.ProbeRecords.Buffer);
            _buffers.Add(probes.TetRecords.Buffer);
            _buffers.Add(probes.Requests.Buffer);
            _buffers.Add(probes.ObjectSh);
            StaticRegions<ReflectionProbeRecord> reflections = world.World.Reflections.Regions;
            _buffers.Add(reflections.Records.Buffer);
            _buffers.Add(reflections.Nodes.Buffer);
            _buffers.Add(world.World.Reflections.SkyAmbient);

            foreach (GpuTable table in render.Tables)
                _buffers.Add(table.Buffer);
            WorldLighting lighting = world.World.Lighting;
            foreach (GpuTable table in lighting.Tables)
                _buffers.Add(table.Buffer);

        }
        foreach (CameraFrame camera in Active())
            camera.AddBuffers(_buffers);

        if (Same(Buffers, _buffers))
            return;

        Buffers = [.. _buffers];
        _upload.Buffers = Buffers;
        _probes.Buffers = Buffers;
        _frameStart.SetPasses([_upload, _probes]);
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
            _textures.Untrack(_cameras[id].ViewTable);
            _cameras[id].Dispose();
            _cameras.Remove(id);
        }
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in _cameras.Values)
            camera.Dispose();
        _cameras.Clear();
        _frameStart.Dispose();
        _readbackPipeline.Dispose();
        _fencePipeline.Dispose();
        _probes.Dispose();
        Programs.Dispose();
        PresentProgram.Dispose();
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

/// <summary>Copies the frame's readbacks into staging buffers, last in the execution.</summary>
internal sealed class ReadbackPass(GraphicsDevice device, Action<RendererError> report) : IPass
{
    private static readonly List<RenderResourceID> s_ids = new();
    private readonly List<ReadbackRequest> _requests = new();

    public string Name => "Graphite.Readback";

    // Staging buffers are made now, and requests whose source was disposed fail
    public void Prepare(List<ReadbackRequest> requests)
    {
        _requests.Clear();
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
            _requests.Add(request);
        }
    }

    public void Setup(RenderContextBuilder builder)
    {
        int id = 0;
        foreach (ReadbackRequest request in _requests)
        {
            if (request.Buffer is { } buffer)
                builder.DeclareImportedBuffer(Id(id++), buffer, BufferAccess.TransferRead);
        }
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        foreach (ReadbackRequest request in _requests)
        {
            if (request.Texture is { } texture)
                cmd.CopyTextureToBuffer(texture, request.Staging!, 0, request.Region);
            else
                cmd.CopyBuffer(request.Buffer!, request.Offset, request.Staging!, 0, request.Size);
        }
    }

    private static RenderResourceID Id(int index)
    {
        lock (s_ids)
        {
            while (s_ids.Count <= index)
                s_ids.Add(RenderResourceID.Intern("Graphite.Readback" + s_ids.Count));
            return s_ids[index];
        }
    }
}

/// <summary>
/// Reads every buffer draws may read, after the passes that write them and before the views and host pipelines, so their
/// first use in this graph waits for every earlier write. Views then import nothing and never rebuild when a buffer grows.
/// </summary>
internal sealed class FencePass : IPass
{
    private DeviceBuffer[] _buffers = [];
    private readonly List<DeviceBuffer> _wanted = new();

    public string Name => "Graphite.Fence";

    public void Update(DeviceBuffer[] frameBuffers, IEnumerable<CameraFrame> cameras, int frame, RenderPipeline pipeline)
    {
        _wanted.Clear();
        _wanted.AddRange(frameBuffers);
        foreach (CameraFrame camera in cameras)
        {
            if (camera.LastFrame != frame)
                continue;
            foreach (DeviceBuffer buffer in camera.HostBuffers)
            {
                if (!_wanted.Contains(buffer))
                    _wanted.Add(buffer);
            }
        }
        if (CollectionsMarshal.AsSpan(_wanted).SequenceEqual(_buffers))
            return;
        _buffers = [.. _wanted];
        pipeline.SetPasses([this]);
    }

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < _buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), _buffers[i], BufferAccess.AllReads);
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}
