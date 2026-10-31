using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Temporal anti aliasing, in the Upscale slot: while it runs the view's projection jitters through Halton(2, 3)
/// sub pixel offsets, and each frame blends with the last ones reprojected by the scene's motion, clipped to the
/// neighbourhood's colour spread, then sharpened. Where the scene's reactive mask says transmissive surfaces show what
/// they refract, which their motion does not carry, at most <see cref="ReactiveBlendFactor"/> of history is kept. Layered
/// views keep a history per layer. In the AntiAliasing group with FXAA and SMAA.
/// </summary>
public sealed class TemporalAntiAliasing : RenderFeature
{
    private static readonly PropertyID s_history = "_History", s_motion = "_Motion", s_reactive = "_Reactive", s_depth = "_Depth",
        s_resolution = "_Resolution", s_historyValid = "_HistoryValid", s_blendFactor = "_BlendFactor", s_motionBlendFactor = "_MotionBlendFactor",
        s_reactiveBlendFactor = "_ReactiveBlendFactor", s_motionScale = "_MotionScale", s_resolved = "_Resolved", s_sharpness = "_Sharpness";

    private readonly EffectResources _resources;
    private readonly RendererResources _shared;
    private readonly FullscreenProgram _resolve, _reactiveResolve, _sharpen;
    private readonly PropertySet _properties = new(), _sharpening = new();
    private Settings _settings;

    // The history texture of one view
    private sealed class ViewHistory
    {
        public TextureHandle Texture;
    }

    private readonly record struct Settings(float BlendFactor, float MotionBlendFactor, float ReactiveBlendFactor, float MotionScale, float Sharpness);

    public TemporalAntiAliasing(Renderer renderer) : base(RenderSlot.Upscale, group: FeatureGroup.AntiAliasing, sceneReads: SceneTargets.Motion | SceneTargets.Reactive)
    {
        _resources = renderer.Effects;
        _shared = renderer.Resources;
        _resolve = _resources.Program("Taa");
        _reactiveResolve = _resources.Program("Taa", "#define TAA_REACTIVE 1\n");
        _sharpen = _resources.Program("Taa", "#define TAA_SHARPEN 1\n");
    }

    /// <summary>How much history is kept, 0 to 0.99. Higher is smoother and ghosts more.</summary>
    public float BlendFactor { get; set; } = 0.95f;

    /// <summary>How much history is kept where the image moves fast, 0 to 0.99.</summary>
    public float MotionBlendFactor { get; set; } = 0.85f;

    /// <summary>How much history is kept where the reactive mask is full, 0 to 0.99, blended toward the usual amount as it falls.</summary>
    public float ReactiveBlendFactor { get; set; } = 0.5f;

    /// <summary>How much faster motion tightens the history's clip.</summary>
    public float MotionScale { get; set; } = 2;

    /// <summary>Sharpening after the resolve, 0 for none to 1 for strong.</summary>
    public float Sharpness { get; set; } = 0.025f;

    /// <summary>Halton offsets before the jitter repeats.</summary>
    public int JitterSpread { get; set; } = 8;

    /// <summary>Texture LOD bias while jittered, negative so textures are not blurred twice.</summary>
    public float MipBias { get; set; } = -0.5f;

    public override int JitterPhases => Math.Max(JitterSpread, 1);
    public override float JitterMipBias => MipBias;

    public override void Extract()
        => _settings = new Settings(Math.Clamp(BlendFactor, 0, 0.99f), Math.Clamp(MotionBlendFactor, 0, 0.99f), Math.Clamp(ReactiveBlendFactor, 0, 0.99f), Math.Max(MotionScale, 0), Math.Clamp(Sharpness, 0, 1));

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        builder.GetViewState<ViewHistory>().Texture = builder.History("History", view.Texture(PixelFormat.R16_G16_B16_A16_Float), usage: TextureState.Attachment | TextureState.Sampled);
    }

    public override void Render(FeatureContext context)
    {
        TextureHandle handle = context.GetViewState<ViewHistory>().Texture;
        RenderTexture history = context.Get(handle);
        RenderTexture? previous = context.History(handle);
        Float2 resolution = new(context.View.Width, context.View.Height);

        _properties.SetTexture(s_history, previous?.ColorTextures[0] ?? context.Color!, _shared.LinearClamp);
        _properties.SetTexture(s_motion, context.Motion!, _shared.PointClamp);
        if (context.Reactive is { } reactive)
            _properties.SetTexture(s_reactive, reactive, _shared.PointClamp);
        _properties.SetTexture(s_depth, context.Depth!, _shared.PointClamp);
        _properties.SetFloat2(s_resolution, resolution);
        _properties.SetFloat(s_historyValid, previous != null && context.HasPreviousFrame ? 1 : 0);
        _properties.SetFloat(s_blendFactor, _settings.BlendFactor);
        _properties.SetFloat(s_motionBlendFactor, _settings.MotionBlendFactor);
        _properties.SetFloat(s_reactiveBlendFactor, _settings.ReactiveBlendFactor);
        _properties.SetFloat(s_motionScale, _settings.MotionScale);
        context.Fullscreen(context.Reactive != null ? _reactiveResolve : _resolve, history, _properties);

        // Sharpened on the way out, after the history took the resolve, so sharpening never feeds back
        _sharpening.SetTexture(s_resolved, history.ColorTextures[0], _shared.PointClamp);
        _sharpening.SetFloat2(s_resolution, resolution);
        _sharpening.SetFloat(s_sharpness, _settings.Sharpness);
        context.Fullscreen(_sharpen, _sharpening);
    }
}
