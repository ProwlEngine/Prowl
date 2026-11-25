using System;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Lights a deferred view's GBuffer onto its scene before the forward passes, which then draw only the renderables the
/// GBuffer does not hold, in one fullscreen draw of the program <see cref="ShadingModels"/> writes, which lights each
/// pixel by its shading model. It writes the scene's colour and, where the scene has one, its material target, which
/// screen space reflections read, and reads the scene's depth beside the GBuffer.
/// Group <see cref="FeatureGroup.DeferredLighting"/>.
/// </summary>
public sealed class DeferredLightingFeature : RenderFeature
{
    private sealed class State : IDisposable
    {
        public GBufferRead GBuffer;
        public TextureHandle Scene;
        public readonly TargetSets Sets = new();

        public void Dispose() => Sets.Dispose();
    }

    private const int SceneColor = 0, SceneMaterial = 4;

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
        State state = builder.GetViewState<State>();
        state.GBuffer = GBufferRead.Declare(builder);
        builder.ReadViews();
        builder.DrawsRenderables();
        state.Scene = builder.WriteSceneTargets();
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        state.Sets.Age();
        RenderTexture scene = context.Get(state.Scene);
        context.CopyBack(scene);
        state.GBuffer.Bind(context, _properties, _resources.PointClamp, scene);
        Texture[] targets = scene.ColorTextures;
        Framebuffer framebuffer = targets.Length > SceneMaterial
            ? state.Sets.Get(_resources.Factory, context.View.LayerCount, null, targets[SceneColor], targets[SceneMaterial])
            : state.Sets.Get(_resources.Factory, context.View.LayerCount, null, targets[SceneColor]);
        context.Fullscreen(_resources.ShadingModels.Program, framebuffer, _properties);
    }
}
