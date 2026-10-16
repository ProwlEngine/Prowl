using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Prowl's temporal anti aliasing, in the Upscale slot: while it runs the view's projection jitters through Halton(2, 3)
/// sub pixel offsets, and each frame blends with the last ones reprojected by the scene's motion, clipped to the
/// neighbourhood's colour spread, then sharpened. Layered views keep a history per layer. In the AntiAliasing group with
/// FXAA and SMAA.
/// </summary>
public sealed class TemporalAntiAliasing : RenderFeature
{
    public override SceneTargets SceneReads => SceneTargets.Motion;

    private readonly EffectResources _resources;
    private readonly FullscreenProgram _resolve, _sharpen;
    private readonly PropertySet _properties = new(), _sharpening = new();
    private Settings _settings;

    // The history texture of one view
    private sealed class ViewHistory
    {
        public TextureHandle Texture;
    }

    private readonly record struct Settings(float BlendFactor, float MotionBlendFactor, float MotionScale, float Sharpness);

    public TemporalAntiAliasing(Renderer renderer)
    {
        _resources = renderer.Effects;
        _resolve = _resources.Program("Taa");
        _sharpen = _resources.Program("Taa", "#define TAA_SHARPEN 1\n");
    }

    /// <summary>How much history is kept, 0 to 0.99. Higher is smoother and ghosts more.</summary>
    public float BlendFactor { get; set; } = 0.95f;

    /// <summary>How much history is kept where the image moves fast, 0 to 0.99.</summary>
    public float MotionBlendFactor { get; set; } = 0.85f;

    /// <summary>How much faster motion tightens the history's clip.</summary>
    public float MotionScale { get; set; } = 2;

    /// <summary>Sharpening after the resolve, 0 for none to 1 for strong.</summary>
    public float Sharpness { get; set; } = 0.025f;

    /// <summary>Halton offsets before the jitter repeats.</summary>
    public int JitterSpread { get; set; } = 8;

    /// <summary>Texture LOD bias while jittered, negative so textures are not blurred twice.</summary>
    public float MipBias { get; set; } = -0.5f;

    public override RenderSlot Slot => RenderSlot.Upscale;
    public override string? ExclusiveGroup => "AntiAliasing";
    public override int JitterPhases => Math.Max(JitterSpread, 1);
    public override float JitterMipBias => MipBias;

    public override void Extract()
        => _settings = new Settings(Math.Clamp(BlendFactor, 0, 0.99f), Math.Clamp(MotionBlendFactor, 0, 0.99f), Math.Max(MotionScale, 0), Math.Clamp(Sharpness, 0, 1));

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

        _properties.SetTexture("_History", previous?.ColorTextures[0] ?? context.Color!, _resources.LinearClamp);
        _properties.SetTexture("_Motion", context.Motion!, _resources.PointClamp);
        _properties.SetTexture("_Depth", context.Depth!, _resources.PointClamp);
        _properties.SetFloat2("_Resolution", resolution);
        _properties.SetFloat("_HistoryValid", previous != null && context.HasPreviousFrame ? 1 : 0);
        _properties.SetFloat("_BlendFactor", _settings.BlendFactor);
        _properties.SetFloat("_MotionBlendFactor", _settings.MotionBlendFactor);
        _properties.SetFloat("_MotionScale", _settings.MotionScale);
        context.Fullscreen(_resolve, history, _properties);

        // Sharpened on the way out, after the history took the resolve, so sharpening never feeds back
        _sharpening.SetTexture("_Resolved", history.ColorTextures[0], _resources.PointClamp);
        _sharpening.SetFloat2("_Resolution", resolution);
        _sharpening.SetFloat("_Sharpness", _settings.Sharpness);
        context.Fullscreen(_sharpen, _sharpening);
    }
}
