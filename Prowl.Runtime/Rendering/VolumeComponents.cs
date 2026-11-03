// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

internal static class EffectSettings
{
    public static Float4 ToFloat4(this Color color) => new(color.R, color.G, color.B, color.A);
}

/// <summary>How an HDR camera maps its colour to the display.</summary>
public enum TonemappingMode
{
    None,
    ACES,

    /// <summary>AgX with no look, flat and desaturated.</summary>
    AgXNeutral,

    /// <summary>AgX with the reference Punchy look: more contrast and saturation.</summary>
    AgXPunchy,

    /// <summary>Extended Reinhard on luminance, white at 2.</summary>
    Reinhard,
    RomBinDaHouse,
    Uncharted2,
}

/// <summary>
/// Maps HDR colour to the display. Off while the mode is None, when an HDR camera clips light above 1. New cameras get
/// one in their base profile, see <see cref="VolumeProfile.CreateCameraDefault"/>.
/// </summary>
[VolumeComponentMenu("Image/Tonemapping")]
public sealed class Tonemapping : VolumeComponent<G.Tonemapper>
{
    public VolumeParameter<TonemappingMode> Mode = new(TonemappingMode.None);

    public override bool IsActive => Mode.Value != TonemappingMode.None;

    protected override G.Tonemapper CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Tonemapper feature, Camera camera) => feature.Operator = (G.TonemapOperator)((int)Mode.Value - 1);
}

/// <summary>Adapts exposure to the scene's brightness over time, in place of the camera's EV100.</summary>
[VolumeComponentMenu("Camera/Auto Exposure")]
public sealed class AutoExposure : VolumeComponent<G.AutoExposure>
{
    public VolumeParameter<bool> Enabled = new(false);

    /// <summary>Stops brighter than the meter reads. The default puts the metered scene a stop over the 0.18 mid grey, so a bright sky does not pull the ground dark.</summary>
    [Range(-5f, 5f)] public VolumeParameter<float> ExposureCompensation = new(2.5f);
    public VolumeParameter<float> AdaptSpeedUp = new(3f);
    public VolumeParameter<float> AdaptSpeedDown = new(1.5f);

    /// <summary>The darkest scene metered, as the lowest EV100 the camera takes.</summary>
    public VolumeParameter<float> MinEV100 = new(-6f);

    /// <summary>The brightest scene metered, as the highest EV100 the camera takes.</summary>
    public VolumeParameter<float> MaxEV100 = new(20f);

    public override bool IsActive => Enabled.Value;

    protected override G.AutoExposure CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.AutoExposure feature, Camera camera)
    {
        feature.ExposureCompensation = ExposureCompensation;
        feature.AdaptSpeedUp = AdaptSpeedUp;
        feature.AdaptSpeedDown = AdaptSpeedDown;
        feature.MinEV100 = MinEV100;
        feature.MaxEV100 = MaxEV100;
    }
}

/// <summary>Bright colour bleeding into its surroundings. Off at zero intensity.</summary>
[VolumeComponentMenu("Image/Bloom")]
public sealed class Bloom : VolumeComponent<G.Bloom>
{
    [Range(0f, 10f)] public VolumeParameter<float> Intensity = new(0f);
    public VolumeParameter<float> Threshold = new(1.2f);
    [Range(0f, 1f)] public VolumeParameter<float> SoftKnee = new(0.5f);
    public VolumeParameter<float> Clamp = new(65000f);
    public VolumeParameter<bool> AntiFlicker = new(false);
    [Range(0f, 16f)] public VolumeParameter<int> Iterations = new(6);
    [Range(0f, 1f)] public VolumeParameter<float> Scatter = new(0.7f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.Bloom CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Bloom feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Threshold = Threshold;
        feature.SoftKnee = SoftKnee;
        feature.Clamp = Clamp;
        feature.AntiFlicker = AntiFlicker;
        feature.Iterations = Iterations.Value;
        feature.Scatter = Scatter;
    }
}

