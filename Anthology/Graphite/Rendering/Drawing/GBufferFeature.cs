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

    public override bool IsActive(in FeatureView view) => _resources.SupportsGBuffer;

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
/// A feature's read of the view's GBuffer: its targets as Graphite/GBuffer.glsl names them, the Base, Normal and Surface
/// ones from <see cref="RendererOutputs.GBufferDecals"/> while the world has decals.
/// </summary>
internal struct GBufferRead
{
    private static readonly PropertyID s_base = "Graphite_GBufferBase", s_normal = "Graphite_GBufferNormal", s_surface = "Graphite_GBufferSurface",
        s_custom = "Graphite_GBufferCustom", s_baked = "Graphite_GBufferBaked", s_emission = "Graphite_GBufferEmission",
        s_motion = "Graphite_GBufferMotion", s_depth = "Graphite_GBufferDepth";

    private TextureHandle _gbuffer;
    private TextureHandle? _decals;

    /// <summary>Declares the read in Setup. With decals false it reads the GBuffer as its pass left it.</summary>
    public static GBufferRead Declare(FeatureBuilder builder, bool decals = true) => new()
    {
        _gbuffer = builder.Read(RendererOutputs.GBuffer),
        _decals = decals && builder.Has(RendererOutputs.GBufferDecals) ? builder.Read(RendererOutputs.GBufferDecals) : null,
    };

    /// <summary>The GBuffer itself, for its depth and size.</summary>
    public readonly RenderTexture Texture(FeatureContext context) => context.Get(_gbuffer);

    public readonly void Bind(FeatureContext context, PropertySet properties, Sampler point)
    {
        RenderTexture gbuffer = context.Get(_gbuffer);
        Texture[] targets = gbuffer.ColorTextures;
        Texture[]? decaled = _decals is { } decals ? context.Get(decals).ColorTextures : null;
        properties.SetTexture(s_base, decaled?[0] ?? targets[(int)GBufferTarget.Base], point);
        properties.SetTexture(s_normal, decaled?[1] ?? targets[(int)GBufferTarget.Normal], point);
        properties.SetTexture(s_surface, decaled?[2] ?? targets[(int)GBufferTarget.Surface], point);
        properties.SetTexture(s_custom, targets[(int)GBufferTarget.Custom], point);
        properties.SetTexture(s_baked, targets[(int)GBufferTarget.Baked], point);
        properties.SetTexture(s_emission, targets[(int)GBufferTarget.Emission], point);
        properties.SetTexture(s_motion, targets[(int)GBufferTarget.Motion], point);
        properties.SetTexture(s_depth, gbuffer.DepthTexture!, point);
    }
}

/// <summary>
/// Lays the world's decals over a deferred view's GBuffer once its pass has drawn, into the targets it publishes as
/// <see cref="RendererOutputs.GBufferDecals"/>. Adds its pass while the world has a decal. Group
/// <see cref="FeatureGroup.GBufferDecals"/>.
/// </summary>
public sealed class GBufferDecalsFeature : RenderFeature
{
    private sealed class State
    {
        public GBufferRead GBuffer;
        public TextureHandle Target;
    }

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal GBufferDecalsFeature(RendererResources resources) : base(RenderSlot.GBuffer, 50, FeatureGroup.GBufferDecals)
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.GBufferDecalsProgram);
    }

    public override bool IsActive(in FeatureView view) => view.World.Lighting.LiveDecals > 0;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        State state = builder.GetViewState<State>();
        state.GBuffer = GBufferRead.Declare(builder, decals: false);
        builder.ReadViews();
        builder.DrawsRenderables();
        PixelFormat[] formats = _resources.GBufferFormats;
        GraphTextureDesc desc = new()
        {
            Width = (int)view.Width,
            Height = (int)view.Height,
            ColorFormats = [formats[(int)GBufferTarget.Base], formats[(int)GBufferTarget.Normal], formats[(int)GBufferTarget.Surface]],
            ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0,
        };
        state.Target = builder.Temporary("Decaled", desc);
        builder.Publish(RendererOutputs.GBufferDecals, state.Target);
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        state.GBuffer.Bind(context, _properties, _resources.PointClamp);
        context.Fullscreen(_resources.GBufferDecalsProgram, context.Get(state.Target), _properties);
    }
}

/// <summary>
/// Copies a deferred view's GBuffer depth, motion and normals onto its scene, where the prepass drew nothing nearer, for
/// the features before lighting that read the scene. Group <see cref="FeatureGroup.GBufferResolve"/>.
/// </summary>
public sealed class GBufferResolveFeature : RenderFeature
{
    private sealed class State
    {
        public GBufferRead GBuffer;
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
        builder.GetViewState<State>().GBuffer = GBufferRead.Declare(builder);
        builder.ReadViews();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.GetViewState<State>().GBuffer.Bind(context, _properties, _resources.PointClamp);
        context.FullscreenScene(_resources.GBufferResolveProgram, _properties);
    }
}
