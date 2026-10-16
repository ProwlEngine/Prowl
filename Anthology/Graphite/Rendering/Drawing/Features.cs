using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Where in a view's frame a feature runs, in this order. Spec 6.3 lists what each holds.</summary>
public enum RenderSlot : byte
{
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
    internal FeatureView(in ViewDesc desc, uint width, uint height, bool sceneColor = false)
    {
        SceneColor = sceneColor;
        World = desc.World;
        Kind = desc.Kind;
        ViewId = desc.ViewId;
        LayerCount = desc.LayerCount;
        Width = width;
        Height = height;
        RenderScale = desc.RenderScale;
        Position = desc.Layers[0].Position;
        OutputWidth = desc.Target is { } target ? Math.Max(1u, (uint)MathF.Round(target.Width * desc.Viewport.Z)) : width;
        OutputHeight = desc.Target is { } output ? Math.Max(1u, (uint)MathF.Round(output.Height * desc.Viewport.W)) : height;
    }

    /// <summary>The view as features after an upscale see it, its colour at the output resolution.</summary>
    internal FeatureView AtOutput() => this with { Width = OutputWidth, Height = OutputHeight, RenderScale = 1, Upscaled = true };

    public RenderWorld World { get; }
    public ViewKind Kind { get; }
    public int ViewId { get; }

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

    /// <summary>Whether the view keeps an opaque colour copy, made once one of its transparent passes read it.</summary>
    internal bool SceneColor { get; }

    /// <summary>A texture sized as a part of the colour's resolution, <see cref="Width"/> by <see cref="Height"/>, an array on layered views.</summary>
    public GraphTextureDesc Texture(PixelFormat format, float scale = 1)
        => Upscaled
            ? Texture(Math.Max(1, (int)(Width * scale)), Math.Max(1, (int)(Height * scale)), format)
            : GraphTextureDesc.ViewSized(format, scale) with { ArrayLayers = LayerCount > 1 ? (uint)LayerCount : 0 };

    /// <summary>A texture of a fixed size, an array on layered views.</summary>
    public GraphTextureDesc Texture(int width, int height, PixelFormat format) => GraphTextureDesc.Sized(width, height, false, format) with { ArrayLayers = LayerCount > 1 ? (uint)LayerCount : 0 };

    /// <summary>A texture of one layer whatever the view, for state the view's layers share.</summary>
    public static GraphTextureDesc Shared(int width, int height, PixelFormat format) => GraphTextureDesc.Sized(width, height, false, format);
}

/// <summary>Features added to a world or a view. Host thread.</summary>
public sealed class FeatureSet : IEnumerable<RenderFeature>
{
    private readonly List<RenderFeature> _features = new();
    private RenderFeature[] _snapshot = [];
    private int _version, _snapshotVersion = -1;

    public int Count => _features.Count;

    public void Add(RenderFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (_features.Contains(feature))
            return;
        _features.Add(feature);
        _version++;
    }

    public bool Remove(RenderFeature feature)
    {
        if (!_features.Remove(feature))
            return false;
        _version++;
        return true;
    }

    public bool Contains(RenderFeature feature) => _features.Contains(feature);

    public IEnumerator<RenderFeature> GetEnumerator() => _features.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The features as of this call, the same array while the set does not change. Host thread.</summary>
    internal RenderFeature[] Snapshot()
    {
        if (_snapshotVersion != _version)
        {
            _snapshot = [.. _features];
            _snapshotVersion = _version;
        }
        return _snapshot;
    }
}

/// <summary>
/// Work a host adds to views: a pass in a slot of every view of a world, or of one view. Its slot, order, kinds, group
/// and Enabled are read at SubmitFrame along with <see cref="Extract"/> on the host thread, everything else runs on the
/// render thread. Of features sharing an <see cref="ExclusiveGroup"/> the view's wins over the world's, which wins over
/// the built in one, so a feature replaces a built in one by taking its group: Prepass, Opaque, Sky, SceneColor or
/// Transparent. A feature that throws fails the frame.
/// </summary>
/// <summary>
/// Scene targets past colour and depth that a feature reads. A view allocates the ones its features read: motion as the
/// second target, then normals and material together, each also bringing the ones before it.
/// </summary>
[Flags]
public enum SceneTargets
{
    None = 0,
    Motion = 1,
    Normals = 2,
    Material = 4,
}

public abstract class RenderFeature
{
    private int _version;

    public abstract RenderSlot Slot { get; }

    /// <summary>Order inside the slot, built in features at 0.</summary>
    public virtual int Order { get; set; }

    public virtual ViewKinds RunsOn => ViewKinds.Cameras;
    public virtual string? ExclusiveGroup => null;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Halton phases the view's projection jitters through while the feature runs, for temporal effects, 0 for none. The
    /// host's projection stays as it gave it; shaders read the jitter from the view block.
    /// </summary>
    public virtual int JitterPhases => 0;

    /// <summary>The texture LOD bias the view's samplers take while it jitters for this feature.</summary>
    public virtual float JitterMipBias => 0;

    /// <summary>The scene targets past colour and depth the feature reads, so its views have them.</summary>
    public virtual SceneTargets SceneReads => SceneTargets.None;

    /// <summary>Copies settings the render thread reads. Host thread, at SubmitFrame.</summary>
    public virtual void Extract() { }

    /// <summary>Whether the feature runs on a view this frame. Turning it on or off rebuilds that view's graph. Render thread.</summary>
    public virtual bool IsActive(in FeatureView view) => true;

    /// <summary>Once a frame before any view, for per frame work. Render thread.</summary>
    public virtual void PrepareFrame() { }

    /// <summary>Declares what the feature reads and writes, when the view's graph is built. Render thread.</summary>
    public abstract void Setup(FeatureBuilder builder, in FeatureView view);

    /// <summary>Records the feature's work for a view. Render thread.</summary>
    public abstract void Render(FeatureContext context);

    /// <summary>Rebuilds the graphs of the views the feature runs on, after a change to what its Setup declares. Any thread.</summary>
    public void Invalidate() => Interlocked.Increment(ref _version);

    internal int Version => Volatile.Read(ref _version);

    internal bool EnabledSnapshot;
    internal RenderSlot SlotSnapshot;
    internal int OrderSnapshot;
    internal ViewKinds RunsOnSnapshot;
    internal string? GroupSnapshot;
    internal int JitterPhasesSnapshot;
    internal float JitterMipBiasSnapshot;
    internal SceneTargets SceneReadsSnapshot;

