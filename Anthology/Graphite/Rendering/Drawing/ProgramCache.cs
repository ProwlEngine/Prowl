using System;
using System.Collections.Generic;
using System.Threading;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A view's global keywords, interned so draws look their programs up by a small id rather than by the keywords.
/// Sets with the same keywords are the same instance. Any thread.
/// </summary>
internal sealed class KeywordSet
{
    private static readonly Dictionary<string, KeywordSet> s_sets = new(StringComparer.Ordinal);

    private KeywordSet(int id, IReadOnlyDictionary<string, string> values)
    {
        Id = id;
        Values = values;
    }

    public int Id { get; }

    public IReadOnlyDictionary<string, string> Values { get; }

    public static KeywordSet Empty { get; } = Intern(new Dictionary<string, string>());

    public static KeywordSet Intern(IReadOnlyDictionary<string, string> values)
    {
        List<string> names = [.. values.Keys];
        names.Sort(StringComparer.Ordinal);
        System.Text.StringBuilder text = new();
        foreach (string name in names)
            text.Append(name).Append('=').Append(values[name]).Append('\n');
        string key = text.ToString();
        lock (s_sets)
        {
            if (!s_sets.TryGetValue(key, out KeywordSet? set))
                s_sets[key] = set = new KeywordSet(s_sets.Count, new Dictionary<string, string>(values, StringComparer.Ordinal));
            return set;
        }
    }
}

/// <summary>
/// Draw programs built from each pass's shared compiled variants: the global offsets, the renderer's layouts and the
/// pipeline state of the draw put on top. A variant not ready yet, or failed, draws with the pass's default variant, every axis at its first
/// value, else with the matching pass of the shader's declared fallback. Compiles follow the policy, blocking when Sync. Async
/// compiles queue at the priority they are asked for at. Only compiles run on workers, programs are created on the render thread.
/// Draws look programs up by draw program id and keyword set in a flat array; draws of the same variant and state share one
/// built program. Failures are reported once.
/// </summary>
internal sealed class ProgramCache(GraphicsDevice device, IShaderCompiler? compiler, GlobalRegistry globals, DrawPrograms programs, Action<RendererError> report, CompilePolicy policy = CompilePolicy.Async, Action<IDisposable>? retire = null) : IDisposable
{
    private readonly record struct BuildKey(PassVariants Variants, int Key, PassState? StateOverride, bool Mirrored);

    private sealed class Entry
    {
        public required BuildKey Id;
        public required DrawProgram Program;
        public required int Revision;
        public required CompilePriority Priority;
        public int References;
        public GraphicsProgram? Built;
        public bool Failed;
        public bool StandInsFound;
        public Entry? Default;
        public Entry? Declared;

        public PassVariants Variants => Id.Variants;
        public int Key => Id.Key;
        public bool Mirrored => Id.Mirrored;
        public bool Waiting => Built == null && !Failed;
    }

    private readonly record struct Use(bool Mirrored, int Keywords, Entry Entry);

    private readonly CompilePolicy _policy = compiler == null ? CompilePolicy.BakedOnly : policy;
    private readonly Dictionary<BuildKey, Entry> _entries = new();
    private List<Use>?[] _uses = new List<Use>?[256];

    /// <summary>The hardware constant and slope offsets shadow casters draw with.</summary>
    public (float Constant, float Slope) ShadowBias { get; set; } = (0, 1.5f);

    public static readonly ResourceLayoutDescription FrameLayout = GraphiteBindings.FrameTable(RendererTextures.Textures2D, RendererTextures.OtherTextures);
    public static readonly ResourceLayoutDescription ViewLayout = GraphiteBindings.ViewTable(RendererTextures.Samplers);

