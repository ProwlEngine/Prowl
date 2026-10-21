using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal sealed class FeatureShape : IDisposable
{
    internal static PixelFormat[] SceneFormats(Func<PixelFormat, bool> drawable)
        => [PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R16_G16_Float, drawable(PixelFormat.R16_G16_SNorm) ? PixelFormat.R16_G16_SNorm : PixelFormat.R16_G16_Float, PixelFormat.R8_G8_UNorm];

    internal static PixelFormat SceneDepth(Func<PixelFormat, bool> drawable)
        => drawable(PixelFormat.D32_Float) ? PixelFormat.D32_Float : PixelFormat.D16_UNorm;

    public const PixelFormat LdrColorFormat = PixelFormat.R8_G8_B8_A8_UNorm_SRgb;

    public static int SceneTargetCount(SceneTargets targets)
        => (targets & (SceneTargets.Normals | SceneTargets.Material)) != 0 ? 4 : (targets & SceneTargets.Motion) != 0 ? 2 : 1;

    private readonly Dictionary<RenderFeature, FeaturePass> _passes = new();
    private readonly Dictionary<RenderFeature, int> _used = new();
    private bool _sceneDescribed;

    public FeatureShape(GraphKind kind, ViewKind viewKind, CameraShape shape, CascadeSlot[] cascades)
    {
        Kind = kind;
        Cascades = cascades;
        ViewKind = viewKind;
        Shape = shape;
        SceneId = RenderResourceID.Intern($"{kind.Name}/Scene");
        ScreenVisibilityId = RenderResourceID.Intern($"{kind.Name}/ScreenVisibility");
        ScreenIrradianceId = RenderResourceID.Intern($"{kind.Name}/ScreenIrradiance");
        CaptureId = RenderResourceID.Intern($"{kind.Name}/Capture");
        Setup = new SetupPass(this);
        Present = new PresentPass(this);
        View = StartView();
    }

    public GraphKind Kind { get; }
    public ViewKind ViewKind { get; }
    public CameraShape Shape { get; }
    public CascadeSlot[] Cascades { get; }
    public RenderResourceID SceneId { get; }
    public RenderResourceID ScreenVisibilityId { get; }
    public RenderResourceID ScreenIrradianceId { get; }
    public RenderResourceID CaptureId { get; }

    public bool Captures => ViewKind is ViewKind.SkyCapture or ViewKind.ReflectionProbe;
    public SetupPass Setup { get; }
    public PresentPass Present { get; }

    public FramePipeline Frame { get; set; } = null!;

    public int LastFrame { get; set; }

    public readonly List<RenderFeature> Features = new();
    public readonly List<PassType> PassTypes = new();
    public readonly Dictionary<string, TextureHandle> Published = new(StringComparer.Ordinal);
    public readonly Dictionary<(RenderFeature Feature, Type Type), object> States = new();

    public RenderResourceID Current { get; set; }
    public bool CurrentIsScene => Current == SceneId;
    public bool CurrentWritten { get; set; }
    public bool SceneWritten { get; set; }
    public bool ScreenVisibilityWritten { get; set; }
    public bool ScreenIrradianceWritten { get; set; }
    public bool Upscaled { get; set; }
    public FeatureView View { get; private set; }

    private bool ColorDrawable(PixelFormat format) => Frame.Device.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.RenderTarget | TextureUsage.Sampled);

    private bool DepthDrawable(PixelFormat format) => Frame.Device.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.DepthStencil | TextureUsage.Sampled);

    public PixelFormat ColorFormat => Shape.Ldr ? LdrColorFormat : SceneFormats(ColorDrawable)[0];

    public IReadOnlyList<IInstancePass> Passes()
    {
        List<IInstancePass> passes = [Setup];
        if (Cascades.Length > 0)
        {
            passes.Add(new CascadePass(this));
            passes.Add(new CascadePublishPass(this));
        }
        passes.Add(new SceneClearPass(this));
        foreach (RenderFeature feature in Features)
            passes.Add(PassOf(feature));
        passes.Add(Present);
        return passes;
    }

    public FeaturePass PassOf(RenderFeature feature)
    {
        if (!_passes.TryGetValue(feature, out FeaturePass? pass))
            _passes[feature] = pass = new FeaturePass(feature, this);
        return pass;
    }

    public bool Prune(int frame)
    {
        bool removed = false;
        for (int i = Features.Count - 1; i >= 0; i--)
        {
            RenderFeature feature = Features[i];
            if (_used.GetValueOrDefault(feature, -1) == frame)
                continue;
            Features.RemoveAt(i);
            _passes.Remove(feature);
            _used.Remove(feature);
            removed = true;
        }
        return removed;
    }

    public bool Add(RenderFeature feature, int frame)
    {
        _used[feature] = frame;
        if (Features.Contains(feature))
            return false;
        int at = Features.Count;
        while (at > 0 && FeatureSelection.Compare(Features[at - 1], feature) > 0)
            at--;
        Features.Insert(at, feature);
        PassOf(feature);
        return true;
    }

    public void Begin()
    {
        Current = SceneId;
        CurrentWritten = false;
        SceneWritten = false;
        ScreenVisibilityWritten = false;
        ScreenIrradianceWritten = false;
        Upscaled = false;
        _sceneDescribed = false;
        PassTypes.Clear();
        Published.Clear();
        View = StartView();
    }

    public void Upscale() => View = View.AtOutput();

    public T State<T>(RenderFeature feature) where T : class, new()
    {
        if (!States.TryGetValue((feature, typeof(T)), out object? state))
            States[(feature, typeof(T))] = state = new T();
        return (T)state;
    }

    public RenderResourceID IdOf(RenderFeature feature, string name)
        => RenderResourceID.Intern($"{Kind.Name}/{feature.GetType().Name}.{feature.Id}/{name}");

    public RenderResourceID CascadeId(int slot, int cascade) => RenderResourceID.Intern($"{Kind.Name}/Cascade{slot}.{cascade}");

    public RenderResourceID HostId(int index) => RenderResourceID.Intern($"{Kind.Name}/Host{index}");

    public GraphTextureDesc SceneDesc()
    {
        PixelFormat[] formats = SceneFormats(ColorDrawable)[..SceneTargetCount(Shape.Targets)];
        formats[0] = ColorFormat;
        return new GraphTextureDesc
        {
            SizeMode = TextureSizeMode.InstanceRelative,
            Scale = 1,
            ColorFormats = formats,
            EnableDepth = true,
            DepthFormat = SceneDepth(DepthDrawable),
            ArrayLayers = Layers,
        };
    }

    public GraphTextureDesc ColorDesc(bool output) => GraphTextureDesc.InstanceSized(ColorFormat, output ? Shape.OutputScale : 1f) with { ArrayLayers = Layers };

    public GraphTextureDesc ScreenDesc(PixelFormat format) => GraphTextureDesc.InstanceSized(format, Shape.ScreenFraction) with { ArrayLayers = Layers };

    public TextureHandle DeclareScene(RenderContextBuilder builder)
    {
        if (_sceneDescribed)
            return builder.DeclareWriteTexture(SceneId);
        _sceneDescribed = true;
        return builder.DeclareOutputTexture(SceneId, SceneDesc());
    }

    public void Dispose()
    {
        foreach (object state in States.Values)
            (state as IDisposable)?.Dispose();
        States.Clear();
    }

    private uint Layers => Shape.Layers > 1 ? (uint)Shape.Layers : 0;

    private FeatureView StartView() => new(ViewKind, Shape);
}
