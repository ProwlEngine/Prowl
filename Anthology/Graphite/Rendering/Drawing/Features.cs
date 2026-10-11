using System;
using System.Collections;
using System.Collections.Generic;

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
    internal FeatureView(in ViewDesc desc, uint width, uint height)
    {
        World = desc.World;
        Kind = desc.Kind;
        ViewId = desc.ViewId;
        LayerCount = desc.LayerCount;
        Width = width;
        Height = height;
        RenderScale = desc.RenderScale;
        Position = desc.Layers[0].Position;
    }

    public RenderWorld World { get; }
    public ViewKind Kind { get; }
    public int ViewId { get; }

    /// <summary>1 for mono, 2 for stereo, 6 for a cube capture. Textures of layered views are arrays drawn through multiview.</summary>
    public int LayerCount { get; }

    /// <summary>Render resolution: the target's size within the viewport times the render scale.</summary>
    public uint Width { get; }
    public uint Height { get; }
    public float RenderScale { get; }

    /// <summary>The first layer's camera position.</summary>
    public Double3 Position { get; }
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
/// Work a host adds to views: a pass in a slot of every view of a world, or of one view. Settings are copied in
/// <see cref="Extract"/> on the host thread, everything else runs on the render thread. A feature replacing a built in
/// one names its type in <see cref="Replaces"/>. Of features sharing an <see cref="ExclusiveGroup"/> the view's wins over
/// the world's, which wins over the built in one.
/// </summary>
public abstract class RenderFeature
{
    public abstract RenderSlot Slot { get; }

    /// <summary>Order inside the slot, built in features at 0.</summary>
    public virtual int Order => 0;

    public virtual ViewKinds RunsOn => ViewKinds.Cameras;
    public virtual string? ExclusiveGroup => null;
    public virtual Type? Replaces => null;

    /// <summary>Read at SubmitFrame. Host thread.</summary>
    public bool Enabled { get; set; } = true;

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

    internal bool EnabledSnapshot;
}

/// <summary>
/// A fullscreen effect made by <see cref="Renderer.CreateFullscreenProgram"/>, built for mono views and for layered views
/// through multiview.
/// </summary>
public sealed class FullscreenProgram : IDisposable
{
    private FullscreenProgram(GraphicsProgram mono, GraphicsProgram layered)
    {
        Mono = mono;
        Layered = layered;
    }

    internal GraphicsProgram Mono { get; }
    internal GraphicsProgram Layered { get; }

    internal GraphicsProgram For(int layers) => layers > 1 ? Layered : Mono;

    public void Dispose()
    {
        Mono.Dispose();
        Layered.Dispose();
    }

    // The fragment code follows Graphite/Fullscreen.glsl, which gives it vUV, outColor and Graphite_SampleSource
    internal static FullscreenProgram Create(GraphicsDevice device, IShaderCompiler compiler, string fragment, string name)
    {
        GraphicsProgram Build(bool layered)
        {
            string define = layered ? "#define GRAPHITE_LAYERED 1\n" : "";
            string source = $"Vertex\n{{\n{define}#include \"Graphite/Fullscreen.glsl\"\n}}\nFragment\n{{\n{define}#include \"Graphite/Fullscreen.glsl\"\n{fragment}\n}}\n";
            ShaderPass pass = new() { State = new PassState(), Source = source, SourceName = name };
            ShaderDescription description = compiler.Compile(pass, [], device.BackendType);
            description.BlendState = BlendStateDescription.SingleOverrideBlend;
            description.DepthStencilState = DepthStencilStateDescription.Disabled;
            description.RasterizerState = RasterizerStateDescription.CullNone;
            GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
            program.Name = layered ? name + " layered" : name;
            return program;
        }

        GraphicsProgram mono = Build(false);
        try
        {
            return new FullscreenProgram(mono, Build(true));
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
        if (Texture != null)
            retired.Add(Texture);
        Texture = device.ResourceFactory.CreateRenderTexture(desc);
        Texture.Name = name;
        return true;
    }

    public void Dispose() => Texture?.Dispose();
}

/// <summary>The textures a view's features pass along: the scene target, two colours ping ponging after it, and named ones.</summary>
internal sealed class FeatureChain : IDisposable
{
    public static readonly PixelFormat[] SceneFormats = [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_SNorm, PixelFormat.R8_G8_UNorm];

