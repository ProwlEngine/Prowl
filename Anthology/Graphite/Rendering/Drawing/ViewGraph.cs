using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Where each of a scene's colour targets sits, the locations Graphite/Passes.glsl writes them at. A scene has the
/// first <see cref="ViewGraph.SceneTargetCount"/> of them.
/// </summary>
internal static class SceneTarget
{
    public const int Color = 0, Motion = 1, Reactive = 2, Normals = 3, Material = 4;
}

/// <summary>Adds one view's passes to the frame graph and tracks the colour its features hand on, rebuilt every frame.</summary>
internal sealed class ViewGraph
{
    /// <summary>The formats of a scene's colour targets, in <see cref="SceneTarget"/> order.</summary>
    internal static PixelFormat[] SceneFormats(Func<PixelFormat, bool> drawable)
        => [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_Float, PixelFormat.R8_UNorm, drawable(PixelFormat.R16_G16_SNorm) ? PixelFormat.R16_G16_SNorm : PixelFormat.R16_G16_Float, PixelFormat.R8_G8_B8_A8_UNorm];

    // Stencil bits when the device has them, keeping float depth for reversed Z
    internal static PixelFormat SceneDepth(Func<PixelFormat, bool> drawable)
        => drawable(PixelFormat.D32_Float_S8_UInt) ? PixelFormat.D32_Float_S8_UInt
        : drawable(PixelFormat.D32_Float) ? PixelFormat.D32_Float : PixelFormat.D16_UNorm;

    public const PixelFormat LdrColorFormat = PixelFormat.R8_G8_B8_A8_UNorm_SRgb;

    public static int SceneTargetCount(SceneTargets targets)
        => (targets & (SceneTargets.Normals | SceneTargets.Material)) != 0 ? SceneTarget.Material + 1
            : (targets & SceneTargets.Reactive) != 0 ? SceneTarget.Reactive + 1
            : (targets & SceneTargets.Motion) != 0 ? SceneTarget.Motion + 1 : SceneTarget.Color + 1;

    public static readonly RenderResourceID SceneId = RenderResourceID.Intern(RendererOutputs.Scene);

    private readonly Dictionary<RenderFeature, FeaturePass> _passes = new();
    private readonly Dictionary<RenderFeature, int> _used = new();
    private int _frame;
    private bool _begun;

    public ViewGraph(FramePipeline frame, CameraFrame camera, FrameGraph graph)
    {
        Frame = frame;
        Camera = camera;
        Graph = graph;
    }

    public FramePipeline Frame { get; }
    public CameraFrame Camera { get; }
    public FrameGraph Graph { get; }
    public CameraShape Shape { get; private set; }

    public readonly Dictionary<string, TextureHandle> Published = new(StringComparer.Ordinal);

    public TextureHandle Scene { get; set; }
    public TextureHandle Current { get; set; }
    public bool CurrentIsScene { get; set; }
    public bool CurrentWritten { get; set; }
    public bool SceneWritten { get; set; }
    public bool ScreenVisibilityWritten { get; set; }
    public bool ScreenIrradianceWritten { get; set; }
    public bool Upscaled { get; set; }

    /// <summary>Whether a feature added so far encodes the colour for display, so colours made from here on hold 0 to 1.</summary>
    public bool DisplayEncoded { get; set; }
    public FeatureView View { get; set; }

    public bool Captures => Camera.Desc.Kind is ViewKind.SkyCapture or ViewKind.ReflectionProbe;

    public PixelFormat SceneDepthFormat => Frame.Resources.SceneDepthFormat;

    public PixelFormat ColorFormat => ColorFormatOf(Shape);

    public PixelFormat ColorFormatOf(in CameraShape shape)
        => shape.Ldr ? LdrColorFormat : shape.KeepsAlpha ? Frame.Resources.SceneFormats[0] : Frame.Resources.OpaqueHdrFormat;

