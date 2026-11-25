using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>The targets of a view's GBuffer, in the order its texture holds them. See Graphite/GBuffer.glsl.</summary>
public enum GBufferTarget
{
    /// <summary>Base colour in rgb, metalness in a.</summary>
    Base,

    /// <summary>The world normal, octahedral.</summary>
    Normal,

    /// <summary>Roughness, specular and occlusion, then the shading model, light layers and shadow flag in a.</summary>
    Surface,

    /// <summary>Four values the shading model reads its own way.</summary>
    Custom,

    /// <summary>The diffuse light that is not realtime, pre-exposed.</summary>
    Baked,

    /// <summary>Pre-exposed emission.</summary>
    Emission,

    /// <summary>How far the surface moved across the screen since last frame, in uv.</summary>
    Motion,
}

/// <summary>
/// Draws the GBuffer pass of a deferred view's opaque renderables, the shader pass tagged "LightMode" = "GBuffer", into
/// the view's GBuffer and its depth, and publishes it as <see cref="RendererOutputs.GBuffer"/>. Renderables whose shader
/// has no such pass are left to the forward passes. Its depth starts as the scene's after the prepass, which the
/// surfaces the prepass drew test equal against, see <see cref="EarlyDepth"/>. Group <see cref="FeatureGroup.GBuffer"/>.
/// </summary>
public sealed class GBufferFeature : RenderFeature
{
    public const int TargetCount = 7;

    private sealed class State
    {
        public TextureHandle Target;
    }

    private static readonly PropertyID s_sceneDepth = "_SceneDepth";

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal GBufferFeature(RendererResources resources)
        : base(RenderSlot.GBuffer, group: FeatureGroup.GBuffer, passes: [PassType.GBuffer])
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.GBufferDepthProgram);
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.ReadScene();
        GraphTextureDesc desc = new()
        {
            Width = (int)view.Width,
            Height = (int)view.Height,
            ColorFormats = _resources.GBufferFormats,
            EnableDepth = true,
            DepthFormat = _resources.SceneDepthFormat,
            ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0,
        };
        TextureHandle target = builder.Temporary("GBuffer", desc);
        builder.GetViewState<State>().Target = target;
        builder.Publish(RendererOutputs.GBuffer, target);
    }

    public override void Render(FeatureContext context)
    {
        RenderTexture target = context.Get(context.GetViewState<State>().Target);
        context.Command.SetFramebuffer(target.Framebuffer, TargetLoadStoreOps.Clear(new Color(0f, 0f, 0f, 0f), 0f));
        context.Command.SetFullViewport();
        context.Command.SetShader(_resources.GBufferDepthProgram.For(context.View.LayerCount));
        context.Command.SetVertexSource(VertexSource.None);
        _properties.SetTexture(s_sceneDepth, context.Depth!, _resources.PointClamp);
        context.Command.SetProperties(_properties);
        context.Command.Draw(3);
        context.DrawPass(PassType.GBuffer);
    }
}

/// <summary>
/// Copies a deferred view's GBuffer depth, motion and normals onto its scene, where the prepass drew nothing nearer, for
/// the features before lighting that read the scene. Group <see cref="FeatureGroup.GBufferResolve"/>.
/// </summary>
public sealed class GBufferResolveFeature : RenderFeature
{
    private static readonly PropertyID s_normal = "Graphite_GBufferNormal", s_surface = "Graphite_GBufferSurface", s_motion = "Graphite_GBufferMotion",
        s_depth = "Graphite_GBufferDepth";

    private sealed class State
    {
        public TextureHandle GBuffer;
    }

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal GBufferResolveFeature(RendererResources resources) : base(RenderSlot.GBuffer, 100, FeatureGroup.GBufferResolve)
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.GBufferResolveProgram);
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().GBuffer = builder.Read(RendererOutputs.GBuffer);
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        RenderTexture gbuffer = context.Get(context.GetViewState<State>().GBuffer);
        _properties.SetTexture(s_normal, gbuffer.ColorTextures[(int)GBufferTarget.Normal], _resources.PointClamp);
        _properties.SetTexture(s_surface, gbuffer.ColorTextures[(int)GBufferTarget.Surface], _resources.PointClamp);
        _properties.SetTexture(s_motion, gbuffer.ColorTextures[(int)GBufferTarget.Motion], _resources.PointClamp);
        _properties.SetTexture(s_depth, gbuffer.DepthTexture!, _resources.PointClamp);
        context.FullscreenScene(_resources.GBufferResolveProgram, _properties);
    }
}