/// <summary>Ground truth ambient occlusion, darkening indirect light where geometry crowds it. Off at zero intensity.</summary>
[VolumeComponentMenu("Lighting/Ambient Occlusion")]
public sealed class AmbientOcclusion : VolumeComponent<G.Gtao>
{
    [Range(0f, 4f)] public VolumeParameter<float> Intensity = new(0f);
    public VolumeParameter<float> Radius = new(1f);
    [Range(1f, 16f)] public VolumeParameter<int> Slices = new(6);
    [Range(1f, 64f)] public VolumeParameter<int> DirectionSamples = new(32);
    public VolumeParameter<float> BlurRadius = new(1f);
    public VolumeParameter<bool> UseTemporal = new(true);
    [Range(0f, 1f)] public VolumeParameter<float> TemporalResponse = new(0.9f);
    public VolumeParameter<EffectResolution> Resolution = new(EffectResolution.Quarter);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.Gtao CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Gtao feature, Camera camera)
    {
        feature.Slices = Slices;
        feature.DirectionSamples = DirectionSamples;
        feature.Radius = Radius;
        feature.Intensity = Intensity;
        feature.BlurRadius = BlurRadius;
        feature.UseTemporal = UseTemporal;
        feature.TemporalResponse = TemporalResponse;
        feature.Resolution = (G.EffectResolution)Resolution.Value;
    }
}

/// <summary>Stochastic screen space reflections over the opaque scene.</summary>
[VolumeComponentMenu("Lighting/Screen Space Reflections")]
public sealed class ScreenSpaceReflections : VolumeComponent<G.ScreenSpaceReflections>
{
    public VolumeParameter<bool> Enabled = new(false);
    public VolumeParameter<EffectResolution> RayResolution = new(EffectResolution.Half);
    [Range(1f, 100f)] public VolumeParameter<float> RayDistance = new(70f);
    [Range(0f, 1f)] public VolumeParameter<float> BRDFBias = new(0.7f);
    public VolumeParameter<bool> RayReuse = new(true);
    public VolumeParameter<bool> Normalization = new(true);
    public VolumeParameter<bool> ReduceFireflies = new(true);
    public VolumeParameter<bool> UseMipMap = new(true);
    public VolumeParameter<bool> UseTemporal = new(true);
    public VolumeParameter<float> TemporalScale = new(2f);
    [Range(0f, 1f)] public VolumeParameter<float> TemporalResponse = new(0.85f);
    public VolumeParameter<bool> ReflectionVelocity = new(true);
    [Range(0f, 1f)] public VolumeParameter<float> ScreenFadeSize = new(0.25f);

    public override bool IsActive => Enabled.Value;

    protected override G.ScreenSpaceReflections CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.ScreenSpaceReflections feature, Camera camera)
    {
        feature.RayResolution = (G.EffectResolution)RayResolution.Value;
        feature.RayDistance = RayDistance;
        feature.BrdfBias = BRDFBias;
        feature.RayReuse = RayReuse;
        feature.Normalization = Normalization;
        feature.ReduceFireflies = ReduceFireflies;
        feature.UseMipMap = UseMipMap;
        feature.UseTemporal = UseTemporal;
        feature.TemporalScale = TemporalScale;
        feature.TemporalResponse = TemporalResponse;
        feature.ReflectionVelocity = ReflectionVelocity;
        feature.ScreenFadeSize = ScreenFadeSize;
    }
}

/// <summary>Blur along each pixel's motion. Off at zero intensity.</summary>
[VolumeComponentMenu("Camera/Motion Blur")]
public sealed class MotionBlur : VolumeComponent<G.MotionBlur>
{
    [Range(0f, 2f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(1f, 32f)] public VolumeParameter<int> Samples = new(8);
    public VolumeParameter<float> MaxBlurRadius = new(40f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.MotionBlur CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.MotionBlur feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Samples = Samples;
        feature.MaxBlurRadius = MaxBlurRadius;
    }
}

public enum DepthOfFieldMode
{
    Off,

    /// <summary>Focuses on what is at the center of the view.</summary>
    Auto,

