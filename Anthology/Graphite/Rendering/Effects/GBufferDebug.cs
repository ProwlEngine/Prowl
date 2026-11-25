namespace Prowl.Graphite.Rendering;

/// <summary>What <see cref="GBufferDebug"/> shows of a deferred view's GBuffer.</summary>
public enum GBufferDebugMode
{
    BaseColor,
    Metalness,
    Normals,
    Roughness,
    Specular,
    Occlusion,

    /// <summary>A colour per shading model.</summary>
    ShadingModel,
    Custom,
    Baked,
    Emission,
    Motion,
    Depth,
}

/// <summary>
/// Shows one part of a deferred view's GBuffer in place of the view's image, black where no surface was drawn into it.
/// Runs on deferred views only.
/// </summary>
public sealed class GBufferDebug : PostEffect
{
    private static readonly PropertyID s_base = "Graphite_GBufferBase", s_normal = "Graphite_GBufferNormal", s_surface = "Graphite_GBufferSurface",
        s_custom = "Graphite_GBufferCustom", s_baked = "Graphite_GBufferBaked", s_emission = "Graphite_GBufferEmission",
        s_motion = "Graphite_GBufferMotion", s_depth = "Graphite_GBufferDepth", s_mode = "_Mode";

    private sealed class State
    {
        public TextureHandle GBuffer;
    }

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private GBufferDebugMode _mode;

    public GBufferDebug(Renderer renderer) : base(renderer, RenderSlot.DisplayPost)
    {
        _program = Program("GBufferDebug");
        Order = 1000;
        Paths = RenderPaths.Deferred;
    }

    public GBufferDebugMode Mode { get; set; }

    public override void Extract() => _mode = Mode;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().GBuffer = builder.Read(RendererOutputs.GBuffer);
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        RenderTexture gbuffer = context.Get(context.GetViewState<State>().GBuffer);
        Texture[] targets = gbuffer.ColorTextures;
        _properties.SetTexture(s_base, targets[(int)GBufferTarget.Base], PointClamp);
        _properties.SetTexture(s_normal, targets[(int)GBufferTarget.Normal], PointClamp);
        _properties.SetTexture(s_surface, targets[(int)GBufferTarget.Surface], PointClamp);
        _properties.SetTexture(s_custom, targets[(int)GBufferTarget.Custom], PointClamp);
        _properties.SetTexture(s_baked, targets[(int)GBufferTarget.Baked], PointClamp);
        _properties.SetTexture(s_emission, targets[(int)GBufferTarget.Emission], PointClamp);
        _properties.SetTexture(s_motion, targets[(int)GBufferTarget.Motion], PointClamp);
        _properties.SetTexture(s_depth, gbuffer.DepthTexture!, PointClamp);
        _properties.SetInt(s_mode, (int)_mode);
        context.Fullscreen(_program, _properties);
    }
}
