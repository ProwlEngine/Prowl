using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Contact shadows of a deferred view: short shadows marched through the view's own depth from each pixel of its
/// GBuffer toward the sun, for the small gaps and contact points a shadow map misses. It runs before the lighting and
/// publishes the result as <see cref="RendererOutputs.SunShadowMask"/>, which the lighting dims the sun by.
/// </summary>
public sealed class ContactShadows : PostEffect
{
    private static readonly PropertyID s_depth = "_Depth", s_normal = "_Normal", s_settings = "_Settings";

    private sealed class State
    {
        public TextureHandle Mask;
        public GBufferRead GBuffer;
    }

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private Float4 _settings;

    public ContactShadows(Renderer renderer) : base(renderer, RenderSlot.AfterPrepass, sceneReads: SceneTargets.Normals)
    {
        Paths = RenderPaths.Deferred;
        _program = Program("ContactShadows", source: false, lit: true);
    }

    /// <summary>How far a ray reaches, as a part of the pixel's distance from the camera. 0 for none.</summary>
    public float Length { get; set; } = 0.05f;

    /// <summary>Depth samples a ray takes, 1 to 64.</summary>
    public int Steps { get; set; } = 8;

    /// <summary>How thick what the depth shows is taken to be, as a part of the pixel's distance from the camera.</summary>
    public float Thickness { get; set; } = 0.02f;

    public override void Extract() => _settings = new Float4(MathF.Max(Length, 0), Math.Clamp(Steps, 1, 64), MathF.Max(Thickness, 0), 0);

    public override bool IsActive(in FeatureView view) => _settings.X > 0;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.DrawsRenderables();
        State state = builder.GetViewState<State>();
        state.GBuffer = GBufferRead.Declare(builder);
        state.Mask = builder.Temporary("Mask", view.Texture(PixelFormat.R8_UNorm));
        builder.Publish(RendererOutputs.SunShadowMask, state.Mask);
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        _properties.SetTexture(s_depth, context.Depth!, PointClamp);
        _properties.SetTexture(s_normal, context.Normals!, PointClamp);
        state.GBuffer.BindSurface(context, _properties, PointClamp);
        _properties.SetFloat4(s_settings, _settings);
        context.Fullscreen(_program, context.Get(state.Mask), _properties);
    }
}