    // Host thread, at SubmitFrame
    internal void TakeSnapshot()
    {
        EnabledSnapshot = Enabled;
        SlotSnapshot = Slot;
        OrderSnapshot = Order;
        RunsOnSnapshot = RunsOn;
        GroupSnapshot = ExclusiveGroup;
        JitterPhasesSnapshot = Math.Max(JitterPhases, 0);
        JitterMipBiasSnapshot = JitterMipBias;
        SceneReadsSnapshot = SceneReads;
        if (this is ScreenLightingFeature lighting)
            (lighting.ScaleSnapshot, lighting.IrradianceSnapshot) = (Math.Clamp(lighting.Scale, 0.0625f, 1f), lighting.WritesIrradiance);
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
    public override RenderSlot Slot => RenderSlot.AfterPrepass;
    public override string? ExclusiveGroup => "ScreenLighting";

    /// <summary>The part of the view's render resolution the screen lighting takes, 1/16 to 1.</summary>
    public abstract float Scale { get; }

    /// <summary>Whether the feature writes indirect irradiance as well as visibility.</summary>
    public virtual bool WritesIrradiance => false;

    internal float ScaleSnapshot = 1;
    internal bool IrradianceSnapshot;
}

/// <summary>
/// A fullscreen effect made by <see cref="Renderer.CreateFullscreenProgram"/>, built for mono views and for layered views
/// through multiview.
/// </summary>
public sealed class FullscreenProgram : IDisposable
{
    private FullscreenProgram(GraphicsProgram mono, GraphicsProgram layered, bool source, bool lit, bool procedural)
    {
        Mono = mono;
        Layered = layered;
        HasSource = source;
        Lit = lit;
        Procedural = procedural;
    }

    /// <summary>Whether the program reads Graphite_Source.</summary>
    internal bool HasSource { get; }

    /// <summary>Whether the program reads the world's lights, shadows and textures through Graphite's library.</summary>
    public bool Lit { get; }

    /// <summary>Whether the program writes its own vertex stage, drawn through <see cref="FeatureContext.DrawProcedural"/>.</summary>
    public bool Procedural { get; }

    internal GraphicsProgram Mono { get; }
    internal GraphicsProgram Layered { get; }

    internal GraphicsProgram For(int layers) => layers > 1 ? Layered : Mono;

    public void Dispose()
    {
        Mono.Dispose();
        Layered.Dispose();
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

    internal static FullscreenProgram Create(GraphicsDevice device, IShaderCompiler? compiler, string fragment, string name, bool source = true, BlendStateDescription? blend = null, bool lit = false, bool procedural = false, string variant = "")
    {
        source &= !procedural;
        PassVariants variants = Pass(fragment, name, lit, procedural).Variants(compiler, device.BackendType);
        List<Keyword> keywords = Keywords(variants.Axes, variant);
        keywords.Add(new Keyword("GRAPHITE_NO_SOURCE", source ? "false" : "true"));
        GraphicsProgram Build(bool layered)
        {
            if (!variants.TryGetKey([.. keywords, new Keyword("GRAPHITE_LAYERED", layered ? "true" : "false")], out int key))
                throw new ArgumentException($"{name} has no variant '{variant}'.", nameof(variant));
            ShaderDescription description = variants.Require(key);
            description.BlendState = blend ?? BlendStateDescription.SingleOverrideBlend;
            description.DepthStencilState = DepthStencilStateDescription.Disabled;
            description.RasterizerState = RasterizerStateDescription.CullNone;
            if (lit)
                description.ResourceLayouts = LitLayouts(description.ResourceLayouts ?? []);
            GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
            program.Name = layered ? name + " layered" : name;
            return program;
        }

        GraphicsProgram mono = Build(false);
        try
        {
            return new FullscreenProgram(mono, Build(true), source, lit, procedural);
        }
        catch
        {
            mono.Dispose();
            throw;
        }
    }
}

/// <summary>
/// A texture a view's features pass along, owned by the view. Each write after a read gets a new graph id over the same
/// texture, so the graph keeps the features in their slot order.
/// </summary>
internal sealed class ChainTexture(string name) : IDisposable
{
    private readonly List<RenderResourceID> _ids = new();

    public RenderTexture? Texture;
    private TextureView? _arrayView;

    /// <summary>Whether a frame drew the texture since it was made, so readers find what a producer wrote.</summary>
    public bool Filled;
    public int Version;
    public bool ReadSinceWrite;
    public bool Written;

    public RenderResourceID Id
    {
        get
        {
            while (_ids.Count <= Version)
                _ids.Add(RenderResourceID.Intern($"{name}.{_ids.Count}"));
            return _ids[Version];
        }
    }

    public void Reset()
    {
        Version = 0;
        ReadSinceWrite = false;
        Written = false;
    }

    /// <summary>Recreates the texture when its size or layers change. Returns whether it did.</summary>
    public bool Ensure(GraphicsDevice device, in RenderTextureDescription desc, List<IDisposable> retired)
    {
        if (Texture != null && Texture.Desc == desc)
            return false;
        Release(retired);
        Filled = false;
        Texture = device.ResourceFactory.CreateRenderTexture(desc);
        Texture.Name = name;
        return true;
    }

    public void Release(List<IDisposable> retired)
    {
        if (_arrayView != null)
            retired.Add(_arrayView);
        if (Texture != null)
            retired.Add(Texture);
        (Texture, _arrayView, Filled) = (null, null, false);
    }

    /// <summary>The texture's first colour viewed as an array whatever its layer count, made the first time.</summary>
    public TextureView ArrayView(GraphicsDevice device)
        => _arrayView ??= device.ResourceFactory.CreateTextureView(new TextureViewDescription(Texture!.ColorTextures[0]) { Arrayed = true });

    public void Dispose()
    {
        _arrayView?.Dispose();
        Texture?.Dispose();
    }
}

/// <summary>
/// The textures a view's features pass along: the scene target with its depth and G-buffer, and two colours ping ponging
/// after it, made the first time a feature writes colour.
/// </summary>
internal sealed class FeatureChain(GraphicsDevice device, int viewId, List<IDisposable> retired) : IDisposable
{
    /// <summary>The scene's colour, motion, normal and material formats, a view taking as many as its targets need.</summary>
    public static readonly PixelFormat[] SceneFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_Float, PixelFormat.R16_G16_SNorm, PixelFormat.R8_G8_UNorm];

    public static int SceneTargetCount(SceneTargets targets)
        => (targets & (SceneTargets.Normals | SceneTargets.Material)) != 0 ? 4 : (targets & SceneTargets.Motion) != 0 ? 2 : 1;

    public readonly ChainTexture Scene = new($"Graphite.Scene {viewId}");
    public readonly ChainTexture[] Colors = [new($"Graphite.ColorA {viewId}"), new($"Graphite.ColorB {viewId}")];

    /// <summary>The colours at the output resolution, ping ponging after an upscaler wrote the first.</summary>
    public readonly ChainTexture[] OutputColors = [new($"Graphite.OutputA {viewId}"), new($"Graphite.OutputB {viewId}")];

