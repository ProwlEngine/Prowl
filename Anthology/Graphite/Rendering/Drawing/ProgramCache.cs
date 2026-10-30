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
/// value, else with the matching pass of the shader's declared fallback. Compiles follow the policy, blocking in deterministic mode. Async
/// compiles queue at the priority they are asked for at. Only compiles run on workers, programs are created on the render thread.
/// Failures are reported once.
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
        public required CompilePriority Priority;
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

    private int _compiling, _background;

    /// <summary>Programs a draw waits on, not built yet and not failed. Any thread.</summary>
    public int Compiling => Volatile.Read(ref _compiling);

    /// <summary>Programs asked for ahead of use, like debug views, not built yet and not failed. Any thread.</summary>
    public int Background => Volatile.Read(ref _background);

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
    public GraphicsProgram? Get(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords, CompilePriority priority = CompilePriority.Visible)
    {
        Entry entry = Wanted(id, mirrored, keywords, priority);
        return entry.Built ?? Fallback(entry);
    }

    /// <summary>Starts compiling a variant without drawing with it. Render thread.</summary>
    public void Prewarm(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords, CompilePriority.Background);

    /// <summary>Carries on every unfinished variant, including ones no draw asks for anymore, so each one is built or fails. Render thread.</summary>
    public void Advance()
    {
        if (Compiling == 0)
            return;
        foreach (Entry entry in Every())
        {
            if (entry.Built == null && !entry.Failed)
                Complete(entry);
        }
    }

    private readonly Dictionary<GraphicsProgram, PropertyID[]> _userBuffers = new();

    /// <summary>The buffers a program reads that the host sets through Globals.SetBuffer.</summary>
    public PropertyID[] UserBuffers(GraphicsProgram program)
    {
        lock (_userBuffers)
            return _userBuffers.TryGetValue(program, out PropertyID[]? buffers) ? buffers : [];
    }

    /// <summary>Whether the wanted variant itself is built, rather than a fallback standing in. Render thread.</summary>
    public bool Ready(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords, CompilePriority.Visible).Built != null;

    /// <summary>Whether the wanted variant failed to compile, so it never becomes ready. Render thread.</summary>
    public bool Failed(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords) => Wanted(id, mirrored, keywords, CompilePriority.Visible).Failed;

    private Entry Wanted(ushort id, bool mirrored, IReadOnlyDictionary<string, string> keywords, CompilePriority priority)
    {
        DrawProgram program = programs[id];
        bool debug = DebugProgram(ref program, keywords);
        if (debug)
            priority = CompilePriority.Background;
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
            entry = Create(program, mirrored, globalValues, priority);
            _entries[(id, mirrored, debug, key)] = entry;
        }
        else if (priority < entry.Priority)
        {
            Raise(entry, priority);
        }
        Complete(entry);
        return entry;
    }

    // A Forward or Transmission pass draws through its shader's Debug pass while the view sets the debug axis
    private static bool DebugProgram(ref DrawProgram program, IReadOnlyDictionary<string, string> keywords)
    {
        if (!keywords.ContainsKey(Shader.DebugAxis))
            return false;
        Shader shader = program.Shader;
        int debug = shader.PassIndex(PassType.Debug);
        ShaderPass pass = shader.Definition.Passes![program.PassIndex];
        if (debug < 0 || !ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Forward.Name) && !ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Transmission.Name))
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

    private Entry Create(DrawProgram program, bool mirrored, ReadOnlySpan<int> globalValues, CompilePriority priority)
    {
        IReadOnlyList<VariantSpace> materialAxes = program.Shader.MaterialAxes;
        IReadOnlyList<VariantSpace> globalAxes = program.Shader.GlobalAxes;
        Keyword[] combo = new Keyword[materialAxes.Count + globalAxes.Count];
        for (int i = 0; i < materialAxes.Count; i++)
            combo[i] = program.Keywords[i];
        for (int i = 0; i < globalAxes.Count; i++)
            combo[materialAxes.Count + i] = new Keyword(globalAxes[i].Name, globalAxes[i].Values[globalValues[i]]);

        PassVariants variants = program.Shader.Definition.Passes![program.PassIndex].Variants(compiler, device.BackendType);
        Entry entry = new() { Program = program, Mirrored = mirrored, Variants = variants, Key = variants.KeyOf(combo), Revision = variants.Revision, Priority = priority };
        Family family = FamilyOf(entry);
        _families[family] = _families.GetValueOrDefault(family) + 1;
        Unfinished(entry, 1);
        return entry;
    }

    // Counts an entry not built yet as a draw waiting on it, or as background work
    private void Unfinished(Entry entry, int change) => Interlocked.Add(ref (entry.Priority >= CompilePriority.Background ? ref _background : ref _compiling), change);

    private void Raise(Entry entry, CompilePriority priority)
    {
        bool waiting = entry.Built == null && !entry.Failed;
        if (waiting)
            Unfinished(entry, -1);
        entry.Priority = priority;
        if (waiting)
            Unfinished(entry, 1);
    }

    // What stands in for a variant not built: the pass's default variant, else the declared fallback shader's pass of the same kind
    private GraphicsProgram? Fallback(Entry wanted)
    {
        Family family = FamilyOf(wanted);
        if (wanted.Key != 0)
        {
            if (!_defaults.TryGetValue(family, out Entry? fallback))
            {
                fallback = new Entry { Program = wanted.Program, Mirrored = wanted.Mirrored, Variants = wanted.Variants, Key = 0, Revision = wanted.Variants.Revision, Priority = CompilePriority.Fallback };
                Unfinished(fallback, 1);
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
            PassVariants variants = passes[i].Variants(compiler, device.BackendType);
            Entry entry = new() { Program = program, Mirrored = wanted.Mirrored, Variants = variants, Key = 0, Revision = variants.Revision, Priority = CompilePriority.Fallback };
            Unfinished(entry, 1);
            return entry;
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
                Unfinished(entry, 1);
            }
            else if (entry.Failed)
            {
                entry.Failed = false;
                Unfinished(entry, 1);
            }
        }
        if (entry.Built != null || entry.Failed)
            return;
        VariantState state = entry.Variants.Request(entry.Key, _policy, entry.Priority, out ShaderDescription compiled, out Exception? error);
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
            Unfinished(entry, -1);
        }
        catch (Exception e)
        {
            Fail(entry, e);
        }
    }

    private void Fail(Entry entry, Exception e)
    {
        Unfinished(entry, -1);
        entry.Failed = true;
        DrawProgram program = entry.Program;
        report(new RendererError("shader.compile", $"{program.Shader.Name}: {e.Message}", RendererErrorSeverity.Error, program.Shader) { Key = $"program.{program.Shader.Name}.{program.PassIndex}.{entry.Key}" });
    }

    // An entry that never finished no longer counts as compiling
    private void Forget(Entry entry)
    {
        if (entry.Built == null && !entry.Failed)
            Unfinished(entry, -1);
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
    // other passes leave them. Transmissive passes write motion and the reactive mask over them, which nothing else
    // writes. The prepass writes no colour at all. Views with fewer targets ignore the extra states.
    internal static BlendStateDescription SceneBlend(PassState state, bool forward, bool prepass, Func<uint, bool> stores, bool transmission = false)
    {
        BlendAttachmentDescription color = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool gbuffer = WritesGBufferTargets(state, forward, prepass);
        BlendAttachmentDescription target = BlendAttachmentDescription.OverrideBlend;
        BlendAttachmentDescription masked = target with { ColorWriteMask = ColorWriteMask.None };
        if (prepass)
            color.ColorWriteMask = ColorWriteMask.None;

        BlendStateDescription blend = state.ToBlendState(BlendStateDescription.SingleOverrideBlend);
        blend.AttachmentStates = [color, (gbuffer || transmission) && stores(1) ? target : masked, transmission && stores(2) ? target : masked, gbuffer && stores(3) ? target : masked, gbuffer && stores(4) ? target : masked];
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
        bool transmission = ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Transmission.Name);
        ShaderDescription compiled = description;
        description.BlendState = SceneBlend(state, forward, prepass, location => MaterialLayout.Stores(compiled, location), transmission);
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
        // Back depth draws the far side alone whatever the material's culling and blending
        if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.BackDepth.Name))
        {
            description.BlendState = BlendStateDescription.SingleOverrideBlend;
            description.DepthStencilState = DepthStencilStateDescription.DepthOnlyGreaterEqual;
            raster.CullMode = FaceCullMode.Front;
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