    public readonly ChainTexture Scene;
    public readonly ChainTexture[] Colors;

    /// <summary>The colour features read and draw onto now: the scene, or the last colour a feature wrote.</summary>
    public ChainTexture Current;
    public readonly Dictionary<string, TextureHandle> Published = new(StringComparer.Ordinal);

    public FeatureChain(int viewId)
    {
        Scene = new ChainTexture($"Graphite.Scene {viewId}");
        Colors = [new ChainTexture($"Graphite.ColorA {viewId}"), new ChainTexture($"Graphite.ColorB {viewId}")];
        Current = Scene;
    }

    public void Reset()
    {
        Scene.Reset();
        Colors[0].Reset();
        Colors[1].Reset();
        Current = Scene;
        Published.Clear();
    }

    /// <summary>Sizes the textures for a view. Returns whether any was recreated.</summary>
    public bool Ensure(GraphicsDevice device, uint width, uint height, int layers, List<IDisposable> retired)
    {
        uint arrayLayers = layers > 1 ? (uint)layers : 0;
        bool changed = Scene.Ensure(device, new RenderTextureDescription(width, height, SceneFormats, PixelFormat.D32_Float) { ArrayLayers = arrayLayers }, retired);
        RenderTextureDescription color = new RenderTextureDescription(width, height, PixelFormat.R16_G16_B16_A16_Float, false) { ArrayLayers = arrayLayers };
        changed |= Colors[0].Ensure(device, color, retired);
        changed |= Colors[1].Ensure(device, color, retired);
        return changed;
    }

    public void Dispose()
    {
        Scene.Dispose();
        Colors[0].Dispose();
        Colors[1].Dispose();
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
        _pass.Input = _graph.DeclareInputTexture(current.Id);
    }

    /// <summary>Draws onto the current colour: the scene with its depth and G-buffer while no feature has written a new colour.</summary>
    public void DrawToColor()
    {
        ChainTexture current = _pass.Chain.Current;
        if (_pass.Input?.Id == current.Id || _pass.SceneRead?.Id == current.Id)
            throw new InvalidOperationException($"{_pass.Name} draws onto the colour it reads.");
        Write(current);
    }

    /// <summary>Writes a new colour from the current one, which later features read. <see cref="FeatureContext.Fullscreen"/> draws it.</summary>
    public void WriteColor()
    {
        ReadColor();
        FeatureChain chain = _pass.Chain;
        ChainTexture next = chain.Current == chain.Colors[0] ? chain.Colors[1] : chain.Colors[0];
        Write(next);
        chain.Current = next;
    }

    /// <summary>A texture for this feature alone this frame.</summary>
    public TextureHandle Temporary(string name, GraphTextureDesc desc)
        => _graph.DeclareOutputTexture(RenderResourceID.Intern($"Graphite.Feature.{_pass.Name}.{name}"), desc);

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
        _pass.Clears = !texture.Written && _pass.Input == null;
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

    internal RenderContext Graph { get; set; } = null!;

    public CommandBuffer Command { get; internal set; } = null!;
    public FeatureView View { get; internal set; }

    /// <summary>The scene's depth target, when the feature reads the scene.</summary>
    public Texture? Depth => Scene?.DepthTexture;

    /// <summary>The scene's octahedral world normals, when the feature reads the scene.</summary>
    public Texture? Normals => Scene?.ColorTextures[1];

    /// <summary>The scene's roughness and metallic, when the feature reads the scene.</summary>
    public Texture? Material => Scene?.ColorTextures[2];

    /// <summary>The colour the feature reads, when it reads or writes colour.</summary>
    public Texture? Color => _pass.Input is { } input ? Graph.GetRenderTexture(input).ColorTextures[0] : null;

    private RenderTexture? Scene => _pass.SceneRead is { } scene ? Graph.GetRenderTexture(scene) : null;