    /// <summary>Lets go of every program a freed draw program id used, before the id is reused for another program.</summary>
    public void Drop(ushort id)
    {
        if (id >= _uses.Length || _uses[id] is not { } uses)
            return;
        foreach (Use use in uses)
            Release(use.Entry);
        uses.Clear();
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
                if (entry.Waiting)
                    pending++;
            }
            return pending;
        }
    }

    /// <summary>The program to draw with now: the wanted variant when it is built, else a fallback, else null. Render thread.</summary>
    public GraphicsProgram? Get(ushort id, bool mirrored, KeywordSet keywords, CompilePriority priority = CompilePriority.Visible)
    {
        Entry entry = Wanted(id, mirrored, keywords, priority);
        return entry.Built ?? Fallback(entry);
    }

    /// <summary>
    /// Takes in finished compiles and source changes for every program, including ones no draw asks for anymore, so each
    /// one is built or fails. Once per frame, render thread.
    /// </summary>
    public void Advance()
    {
        foreach (Entry entry in _entries.Values)
        {
            if (entry.Waiting || entry.Variants.Revision != entry.Revision)
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
    public bool Ready(ushort id, bool mirrored, KeywordSet keywords) => Wanted(id, mirrored, keywords, CompilePriority.Visible).Built != null;

    /// <summary>Whether the wanted variant failed to compile, so it never becomes ready. Render thread.</summary>
    public bool Failed(ushort id, bool mirrored, KeywordSet keywords) => Wanted(id, mirrored, keywords, CompilePriority.Visible).Failed;

    private Entry Wanted(ushort id, bool mirrored, KeywordSet keywords, CompilePriority priority)
    {
        if (id >= _uses.Length)
            Array.Resize(ref _uses, Math.Max(id + 1, _uses.Length * 2));
        List<Use> uses = _uses[id] ??= new List<Use>(2);
        foreach (Use use in uses)
        {
            if (use.Mirrored != mirrored || use.Keywords != keywords.Id)
                continue;
            Entry found = use.Entry;
            if (priority < found.Priority)
                Raise(found, priority);
            if (found.Waiting)
                Complete(found);
            return found;
        }

        Entry entry = Acquire(programs[id], mirrored, keywords.Values, priority);
        uses.Add(new Use(mirrored, keywords.Id, entry));
        return entry;
    }

    // The shared entry of a draw program under global keywords, made when no draw used it yet
    private Entry Acquire(DrawProgram program, bool mirrored, IReadOnlyDictionary<string, string> keywords, CompilePriority priority)
    {
        if (DebugProgram(ref program, keywords))
            priority = CompilePriority.Background;
        IReadOnlyList<VariantSpace> materialAxes = program.Shader.MaterialAxes;
        IReadOnlyList<VariantSpace> globalAxes = program.Shader.GlobalAxes;
        Keyword[] combo = new Keyword[materialAxes.Count + globalAxes.Count];
        for (int i = 0; i < materialAxes.Count; i++)
            combo[i] = program.Keywords[i];
        for (int i = 0; i < globalAxes.Count; i++)
            combo[materialAxes.Count + i] = new Keyword(globalAxes[i].Name, keywords.GetValueOrDefault(globalAxes[i].Name) ?? "");

        PassVariants variants = program.Shader.Definition.Passes![program.PassIndex].Variants(compiler, device.BackendType);
        return Acquire(new BuildKey(variants, variants.KeyOf(combo), program.StateOverride, mirrored), program, priority);
    }

    private Entry Acquire(BuildKey id, DrawProgram program, CompilePriority priority)
    {
        if (!_entries.TryGetValue(id, out Entry? entry))
        {
            entry = new Entry { Id = id, Program = program, Revision = id.Variants.Revision, Priority = priority };
            _entries[id] = entry;
            Unfinished(entry, 1);
        }
        else if (priority < entry.Priority)
        {
            Raise(entry, priority);
        }
        entry.References++;
        if (entry.Waiting)
            Complete(entry);
        return entry;
    }

    private void Release(Entry entry)
    {
        if (--entry.References > 0)
            return;
        _entries.Remove(entry.Id);
        if (entry.Waiting)
            Unfinished(entry, -1);
        if (entry.Built != null)
            Retire(entry.Built);
        if (entry.Default != null)
            Release(entry.Default);
        if (entry.Declared != null)
            Release(entry.Declared);
    }

    // A Forward or Transmission pass draws through its shader's Debug pass while the view sets the debug axis
    private static bool DebugProgram(ref DrawProgram program, IReadOnlyDictionary<string, string> keywords)
    {
        if (!keywords.ContainsKey(GlobalAxis.Debug.Name))
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
        foreach (Entry entry in _entries.Values)
        {
            variants.Add(entry.Variants);
            Discard(entry.Built);
        }
        foreach (PassVariants pass in variants)
            pass.WaitPending();
        _entries.Clear();
        Array.Clear(_uses);
    }

    // Counts an entry not built yet as a draw waiting on it, or as background work
    private void Unfinished(Entry entry, int change) => Interlocked.Add(ref (entry.Priority >= CompilePriority.Background ? ref _background : ref _compiling), change);

    private void Raise(Entry entry, CompilePriority priority)
    {
        if (entry.Waiting)
            Unfinished(entry, -1);
        entry.Priority = priority;
        if (entry.Waiting)
            Unfinished(entry, 1);
    }

    // What stands in for a variant not built: the pass's default variant, else the declared fallback shader's pass of the same kind
    private GraphicsProgram? Fallback(Entry wanted)
    {
        if (!wanted.StandInsFound)
        {
            wanted.StandInsFound = true;
            if (wanted.Key != 0)
                wanted.Default = Acquire(wanted.Id with { Key = 0 }, wanted.Program, CompilePriority.Fallback);
            wanted.Declared = Declared(wanted);
        }
        if (wanted.Default is { } fallback)
        {
            if (fallback.Waiting)
                Complete(fallback);
            if (fallback.Built != null)
                return fallback.Built;
        }
        if (wanted.Declared is not { } declared)
            return null;
        if (declared.Waiting)
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
            return Acquire(new BuildKey(variants, 0, wanted.Program.StateOverride, wanted.Mirrored), program, CompilePriority.Fallback);
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
        if (!entry.Waiting)
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
