namespace Prowl.Graphite.Rendering;

/// <summary>
/// Lights a deferred view's GBuffer onto its scene before the forward passes, which then draw only the renderables the
/// GBuffer does not hold, one fullscreen draw for each shading model with lighting of its own. It writes the scene's
/// colour and its material target, which screen space reflections read.
/// Group <see cref="FeatureGroup.DeferredLighting"/>.
/// </summary>
public sealed class DeferredLightingFeature : RenderFeature
{
    private static readonly PropertyID s_base = "Graphite_GBufferBase", s_normal = "Graphite_GBufferNormal", s_surface = "Graphite_GBufferSurface",
        s_custom = "Graphite_GBufferCustom", s_baked = "Graphite_GBufferBaked", s_emission = "Graphite_GBufferEmission",
        s_motion = "Graphite_GBufferMotion", s_depth = "Graphite_GBufferDepth";

    private sealed class State
    {
        public TextureHandle GBuffer;
    }

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal DeferredLightingFeature(RendererResources resources)
        : base(RenderSlot.Opaque, -100, FeatureGroup.DeferredLighting)
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.DeferredLightingProgram);
        Uses(resources.FoliageLightingProgram);
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().GBuffer = builder.Read(RendererOutputs.GBuffer);
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        RenderTexture gbuffer = context.Get(context.GetViewState<State>().GBuffer);
        Texture[] targets = gbuffer.ColorTextures;
        Sampler point = _resources.PointClamp;
        _properties.SetTexture(s_base, targets[(int)GBufferTarget.Base], point);
        _properties.SetTexture(s_normal, targets[(int)GBufferTarget.Normal], point);
        _properties.SetTexture(s_surface, targets[(int)GBufferTarget.Surface], point);
        _properties.SetTexture(s_custom, targets[(int)GBufferTarget.Custom], point);
        _properties.SetTexture(s_baked, targets[(int)GBufferTarget.Baked], point);
        _properties.SetTexture(s_emission, targets[(int)GBufferTarget.Emission], point);
        _properties.SetTexture(s_motion, targets[(int)GBufferTarget.Motion], point);
        _properties.SetTexture(s_depth, gbuffer.DepthTexture!, point);
        context.FullscreenScene(_resources.DeferredLightingProgram, _properties);
        context.FullscreenScene(_resources.FoliageLightingProgram, _properties);
    }
}