    /// <summary>A texture the feature declared.</summary>
    public RenderTexture Get(TextureHandle texture) => Graph.GetRenderTexture(texture);

    /// <summary>
    /// Binds the colour the feature draws onto or writes, cleared when this is the first feature drawing it, the scene
    /// clearing its depth and G-buffer with it.
    /// </summary>
    public void BeginOutput()
    {
        if (_pass.OutputId is not { } output)
            throw new InvalidOperationException($"{_pass.Name} declared no colour to draw onto or write.");
        Framebuffer framebuffer = Graph.GetRenderTexture(output).Framebuffer;
        bool scene = _pass.Output == _pass.Chain.Scene;
        if (_pass.Clears)
        {
            Float4 clear = _pass.Camera.Desc.ClearColor;
            AttachmentOps depth = scene ? AttachmentOps.Clear(0f) : AttachmentOps.Loaded;
            Command.SetFramebuffer(framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(clear.X, clear.Y, clear.Z, clear.W)), depth));
            Command.SetFullViewport();
            if (scene)
            {
                Command.ClearColorTarget(1, new Color(0, 0, 0, 0));
                Command.ClearColorTarget(2, new Color(0, 0, 0, 0));
            }
            return;
        }

        bool overwritten = _pass.Input != null && !scene;
        AttachmentOps color = overwritten ? new AttachmentOps(LoadAction.DontCare, StoreAction.Store) : AttachmentOps.Loaded;
        Command.SetFramebuffer(framebuffer, new TargetLoadStoreOps(color, AttachmentOps.Loaded));
        Command.SetFullViewport();
    }

    /// <summary>
    /// Draws a fullscreen program over the colour the feature writes, the colour it reads bound as Graphite_Source, with
    /// any properties given.
    /// </summary>
    public void Fullscreen(FullscreenProgram program, PropertySet? properties = null)
    {
        if (Color is not { } source)
            throw new InvalidOperationException($"{_pass.Name} draws a fullscreen program without reading a colour, declare WriteColor.");
        BeginOutput();
        Command.SetShader(program.For(View.LayerCount));
        Command.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", source, _pass.Frame.LinearSampler);
        Command.SetProperties(_properties);
        if (properties != null)
            Command.SetProperties(properties);
        Command.Draw(3);
    }

    internal void DrawList(int list) => _pass.Camera.Drawer.DrawList(Command, list);

    internal void DrawSky() => _pass.Camera.Drawer.DrawSky(Command);
}

/// <summary>Runs one feature as a pass of its view's graph.</summary>
internal sealed class FeaturePass : IPass
{
    private readonly FeatureContext _context;

    public FeaturePass(RenderFeature feature, CameraFrame camera, FramePipeline frame)
    {
        Feature = feature;
        Camera = camera;
        Frame = frame;
        Name = feature.GetType().Name;
        _context = new FeatureContext(this);
    }

    public RenderFeature Feature { get; }
    public CameraFrame Camera { get; }
    public FramePipeline Frame { get; }
    public FeatureChain Chain => Camera.Chain;
    public string Name { get; }

    public TextureHandle? Input;
    public TextureHandle? SceneRead;
    public ChainTexture? Output;
    public TextureHandle? OutputId;
    public bool Clears;

    public void Setup(RenderContextBuilder builder)
    {
        Input = null;
        SceneRead = null;
        Output = null;
        OutputId = null;
        Clears = false;
        Feature.Setup(new FeatureBuilder(this, builder), Camera.Info);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        _context.Graph = context;
        _context.Command = cmd;
        _context.View = Camera.Info;
        Feature.Render(_context);
    }
}

/// <summary>A view's first pass: its tables for the frame, and every persistent buffer imported so draws see their writes.</summary>
internal sealed class SetupPass(FramePipeline frame, CameraFrame camera) : IPass
{
    public string Name => "Graphite.Setup";