    /// <summary>Focuses at <see cref="DepthOfField.FocusDistance"/>.</summary>
    Manual,
}

/// <summary>Bokeh depth of field, blurring what is in front of and behind the focus distance.</summary>
[VolumeComponentMenu("Camera/Depth Of Field")]
public sealed class DepthOfField : VolumeComponent<G.DepthOfField>
{
    public VolumeParameter<DepthOfFieldMode> Mode = new(DepthOfFieldMode.Off);
    public VolumeParameter<float> FocusDistance = new(10f);
    public VolumeParameter<float> FocusStrength = new(2f);
    public VolumeParameter<float> MaxBlurRadius = new(2f);
    public VolumeParameter<DepthOfFieldResolution> Resolution = new(DepthOfFieldResolution.Half);
    public VolumeParameter<DepthOfFieldQuality> Quality = new(DepthOfFieldQuality.Medium);
    public VolumeParameter<float> FocusSpeed = new(4f);

    public override bool IsActive => Mode.Value != DepthOfFieldMode.Off;

    protected override G.DepthOfField CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.DepthOfField feature, Camera camera)
    {
        feature.UseAutoFocus = Mode.Value == DepthOfFieldMode.Auto;
        feature.ManualFocusPoint = FocusDistance;
        feature.FocusStrength = FocusStrength;
        feature.MaxBlurRadius = MaxBlurRadius;
        feature.Resolution = (G.DepthOfFieldResolution)Resolution.Value;
        feature.Quality = (G.DepthOfFieldQuality)Quality.Value;
        feature.FocusSpeed = FocusSpeed;
    }
}

/// <summary>Light shafts from the sun across bright sky. Off at zero intensity.</summary>
[VolumeComponentMenu("Image/God Rays")]
public sealed class GodRays : VolumeComponent<G.GodRays>
{
    [Range(0f, 2f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(0.9f, 1f)] public VolumeParameter<float> Decay = new(0.96f);
    [Range(0.1f, 2f)] public VolumeParameter<float> Density = new(0.5f);
    [Range(0f, 1f)] public VolumeParameter<float> Weight = new(0.6f);
    [Range(8f, 128f)] public VolumeParameter<int> Samples = new(64);
    [Range(0f, 1f)] public VolumeParameter<float> Threshold = new(0.8f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.GodRays CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.GodRays feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Decay = Decay;
        feature.Density = Density;
        feature.Weight = Weight;
        feature.Samples = Samples;
        feature.Threshold = Threshold;
    }
}

/// <summary>Darkens toward the edges of the view. Off at zero intensity.</summary>
[VolumeComponentMenu("Image/Vignette")]
public sealed class Vignette : VolumeComponent<G.Vignette>
{
    [Range(0f, 1f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(0.01f, 1f)] public VolumeParameter<float> Smoothness = new(0.2f);
    [Range(0f, 1f)] public VolumeParameter<float> Roundness = new(1f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.Vignette CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Vignette feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Smoothness = Smoothness;
        feature.Roundness = Roundness;
    }
}

/// <summary>Red pushed outward and blue inward toward the edges. Off at zero intensity.</summary>
[VolumeComponentMenu("Image/Chromatic Aberration")]
public sealed class ChromaticAberration : VolumeComponent<G.ChromaticAberration>
{
    [Range(0f, 20f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(0f, 2f)] public VolumeParameter<float> Distortion = new(0.5f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.ChromaticAberration CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.ChromaticAberration feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Distortion = Distortion;
    }
}

/// <summary>Grain changing every frame. Off at zero intensity.</summary>
[VolumeComponentMenu("Image/Film Grain")]
public sealed class FilmGrain : VolumeComponent<G.FilmGrain>
{
    [Range(0f, 1f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(0f, 1f)] public VolumeParameter<float> Response = new(0.5f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.FilmGrain CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.FilmGrain feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Response = Response;
    }
}

/// <summary>Exposure, contrast, saturation, temperature, then lift, gamma and gain. Off while every value is zero.</summary>
[VolumeComponentMenu("Color/Color Grading")]
public sealed class ColorGrading : VolumeComponent<G.ColorGrading>
{
    [Range(-5f, 5f)] public VolumeParameter<float> PostExposure = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Contrast = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Saturation = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Temperature = new(0f);
    [Header("Lift, Gamma, Gain")]
    public VolumeParameter<Color> Lift = new(new Color(0f, 0f, 0f, 0f));
    public VolumeParameter<Color> Gamma = new(new Color(0f, 0f, 0f, 0f));
    public VolumeParameter<Color> Gain = new(new Color(0f, 0f, 0f, 0f));

