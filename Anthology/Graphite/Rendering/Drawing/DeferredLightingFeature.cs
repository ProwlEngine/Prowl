namespace Prowl.Graphite.Rendering;

/// <summary>
/// Lights a deferred view's GBuffer onto its scene before the forward passes, which then draw only the renderables the
/// GBuffer does not hold, in one fullscreen draw of the program <see cref="ShadingModels"/> writes, which lights each
/// pixel by its shading model. It writes the scene's colour and its material target, which screen space reflections read.
/// Group <see cref="FeatureGroup.DeferredLighting"/>.
/// </summary>
public sealed class DeferredLightingFeature : RenderFeature
{
    private sealed class State
    {
        public GBufferRead GBuffer;
    }

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal DeferredLightingFeature(RendererResources resources)
        : base(RenderSlot.Opaque, -100, FeatureGroup.DeferredLighting)
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.ShadingModels.Program);
    }

    public override void PrepareFrame()
    {
        ShadingModels models = _resources.ShadingModels;
        FullscreenProgram last = models.Program;
        if (models.Update())
            ReplaceProgram(last, models.Program);
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().GBuffer = GBufferRead.Declare(builder);
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.GetViewState<State>().GBuffer.Bind(context, _properties, _resources.PointClamp);
        context.FullscreenScene(_resources.ShadingModels.Program, _properties);
    }
}