    /// <summary>Whether a feature upscaled the colour, so later ones write the output colours.</summary>
    public bool Upscaled;

    /// <summary>The screen lighting the forward pass reads: visibility, and indirect irradiance with its confidence.</summary>
    public readonly ChainTexture ScreenVisibility = new($"Graphite.ScreenVisibility {viewId}");
    public readonly ChainTexture ScreenIrradiance = new($"Graphite.ScreenIrradiance {viewId}");

    /// <summary>Whether the colour is display encoded, a feature having said so through EncodesForDisplay.</summary>
    public bool DisplayEncoded;
    public readonly List<IDisposable> Retired = retired;

    /// <summary>The colour features read and draw onto now: the scene, or the last colour a feature wrote.</summary>
    public ChainTexture Current = null!;
    public readonly Dictionary<string, TextureHandle> Published = new(StringComparer.Ordinal);

    /// <summary>
    /// The opaque colour with its mips that passes tagged "SceneColor" = "Read" sample, made the first time the view's
    /// transparent list holds one and kept until the view's textures go. Used says whether this frame's list holds one.
    /// </summary>
    public Texture? SceneColor;
    public bool SceneColorUsed;
    private TextureView? _sceneColorView;

    private uint _width, _height, _layers, _outputWidth, _outputHeight;

    /// <summary>Makes the scene colour for the view's size. Returns whether it did.</summary>
    public bool EnsureSceneColor()
    {
        uint layers = Math.Max(_layers, 1u);
        if (SceneColor is { } existing && existing.Width == _width && existing.Height == _height && existing.ArrayLayers == layers)
            return false;
        ReleaseSceneColor();
        uint mips = (uint)Math.Log2(Math.Max(_width, _height)) + 1;
        SceneColor = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(_width, _height, mips, layers, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        SceneColor.Name = $"Graphite.SceneColor {viewId}";
        return true;
    }

    /// <summary>The scene colour viewed as an array whatever its layer count.</summary>
    public TextureView SceneColorView()
        => _sceneColorView ??= device.ResourceFactory.CreateTextureView(new TextureViewDescription(SceneColor!) { Arrayed = true });

    private void ReleaseSceneColor()
    {
        if (_sceneColorView != null)
            Retired.Add(_sceneColorView);
        if (SceneColor != null)
            Retired.Add(SceneColor);
        (SceneColor, _sceneColorView) = (null, null);
    }

    public void Reset()
    {
        Scene.Reset();
        Colors[0].Reset();
        Colors[1].Reset();
        OutputColors[0].Reset();
        OutputColors[1].Reset();
        ScreenVisibility.Reset();
        ScreenIrradiance.Reset();
        Current = Scene;
        DisplayEncoded = false;
        Upscaled = false;
        Published.Clear();
    }

    /// <summary>Sizes the screen lighting textures for a producer at a part of the view's size.</summary>
    public void EnsureScreenLighting(float scale, bool irradiance)
    {
        uint width = Math.Max(1, (uint)(_width * scale)), height = Math.Max(1, (uint)(_height * scale));
        ScreenVisibility.Ensure(device, new RenderTextureDescription(width, height, PixelFormat.R8_UNorm, false) { ArrayLayers = _layers }, Retired);
        if (irradiance)
            ScreenIrradiance.Ensure(device, new RenderTextureDescription(width, height, PixelFormat.R16_G16_B16_A16_Float, false) { ArrayLayers = _layers }, Retired);
        else
            ScreenIrradiance.Release(Retired);
    }

    /// <summary>Sizes the textures for a view, the scene with the targets its features read. Returns whether any was recreated.</summary>
    public bool Ensure(uint width, uint height, int layers, SceneTargets targets, uint outputWidth, uint outputHeight)
    {
        (_width, _height, _layers, _outputWidth, _outputHeight) = (width, height, layers > 1 ? (uint)layers : 0, outputWidth, outputHeight);
        PixelFormat[] formats = SceneFormats[..SceneTargetCount(targets)];
        bool changed = Scene.Ensure(device, new RenderTextureDescription(width, height, formats, PixelFormat.D32_Float) { ArrayLayers = _layers }, Retired);
        foreach (ChainTexture color in (ChainTexture[])[.. Colors, .. OutputColors])
        {
            if (color.Texture != null)
                changed |= EnsureColor(color);
        }
        if (SceneColor != null)
            EnsureSceneColor();
        return changed;
    }

    public bool EnsureColor(ChainTexture color)
    {
        bool output = Array.IndexOf(OutputColors, color) >= 0;
        return color.Ensure(device, new RenderTextureDescription(output ? _outputWidth : _width, output ? _outputHeight : _height, PixelFormat.R16_G16_B16_A16_Float, false) { ArrayLayers = _layers }, Retired);
    }

    /// <summary>The colour after the current one, at the output resolution once upscaled, made the first time.</summary>
    public ChainTexture NextColor()
    {
        ChainTexture[] pair = Upscaled ? OutputColors : Colors;
        ChainTexture next = Current == pair[0] ? pair[1] : pair[0];
        if (next.Texture == null)
            EnsureColor(next);
        return next;
    }

    /// <summary>Lets the colours go after a graph that wrote none of them.</summary>
    public void ReleaseUnwritten()
    {
        foreach (ChainTexture color in (ChainTexture[])[.. Colors, .. OutputColors])
        {
            if (!color.Written)
                color.Release(Retired);
        }
    }

    /// <summary>Lets every texture go, for a view not drawn for a while.</summary>
    public void Release()
    {
        Scene.Release(Retired);
        Colors[0].Release(Retired);
        Colors[1].Release(Retired);
        OutputColors[0].Release(Retired);
        OutputColors[1].Release(Retired);
        ScreenVisibility.Release(Retired);
        ScreenIrradiance.Release(Retired);
        ReleaseSceneColor();
    }

    public void Dispose()
    {
        _sceneColorView?.Dispose();
        SceneColor?.Dispose();
        Scene.Dispose();
        Colors[0].Dispose();
        Colors[1].Dispose();
        OutputColors[0].Dispose();
        OutputColors[1].Dispose();
        ScreenVisibility.Dispose();
        ScreenIrradiance.Dispose();
    }
}

/// <summary>What a feature declares when its view's graph is built.</summary>
public sealed class FeatureBuilder
{
    private readonly FeaturePass _pass;
    private readonly RenderContextBuilder _graph;

    internal FeatureBuilder(FeaturePass pass, RenderContextBuilder graph)
    {
        _pass = pass;
        _graph = graph;
    }

    /// <summary>The underlying graph builder, for resources the methods here do not cover.</summary>
    public RenderContextBuilder Graph => _graph;