    public override bool IsActive
        => PostExposure.Value != 0f || Contrast.Value != 0f || Saturation.Value != 0f || Temperature.Value != 0f
           || IsSet(Lift.Value) || IsSet(Gamma.Value) || IsSet(Gain.Value);

    private static bool IsSet(Color c) => c.R != 0f || c.G != 0f || c.B != 0f || c.A != 0f;

    protected override G.ColorGrading CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.ColorGrading feature, Camera camera)
    {
        feature.PostExposure = PostExposure;
        feature.Contrast = Contrast;
        feature.Saturation = Saturation;
        feature.Temperature = Temperature;
        feature.Lift = Lift.Value.ToFloat4();
        feature.Gamma = Gamma.Value.ToFloat4();
        feature.Gain = Gain.Value.ToFloat4();
    }
}

/// <summary>Grading from a lookup strip texture, its slices side by side (256 by 16 for a 16 cube). Off without a texture.</summary>
[VolumeComponentMenu("Color/Color Lookup")]
public sealed class ColorLookup : VolumeComponent<G.ColorLookup>
{
    public VolumeParameter<Texture2D?> Texture = new(null);
    [Range(0f, 1f)] public VolumeParameter<float> Contribution = new(1f);

    public override bool IsActive => Texture.Value.IsValid() && Contribution.Value > 0f;

    protected override G.ColorLookup CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.ColorLookup feature, Camera camera)
    {
        Texture2D? texture = Texture.Value;
        feature.Texture = texture.IsValid() ? texture.GpuTexture : null;
        feature.Contribution = Contribution;
    }
}

/// <summary>Contrast adaptive sharpening. Off at zero amount.</summary>
[VolumeComponentMenu("Image/Sharpen")]
public sealed class Sharpen : VolumeComponent<G.Sharpen>
{
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(0f);
    [Range(1f, 4f)] public VolumeParameter<float> Radius = new(1f);

    public override bool IsActive => Amount.Value > 0f;

    protected override G.Sharpen CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Sharpen feature, Camera camera)
    {
        feature.Amount = Amount;
        feature.Radius = Radius;
    }
}

/// <summary>Sobel edges drawn in a colour over the faded image. Off at zero intensity.</summary>
[VolumeComponentMenu("Stylize/Edge Detection")]
public sealed class EdgeDetection : VolumeComponent<G.EdgeDetection>
{
    [Range(0f, 5f)] public VolumeParameter<float> Intensity = new(0f);
    public VolumeParameter<Color> Color = new(new Color(0f, 0f, 0f, 1f));
    [Range(0f, 1f)] public VolumeParameter<float> BackgroundFade = new(1f);

    public override bool IsActive => Intensity.Value > 0f;

    protected override G.EdgeDetection CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.EdgeDetection feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Color = Color.Value.ToFloat4();
        feature.BackgroundFade = BackgroundFade;
    }
}

/// <summary>The image in square blocks. Off at a pixel size of 1.</summary>
[VolumeComponentMenu("Stylize/Pixelation")]
public sealed class Pixelation : VolumeComponent<G.Pixelation>
{
    [Range(1f, 32f)] public VolumeParameter<float> PixelSize = new(1f);

    public override bool IsActive => PixelSize.Value > 1f;

    protected override G.Pixelation CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Pixelation feature, Camera camera) => feature.PixelSize = PixelSize;
}
