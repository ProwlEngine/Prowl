using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Where in a view's frame a feature runs, in this order. Spec 6.3 lists what each holds.</summary>
public enum RenderSlot : byte
{
    Clear,
    Setup,
    Culling,
    Shadows,
    Prepass,
    AfterPrepass,
    Opaque,
    Sky,
    OpaqueColor,
    AfterOpaques,
    Atmosphere,
    Transparent,
    AfterTransparents,
    Upscale,
    HdrPost,
    Tonemap,
    DisplayPost,
    Ui,
    Overlay,
    Present,
}

public enum ViewKind : byte
{
    Game,
    SceneView,
    Preview,
    RenderTexture,

    /// <summary>A reflection probe capture.</summary>
    ReflectionProbe,

    /// <summary>The sky probe capture, the sky alone.</summary>
    SkyCapture,
    Shadow,
    ObjectId,
}

[Flags]
public enum ViewKinds : ushort
{
    None = 0,
    Game = 1 << ViewKind.Game,
    SceneView = 1 << ViewKind.SceneView,
    Preview = 1 << ViewKind.Preview,
    RenderTexture = 1 << ViewKind.RenderTexture,
    ReflectionProbe = 1 << ViewKind.ReflectionProbe,
    SkyCapture = 1 << ViewKind.SkyCapture,
    Shadow = 1 << ViewKind.Shadow,
    ObjectId = 1 << ViewKind.ObjectId,

    /// <summary>Views people look at.</summary>
    Cameras = Game | SceneView | Preview | RenderTexture,

    /// <summary>The probe and sky captures.</summary>
    Captures = ReflectionProbe | SkyCapture,
    All = 0xFFFF,
}

/// <summary>What a feature knows about the view it runs on.</summary>
public readonly struct FeatureView
{
    internal FeatureView(in ViewDesc desc, uint width, uint height)
    {
        World = desc.World;
        Kind = desc.Kind;
        RenderView = desc.Owner;
        LayerCount = desc.LayerCount;
        Width = width;
        Height = height;
        RenderScale = desc.RenderScale;
        Position = desc.Layers[0].Position;
        OutputWidth = desc.Target is { } target ? Math.Max(1u, (uint)MathF.Round(target.Width * desc.Viewport.Z)) : width;
        OutputHeight = desc.Target is { } output ? Math.Max(1u, (uint)MathF.Round(output.Height * desc.Viewport.W)) : height;
        Viewport = desc.Viewport;
        ClearColor = desc.ClearColor;
        ClearTarget = desc.ClearTarget;
        Overlay = desc.Overlay;
    }

    /// <summary>The view as features after an upscale see it, its colour at the output resolution.</summary>
    internal FeatureView AtOutput() => this with { Width = OutputWidth, Height = OutputHeight, RenderScale = 1, Upscaled = true };

    /// <summary>The view once it is culled, with what its draws read this frame.</summary>
    internal FeatureView Culled(bool sceneColor, bool sceneDepth, bool backDepth, bool cascades)
        => this with { ReadsSceneColor = sceneColor, ReadsSceneDepth = sceneDepth, ReadsBackDepth = backDepth, HasCascades = cascades };

    public RenderWorld World { get; }
    public ViewKind Kind { get; }

    /// <summary>The host's view, null on the renderer's own capture and shadow views.</summary>
    public RenderView? RenderView { get; }

    /// <summary>1 for mono, 2 for stereo, 6 for a cube capture. Textures of layered views are arrays drawn through multiview.</summary>
    public int LayerCount { get; }

    /// <summary>
    /// The resolution the colour is at: the render resolution, the target's size within the viewport times the render
    /// scale, until an upscaler wrote the output resolution.
    /// </summary>
    public uint Width { get; private init; }
    public uint Height { get; private init; }
    public float RenderScale { get; private init; }

    /// <summary>The target's size within the viewport, what an upscaler writes and the present draws to.</summary>
    public uint OutputWidth { get; }
    public uint OutputHeight { get; }

    /// <summary>Whether an earlier feature upscaled the colour to the output resolution, which Width and Height then are.</summary>
    public bool Upscaled { get; private init; }

    /// <summary>The first layer's camera position.</summary>
    public Double3 Position { get; }

    /// <summary>The part of the target the view presents into, as x, y, width and height from 0 to 1.</summary>
    public Float4 Viewport { get; }

    /// <summary>The colour the view clears its scene and, when <see cref="ClearTarget"/>, its target to.</summary>
    public Float4 ClearColor { get; }

    /// <summary>Whether the present clears the whole target before it draws the viewport.</summary>
    public bool ClearTarget { get; }

    /// <summary>Whether the view blends over what its target holds, its scene cleared to transparent black.</summary>
    public bool Overlay { get; }

    /// <summary>Whether the view's transparent renderables sample its scene colour this frame. Known from HasWork on, not in IsActive.</summary>
    public bool ReadsSceneColor { get; private init; }

    /// <summary>Whether the view's transparent renderables fetch its scene depth this frame. Known from HasWork on, not in IsActive.</summary>
    public bool ReadsSceneDepth { get; private init; }

    /// <summary>Whether the view's transmissive renderables read its back depth this frame. Known from HasWork on, not in IsActive.</summary>
    public bool ReadsBackDepth { get; private init; }

    /// <summary>Whether the view has directional cascades this frame. Known from HasWork on, not in IsActive.</summary>
    public bool HasCascades { get; private init; }

    /// <summary>A texture sized as a part of the colour's resolution, an array on layered views.</summary>
    public GraphTextureDesc Texture(PixelFormat format, float scale = 1)
        => SharedTexture(format, scale) with { ArrayLayers = LayerCount > 1 ? (uint)LayerCount : 0 };

    /// <summary>A texture of one layer whatever the view, sized by a scale like <see cref="Texture"/>, for state the view's layers share.</summary>
    public GraphTextureDesc SharedTexture(PixelFormat format, float scale = 1)
        => GraphTextureDesc.Sized(Math.Max(1, (int)(Width * scale)), Math.Max(1, (int)(Height * scale)), format);

    /// <summary>A texture of one layer whatever the view, for state the view's layers share.</summary>
    public static GraphTextureDesc Shared(int width, int height, PixelFormat format) => GraphTextureDesc.Sized(width, height, false, format);
}

/// <summary>
/// Scene targets past colour and depth that a feature reads. A view allocates the ones its features read: motion as the
/// second target, then the reactive mask, then normals and material together, each also bringing the ones before it.
/// </summary>
[Flags]
public enum SceneTargets
{
    None = 0,
    Motion = 1,
    Normals = 2,
    Material = 4,

    /// <summary>
    /// How much of each pixel transmissive surfaces drew from what is behind them, 0 to 1, see
    /// <see cref="FeatureContext.Reactive"/>. A view has it only on frames it draws a transmissive surface, else null.
    /// </summary>
    Reactive = 8,
}

/// <summary>
/// A group of features of which one runs. Of view features sharing a group the view's wins over the world's, which wins
/// over the built in one, so a feature replaces a built in one by taking its group. Of frame features sharing a group in
/// one set the last added runs, and any replaces the built in one.
/// </summary>
public sealed class FeatureGroup(string name)
{
    public string Name { get; } = name;

    public override string ToString() => Name;

    /// <summary>The built in view features' groups.</summary>
    public static readonly FeatureGroup SceneClear = new("SceneClear"), Cascades = new("Cascades"), Prepass = new("Prepass"),
        Opaque = new("Opaque"), Sky = new("Sky"), SceneColor = new("SceneColor"), SceneDepth = new("SceneDepth"),
        BackDepth = new("BackDepth"), Transparent = new("Transparent"), Present = new("Present");

    /// <summary>The stock effects' groups, of which a view runs one each.</summary>
    public static readonly FeatureGroup ScreenLighting = new("ScreenLighting"), Tonemap = new("Tonemap"), AntiAliasing = new("AntiAliasing");

    /// <summary>The built in frame features' groups.</summary>
    public static readonly FeatureGroup Deform = new("Deform"), LightProbes = new("LightProbes"), LocalShadows = new("LocalShadows"),
        CaptureFill = new("CaptureFill"), Readback = new("Readback");
}

/// <summary>
/// What a host adds to a world, a view or the renderer: a <see cref="RenderFeature"/>, a <see cref="FrameFeature"/> or a
/// <see cref="RenderProvider"/>. What it is is fixed when it is made, Enabled and its settings are copied every SubmitFrame.
/// </summary>
public abstract class RenderExtension
{
    public bool Enabled { get; set; } = true;

    /// <summary>Copies settings the render thread reads. Host thread, at SubmitFrame, while enabled.</summary>
    public virtual void Extract() { }

    internal bool EnabledSnapshot;

    /// <summary>Copies what the render thread reads of the extension itself. Host thread, at SubmitFrame.</summary>
    internal virtual void Snapshot() => EnabledSnapshot = Enabled;

    /// <summary>Called as the extension joins or leaves a set, for extensions that belong to one.</summary>
    internal virtual void Attach(object set) { }

    internal virtual void Detach(object set) { }
}

/// <summary>Extensions added to a world, a view or the renderer. Host thread.</summary>
public sealed class ExtensionSet<T> : IEnumerable<T> where T : RenderExtension
{
    private readonly List<T> _items = new();
    private T[] _snapshot = [];
    private int _version, _snapshotVersion = -1;

    public ExtensionSet() { }

    internal ExtensionSet(RenderWorld? world) => World = world;

    /// <summary>The world the set belongs to, null for a view's or the renderer's.</summary>
    internal RenderWorld? World { get; }