    private uint Layers => Shape.Layers > 1 ? (uint)Shape.Layers : 0;

    public RenderResourceID IdOf(RenderFeature feature, string name)
        => RenderResourceID.Intern($"{feature.GetType().Name}.{feature.Id}/{name}");

    public GraphTextureDesc ColorDesc(in FeatureView view)
    {
        PixelFormat format = DisplayEncoded && !Shape.Ldr && !Shape.KeepsAlpha ? Frame.Resources.OpaqueDisplayFormat : ColorFormat;
        return GraphTextureDesc.Sized((int)view.Width, (int)view.Height, format) with { ArrayLayers = Layers };
    }

    public GraphTextureDesc SceneDesc()
    {
        return new GraphTextureDesc
        {
            Width = (int)Camera.Info.Width,
            Height = (int)Camera.Info.Height,
            ColorFormats = Frame.Resources.SceneTargetFormats(SceneTargetCount(Shape.Targets), ColorFormat),
            EnableDepth = true,
            DepthFormat = SceneDepthFormat,
            ArrayLayers = Layers,
        };
    }

    public FeaturePass PassOf(RenderFeature feature)
    {
        if (!_passes.TryGetValue(feature, out FeaturePass? pass))
            _passes[feature] = pass = new FeaturePass(feature, this);
        _used[feature] = _frame;
        return pass;
    }

    /// <summary>Fills the view's table for the frame, in the view's first pass to record.</summary>
    internal void BeginRecording(PassContext context)
    {
        if (_begun)
            return;
        _begun = true;
        Camera.DisplayEncoded = false;
        Camera.Drawer.Prepare(context.Task.RingSlot);
    }

    // The scene a Clear slot feature published, for the features after it to draw onto
    private void AdoptScene()
    {
        if (SceneWritten)
            return;
        if (!Published.TryGetValue(RendererOutputs.Scene, out TextureHandle scene))
            throw new InvalidOperationException($"No feature of view {Camera.View.ViewId} published '{RendererOutputs.Scene}' in the Clear slot, which the features after it draw onto.");
        Scene = Current = scene;
        CurrentIsScene = SceneWritten = CurrentWritten = true;
    }

    /// <summary>Adds the view's passes: its features in order.</summary>
    public void Build(CameraShape shape)
    {
        Shape = shape;
        _frame++;
        _begun = false;
        Published.Clear();
        Current = Scene = default;
        CurrentIsScene = true;
        CurrentWritten = SceneWritten = ScreenVisibilityWritten = ScreenIrradianceWritten = Upscaled = DisplayEncoded = false;
        Camera.Info = Camera.Info.Culled(Camera.SceneColorUsed, Camera.SceneDepthUsed, Camera.BackDepthUsed, Camera.Shadows.FirstCascade() != null,
            Camera.BinCounts[SpatialIndex.DecalsSet] > 0,
            Camera.Selected.Exists(static feature => feature.ReadsHiZ && feature.Slot < RenderSlot.OpaqueColor),
            Camera.Selected.Exists(static feature => feature.ReadsHiZ && feature.Slot >= RenderSlot.OpaqueColor));
        View = Camera.Info;

        Frame.SpatialBins.Add(this);
        bool adopted = false;
        foreach (RenderFeature feature in Camera.Selected)
        {
            if (!adopted && feature.Slot > RenderSlot.Clear)
            {
                AdoptScene();
                adopted = true;
            }
            PassOf(feature).Add();
        }
        if (!adopted)
            AdoptScene();

        if (_passes.Count > Camera.Selected.Count)
        {
            List<RenderFeature>? stale = null;
            foreach ((RenderFeature feature, int frame) in _used)
            {
                if (frame != _frame)
                    (stale ??= new()).Add(feature);
            }
            foreach (RenderFeature feature in stale ?? [])
            {
                _passes.Remove(feature);
                _used.Remove(feature);
            }
        }
    }
}