    public void Setup(RenderContextBuilder builder)
    {
        camera.Chain.Reset();
        DeviceBuffer[] buffers = frame.Buffers;
        for (int i = 0; i < buffers.Length; i++)
            builder.DeclareImportedBuffer(FramePipeline.BufferId(i), buffers[i], BufferAccess.AllReads);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => camera.Drawer.Prepare(context.Task.RingSlot);
}

/// <summary>A view's last pass: the colour the features left, copied into the view's viewport of its target.</summary>
internal sealed class PresentPass(FramePipeline frame, CameraFrame camera) : IPass
{
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
        bool whole = rect == new Float4(0, 0, 1, 1);
        Viewport viewport = new(rect.X * target.Width, rect.Y * target.Height, rect.Z * target.Width, rect.W * target.Height, 0, 1);
        if (_source is not { } source)
        {
            Float4 clear = camera.Desc.ClearColor;
            Color color = new(clear.X, clear.Y, clear.Z, clear.W);
            cmd.SetFramebuffer(target, new TargetLoadStoreOps(whole ? AttachmentOps.Clear(color) : AttachmentOps.Loaded, AttachmentOps.Loaded));
            if (!whole)
            {
                cmd.SetViewport(viewport);
                cmd.ClearColorTarget(0, color);
            }
            return;
        }

        cmd.SetFramebuffer(target, new TargetLoadStoreOps(whole ? new AttachmentOps(LoadAction.DontCare, StoreAction.Store) : AttachmentOps.Loaded, AttachmentOps.Loaded));
        cmd.SetViewport(viewport);
        cmd.SetShader(frame.PresentProgram.For(camera.LayerCount));
        cmd.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", context.GetRenderTexture(source).ColorTextures[0], frame.LinearSampler);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}

/// <summary>The depth prepass: the visible opaque renderables' depth before the opaque pass.</summary>
public sealed class PrepassFeature : RenderFeature
{
    internal PrepassFeature() { }

    public override RenderSlot Slot => RenderSlot.Prepass;
    public override ViewKinds RunsOn => ViewKinds.All;
    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.DrawToColor();

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawList(0);
    }
}

/// <summary>The visible opaque renderables' forward pass, writing colour and the thin G-buffer.</summary>
public sealed class OpaqueFeature : RenderFeature
{
    internal OpaqueFeature() { }

    public override RenderSlot Slot => RenderSlot.Opaque;
    public override ViewKinds RunsOn => ViewKinds.All;
    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.DrawToColor();

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawList(1);
    }
}

/// <summary>The world's sky material behind everything the view sees. Exclusive group "Sky".</summary>
public sealed class SkyFeature : RenderFeature
{
    internal SkyFeature() { }

    public override RenderSlot Slot => RenderSlot.Sky;
    public override ViewKinds RunsOn => ViewKinds.All;
    public override string? ExclusiveGroup => "Sky";
    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.DrawToColor();

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
    public override ViewKinds RunsOn => ViewKinds.All;
    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.DrawToColor();

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawList(2);
    }
}

/// <summary>Picks a view's features each frame: built in, world and view features, filtered and ordered.</summary>
internal static class FeatureSelection
{
    public static readonly RenderFeature[] BuiltIn = [new PrepassFeature(), new OpaqueFeature(), new SkyFeature(), new TransparentFeature()];

    static FeatureSelection()
    {
        foreach (RenderFeature feature in BuiltIn)
            feature.EnabledSnapshot = true;
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
            if (i >= result.Count)
                continue;
            RenderFeature feature = result[i];
            for (int j = i - 1; j >= 0; j--)
            {
                RenderFeature other = result[j];
                bool replaced = feature.Replaces is { } type && type.IsInstanceOfType(other);
                bool grouped = feature.ExclusiveGroup is { } group && other.ExclusiveGroup == group;
                if (replaced || grouped)
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
        int slot = a.Slot.CompareTo(b.Slot);
        return slot != 0 ? slot : a.Order.CompareTo(b.Order);
    }

    private static void Add(List<RenderFeature> result, RenderFeature[] features, in FeatureView info)
    {
        ViewKinds kind = (ViewKinds)(1 << (int)info.Kind);
        foreach (RenderFeature feature in features)
        {
            if (feature.EnabledSnapshot && (feature.RunsOn & kind) != 0 && !result.Contains(feature) && feature.IsActive(info))
                result.Add(feature);
        }
    }
}