    public int Count => _items.Count;

    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.Contains(item))
            return;
        item.Attach(this);
        _items.Add(item);
        _version++;
    }

    public bool Remove(T item)
    {
        if (!_items.Remove(item))
            return false;
        item.Detach(this);
        _version++;
        return true;
    }

    public bool Contains(T item) => _items.Contains(item);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The extensions as of this call, each snapshot, the same array while the set does not change. Host thread.</summary>
    internal T[] Snapshot()
    {
        if (_snapshotVersion != _version)
        {
            _snapshot = [.. _items];
            _snapshotVersion = _version;
        }
        foreach (T item in _snapshot)
            item.Snapshot();
        return _snapshot;
    }
}

/// <summary>
/// Work a host adds to views: a pass in a slot of every view of a world, or of one view. Its slot, group, view kinds,
/// scene reads and drawn pass types are fixed when it is made. Enabled, Order and the jitter it asks for are read at
/// SubmitFrame along with <see cref="RenderExtension.Extract"/> on the host thread, everything else runs on the render
/// thread. A feature that throws fails the frame.
/// </summary>
public abstract class RenderFeature : RenderExtension
{
    private static int s_nextId;
    private readonly PassType[] _passes;

    /// <summary>What its pass is called in the frame graph, the debugger and the profiler, its class name by default.</summary>
    public virtual string Name => GetType().Name;

    /// <param name="slot">Where in the view's frame it runs.</param>
    /// <param name="order">Its order inside the slot, built in features at 0.</param>
    /// <param name="group">The group it replaces the other features of, see <see cref="FeatureGroup"/>.</param>
    /// <param name="runsOn">The kinds of view it runs on.</param>
    /// <param name="sceneReads">The scene targets past colour and depth it reads, so its views have them.</param>
    /// <param name="passes">The pass types it draws through <see cref="FeatureContext.DrawPass"/>.</param>
    protected RenderFeature(RenderSlot slot, int order = 0, FeatureGroup? group = null, ViewKinds runsOn = ViewKinds.Cameras, SceneTargets sceneReads = SceneTargets.None, PassType[]? passes = null)
    {
        Slot = slot;
        Order = order;
        Group = group;
        RunsOn = runsOn;
        SceneReads = sceneReads;
        _passes = passes is { Length: > 0 } ? [.. passes] : [];
    }

    internal int Id { get; } = Interlocked.Increment(ref s_nextId);

    public RenderSlot Slot { get; }

    /// <summary>Order inside the slot, built in features at 0.</summary>
    public int Order { get; set; }

    public FeatureGroup? Group { get; }

    public ViewKinds RunsOn { get; }

    /// <summary>The scene targets past colour and depth the feature reads, so its views have them.</summary>
    public SceneTargets SceneReads { get; }

    /// <summary>
    /// The pass types the feature draws through <see cref="FeatureContext.DrawPass"/>, the shader passes tagged
    /// "LightMode" = type. Every view it runs on buckets the visible renderables of each before it is culled.
    /// </summary>
    public IReadOnlyList<PassType> Passes => _passes;

    internal PassType[] PassArray => _passes;

    /// <summary>
    /// Halton phases the view's projection jitters through while the feature runs, for temporal effects, 0 for none. The
    /// host's projection stays as it gave it; shaders read the jitter from the view block.
    /// </summary>
    public virtual int JitterPhases => 0;

    /// <summary>The texture LOD bias the view's samplers take while it jitters for this feature.</summary>
    public virtual float JitterMipBias => 0;

    /// <summary>Whether the feature measures the exposure its views take next frame, through <see cref="FeatureBuilder.WriteExposure"/>.</summary>
    internal virtual bool MeasuresExposure => false;

    /// <summary>Whether the feature runs on a view this frame, asked before the view is culled. Render thread.</summary>
    public virtual bool IsActive(in FeatureView view) => true;

    /// <summary>
    /// Whether the feature adds its pass to a view it runs on this frame, asked once the view is culled, so it can answer
    /// from what the view draws. Render thread.
    /// </summary>
    public virtual bool HasWork(in FeatureView view) => true;

    /// <summary>Once a frame before any view, for per frame work. Render thread.</summary>
    public virtual void PrepareFrame() { }

    /// <summary>Declares what the feature reads and writes, once for every view every frame. Render thread.</summary>
    public abstract void Setup(FeatureBuilder builder, in FeatureView view);

    /// <summary>Records the feature's work for a view. Render thread.</summary>
    public abstract void Render(FeatureContext context);

    internal int OrderSnapshot;
    internal int JitterPhasesSnapshot;
    internal float JitterMipBiasSnapshot;

    private FullscreenProgram[] _programs = [];

    /// <summary>Adds a fullscreen program the feature draws with, so it is left out of views until the program is ready.</summary>
    internal void Uses(FullscreenProgram program)
    {
        if (Array.IndexOf(_programs, program) < 0)
            _programs = [.. _programs, program];
    }

    /// <summary>Whether every fullscreen program the feature draws with is ready for a view of this many layers, asking for the ones that are not. Render thread.</summary>
    internal bool ProgramsReady(int layers)
    {
        bool ready = true;
        foreach (FullscreenProgram program in _programs)
            ready &= program.Ready(layers);
        return ready;
    }

    internal override void Snapshot()
    {
        base.Snapshot();
        OrderSnapshot = Order;
        JitterPhasesSnapshot = Math.Max(JitterPhases, 0);
        JitterMipBiasSnapshot = JitterMipBias;
    }
}

/// <summary>
/// A producer of the view's screen lighting, in AfterPrepass after the depth prepass: the visibility, and optionally the
/// indirect irradiance with its confidence, that every renderable drawn after reads through Graphite_IndirectDiffuse and
/// Graphite_IndirectSpecular. Direct light is never touched. A view runs one, as they share the ScreenLighting group.
/// Normals are not drawn yet, so producers rebuild them from depth.
/// </summary>
public abstract class ScreenLightingFeature : RenderFeature
{
    protected ScreenLightingFeature(int order = 0, ViewKinds runsOn = ViewKinds.Cameras)
        : base(RenderSlot.AfterPrepass, order, FeatureGroup.ScreenLighting, runsOn) { }

    /// <summary>The part of the view's render resolution the screen lighting takes, 1/16 to 1.</summary>
    public abstract float Scale { get; }

    /// <summary>Whether the feature writes indirect irradiance as well as visibility.</summary>
    public virtual bool WritesIrradiance => false;

    internal float ScaleSnapshot = 1;
    internal bool IrradianceSnapshot;

    internal override void Snapshot()
    {
        base.Snapshot();
        ScaleSnapshot = Math.Clamp(Scale, 0.0625f, 1f);
        IrradianceSnapshot = WritesIrradiance;
    }
}