    /// <summary>Reads the scene's depth, normals, and roughness and metallic.</summary>
    public void ReadScene()
    {
        ChainTexture scene = _pass.Chain.Scene;
        if (!scene.Written)
            throw new InvalidOperationException($"{_pass.Name} reads the scene, which no earlier feature drew.");
        if (_pass.Output == scene)
            throw new InvalidOperationException($"{_pass.Name} reads the scene it draws onto.");
        scene.ReadSinceWrite = true;
        _pass.SceneRead = _graph.DeclareInputTexture(scene.Id, TextureState.Sampled, TextureState.Sampled);
    }

    /// <summary>Reads the current colour.</summary>
    public void ReadColor()
    {
        ChainTexture current = _pass.Chain.Current;
        if (!current.Written)
            throw new InvalidOperationException($"{_pass.Name} reads the colour, which no earlier feature drew.");
        if (_pass.Output == current)
            throw new InvalidOperationException($"{_pass.Name} reads the colour it draws onto.");
        current.ReadSinceWrite = true;
        _pass.Input = _pass.SceneRead?.Id == current.Id ? _pass.SceneRead : _graph.DeclareInputTexture(current.Id);
    }

    /// <summary>Draws onto the current colour: the scene with its depth and G-buffer, or the last colour a feature wrote.</summary>
    public void DrawToColor()
    {
        ChainTexture current = _pass.Chain.Current;
        if (_pass.Input?.Id == current.Id || _pass.SceneRead?.Id == current.Id)
            throw new InvalidOperationException($"{_pass.Name} draws onto the colour it reads.");
        Write(current);
    }

    /// <summary>
    /// Draws onto the scene with its depth and G-buffer, as opaque and transparent geometry does. When a feature wrote a
    /// new colour since the scene was last drawn, that colour is copied into the scene first.
    /// </summary>
    public void DrawToScene()
    {
        FeatureChain chain = _pass.Chain;
        if (_pass.SceneRead != null || (chain.Current == chain.Scene && _pass.Input != null))
            throw new InvalidOperationException($"{_pass.Name} draws onto the scene it reads.");
        if (chain.Current != chain.Scene)
        {
            chain.Current.ReadSinceWrite = true;
            _pass.CopyBack = _pass.Input?.Id == chain.Current.Id ? _pass.Input : _graph.DeclareInputTexture(chain.Current.Id);
            chain.Current = chain.Scene;
        }
        Write(chain.Scene);
    }

    /// <summary>Writes a new colour from the current one, which later features read. <see cref="FeatureContext.Fullscreen"/> draws it.</summary>
    public void WriteColor()
    {
        ReadColor();
        FeatureChain chain = _pass.Chain;
        ChainTexture next = chain.NextColor();
        Write(next);
        chain.Current = next;
    }

    /// <summary>
    /// Writes a new colour at the view's output resolution from the current one, as an upscaler does. Later features see
    /// the view at the output resolution, and the present draws it as it is.
    /// </summary>
    public void WriteOutputColor()
    {
        ReadColor();
        FeatureChain chain = _pass.Chain;
        chain.Upscaled = true;
        ChainTexture next = chain.NextColor();
        Write(next);
        chain.Current = next;
    }

    /// <summary>
    /// Says the colour this feature writes or draws is display encoded, as a tonemapper's is, so the present undoes the
    /// encode for targets that take linear light, sRGB and float ones. Later features keep it so.
    /// </summary>
    public void EncodesForDisplay() => _pass.Chain.DisplayEncoded = true;

    /// <summary>A texture for this feature alone this frame, drawn and then read back in its pass by default.</summary>
    public TextureHandle Temporary(string name, GraphTextureDesc desc, TextureState usage = TextureState.Attachment | TextureState.Sampled)
        => _graph.DeclareOutputTexture(RenderResourceID.Intern($"Graphite.Feature.{_pass.Id}.Temporary.{name}"), desc, usage: usage);

    /// <summary>
    /// A texture for this feature that keeps its last frames, read through <see cref="FeatureContext.History"/>. A graph
    /// rebuild starts it over. Usage adds Sampled for a feature that reads it back in the same pass it draws it.
    /// </summary>
    public TextureHandle History(string name, GraphTextureDesc desc, int frames = 1, TextureState usage = TextureState.Attachment)
        => _graph.DeclareOutputTexture(RenderResourceID.Intern($"Graphite.Feature.{_pass.Id}.History.{name}"), desc, history: frames, usage: usage);

    /// <summary>
    /// Asks for the visible renderables whose materials have a pass of this type, the shader pass tagged
    /// "LightMode" = type, for <see cref="FeatureContext.DrawPass"/> to draw: opaque ones in bucket order, then transparent
    /// ones back to front. Any pass type works; the first time one is asked for, every renderable of the world is bucketed
    /// for it. Prepass and Forward are the built in opaque lists.
    /// </summary>
    public void DrawsPass(PassType type)
    {
        DrawsRenderables();
        DrawList list = _pass.Camera.ListFor(type);
        if (!_pass.Lists.Contains(list))
            _pass.Lists.Add(list);
    }

    /// <summary>
    /// Declares the feature records the view's renderables or sky through the context's Draw methods, or draws lit
    /// programs, which read the view's lights, shadows and probes.
    /// </summary>
    public void DrawsRenderables()
    {
        if (_pass.Draws)
            return;
        _pass.Draws = true;
        DeviceBuffer[] host = _pass.Camera.HostBuffers;
        for (int i = 0; i < host.Length; i++)
            _graph.DeclareImportedBuffer(FramePipeline.BufferId(i), host[i], BufferAccess.AllReads);
        FeatureChain chain = _pass.Chain;
        foreach (ChainTexture lighting in (ReadOnlySpan<ChainTexture>)[chain.ScreenVisibility, chain.ScreenIrradiance])
        {
            if (lighting.Written)
                _graph.DeclareInputTexture(lighting.Id, TextureState.Sampled, TextureState.Sampled);
        }
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
        FeatureChain chain = _pass.Chain;
        ChainTexture visibility = chain.ScreenVisibility;
        visibility.Written = true;
        TextureHandle visibilityHandle = _graph.DeclareImportedTexture(visibility.Id, visibility.Texture!);
        if (!irradiance)
            return (visibilityHandle, null);
        chain.ScreenIrradiance.Written = true;
        return (visibilityHandle, _graph.DeclareImportedTexture(chain.ScreenIrradiance.Id, chain.ScreenIrradiance.Texture!));
    }

    /// <summary>State this feature keeps for this view, made the first time it is asked for and disposed with the view.</summary>
    public T GetViewState<T>() where T : class, new() => _pass.Camera.State<T>(_pass.Feature);