/// <summary>
/// A fullscreen effect made by <see cref="Renderer.CreateFullscreenProgram"/>, built for mono views and for layered views
/// through multiview. Under <see cref="CompilePolicy.Async"/> each form compiles on the compile queue the first time it is
/// wanted and is built on the render thread once compiled. A feature drawing one that is not ready yet is left out of
/// its views until it is, so their colour passes through it unchanged.
/// </summary>
public sealed class FullscreenProgram : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly PassVariants _variants;
    private readonly int[] _keys;
    private readonly GraphicsProgram?[] _built = new GraphicsProgram?[2];
    private readonly BlendStateDescription _blend;
    private readonly string _name;
    private readonly CompilePolicy _policy;
    private readonly CompilePriority _priority;
    private readonly Action<RendererError>? _report;
    private bool _failed;

    private FullscreenProgram(GraphicsDevice device, PassVariants variants, int[] keys, BlendStateDescription blend, string name, bool source, bool lit, bool procedural, CompilePolicy policy, CompilePriority priority, Action<RendererError>? report)
    {
        _device = device;
        _variants = variants;
        _keys = keys;
        _blend = blend;
        _name = name;
        HasSource = source;
        Lit = lit;
        Procedural = procedural;
        _policy = policy;
        _priority = priority;
        _report = report;
    }

    /// <summary>Whether the program reads Graphite_Source.</summary>
    internal bool HasSource { get; }

    /// <summary>Whether the program reads the world's lights, shadows and textures through Graphite's library.</summary>
    public bool Lit { get; }

    /// <summary>Whether the program writes its own vertex stage, drawn through <see cref="FeatureContext.DrawProcedural"/>.</summary>
    public bool Procedural { get; }

    /// <summary>The program to draw over a view of this many layers, for features that bind it themselves, compiled now when it is not ready. Render thread.</summary>
    public GraphicsProgram For(int layers)
    {
        int form = layers > 1 ? 1 : 0;
        return _built[form] ??= Build(_variants.Require(_keys[form]), form);
    }

    /// <summary>
    /// Whether the form for a view of this many layers is built, asking for its compile at the program's priority when
    /// it is not. A failed compile is reported once and never becomes ready. Render thread.
    /// </summary>
    internal bool Ready(int layers)
    {
        int form = layers > 1 ? 1 : 0;
        if (_built[form] != null)
            return true;
        if (_failed)
            return false;
        VariantState state = _variants.Request(_keys[form], _policy, _priority, out ShaderDescription description, out Exception? error);
        try
        {
            if (state == VariantState.Ready)
                _built[form] = Build(description, form);
        }
        catch (Exception e)
        {
            error = e;
        }
        if (state == VariantState.Failed || error != null)
        {
            _failed = true;
            _report?.Invoke(new RendererError("shader.compile", $"{_name}: {error?.Message}", RendererErrorSeverity.Error, this) { Key = $"fullscreen.{_name}" });
        }
        return _built[form] != null;
    }

    public void Dispose()
    {
        foreach (GraphicsProgram? program in _built)
            program?.Dispose();
        Array.Clear(_built);
    }

    private GraphicsProgram Build(ShaderDescription description, int form)
    {
        description.BlendState = _blend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        if (Lit)
            description.ResourceLayouts = LitLayouts(description.ResourceLayouts ?? []);
        GraphicsProgram program = _device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = form == 1 ? _name + " layered" : _name;
        return program;
    }

    // The renderer's frame and view tables whole, so the camera's bind as they are, and the program's own resources in the user set
    private static ResourceLayoutDescription[] LitLayouts(ResourceLayoutDescription[] compiled)
    {
        List<ResourceLayoutElementDescription> user = new();
        foreach (ResourceLayoutDescription layout in compiled)
        {
            if (layout.Set == GraphiteBindings.UserSet)
                user.AddRange(layout.Elements);
        }
        return [ProgramCache.FrameLayout, ProgramCache.ViewLayout, new ResourceLayoutDescription(GraphiteBindings.PassSet, []), new ResourceLayoutDescription(GraphiteBindings.UserSet, [.. user])];
    }

    /// <summary>
    /// The pass a fullscreen program compiles from. The fragment code follows Graphite/Fullscreen.glsl, which gives it vUV,
    /// outColor and Graphite_SampleSource, and its layered and sourceless forms as variants. A procedural program's code
    /// holds both stages, the vertex one under GRAPHITE_VERTEX.
    /// </summary>
    public static ShaderPass Pass(string fragment, string name, bool lit = false, bool procedural = false)
    {
        string defines = (lit ? $"#pragma resource_set {GraphiteBindings.UserSet}\n#define GRAPHITE_NO_OBJECT 1\n" : "")
            + (procedural ? "#define GRAPHITE_PROCEDURAL 1\n" : "");
        string vertex = procedural ? fragment : "";
        string text = $"Vertex\n{{\n{defines}#include \"Graphite/Fullscreen.glsl\"\n{vertex}\n}}\nFragment\n{{\n{defines}#include \"Graphite/Fullscreen.glsl\"\n{fragment}\n}}\n";
        return new ShaderPass { State = new PassState(), Source = text, SourceName = name };
    }

    // Each "#define NAME 1" line as the keyword it picks: the toggle called NAME, or the value of the axis NAME starts with
    private static List<Keyword> Keywords(IReadOnlyList<VariantSpace> axes, string variant)
    {
        List<Keyword> keywords = new();
        foreach (string line in variant.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "#define")
                throw new ArgumentException($"A variant is #define lines, not '{line}'.", nameof(variant));
            string define = parts[1];
            Keyword? found = null;
            foreach (VariantSpace axis in axes)
            {
                if (axis.IsToggle && axis.Name == define)
                    found = new Keyword(axis.Name, "true");
                else if (!axis.IsToggle && define.StartsWith(axis.Name + "_", StringComparison.Ordinal) && System.Linq.Enumerable.Contains(axis.Values, define[(axis.Name.Length + 1)..]))
                    found = new Keyword(axis.Name, define[(axis.Name.Length + 1)..]);
            }
            keywords.Add(found ?? throw new ArgumentException($"No variant axis declares {define}.", nameof(variant)));
        }
        return keywords;
    }

    /// <summary>
    /// A program whose forms compile under the policy: Async asks for the mono form now at the priority and the layered
    /// one when a layered view first wants it, the other policies build both now.
    /// </summary>
    internal static FullscreenProgram Create(GraphicsDevice device, IShaderCompiler? compiler, string fragment, string name, bool source = true, BlendStateDescription? blend = null, bool lit = false, bool procedural = false, string variant = "",
        CompilePolicy policy = CompilePolicy.Sync, CompilePriority priority = CompilePriority.Visible, Action<RendererError>? report = null)
    {
        source &= !procedural;
        PassVariants variants = Pass(fragment, name, lit, procedural).Variants(compiler, device.BackendType);
        List<Keyword> keywords = Keywords(variants.Axes, variant);
        keywords.Add(new Keyword("GRAPHITE_NO_SOURCE", source ? "false" : "true"));
        int[] keys = new int[2];
        for (int form = 0; form < 2; form++)
        {
            if (!variants.TryGetKey([.. keywords, new Keyword("GRAPHITE_LAYERED", form == 1 ? "true" : "false")], out keys[form]))
                throw new ArgumentException($"{name} has no variant '{variant}'.", nameof(variant));
        }

        FullscreenProgram program = new(device, variants, keys, blend ?? BlendStateDescription.SingleOverrideBlend, name, source, lit, procedural, compiler == null ? CompilePolicy.BakedOnly : policy, priority, report);
        if (program._policy == CompilePolicy.Async)
        {
            program.Ready(1);
            return program;
        }
        try
        {
            program.For(1);
            program.For(2);
        }
        catch
        {
            program.Dispose();
            throw;
        }
        return program;
    }
}

/// <summary>What a feature declares in its view's graph. It runs once for every view every frame.</summary>
public sealed class FeatureBuilder
{
    private readonly FeaturePass _pass;
    private readonly PassBuilder _graph;

    internal FeatureBuilder(FeaturePass pass, PassBuilder graph)
    {
        _pass = pass;
        _graph = graph;
    }

    private ViewGraph View => _pass.View;

    private FrameGraph FrameGraph => View.Graph;

    private int Scope => View.Camera.View.ViewId;

    /// <summary>The underlying pass builder, for resources the methods here do not cover.</summary>
    public PassBuilder Graph => _graph;

    /// <summary>Reads a buffer a frame feature exported.</summary>
    public BufferHandle ReadFrame(BufferHandle handle, BufferAccess usage = BufferAccess.AllReads)
    {
        if (!View.Frame.Exports.Buffers.TryGetValue(handle.Id, out BufferHandle exported))
            throw new InvalidOperationException($"{_pass.Name} reads a frame buffer that no active frame feature exported.");
        _graph.Read(exported, usage);
        return exported;
    }

    /// <summary>Reads a texture a frame feature exported.</summary>
    public TextureHandle ReadFrame(TextureHandle handle, TextureState usage = TextureState.Sampled)
    {
        if (!View.Frame.Exports.Textures.TryGetValue(handle.Id, out TextureHandle exported))
            throw new InvalidOperationException($"{_pass.Name} reads a frame texture that no active frame feature exported.");
        _graph.Read(exported, usage);
        return exported;
    }

    /// <summary>Reads the scene's depth, normals, and roughness and metallic.</summary>
    public void ReadScene()
    {
        if (!View.SceneWritten)
            throw new InvalidOperationException($"{_pass.Name} reads the scene, which no earlier feature drew.");
        if (_pass.Output is { } output && output == View.Scene)
            throw new InvalidOperationException($"{_pass.Name} reads the scene it draws onto.");
        _pass.SceneRead = _graph.Read(View.Scene, TextureState.Sampled, TextureState.Sampled);
        ReadPostViews();
    }

    /// <summary>Reads the current colour.</summary>
    public void ReadColor()
    {
        if (!View.CurrentWritten)
            throw new InvalidOperationException($"{_pass.Name} reads the colour, which no earlier feature drew.");
        if (_pass.Wrote)
            throw new InvalidOperationException($"{_pass.Name} reads the colour it draws onto.");
        _pass.Input = _pass.SceneRead is { } scene && View.CurrentIsScene ? scene : _graph.Read(View.Current);
    }

    /// <summary>Draws onto the current colour: the scene with its depth and G-buffer, or the last colour a feature wrote.</summary>
    public void DrawToColor()
    {
        if (_pass.Input != null || _pass.SceneRead != null && View.CurrentIsScene)
            throw new InvalidOperationException($"{_pass.Name} draws onto the colour it reads.");
        Write(View.Current);
    }

    /// <summary>
    /// Draws onto the scene with its depth and G-buffer, as opaque and transparent geometry does. When a feature wrote a
    /// new colour since the scene was last drawn, that colour is copied into the scene first.
    /// </summary>
    public void DrawToScene()
    {
        if (_pass.SceneRead != null || (View.CurrentIsScene && _pass.Input != null))
            throw new InvalidOperationException($"{_pass.Name} draws onto the scene it reads.");
        if (!View.CurrentIsScene)
        {
            _pass.CopyBack = _pass.Input ?? _graph.Read(View.Current);
            View.CurrentIsScene = true;
        }
        Write(View.Scene);
    }

    /// <summary>Writes a new colour from the current one, which later features read. <see cref="FeatureContext.Fullscreen"/> draws it.</summary>
    public void WriteColor()
    {
        ReadColor();
        ReadPostViews();
        Next(ColorFor(View.View));
    }

    /// <summary>
    /// Writes a new colour at the view's output resolution from the current one, as an upscaler does. Later features see
    /// the view at the output resolution, and the present draws it as it is. Only features in the Upscale slot can.
    /// </summary>
    public void WriteOutputColor()
    {
        if (!View.Shape.Upscales)
            throw new InvalidOperationException($"{_pass.Name} writes the output colour outside the Upscale slot.");
        ReadColor();
        ReadPostViews();
        View.Upscaled = true;
        View.View = View.View.AtOutput();
        Next(ColorFor(View.View));
    }

    /// <summary>
    /// Says the colour this feature writes or draws is display encoded, as a tonemapper's is, so the present undoes the
    /// encode for targets that take linear light, sRGB and float ones. Later features keep it so.
    /// </summary>
    public void EncodesForDisplay() => _pass.EncodesForDisplay = true;

    /// <summary>A texture for this feature alone this frame, drawn and then read back in its pass by default.</summary>
    public TextureHandle Temporary(string name, GraphTextureDesc desc, TextureState usage = TextureState.Attachment | TextureState.Sampled)
        => Temporary(name, -1, desc, usage);

    /// <summary>One of a numbered run of textures for this feature alone this frame, such as the levels of a chain.</summary>
    public TextureHandle Temporary(string name, int index, GraphTextureDesc desc, TextureState usage = TextureState.Attachment | TextureState.Sampled)
        => _graph.Write(FrameGraph.Create(_pass.IdOf("Temporary", name, index), desc, Lifetime.Transient, Scope), usage);

    /// <summary>
    /// A texture for this feature that keeps its last frames, read through <see cref="FeatureContext.History"/>. It is kept
    /// for each view. Usage adds Sampled for a feature that reads it back in the same pass it draws it.
    /// </summary>
    public TextureHandle History(string name, GraphTextureDesc desc, int frames = 1, TextureState usage = TextureState.Attachment)
        => _graph.Write(FrameGraph.Create(_pass.IdOf("History", name, -1), desc, Lifetime.History(frames), Scope), usage);

    /// <summary>
    /// Declares the feature records the view's renderables or sky through the context's Draw methods, or draws lit
    /// programs, which read the view's lights, shadows and probes.
    /// </summary>
    public void DrawsRenderables()
    {
        if (_pass.Draws)
            return;
        _pass.Draws = true;
        View.Frame.DeclareDrawReads(View, _graph, View.Camera, View.ScreenVisibilityWritten, View.ScreenIrradianceWritten);
    }

    /// <summary>
    /// Writes the view's screen lighting, which renderables drawn after read through Graphite_IndirectDiffuse and
    /// Graphite_IndirectSpecular: visibility always, irradiance with its confidence when asked. Its size is set by the
    /// feature's <see cref="ScreenLightingFeature.Scale"/>. Returns the textures to draw, irradiance null when not asked.
    /// </summary>
    public (TextureHandle Visibility, TextureHandle? Irradiance) WriteScreenLighting(bool irradiance)
    {
        if (_pass.Feature is not ScreenLightingFeature producer)
            throw new InvalidOperationException($"{_pass.Name} writes screen lighting without being a ScreenLightingFeature.");
        if (irradiance && !producer.IrradianceSnapshot)
            throw new InvalidOperationException($"{_pass.Name} writes screen irradiance without saying so through WritesIrradiance.");
        CameraFrame camera = View.Camera;
        TextureHandle visibility = _graph.Write(FrameGraph.Import(camera.ScreenVisibility!), TextureState.Attachment);
        View.ScreenVisibilityWritten = true;
        if (!irradiance)
            return (visibility, null);
        View.ScreenIrradianceWritten = true;
        return (visibility, _graph.Write(FrameGraph.Import(camera.ScreenIrradiance!), TextureState.Attachment));
    }

    /// <summary>State this feature keeps for this view, made the first time it is asked for and disposed with the view.</summary>
    public T GetViewState<T>() where T : class, new() => View.Camera.State<T>(_pass.Feature);

    /// <summary>The texel a feature that measures exposure writes the view's next exposure into, as R32 float.</summary>
    internal TextureHandle WriteExposure()
    {
        if (!_pass.Feature.MeasuresExposure || View.Camera.MeasuredExposure is not { } measured)
            throw new InvalidOperationException($"{_pass.Name} writes the view's exposure without measuring it.");
        return _graph.Write(FrameGraph.Import(measured), TextureState.Attachment);
    }

    /// <summary>
    /// The view's back depth, which passes tagged "BackDepth" = "Read" read, for the BackDepth pass to be drawn into on
    /// frames <see cref="FeatureView.ReadsBackDepth"/>, as <see cref="TransmissionDepthFeature"/> does.
    /// </summary>
    public TextureHandle WriteBackDepth()
    {
        if (!View.Camera.BackDepthUsed || View.Camera.BackDepth is not { } target)
            throw new InvalidOperationException($"{_pass.Name} draws the view's back depth, which the view does not read this frame.");
        return _graph.Write(FrameGraph.Import(target), TextureState.Attachment);
    }

    /// <summary>
    /// Makes the view's scene, its colour, depth and G-buffer in one texture, first written with the given ops, the
    /// colour the features after this one draw onto, as <see cref="SceneClearFeature"/> does in the Clear slot. Publish it
    /// as <see cref="RendererOutputs.Scene"/> for features that read it by name.
    /// </summary>
    public TextureHandle WriteScene(TargetLoadStoreOps firstWrite)
    {
        if (View.SceneWritten)
            throw new InvalidOperationException($"{_pass.Name} makes the view's scene, which an earlier feature made.");
        TextureHandle scene = FrameGraph.Create(ViewGraph.SceneId, View.SceneDesc(), Lifetime.Transient, Scope);
        TextureHandle written = _graph.Write(scene, TextureState.Attachment, firstWrite: firstWrite);
        View.Scene = View.Current = written;
        View.CurrentIsScene = View.SceneWritten = View.CurrentWritten = true;
        return written;
    }

    /// <summary>
    /// Writes the view's target, the host's framebuffer or a capture's cube, its colour first written with the given ops,
    /// as <see cref="PresentFeature"/> does.
    /// </summary>
    public TextureHandle WriteTarget(AttachmentOps color)
    {
        CameraFrame camera = View.Camera;
        TextureHandle target = View.Captures ? FrameGraph.Import(camera.CaptureCube!) : FrameGraph.ImportTarget(camera.Desc.Target!);
        return _graph.Write(target, TextureState.Attachment, firstWrite: new TargetLoadStoreOps(color, AttachmentOps.Loaded));
    }

    /// <summary>
    /// Declares the view's directional cascades that draw this frame, for <see cref="FeatureContext.DrawCascades"/> to
    /// draw, as <see cref="CascadeFeature"/> does. Returns a texture of them to publish as <see cref="RendererOutputs.Cascades"/>,
    /// which the view's draws read. The view has cascades when <see cref="FeatureView.HasCascades"/>.
    /// </summary>
    public TextureHandle WriteCascades()
    {
        CameraFrame camera = View.Camera;
        if (camera.Shadows.FirstCascade() is not { } first)
            throw new InvalidOperationException($"{_pass.Name} draws the view's cascades, which it has none of.");
        List<(ViewShadows.Cascade Cascade, TextureHandle Handle)> targets = _pass.Cascades;
        targets.Clear();
        View.Frame.Buffers.ReadWorld(FrameGraph, _graph, camera.World);
        List<RenderTexture> written = new();
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                if (light.Cascades[c].Draws.Count > 0)
                    written.Add(light.Cascades[c].Texture!.Texture!);
            }
        }
        View.Frame.DeclareShadowReads(FrameGraph, _graph, alsoWritten: written);
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                ViewShadows.Cascade cascade = light.Cascades[c];
                if (cascade.Draws.Count == 0)
                    continue;
                foreach (ShadowDraw draw in cascade.Draws)
                    draw.Camera.Buffers.Read(FrameGraph, _graph, BufferAccess.AllReads);
                targets.Add((cascade, _graph.Write(FrameGraph.Import(cascade.Texture!.Texture!), TextureState.Attachment)));
            }
        }
        return targets.Count > 0 ? targets[0].Handle : FrameGraph.Import(first.Texture!.Texture!);
    }

    /// <summary>Hands a texture to later features of the view by name.</summary>
    public void Publish(string name, TextureHandle texture) => View.Published[name] = texture;

    /// <summary>A texture an earlier feature of the view published.</summary>
    public TextureHandle Read(string name)
    {
        if (!View.Published.TryGetValue(name, out TextureHandle texture))
            throw new InvalidOperationException($"{_pass.Name} reads {name}, which no earlier feature published.");
        return _graph.Read(texture);
    }

    // The views the fullscreen programs of a new colour read
    private void ReadPostViews() => _graph.Read(FrameGraph.Import(View.Camera.ViewBlocks.Buffer), BufferAccess.AllReads);

    private TextureHandle ColorFor(in FeatureView view)
        => FrameGraph.Create(_pass.ColorId, View.ColorDesc(view), Lifetime.Transient, Scope);

    private void Write(TextureHandle target)
    {
        if (_pass.Wrote)
            throw new InvalidOperationException($"{_pass.Name} draws onto more than one colour.");
        bool scene = target == View.Scene;
        _pass.OutputIsScene = scene;
        TextureHandle written = _graph.Write(target, TextureState.Attachment);
        _pass.Output = written;
        _pass.Wrote = true;
        View.Current = written;
        if (scene)
            View.Scene = written;
        View.CurrentWritten = true;
    }

    private void Next(TextureHandle created)
    {
        if (_pass.Wrote)
            throw new InvalidOperationException($"{_pass.Name} draws onto more than one colour.");
        TextureHandle written = _graph.Write(created, TextureState.Attachment, firstWrite: new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        _pass.Output = written;
        _pass.Wrote = true;
        View.Current = written;
        View.CurrentIsScene = false;
        View.CurrentWritten = true;
    }
}

/// <summary>What a feature records with.</summary>
public sealed class FeatureContext
{
    private static readonly PropertyID s_source = "Graphite_Source", s_postViews = "Graphite_PostViews";

    private readonly FeaturePass _pass;
    private readonly PropertySet _properties = new();

    internal FeatureContext(FeaturePass pass) => _pass = pass;

    /// <summary>The underlying pass context, for resources the feature declared through <see cref="FeatureBuilder.Graph"/>.</summary>
    public PassContext Graph { get; internal set; } = null!;

    public CommandBuffer Command { get; internal set; } = null!;
    public FeatureView View { get; internal set; }

    /// <summary>The scene's depth target, when the feature reads the scene.</summary>
    public Texture? Depth => Scene?.DepthTexture;

    /// <summary>How far each opaque surface moved across the screen since last frame, in uv, when the feature reads the scene and declares it in SceneReads.</summary>
    public Texture? Motion => SceneTarget(1);