    /// <summary>Hands a texture to later features of the view by name.</summary>
    public void Publish(string name, TextureHandle texture) => _pass.Chain.Published[name] = texture;

    /// <summary>A texture an earlier feature of the view published.</summary>
    public TextureHandle Read(string name)
    {
        if (!_pass.Chain.Published.TryGetValue(name, out TextureHandle texture))
            throw new InvalidOperationException($"{_pass.Name} reads {name}, which no earlier feature published.");
        return _graph.DeclareInputTexture(texture.Id);
    }

    private void Write(ChainTexture texture)
    {
        if (_pass.Output != null)
            throw new InvalidOperationException($"{_pass.Name} draws onto more than one colour.");
        if (texture.ReadSinceWrite)
        {
            texture.Version++;
            texture.ReadSinceWrite = false;
        }
        _pass.Clears = !texture.Written && _pass.Input == null && _pass.CopyBack == null;
        texture.Written = true;
        _pass.Output = texture;
        _pass.OutputId = _graph.DeclareImportedTexture(texture.Id, texture.Texture!);
    }
}

/// <summary>What a feature records with.</summary>
public sealed class FeatureContext
{
    private readonly FeaturePass _pass;
    private readonly PropertySet _properties = new();

    internal FeatureContext(FeaturePass pass) => _pass = pass;

    internal FeatureChain Chain => _pass.Chain;

    internal RenderContext Graph { get; set; } = null!;

    public CommandBuffer Command { get; internal set; } = null!;
    public FeatureView View { get; internal set; }

    /// <summary>The scene's depth target, when the feature reads the scene.</summary>
    public Texture? Depth => Scene?.DepthTexture;

    /// <summary>How far each opaque surface moved across the screen since last frame, in uv, when the feature reads the scene and declares it in SceneReads.</summary>
    public Texture? Motion => SceneTarget(1);

    /// <summary>The scene's octahedral world normals, when the feature reads the scene and declares them in SceneReads.</summary>
    public Texture? Normals => SceneTarget(2);

    /// <summary>The scene's roughness and metallic, when the feature reads the scene and declares them in SceneReads.</summary>
    public Texture? Material => SceneTarget(3);

    private Texture? SceneTarget(int index) => Scene is { } scene && index < scene.ColorTextures.Length ? scene.ColorTextures[index] : null;

    /// <summary>The colour the feature reads, when it reads or writes colour.</summary>
    public Texture? Color => _pass.Input is { } input ? Graph.GetRenderTexture(input).ColorTextures[0] : null;

    private RenderTexture? Scene => _pass.SceneRead is { } scene ? Graph.GetRenderTexture(scene) : null;

    /// <summary>A texture the feature declared.</summary>
    public RenderTexture Get(TextureHandle texture) => Graph.GetRenderTexture(texture);

    /// <summary>The view's first layer as shaders read it this frame, jittered when the view jitters.</summary>
    internal ViewBlock Block => _pass.Camera.Blocks[0];

    /// <summary>Whether the view drew last frame, so motion and history from then mean something.</summary>
    public bool HasPreviousFrame => _pass.Camera.HadPrevious;

    /// <summary>The frame's time as the host gave it.</summary>
    public FrameTime Time => _pass.Camera.Time;

    /// <summary>Whether a feature before this one in the view encoded the colour for display, as tonemappers do.</summary>
    public bool DisplayEncoded => _pass.Chain.DisplayEncoded;

    /// <summary>The renderer's noise: blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise => _pass.Frame.Noise;

    /// <summary>The SMAA reference's area and search tables, arrays of as many layers as the view has.</summary>
    internal (Texture Area, Texture Search) SmaaTables => View.LayerCount > 1 ? (_pass.Frame.SmaaAreaLayered, _pass.Frame.SmaaSearchLayered) : (_pass.Frame.SmaaArea, _pass.Frame.SmaaSearch);

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
    internal ReadOnlySpan<ViewBlock> Blocks => _pass.Camera.Blocks.AsSpan(0, View.LayerCount);

    /// <summary>A history texture as it was some frames ago, or null before it holds one.</summary>
    public RenderTexture? History(TextureHandle texture, int framesAgo = 1)
        => Graph.IsHistoryValid(texture) ? Graph.GetRenderTexture(texture, framesAgo) : null;

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

    /// <summary>Draws the visible renderables with a pass of a type asked for in Setup through <see cref="FeatureBuilder.DrawsPass"/>.</summary>
    public void DrawPass(PassType type)
    {
        if (_pass.Camera.FindList(type) is not { } list || !_pass.Lists.Contains(list))
            throw new InvalidOperationException($"{_pass.Name} draws the {type.Name} pass without asking for it in Setup through DrawsPass.");
        Drawer.DrawList(Command, list);
    }

    private void BindLighting(FullscreenProgram program)
    {
        if (!program.Lit)
            return;
        if (!_pass.Draws)
            throw new InvalidOperationException($"{_pass.Name} draws a lit fullscreen program without declaring DrawsRenderables in Setup.");
        _pass.Camera.Drawer.BindTables(Command);
    }

    private SceneDrawer Drawer => _pass.Draws ? _pass.Camera.Drawer
        : throw new InvalidOperationException($"{_pass.Name} draws renderables without declaring DrawsRenderables or DrawsPass in Setup.");

    /// <summary>
    /// Binds the colour the feature draws onto or writes, cleared when this is the first feature drawing it, the scene
    /// clearing its depth and G-buffer with it, and copies a colour written since into the scene.
    /// </summary>
    public void BeginOutput()
    {
        if (_pass.OutputId is not { } output)
            throw new InvalidOperationException($"{_pass.Name} declared no colour to draw onto or write.");
        Framebuffer framebuffer = Graph.GetRenderTexture(output).Framebuffer;
        bool scene = _pass.Output == _pass.Chain.Scene;
        if (_pass.Clears)
        {
            Float4 clear = _pass.Camera.Desc.Overlay ? default : _pass.Camera.Desc.ClearColor with { W = 1 };
            AttachmentOps depth = scene ? AttachmentOps.Clear(0f) : AttachmentOps.Loaded;
            Command.SetFramebuffer(framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W)), depth));
            Command.SetFullViewport();
            if (scene)
            {
                for (uint target = 1; target < framebuffer.ColorTargets.Count; target++)
                    Command.ClearColorTarget(target, new Color(0, 0, 0, 0));
            }
            return;
        }