    /// <summary>
    /// How much of each pixel shows what transmissive surfaces refract from behind them, 0 to 1 in R8, when the feature
    /// reads the scene, declares it in SceneReads and the view draws a transmissive surface this frame. Their motion does
    /// not move that, so temporal effects trust history less there.
    /// </summary>
    public Texture? Reactive => SceneTarget(2);

    /// <summary>The scene's octahedral world normals, when the feature reads the scene and declares them in SceneReads.</summary>
    public Texture? Normals => SceneTarget(3);

    /// <summary>The scene's top layer specular reflectance in rgb and perceptual roughness in a, when the feature reads the scene and declares them in SceneReads.</summary>
    public Texture? Material => SceneTarget(4);

    private Texture? SceneTarget(int index) => Scene is { } scene && index < scene.ColorTextures.Length ? scene.ColorTextures[index] : null;

    /// <summary>The colour the feature reads, when it reads or writes colour.</summary>
    public Texture? Color => _pass.Input is { } input ? Graph.Get(input).ColorTextures[0] : null;

    private RenderTexture? Scene => _pass.SceneRead is { } scene ? Graph.Get(scene) : null;

    /// <summary>A texture the feature declared.</summary>
    public RenderTexture Get(TextureHandle texture) => Graph.Get(texture);

    /// <summary>A buffer the feature declared or read from a frame feature.</summary>
    public DeviceBuffer Get(BufferHandle buffer) => Graph.Get(buffer);

    /// <summary>The view's first layer as shaders read it this frame, jittered when the view jitters.</summary>
    public ViewBlock Block => _pass.Camera.Blocks[0];

    /// <summary>Whether the view drew last frame, so motion and history from then mean something.</summary>
    public bool HasPreviousFrame => _pass.Camera.HadPrevious;

    /// <summary>The frame's time as the host gave it.</summary>
    public FrameTime Time => _pass.Camera.Time;

    /// <summary>Whether a feature before this one in the view encoded the colour for display, as tonemappers do.</summary>
    public bool DisplayEncoded => _pass.Camera.DisplayEncoded;

    /// <summary>The renderer's noise: blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise => _pass.Frame.Resources.Noise;

    /// <summary>The SMAA reference's area and search tables, arrays of as many layers as the view has.</summary>
    internal (TextureView Area, TextureView Search) SmaaTables => View.LayerCount > 1
        ? (_pass.Frame.Resources.SmaaAreaLayered, _pass.Frame.Resources.SmaaSearchLayered) : (_pass.Frame.Resources.SmaaArea, _pass.Frame.Resources.SmaaSearch);

    /// <summary>The direction the world's sun shines, its chosen sun or else its brightest directional light, or null without one.</summary>
    public Float3? SunDirection
    {
        get
        {
            WorldLighting lighting = _pass.Camera.World.World.Lighting;
            return lighting.DirectionalOrder.Count > 0 ? lighting.Light(lighting.DirectionalOrder[0]).Direction : null;
        }
    }

    /// <summary>The view's layers as shaders read them this frame, jittered when the view jitters.</summary>
    public ReadOnlySpan<ViewBlock> Blocks => _pass.Camera.Blocks.AsSpan(0, View.LayerCount);

    /// <summary>A history texture as it was some frames ago, or null before it holds one or after a cut.</summary>
    public RenderTexture? History(TextureHandle texture, int framesAgo = 1)
        => _pass.Camera.HadPrevious ? Graph.History(texture, framesAgo) : null;

    /// <summary>State this feature keeps for this view, made the first time it is asked for and disposed with the view.</summary>
    public T GetViewState<T>() where T : class, new() => _pass.Camera.State<T>(_pass.Feature);

    /// <summary>Draws the visible opaque renderables' depth prepass onto the current output.</summary>
    public void DrawPrepass() => Drawer.DrawList(Command, _pass.Camera.Prepass);

    /// <summary>Draws the visible opaque renderables' forward pass onto the current output.</summary>
    public void DrawOpaque() => Drawer.DrawList(Command, _pass.Camera.Forward);

    /// <summary>Draws the visible transparent renderables back to front onto the current output.</summary>
    public void DrawTransparent() => Drawer.DrawList(Command, _pass.Camera.Transparent);

    /// <summary>Draws the world's sky material onto the current output.</summary>
    public void DrawSky() => Drawer.DrawSky(Command);

    /// <summary>
    /// Draws the visible renderables with a pass of a type the feature declared in <see cref="RenderFeature.Passes"/>:
    /// opaque ones in bucket order, then transparent ones back to front. Prepass and Forward are the opaque lists.
    /// </summary>
    public void DrawPass(PassType type)
    {
        if (Array.IndexOf(_pass.Feature.PassArray, type) < 0 || _pass.Camera.FindList(type) is not { } list)
            throw new InvalidOperationException($"{_pass.Name} draws the {type.Name} pass without declaring it in its Passes.");
        Drawer.DrawList(Command, list);
    }

    /// <summary>Draws the cascades <see cref="FeatureBuilder.WriteCascades"/> declared into their depth textures.</summary>
    public void DrawCascades()
    {
        foreach ((ViewShadows.Cascade cascade, TextureHandle handle) in _pass.Cascades)
            ShadowRecorder.Record(Command, Get(handle), cascade.Draws, _pass.Frame.TileClearProgram, Graph.Task.RingSlot);
    }

    /// <summary>
    /// The view's scene colour, which passes tagged "SceneColor" = "Read" sample, on frames <see cref="FeatureView.ReadsSceneColor"/>,
    /// for a feature in the OpaqueColor slot to copy the colour into and fill the mips of, as <see cref="SceneColorFeature"/> does.
    /// </summary>
    public Texture? SceneColorCopy => _pass.Camera.SceneColorUsed ? _pass.Camera.SceneColor : null;

    /// <summary>
    /// The view's scene depth, which passes tagged "SceneDepth" = "Read" fetch, on frames <see cref="FeatureView.ReadsSceneDepth"/>,
    /// for a feature in the OpaqueColor slot to copy the depth into, as <see cref="SceneDepthFeature"/> does.
    /// </summary>
    public Texture? SceneDepthCopy => _pass.Camera.SceneDepthUsed ? _pass.Camera.SceneDepth : null;

    private void BindLighting(FullscreenProgram program)
    {
        if (!program.Lit)
            return;
        if (!_pass.Draws)
            throw new InvalidOperationException($"{_pass.Name} draws a lit fullscreen program without declaring DrawsRenderables in Setup.");
        _pass.Camera.Drawer.BindTables(Command);
    }

    private SceneDrawer Drawer => _pass.Draws ? _pass.Camera.Drawer
        : throw new InvalidOperationException($"{_pass.Name} draws renderables without declaring DrawsRenderables in Setup.");

    /// <summary>
    /// Binds the colour the feature draws onto or writes, as the graph's first write clears or loads it, and copies a
    /// colour written since into the scene.
    /// </summary>
    public void BeginOutput()
    {
        if (_pass.Output is not { } output)
            throw new InvalidOperationException($"{_pass.Name} declared no colour to draw onto or write.");
        Command.SetFramebuffer(Graph.Get(output).Framebuffer);
        Command.SetFullViewport();
        if (_pass.CopyBack is { } copy)
        {
            Command.SetShader(_pass.Frame.Resources.CopyBackProgram.For(View.LayerCount));
            Command.SetVertexSource(VertexSource.None);
            _properties.SetTexture(s_source, Graph.Get(copy).ColorTextures[0], _pass.Frame.Resources.LinearRepeat);
            Command.SetProperties(_properties);
            Command.Draw(3);
        }
    }

    /// <summary>
    /// Draws a fullscreen program over a texture the feature declared, Temporary or History, the colour it reads bound as
    /// Graphite_Source, with any properties given. Clear first clears it to transparent black, for programs that discard.
    /// Shared draws once over a texture of one layer, for state a layered view's layers share, with the program's layered
    /// form on a layered view so the view sized textures it reads give their first layer.
    /// </summary>
    public void Fullscreen(FullscreenProgram program, RenderTexture target, PropertySet? properties = null, bool clear = false, bool shared = false)
        => DrawFullscreen(program, target, properties, shared ? 1 : View.LayerCount, clear);

    /// <summary>
    /// Draws a procedural program's vertices over a texture the feature declared, for geometry made in the shader such as
    /// particles, blending over what the texture holds unless cleared to transparent black first.
    /// </summary>
    public void DrawProcedural(FullscreenProgram program, RenderTexture target, uint vertexCount, PropertySet? properties = null, bool clear = false)
    {
        if (!program.Procedural)
            throw new InvalidOperationException($"{_pass.Name} draws a fullscreen program procedurally; create it with CreateProceduralProgram.");
        if (Program(program) is not { } shader)
            return;
        AttachmentOps color = clear ? AttachmentOps.Clear(new Color(0f, 0f, 0f, 0f)) : AttachmentOps.Loaded;
        Command.SetFramebuffer(target.Framebuffer, new TargetLoadStoreOps(color, AttachmentOps.Loaded));
        Command.SetFullViewport();
        Command.SetShader(shader);
        Command.SetVertexSource(VertexSource.None);
        BindLighting(program);
        _properties.SetBuffer(s_postViews, _pass.Camera.ViewBlocks.Buffer);
        Command.SetProperties(_properties);
        if (properties != null)
            Command.SetProperties(properties);
        Command.Draw(vertexCount);
    }

    private void DrawFullscreen(FullscreenProgram program, RenderTexture target, PropertySet? properties, int layers, bool clear)
    {
        if (program.Procedural)
            throw new InvalidOperationException($"{_pass.Name} draws a procedural program as a fullscreen one; draw it with DrawProcedural.");
        if (layers == 1 && View.LayerCount > 1 && program.HasSource)
            throw new InvalidOperationException($"{_pass.Name} draws a program reading the view's colour once for every layer; create it with source: false and bind what it reads.");
        if (Program(program) is not { } shader)
            return;
        AttachmentOps color = clear ? AttachmentOps.Clear(new Color(0f, 0f, 0f, 0f)) : new AttachmentOps(LoadAction.DontCare, StoreAction.Store);
        Command.SetFramebuffer(target.Framebuffer, new TargetLoadStoreOps(color, AttachmentOps.Loaded));
        Command.SetFullViewport();
        Command.SetShader(shader);
        Command.SetVertexSource(VertexSource.None);
        BindLighting(program);
        _properties.SetBuffer(s_postViews, _pass.Camera.ViewBlocks.Buffer);
        if (program.HasSource && Color is { } source)
            _properties.SetTexture(s_source, source, _pass.Frame.Resources.LinearClamp);
        Command.SetProperties(_properties);
        if (properties != null)
            Command.SetProperties(properties);
        Command.Draw(3);
    }

    /// <summary>
    /// Draws a fullscreen program over the colour the feature writes, the colour it reads bound as Graphite_Source, with
    /// any properties given.
    /// </summary>
    public void Fullscreen(FullscreenProgram program, PropertySet? properties = null)
    {
        if (Color is not { } source || _pass.OutputIsScene)
            throw new InvalidOperationException($"{_pass.Name} draws a fullscreen program without writing a new colour, declare WriteColor.");
        if (Program(program) is not { } shader)
            return;
        BeginOutput();
        Command.SetShader(shader);
        Command.SetVertexSource(VertexSource.None);
        BindLighting(program);
        _properties.SetBuffer(s_postViews, _pass.Camera.ViewBlocks.Buffer);
        _properties.SetTexture(s_source, source, _pass.Frame.Resources.LinearClamp);
        Command.SetProperties(_properties);
        if (properties != null)
            Command.SetProperties(properties);
        Command.Draw(3);
    }

    // A program the feature draws with, or null once a program it draws with first turned out not ready this frame
    private GraphicsProgram? Program(FullscreenProgram program)
    {
        _pass.Feature.Uses(program);
        if (!_pass.PassedThrough && program.Ready(View.LayerCount))
            return program.For(View.LayerCount);
        PassThrough();
        return null;
    }

    // The new colour the pass writes is the colour it reads, unchanged, and the rest of its fullscreen draws are skipped
    private void PassThrough()
    {
        if (_pass.PassedThrough)
            return;
        _pass.PassedThrough = true;
        if (_pass.Output is not { } output || _pass.OutputIsScene || Color is not { } source)
            return;
        Command.SetFramebuffer(Graph.Get(output).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        Command.SetFullViewport();
        Command.SetShader(_pass.Frame.Resources.CopyBackProgram.For(View.LayerCount));
        Command.SetVertexSource(VertexSource.None);
        _properties.SetTexture(s_source, source, _pass.Frame.Resources.LinearClamp);
        Command.SetProperties(_properties);
        Command.Draw(3);
    }
}

/// <summary>Runs one feature as a pass of the views that selected it.</summary>
internal sealed class FeaturePass
{
    private readonly FeatureContext _context;
    private readonly Func<PassBuilder, FeaturePass> _setup;
    private readonly Dictionary<(string Kind, string Name, int Index), RenderResourceID> _ids = new();
    private RenderResourceID _colorId;

    public FeaturePass(RenderFeature feature, ViewGraph view)
    {
        Feature = feature;
        View = view;
        Name = feature.Name;
        _context = new FeatureContext(this);
        _setup = Setup;
    }

    public RenderFeature Feature { get; }
    public ViewGraph View { get; }
    public CameraFrame Camera => View.Camera;
    public FramePipeline Frame => View.Frame;
    public string Name { get; }

    public bool AfterUpscale;

    /// <summary>The cascades the feature declared this frame, with their textures.</summary>
    public readonly List<(ViewShadows.Cascade Cascade, TextureHandle Handle)> Cascades = new();
    public bool Draws;
    public bool PassedThrough;
    public bool EncodesForDisplay;
    public bool Wrote;
    public bool OutputIsScene;
    public TextureHandle? Input;
    public TextureHandle? SceneRead;
    public TextureHandle? CopyBack;
    public TextureHandle? Output;

    /// <summary>The id of the colour this feature writes in the view.</summary>
    public RenderResourceID ColorId => _colorId.IsValid ? _colorId : _colorId = View.IdOf(Feature, "Color");

    /// <summary>The id of a texture this feature declares in the view, interned the first time it is asked for.</summary>
    public RenderResourceID IdOf(string kind, string name, int index)
    {
        if (!_ids.TryGetValue((kind, name, index), out RenderResourceID id))
            _ids[(kind, name, index)] = id = View.IdOf(Feature, index < 0 ? $"{kind}.{name}" : $"{kind}.{name}{index}");
        return id;
    }

    public void Add()
    {
        if (!Feature.HasWork(View.View))
            return;
        View.Graph.AddPass(Name, _setup, static (context, pass) => pass.Render(context));
    }

    private FeaturePass Setup(PassBuilder builder)
    {
        Input = null;
        SceneRead = null;
        CopyBack = null;
        Output = null;
        EncodesForDisplay = false;
        Draws = false;
        PassedThrough = false;
        Wrote = false;
        OutputIsScene = false;
        Cascades.Clear();
        AfterUpscale = View.Upscaled;
        Feature.Setup(new FeatureBuilder(this, builder), View.View);
        return this;
    }

    private void Render(PassContext context)
    {
        View.BeginRecording(context);
        _context.Graph = context;
        _context.Command = context.Command;
        _context.View = AfterUpscale ? Camera.Info.AtOutput() : Camera.Info;
        if (EncodesForDisplay)
            Camera.DisplayEncoded = true;
        Feature.Render(_context);
    }
}

/// <summary>
/// Clears the scene's colour, depth and G-buffer first, so a view whose features draw nothing still presents its clear
/// colour. Publishes the scene as <see cref="RendererOutputs.Scene"/>. Group <see cref="FeatureGroup.SceneClear"/>.
/// </summary>
public sealed class SceneClearFeature : RenderFeature
{
    internal SceneClearFeature() : base(RenderSlot.Clear, group: FeatureGroup.SceneClear, runsOn: ViewKinds.All) { }

    private sealed class State
    {
        public TextureHandle Scene;
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        Float4 clear = view.Overlay ? default : view.ClearColor with { W = 1 };
        TextureHandle scene = builder.WriteScene(new TargetLoadStoreOps(AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W)), AttachmentOps.Clear(0f)));
        builder.GetViewState<State>().Scene = scene;
        builder.Publish(RendererOutputs.Scene, scene);
    }

    public override void Render(FeatureContext context)
    {
        Framebuffer framebuffer = context.Get(context.GetViewState<State>().Scene).Framebuffer;
        for (uint target = 1; target < framebuffer.ColorTargets.Count; target++)
            context.Command.ClearColorTarget(target, new Color(0f, 0f, 0f, 0f));
    }
}

/// <summary>
/// A view's last pass: the colour the features left, copied into the view's viewport of its target. Group
/// <see cref="FeatureGroup.Present"/>.
/// </summary>
public sealed class PresentFeature : RenderFeature
{
    private static readonly PropertyID s_source = "Graphite_Source", s_mode = "Graphite_PresentMode";

    private readonly FullscreenProgram _present, _presentOver;
    private readonly Sampler _sampler;

    internal PresentFeature(RendererResources resources) : base(RenderSlot.Present, group: FeatureGroup.Present, runsOn: ViewKinds.All)
        => (_present, _presentOver, _sampler) = (resources.Present(false), resources.Present(true), resources.LinearRepeat);

    // sRGB targets encode what they are given and float targets hold linear light, so display encoded colour is decoded
    // for both. Other formats store display values, so linear colour is encoded for them.
    private static bool TakesLinear(PixelFormat format) => format is PixelFormat.R8_G8_B8_A8_UNorm_SRgb or PixelFormat.B8_G8_R8_A8_UNorm_SRgb
        or PixelFormat.R16_Float or PixelFormat.R16_G16_Float or PixelFormat.R16_G16_B16_A16_Float or PixelFormat.R11_G11_B10_Float
        or PixelFormat.R32_Float or PixelFormat.R32_G32_Float or PixelFormat.R32_G32_B32_A32_Float;

    private sealed class State
    {
        public readonly PropertySet Properties = new();
        public TextureHandle Target;
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadColor();
        Float4 clear = view.ClearColor with { W = 1 };
        bool whole = view.Viewport == new Float4(0, 0, 1, 1);
        AttachmentOps color = view.Overlay ? AttachmentOps.Loaded
            : view.ClearTarget ? AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W))
            : whole ? new AttachmentOps(LoadAction.DontCare, StoreAction.Store) : AttachmentOps.Loaded;
        builder.GetViewState<State>().Target = builder.WriteTarget(color);
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        CommandBuffer cmd = context.Command;
        Framebuffer target = context.Get(state.Target).Framebuffer;
        Float4 rect = context.View.Viewport;
        cmd.SetViewport(new Viewport(rect.X * target.Width, rect.Y * target.Height, rect.Z * target.Width, rect.W * target.Height, 0, 1));
        cmd.SetVertexSource(VertexSource.None);
        bool linearTarget = TakesLinear(target.ColorTargets[0].Target.Format);
        bool decode = context.DisplayEncoded && linearTarget;
        bool encode = !context.DisplayEncoded && !linearTarget;
        cmd.SetShader((context.View.Overlay ? _presentOver : _present).For(context.View.LayerCount));
        state.Properties.SetInt(s_mode, decode ? 1 : encode ? 2 : 0);
        state.Properties.SetTexture(s_source, context.Color!, _sampler);
        cmd.SetProperties(state.Properties);
        cmd.Draw(3);
    }
}