        bool overwritten = _pass.Input != null && !scene;
        AttachmentOps color = overwritten ? new AttachmentOps(LoadAction.DontCare, StoreAction.Store) : AttachmentOps.Loaded;
        Command.SetFramebuffer(framebuffer, new TargetLoadStoreOps(color, AttachmentOps.Loaded));
        Command.SetFullViewport();
        if (_pass.CopyBack is { } copy)
        {
            Command.SetShader(_pass.Frame.CopyBackProgram.For(View.LayerCount));
            Command.SetVertexSource(VertexSource.None);
            _properties.SetTexture("Graphite_Source", Graph.GetRenderTexture(copy).ColorTextures[0], _pass.Frame.LinearSampler);
            Command.SetProperties(_properties);
            Command.Draw(3);
        }
    }

    /// <summary>
    /// Draws a fullscreen program over a texture the feature declared, Temporary or History, the colour it reads bound as
    /// Graphite_Source, with any properties given. Clear first clears it to transparent black, for programs that discard.
    /// Shared draws once over a texture of one layer, for state a layered view's layers share.
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
        AttachmentOps color = clear ? AttachmentOps.Clear(new Color(0, 0, 0, 0)) : AttachmentOps.Loaded;
        Command.SetFramebuffer(target.Framebuffer, new TargetLoadStoreOps(color, AttachmentOps.Loaded));
        Command.SetFullViewport();
        Command.SetShader(program.For(View.LayerCount));
        Command.SetVertexSource(VertexSource.None);
        BindLighting(program);
        _properties.SetBuffer("Graphite_PostViews", _pass.Camera.ViewBlocks.Buffer);
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
        AttachmentOps color = clear ? AttachmentOps.Clear(new Color(0, 0, 0, 0)) : new AttachmentOps(LoadAction.DontCare, StoreAction.Store);
        Command.SetFramebuffer(target.Framebuffer, new TargetLoadStoreOps(color, AttachmentOps.Loaded));
        Command.SetFullViewport();
        Command.SetShader(program.For(layers));
        Command.SetVertexSource(VertexSource.None);
        BindLighting(program);
        _properties.SetBuffer("Graphite_PostViews", _pass.Camera.ViewBlocks.Buffer);
        if (program.HasSource && Color is { } source)
            _properties.SetTexture("Graphite_Source", source, _pass.Frame.TrilinearClamp);
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
        if (Color is not { } source || _pass.Output == _pass.Chain.Scene)
            throw new InvalidOperationException($"{_pass.Name} draws a fullscreen program without writing a new colour, declare WriteColor.");
        BeginOutput();
        Command.SetShader(program.For(View.LayerCount));
        Command.SetVertexSource(VertexSource.None);
        BindLighting(program);
        _properties.SetBuffer("Graphite_PostViews", _pass.Camera.ViewBlocks.Buffer);
        _properties.SetTexture("Graphite_Source", source, _pass.Frame.TrilinearClamp);
        Command.SetProperties(_properties);
        if (properties != null)
            Command.SetProperties(properties);
        Command.Draw(3);
    }

}

/// <summary>Runs one feature as a pass of its view's graph.</summary>
internal sealed class FeaturePass : IPass
{
    private static int s_nextId;
    private readonly FeatureContext _context;

    public FeaturePass(RenderFeature feature, CameraFrame camera, FramePipeline frame)
    {
        Feature = feature;
        Camera = camera;
        Frame = frame;
        Name = feature.GetType().Name;
        Id = Interlocked.Increment(ref s_nextId);
        _context = new FeatureContext(this);
    }

    public RenderFeature Feature { get; }
    public CameraFrame Camera { get; }
    public FramePipeline Frame { get; }
    public FeatureChain Chain => Camera.Chain;
    public string Name { get; }
    public int Id { get; }

    /// <summary>The feature's version its declarations were made at.</summary>
    public int BuiltVersion = -1;

    /// <summary>The view as the feature saw it at Setup, at the output resolution after an upscale.</summary>
    public FeatureView View;

    public readonly List<DrawList> Lists = new();
    public bool Draws;
    public TextureHandle? Input;
    public TextureHandle? SceneRead;
    public TextureHandle? CopyBack;
    public ChainTexture? Output;
    public TextureHandle? OutputId;
    public bool Clears;

    public void Setup(RenderContextBuilder builder)
    {
        Input = null;
        SceneRead = null;
        CopyBack = null;
        Output = null;
        OutputId = null;
        Clears = false;
        Draws = false;
        Lists.Clear();
        BuiltVersion = Feature.Version;
        View = Chain.Upscaled ? Camera.Info.AtOutput() : Camera.Info;
        Feature.Setup(new FeatureBuilder(this, builder), View);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        _context.Graph = context;
        _context.Command = cmd;
        _context.View = View;
        Feature.Render(_context);
        if (Feature is ScreenLightingFeature)
        {
            Chain.ScreenVisibility.Filled |= Chain.ScreenVisibility.Written;
            Chain.ScreenIrradiance.Filled |= Chain.ScreenIrradiance.Written;
        }
    }
}

/// <summary>A view's first pass: its tables for the frame. The frame's buffers are made ready for it by a fence before the views.</summary>
internal sealed class SetupPass(FramePipeline frame, CameraFrame camera) : IPass
{
    public string Name => "Graphite.Setup";

    public void Setup(RenderContextBuilder builder)
    {
        camera.Chain.Reset();
        camera.ResetPassLists();
    }

    public void Render(RenderContext context, CommandBuffer cmd) => camera.Drawer.Prepare(context.Task.RingSlot);
}

/// <summary>A view's last pass: the colour the features left, copied into the view's viewport of its target.</summary>
internal sealed class PresentPass(FramePipeline frame, CameraFrame camera) : IPass
{
    // sRGB targets encode what they are given and float targets hold linear light, so display encoded colour is decoded
    // for both. Other formats take display values as they are.
    private static bool TakesLinear(PixelFormat format) => format is PixelFormat.R8_G8_B8_A8_UNorm_SRgb or PixelFormat.B8_G8_R8_A8_UNorm_SRgb
        or PixelFormat.R16_Float or PixelFormat.R16_G16_Float or PixelFormat.R16_G16_B16_A16_Float or PixelFormat.R11_G11_B10_Float
        or PixelFormat.R32_Float or PixelFormat.R32_G32_Float or PixelFormat.R32_G32_B32_A32_Float;

    private readonly PropertySet _properties = new();
    private TextureHandle? _source;
    private TextureHandle _target;

    public string Name => "Graphite.Present";