/// <summary>
/// Draws the visible renderables' pass of one type, the shader pass tagged "LightMode" = type, onto the scene in a slot,
/// such as "Outline" after the opaques. Each pass draws with its own render state, so outlines, silhouettes seen through
/// walls and similar effects need only the pass in their shaders. The built in depth prepass, opaque pass, sky and
/// transparent pass are draw features of the Prepass, Forward and Sky passes in their groups. The Sky pass draws the
/// world's sky material. Transparent draws the Forward pass's transparent renderables back to front, as the transparent
/// pass does, rather than its opaque ones.
/// </summary>
public sealed class DrawPassFeature : RenderFeature
{
    public DrawPassFeature(PassType type, RenderSlot slot, int order = 0, FeatureGroup? group = null, ViewKinds runsOn = ViewKinds.Cameras, bool transparent = false)
        : base(slot, order, group, runsOn, passes: type == PassType.Sky ? null : [type])
    {
        if (transparent && type != PassType.Forward)
            throw new ArgumentException($"Only the Forward pass draws its transparent renderables apart, not {type.Name}.", nameof(transparent));
        Type = type;
        Transparent = transparent;
    }

    public PassType Type { get; }

    public override string Name => Group is { } group ? group.Name + "Feature" : Type.Name + "Feature";

    /// <summary>Whether it draws the Forward pass's transparent renderables back to front rather than its opaque ones.</summary>
    public bool Transparent { get; }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        if (Type == PassType.Sky)
            context.DrawSky();
        else if (Transparent)
            context.DrawTransparent();
        else
            context.DrawPass(Type);
    }
}

/// <summary>
/// Copies the view's colour after the opaque surfaces and the sky into its scene colour and fills the mips, for passes
/// tagged "SceneColor" = "Read" to sample through Graphite/SceneColor.glsl. Adds its pass on frames such a pass is in
/// the view's transparent list. Group <see cref="FeatureGroup.SceneColor"/>.
/// </summary>
public sealed class SceneColorFeature : RenderFeature
{
    internal SceneColorFeature() : base(RenderSlot.OpaqueColor, group: FeatureGroup.SceneColor, runsOn: ViewKinds.All) { }

    public override bool HasWork(in FeatureView view) => view.ReadsSceneColor;

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.ReadColor();

    public override void Render(FeatureContext context)
    {
        if (context.SceneColorCopy is not { } target || context.Color is not { } source)
            return;
        context.Command.CopyTexture(source, 0, 0, 0, 0, 0, target, 0, 0, 0, 0, 0, target.Width, target.Height, 1, target.ArrayLayers);
        context.Command.GenerateMipmaps(target);
    }
}

/// <summary>
/// Copies the view's depth after the opaque surfaces into its scene depth, for passes tagged "SceneDepth" = "Read" to
/// fetch through Graphite/SceneDepth.glsl. Adds its pass on frames such a pass is in the view's transparent list. Group
/// <see cref="FeatureGroup.SceneDepth"/>.
/// </summary>
public sealed class SceneDepthFeature : RenderFeature
{
    internal SceneDepthFeature() : base(RenderSlot.OpaqueColor, group: FeatureGroup.SceneDepth, runsOn: ViewKinds.All) { }

    public override bool HasWork(in FeatureView view) => view.ReadsSceneDepth;

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.ReadScene();

    public override void Render(FeatureContext context)
    {
        if (context.SceneDepthCopy is not { } target || context.Depth is not { } source)
            return;
        context.Command.CopyTexture(source, 0, 0, 0, 0, 0, target, 0, 0, 0, 0, 0, target.Width, target.Height, 1, target.ArrayLayers);
    }
}

/// <summary>
/// Draws the back faces of the view's transmissive volumes, their <see cref="PassType.BackDepth"/> pass, into its back
/// depth after the opaque surfaces, for Transmission passes tagged "BackDepth" = "Read" to measure the path through
/// them. Like <see cref="DrawPassFeature"/> it draws one pass of the visible renderables, onto a target of its own.
/// Adds its pass on frames such a pass is in the view's transparent list. Group <see cref="FeatureGroup.BackDepth"/>.
/// </summary>
public sealed class TransmissionDepthFeature : RenderFeature
{
    private sealed class State
    {
        public TextureHandle Target;
    }

    internal TransmissionDepthFeature() : base(RenderSlot.OpaqueColor, 1, FeatureGroup.BackDepth, ViewKinds.All, passes: [PassType.BackDepth]) { }

    public override bool HasWork(in FeatureView view) => view.ReadsBackDepth;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.GetViewState<State>().Target = builder.WriteBackDepth();
    }

    public override void Render(FeatureContext context)
    {
        RenderTexture target = context.Get(context.GetViewState<State>().Target);
        context.Command.SetFramebuffer(target.Framebuffer, TargetLoadStoreOps.Clear(new Color(0f, 0f, 0f, 0f), 0f));
        context.Command.SetFullViewport();
        context.DrawPass(PassType.BackDepth);
    }
}

/// <summary>Picks a view's features each frame: built in, world and view features, filtered and ordered.</summary>
internal static class FeatureSelection
{
    /// <summary>A renderer's own built in features, in the order they run.</summary>
    public static RenderFeature[] BuiltIns(RendererResources resources)
    {
        RenderFeature[] features =
        [
            new SceneClearFeature(),
            new CascadeFeature(),
            new DrawPassFeature(PassType.Prepass, RenderSlot.Prepass, group: FeatureGroup.Prepass, runsOn: ViewKinds.All),
            new DrawPassFeature(PassType.Forward, RenderSlot.Opaque, group: FeatureGroup.Opaque, runsOn: ViewKinds.All),
            new DrawPassFeature(PassType.Sky, RenderSlot.Sky, group: FeatureGroup.Sky, runsOn: ViewKinds.All),
            new SceneColorFeature(),
            new SceneDepthFeature(),
            new TransmissionDepthFeature(),
            new DrawPassFeature(PassType.Forward, RenderSlot.Transparent, group: FeatureGroup.Transparent, runsOn: ViewKinds.All, transparent: true),
            new PresentFeature(resources),
        ];
        foreach (RenderFeature feature in features)
            feature.Snapshot();
        return features;
    }

    public static void Select(List<RenderFeature> result, RenderFeature[] builtIns, RenderFeature[] world, RenderFeature[] view, in FeatureView info)
    {
        result.Clear();
        Add(result, builtIns, info);
        Add(result, world, info);
        Add(result, view, info);

        // Later entries win: the view's over the world's over the built in ones
        for (int i = result.Count - 1; i > 0; i--)
        {
            if (i >= result.Count || result[i].Group is not { } group)
                continue;
            for (int j = i - 1; j >= 0; j--)
            {
                if (result[j].Group == group)
                {
                    result.RemoveAt(j);
                    i--;
                }
            }
        }

        // Stable by slot then order, so ties keep built in before world before view
        for (int i = 1; i < result.Count; i++)
        {
            RenderFeature feature = result[i];
            int j = i - 1;
            while (j >= 0 && Compare(result[j], feature) > 0)
            {
                result[j + 1] = result[j];
                j--;
            }
            result[j + 1] = feature;
        }
    }

    public static int Compare(RenderFeature a, RenderFeature b)
    {
        int slot = a.Slot.CompareTo(b.Slot);
        return slot != 0 ? slot : a.OrderSnapshot.CompareTo(b.OrderSnapshot);
    }

    private static void Add(List<RenderFeature> result, RenderFeature[] features, in FeatureView info)
    {
        ViewKinds kind = (ViewKinds)(1 << (int)info.Kind);
        foreach (RenderFeature feature in features)
        {
            if (!feature.EnabledSnapshot || (feature.RunsOn & kind) == 0 || result.Contains(feature))
                continue;
            if (feature.IsActive(info) && feature.ProgramsReady(info.LayerCount))
                result.Add(feature);
        }
    }
}

/// <summary>
/// Geometry a host draws into each view without making renderables: particles, terrain patches, GPU generated draws. Its
/// items join the view's passes like renderables, opaque ones in the prepass and opaque pass, transparent ones sorted
/// with the world's, and any pass type a feature draws. <see cref="RenderExtension.Extract"/> copies what Cull reads on
/// the host thread at SubmitFrame, <see cref="Cull"/> runs on the render thread once per view.
/// </summary>
public abstract class RenderProvider : RenderExtension
{
    /// <param name="runsOn">The kinds of view it draws into.</param>
    protected RenderProvider(ViewKinds runsOn = ViewKinds.All) => RunsOn = runsOn;

    public ViewKinds RunsOn { get; }

    /// <summary>Adds this view's items to the sink. Render thread.</summary>
    public abstract void Cull(in ProviderView view, DrawItemSink sink);
}

/// <summary>The view a provider culls for.</summary>
public readonly struct ProviderView
{
    private readonly CullFrustum _frustum;

    internal ProviderView(in FeatureView view, in CullFrustum frustum)
    {
        View = view;
        _frustum = frustum;
    }

    public FeatureView View { get; }

    /// <summary>The first layer's camera position.</summary>
    public Double3 Camera => _frustum.Camera;

    /// <summary>Whether any layer of the view sees a sphere.</summary>
    public bool Sees(Double3 center, float radius) => _frustum.Sphere(center, radius);
}

/// <summary>What one of a provider's items is drawn with: its object's data, as a renderable's.</summary>
public struct DrawItem
{
    public Double4x4 Transform;

    /// <summary>Multiplies the standard surfaces' vertex colour, so their base colour and coverage. White by default.</summary>
    public Float4 Color;
    public Float4 Custom0, Custom1;
    public uint LayerMask;
    public uint ObjectId;
    public RenderableFlags Flags;

    /// <summary>The distance transparent items sort back to front by, NaN for the distance to the transform's position.</summary>
    public float SortDepth;

    /// <summary>Where the item is lit by the world's light probes, none for ambient light only.</summary>
    public ProbeAnchorHandle Probe;

    public DrawItem()
    {
        Transform = Double4x4.Identity;
        LayerMask = ~0u;
        Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
        SortDepth = float.NaN;
        Color = Float4.One;
    }
}

/// <summary>A view list's provider items, opaque ones drawn after the list's buckets, sorted ones among its transparents.</summary>
internal sealed class ProvidedItems
{
    public readonly List<BucketSet.ItemDraw> Opaque = new();
    public readonly List<BucketSet.ItemDraw> Sorted = new();
}

/// <summary>Where a provider adds its items for a view. Valid during Cull only. Render thread.</summary>
public sealed class DrawItemSink
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshPlacement, BucketSet.ChunkRef[]?[]> s_chunks = new();
    private readonly List<BucketSet.ItemDraw> _pool = new();
    private readonly Dictionary<DrawList, ProvidedItems> _lists = new();
    private int _used;
    private CameraFrame _camera = null!;
    private WorldFrame _world = null!;
    private CullFrustum _frustum;
    private uint _cullingMask;
    private bool _shadowView;

    /// <summary>Indirect items whose instance count is copied from the provider's buffer into the view's commands.</summary>
    internal readonly List<(BucketSet.ItemDraw Item, DrawList List, RenderResourceID Source, uint Offset)> Counts = new();

    internal void Begin(CameraFrame camera, WorldFrame world, in CullFrustum frustum, uint cullingMask, bool shadowView = false)
    {
        _shadowView = shadowView;
        _camera = camera;
        _world = world;
        _frustum = frustum;
        _cullingMask = cullingMask;
        _used = 0;
        foreach (ProvidedItems items in _lists.Values)
        {
            items.Opaque.Clear();
            items.Sorted.Clear();
        }
        Counts.Clear();
    }

    /// <summary>The items providers added to a list this view, or null.</summary>
    internal ProvidedItems? For(DrawList list)
        => _lists.TryGetValue(list, out ProvidedItems? items) && (items.Opaque.Count > 0 || items.Sorted.Count > 0) ? items : null;

    /// <summary>One submesh of a mesh with a material, for one object.</summary>
    public void Add(GpuMesh mesh, int subMesh, Material material, in DrawItem item) => Emit(mesh, subMesh, material, item, 1, false, default, 0);

    /// <summary>Instances of one submesh sharing one object, shaders telling them apart by Graphite_InstanceIndex.</summary>
    public void AddInstanced(GpuMesh mesh, int subMesh, Material material, in DrawItem item, uint count)
    {
        if (count > 0)
            Emit(mesh, subMesh, material, item, count, true, default, 0);
    }

    /// <summary>
    /// Instances of one submesh sharing one object, as many as a 32 bit count at offset in the frame feature buffer
    /// named by instanceCount, written before the view draws.
    /// </summary>
    public void AddIndirect(GpuMesh mesh, int subMesh, Material material, in DrawItem item, RenderResourceID instanceCount, uint offset)
    {
        if (!instanceCount.IsValid)
            throw new ArgumentException("The instance count names a frame feature buffer.", nameof(instanceCount));
        if (offset % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "The instance count is a 32 bit word.");
        Emit(mesh, subMesh, material, item, 0, true, instanceCount, offset);
    }

    private void Emit(GpuMesh mesh, int subMesh, Material material, in DrawItem item, uint count, bool shared, RenderResourceID source, uint offset)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        if (mesh.Placement is { } placement)
            Emit(placement, subMesh, material, item, count, shared, source, offset);
    }

    /// <summary>One submesh of geometry placed in the pool without a mesh, for one object.</summary>
    internal void AddPlaced(MeshPlacement placement, int subMesh, Material material, in DrawItem item) => Emit(placement, subMesh, material, item, 1, false, default, 0);

    private void Emit(MeshPlacement placement, int subMesh, Material material, in DrawItem item, uint count, bool shared, RenderResourceID source, uint offset)
    {
        ArgumentNullException.ThrowIfNull(material);
        if ((item.LayerMask & _cullingMask) == 0 || (item.Flags & RenderableFlags.Hidden) != 0)
            return;
        if (material.IsDisposed || material.Draw is not { } draw || material.Table == null)
            return;
        if ((uint)subMesh >= (uint)placement.SubMeshes.Length)
            throw new ArgumentOutOfRangeException(nameof(subMesh), $"The mesh has {placement.SubMeshes.Length} submeshes.");

        RenderWorld.RenderSide render = _world.World.Render;
        ObjectAttributes attributes = new()
        {
            Flags = (uint)item.Flags,
            ObjectId = item.ObjectId,
            PreviousTransform = ObjectAttributes.NoPrevious,
            LightmapTexture = ObjectAttributes.NoLightmap,
            ProbeIndex = item.Probe.IsValid ? _world.World.Probes.AnchorSlotOf(item.Probe.Index) : uint.MaxValue,
            LodFade = 1,
            LayerMask = item.LayerMask,
            LightmapScaleOffset = new Float4(1, 1, 0, 0),
            Color = item.Color,
            Custom0 = item.Custom0,
            Custom1 = item.Custom1,
        };
        uint objectIndex = render.AddTransientObject(item.Transform, attributes);
        bool mirrored = RenderWorld.RenderSide.Determinant3(item.Transform) < 0;
        Double4x4 m = item.Transform;
        float depth = float.IsNaN(item.SortDepth) ? Length(_frustum.Relative(new Double3(m.c3.X, m.c3.Y, m.c3.Z))) : item.SortDepth;

        BucketSet.ChunkRef[] refs = Chunks(placement, subMesh);
        PrimitiveTopology topology = placement.SubMeshes[subMesh].Topology;
        RenderableFlags flags = item.Flags;
        bool shadowsOnly = (flags & RenderableFlags.ShadowsOnly) != 0;

        void Into(DrawList list, PassType pass, bool sorted)
        {
            if (!_world.Programs.Hold(draw.Sides?.Front ?? draw, pass, out ushort program))
                return;
            BucketSet.ItemDraw entry = Next();
            entry.BackProgram = draw.Sides is { } sides && _world.Programs.Hold(sides.Back, pass, out ushort back) ? back : -1;
            entry.Provided = true;
            entry.Object = (int)objectIndex;
            entry.Program = program;
            entry.Page = placement.Page;
            entry.Topology = topology;
            entry.Mirrored = mirrored;
            entry.Queue = draw.RenderQueue;
            entry.SubMesh = (uint)subMesh;
            entry.Chunks = refs;
            entry.Depth = depth;
            entry.MaterialSlot = material.Slot;
            entry.Shared = shared;
            entry.InstanceCount = count;
            if (!_lists.TryGetValue(list, out ProvidedItems? items))
                _lists[list] = items = new ProvidedItems();
            (sorted ? items.Sorted : items.Opaque).Add(entry);
            if (source.IsValid)
                Counts.Add((entry, list, source, offset));
        }

        // The rules renderables' buckets follow: built in passes and feature passes skip shadow only items, the shadow pass draws every caster unsorted
        if (!shadowsOnly && !_shadowView)
        {
            if (draw.Transparent)
            {
                Into(_camera.Transparent, PassType.Forward, sorted: true);
            }
            else
            {
                Into(_camera.Prepass, PassType.Prepass, sorted: false);
                Into(_camera.Forward, PassType.Forward, sorted: false);
            }
        }
        foreach ((PassType type, DrawList list) in _camera.PassLists)
        {
            bool shadows = type == PassType.ShadowCaster;
            bool skip = shadows ? (flags & RenderableFlags.CastShadows) == 0 || !draw.CastShadows : shadowsOnly;
            if (!skip)
                Into(list, type, sorted: draw.Transparent && !shadows);
        }
    }

    // A submesh's chunks, made once per placement
    private static BucketSet.ChunkRef[] Chunks(MeshPlacement placement, int subMesh)
    {
        BucketSet.ChunkRef[]?[] bySubMesh = s_chunks.GetValue(placement, p => new BucketSet.ChunkRef[]?[p.SubMeshes.Length]);
        if (bySubMesh[subMesh] is { } known)
            return known;
        List<BucketSet.ChunkRef> chunks = new();
        for (int c = 0; c < placement.Chunks.Length; c++)
        {
            MeshChunk chunk = placement.Chunks[c];
            if (chunk.SubMesh == subMesh)
                chunks.Add(new BucketSet.ChunkRef(placement.FirstChunk + (uint)c, c, placement.IndexBase + chunk.IndexStart, chunk.IndexCount));
        }
        return bySubMesh[subMesh] = [.. chunks];
    }

    private static float Length(Float3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private BucketSet.ItemDraw Next()
    {
        if (_used == _pool.Count)
        {
            _pool.Add(new BucketSet.ItemDraw
            {
                Pass = 1,
                Object = 0,
                Program = 0,
                Page = 0,
                Topology = default,
                Mirrored = false,
                Queue = 0,
                SubMesh = 0,
                Chunks = [],
            });
        }
        BucketSet.ItemDraw item = _pool[_used++];
        item.FirstCommand = -1;
        return item;
    }
}