    public void Setup(RenderContextBuilder builder)
    {
        ChainTexture current = camera.Chain.Current;
        _source = current.Written ? builder.DeclareInputTexture(current.Id) : null;
        _target = builder.DeclareViewTarget(new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        Framebuffer target = context.GetRenderTexture(_target).Framebuffer;
        Float4 rect = camera.Desc.Viewport;
        Float4 clear = camera.Desc.ClearColor with { W = 1 };
        Color clearColor = new(clear.X, clear.Y, clear.Z, clear.W);
        bool whole = rect == new Float4(0, 0, 1, 1), overlay = camera.Desc.Overlay;
        if (overlay && _source == null)
            return;
        AttachmentOps ops = overlay ? AttachmentOps.Loaded
            : whole && _source == null || camera.Desc.ClearTarget ? AttachmentOps.Clear(clearColor)
            : whole ? new AttachmentOps(LoadAction.DontCare, StoreAction.Store) : AttachmentOps.Loaded;
        cmd.SetFramebuffer(target, new TargetLoadStoreOps(ops, AttachmentOps.Loaded));
        cmd.SetViewport(new Viewport(rect.X * target.Width, rect.Y * target.Height, rect.Z * target.Width, rect.W * target.Height, 0, 1));
        cmd.SetVertexSource(VertexSource.None);
        if (_source is { } source)
        {
            bool decode = camera.Chain.DisplayEncoded && TakesLinear(target.ColorTargets[0].Target.Format);
            cmd.SetShader(frame.Present(decode, overlay).For(camera.LayerCount));
            _properties.SetTexture("Graphite_Source", context.GetRenderTexture(source).ColorTextures[0], frame.LinearSampler);
        }
        else if (!whole)
        {
            cmd.SetShader(frame.ClearProgram.For(camera.LayerCount));
            _properties.SetFloat4("Graphite_ClearColor", clear);
        }
        else
        {
            return;
        }
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}

/// <summary>The depth prepass: the visible opaque renderables' depth before the opaque pass.</summary>
public sealed class PrepassFeature : RenderFeature
{
    internal PrepassFeature() { }

    public override RenderSlot Slot => RenderSlot.Prepass;
    public override string? ExclusiveGroup => "Prepass";
    public override ViewKinds RunsOn => ViewKinds.All;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawPrepass();
    }
}

/// <summary>The visible opaque renderables' forward pass, writing colour and the thin G-buffer.</summary>
public sealed class OpaqueFeature : RenderFeature
{
    internal OpaqueFeature() { }

    public override RenderSlot Slot => RenderSlot.Opaque;
    public override string? ExclusiveGroup => "Opaque";
    public override ViewKinds RunsOn => ViewKinds.All;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawOpaque();
    }
}

/// <summary>The world's sky material behind everything the view sees. Exclusive group "Sky".</summary>
public sealed class SkyFeature : RenderFeature
{
    internal SkyFeature() { }

    public override RenderSlot Slot => RenderSlot.Sky;
    public override ViewKinds RunsOn => ViewKinds.All;
    public override string? ExclusiveGroup => "Sky";

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawSky();
    }
}

/// <summary>The visible transparent renderables, back to front.</summary>
public sealed class TransparentFeature : RenderFeature
{
    internal TransparentFeature() { }

    public override RenderSlot Slot => RenderSlot.Transparent;
    public override string? ExclusiveGroup => "Transparent";
    public override ViewKinds RunsOn => ViewKinds.All;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawTransparent();
    }
}

/// <summary>
/// Copies the view's colour after the opaque surfaces and the sky into its scene colour and fills the mips, for passes
/// tagged "SceneColor" = "Read" to sample through Graphite/SceneColor.glsl. Active once such a pass was in the view's
/// transparent list, recording nothing on frames without one.
/// </summary>
public sealed class SceneColorFeature : RenderFeature
{
    internal SceneColorFeature() { }

    public override RenderSlot Slot => RenderSlot.OpaqueColor;
    public override string? ExclusiveGroup => "SceneColor";
    public override ViewKinds RunsOn => ViewKinds.All;

    public override bool IsActive(in FeatureView view) => view.SceneColor;

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.ReadColor();

    public override void Render(FeatureContext context)
    {
        FeatureChain chain = context.Chain;
        if (!chain.SceneColorUsed || chain.SceneColor is not { } target || context.Color is not { } source)
            return;
        context.Command.CopyTexture(source, 0, 0, 0, 0, 0, target, 0, 0, 0, 0, 0, target.Width, target.Height, 1, target.ArrayLayers);
        context.Command.GenerateMipmaps(target);
    }
}

/// <summary>Picks a view's features each frame: built in, world and view features, filtered and ordered.</summary>
internal static class FeatureSelection
{
    public static readonly RenderFeature[] BuiltIn = [new PrepassFeature(), new OpaqueFeature(), new SkyFeature(), new SceneColorFeature(), new TransparentFeature()];

    static FeatureSelection()
    {
        foreach (RenderFeature feature in BuiltIn)
            feature.TakeSnapshot();
    }

    public static void Select(List<RenderFeature> result, RenderFeature[] world, RenderFeature[] view, in FeatureView info)
    {
        result.Clear();
        Add(result, BuiltIn, info);
        Add(result, world, info);
        Add(result, view, info);

        // Later entries win: the view's over the world's over the built in ones
        for (int i = result.Count - 1; i > 0; i--)
        {
            if (i >= result.Count || result[i].GroupSnapshot is not { } group)
                continue;
            for (int j = i - 1; j >= 0; j--)
            {
                if (result[j].GroupSnapshot == group)
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

    private static int Compare(RenderFeature a, RenderFeature b)
    {
        int slot = a.SlotSnapshot.CompareTo(b.SlotSnapshot);
        return slot != 0 ? slot : a.OrderSnapshot.CompareTo(b.OrderSnapshot);
    }

    private static void Add(List<RenderFeature> result, RenderFeature[] features, in FeatureView info)
    {
        ViewKinds kind = (ViewKinds)(1 << (int)info.Kind);
        foreach (RenderFeature feature in features)
        {
            if (!feature.EnabledSnapshot || (feature.RunsOnSnapshot & kind) == 0 || result.Contains(feature))
                continue;
            if (feature.IsActive(info))
                result.Add(feature);
        }
    }
}

/// <summary>Providers added to a world. Host thread.</summary>
public sealed class ProviderSet : IEnumerable<RenderProvider>
{
    private readonly List<RenderProvider> _providers = new();
    private RenderProvider[] _snapshot = [];
    private int _version, _snapshotVersion = -1;

    public int Count => _providers.Count;

    public void Add(RenderProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Contains(provider))
            return;
        _providers.Add(provider);
        _version++;
    }

    public bool Remove(RenderProvider provider)
    {
        if (!_providers.Remove(provider))
            return false;
        _version++;
        return true;
    }

    public IEnumerator<RenderProvider> GetEnumerator() => _providers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal RenderProvider[] Snapshot()
    {
        if (_snapshotVersion != _version)
        {
            _snapshot = [.. _providers];
            _snapshotVersion = _version;
        }
        return _snapshot;
    }
}

/// <summary>
/// Geometry a host draws into each view without making renderables: particles, terrain patches, GPU generated draws. Its
/// items join the view's passes like renderables, opaque ones in the prepass and opaque pass, transparent ones sorted
/// with the world's, and any pass type a feature draws. <see cref="Extract"/> runs on the host thread at SubmitFrame,
/// <see cref="Cull"/> on the render thread once per view.
/// </summary>
public abstract class RenderProvider
{
    public bool Enabled { get; set; } = true;

    /// <summary>The kinds of view it draws into, read at SubmitFrame.</summary>
    public virtual ViewKinds RunsOn => ViewKinds.All;

    /// <summary>Copies what Cull reads. Host thread, at SubmitFrame.</summary>
    public virtual void Extract() { }

    /// <summary>Adds this view's items to the sink. Render thread.</summary>
    public abstract void Cull(in ProviderView view, DrawItemSink sink);

    /// <summary>The buffers <see cref="Compute"/> reads and writes, so the frame orders it before what reads them. Compute runs only for providers naming some. Render thread.</summary>
    public virtual void ComputeBuffers(List<DeviceBuffer> buffers) { }

    /// <summary>
    /// GPU work recorded once a frame, after the frame's uploads and before any view draws: the instance counts
    /// <see cref="DrawItemSink.AddIndirect"/> reads, instance data. Render thread.
    /// </summary>
    public virtual void Compute(CommandBuffer command) { }

    internal bool EnabledSnapshot;
    internal ViewKinds RunsOnSnapshot;
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
    }
}

/// <summary>A view list's provider items, opaque ones drawn after the list's buckets, sorted ones among its transparents.</summary>
internal sealed class ProvidedItems
{
    public readonly List<BucketSet.TransparentItem> Opaque = new();
    public readonly List<BucketSet.TransparentItem> Sorted = new();
}

/// <summary>Where a provider adds its items for a view. Valid during Cull only. Render thread.</summary>
public sealed class DrawItemSink
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshPlacement, BucketSet.ChunkRef[]?[]> s_chunks = new();
    private readonly List<BucketSet.TransparentItem> _pool = new();
    private readonly Dictionary<DrawList, ProvidedItems> _lists = new();
    private int _used;
    private CameraFrame _camera = null!;
    private WorldFrame _world = null!;
    private CullFrustum _frustum;
    private uint _cullingMask;
    private bool _shadowView;

    /// <summary>Indirect items whose instance count is copied from the provider's buffer into the view's commands.</summary>
    internal readonly List<(BucketSet.TransparentItem Item, DrawList List, DeviceBuffer Source, uint Offset)> Counts = new();

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
    public void Add(GpuMesh mesh, int subMesh, Material material, in DrawItem item) => Emit(mesh, subMesh, material, item, 1, false, null, 0);

    /// <summary>Instances of one submesh sharing one object, shaders telling them apart by Graphite_InstanceIndex.</summary>
    public void AddInstanced(GpuMesh mesh, int subMesh, Material material, in DrawItem item, uint count)
    {
        if (count > 0)
            Emit(mesh, subMesh, material, item, count, true, null, 0);
    }

    /// <summary>
    /// Instances of one submesh sharing one object, as many as a 32 bit count the GPU wrote at offset in instanceCount
    /// before the view draws, by a compute pass of the host's.
    /// </summary>
    public void AddIndirect(GpuMesh mesh, int subMesh, Material material, in DrawItem item, DeviceBuffer instanceCount, uint offset)
    {
        ArgumentNullException.ThrowIfNull(instanceCount);
        if (offset % 4 != 0 || offset + 4 > instanceCount.SizeInBytes)
            throw new ArgumentOutOfRangeException(nameof(offset), "The instance count is a 32 bit word inside the buffer.");
        Emit(mesh, subMesh, material, item, 0, true, instanceCount, offset);
    }

    private void Emit(GpuMesh mesh, int subMesh, Material material, in DrawItem item, uint count, bool shared, DeviceBuffer? source, uint offset)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        if ((item.LayerMask & _cullingMask) == 0 || (item.Flags & RenderableFlags.Hidden) != 0)
            return;
        if (mesh.Placement is not { } placement || material.IsDisposed || material.Draw is not { } draw || material.Table == null)
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
            if (!_world.Programs.TryGet(material, draw, pass, out ushort program))
                return;
            BucketSet.TransparentItem entry = Next();
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
            if (source != null)
                Counts.Add((entry, list, source, offset));
        }

        // The rules renderables' buckets follow: built in passes and feature passes skip shadow only items, the shadow pass draws only opaque casters
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
            bool skip = type == PassType.ShadowCaster
                ? (flags & RenderableFlags.CastShadows) == 0 || !draw.CastShadows || draw.Transparent
                : shadowsOnly;
            if (!skip)
                Into(list, type, sorted: draw.Transparent);
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

    private BucketSet.TransparentItem Next()
    {
        if (_used == _pool.Count)
        {
            _pool.Add(new BucketSet.TransparentItem
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
        BucketSet.TransparentItem item = _pool[_used++];
        item.FirstCommand = -1;
        return item;
    }
}

/// <summary>The draw programs of providers' materials, held while the material lives and draws the same. Render thread.</summary>
internal sealed class ProviderPrograms(DrawPrograms programs)
{
    private readonly Dictionary<(Material Material, string Pass), (MaterialDraw Draw, ushort Program, bool Has)> _entries = new();
    private readonly List<(Material, string)> _stale = new();

    public bool TryGet(Material material, MaterialDraw draw, PassType pass, out ushort program)
    {
        (Material, string) key = (material, pass.Name);
        if (_entries.TryGetValue(key, out (MaterialDraw Draw, ushort Program, bool Has) entry))
        {
            if (ReferenceEquals(entry.Draw, draw))
            {
                program = entry.Program;
                return entry.Has;
            }
            if (entry.Has)
                programs.Release(entry.Program);
        }
        bool has = programs.TryGet(draw, pass, out program);
        if (has)
            programs.Acquire(program);
        _entries[key] = (draw, program, has);
        return has;
    }

    /// <summary>Lets the programs of disposed materials go.</summary>
    public void Sweep()
    {
        _stale.Clear();
        foreach (KeyValuePair<(Material Material, string Pass), (MaterialDraw Draw, ushort Program, bool Has)> pair in _entries)
        {
            if (pair.Key.Material.IsDisposed)
                _stale.Add(pair.Key);
        }
        foreach ((Material, string) key in _stale)
            Remove(key);
    }

    public void Clear()
    {
        foreach ((Material, string) key in new List<(Material, string)>(_entries.Keys))
            Remove(key);
    }

    private void Remove((Material, string) key)
    {
        if (_entries.Remove(key, out (MaterialDraw Draw, ushort Program, bool Has) entry) && entry.Has)
            programs.Release(entry.Program);
    }
}
