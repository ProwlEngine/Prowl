using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The fullscreen programs of the stock effects, built from the library's Graphite/Internal files and
/// shared by every effect of a renderer, so a new effect instance compiles nothing already built. The renderer owns
/// them and frees them when it is disposed.
/// </summary>
internal sealed class EffectResources : IDisposable
{
    private readonly Renderer _renderer;
    private readonly Dictionary<string, FullscreenProgram> _programs = new(StringComparer.Ordinal);

    public void Dispose()
    {
        foreach (FullscreenProgram program in _programs.Values)
            program.Dispose();
        _programs.Clear();
    }

    /// <summary>How many programs were built, for tests.</summary>
    internal int ProgramCount => _programs.Count;

    public EffectResources(Renderer renderer) => _renderer = renderer;

    /// <summary>
    /// The program of a library file under a set of defines, each line ending in a newline, made the first time, which
    /// the feature drawing with it waits on. Host thread.
    /// </summary>
    public FullscreenProgram Program(RenderFeature user, string file, string defines = "", bool source = true, BlendStateDescription? blend = null, bool lit = false)
    {
        string key = file + "\n" + defines + (source ? "\n1" : "\n0") + (lit ? "\n1" : "\n0");
        lock (_programs)
        {
            if (!_programs.TryGetValue(key, out FullscreenProgram? program))
            {
                program = _renderer.CreateFullscreenProgram(InternalShaders.PostFragment(file), $"Graphite/{file}", source, blend, lit, defines);
                _programs[key] = program;
            }
            user.Uses(program);
            return program;
        }
    }
}

/// <summary>
/// A stock effect: its programs built from the library's Graphite/Internal files
/// and its settings copied at Extract. Effects of one renderer share their programs, which the renderer owns. An effect
/// sits out of its views until its programs are compiled, so the colour passes through unchanged meanwhile.
/// </summary>
public abstract class PostEffect(Renderer renderer, RenderSlot slot, FeatureGroup? group = null, SceneTargets sceneReads = SceneTargets.None)
    : RenderFeature(slot, group: group, sceneReads: sceneReads)
{
    private protected EffectResources Resources { get; } = renderer.Effects;
    private readonly RendererResources _shared = renderer.Resources;

    protected Sampler LinearClamp => _shared.LinearClamp;
    protected Sampler PointClamp => _shared.PointClamp;
    protected Sampler LinearRepeat => _shared.LinearRepeat;

    /// <summary>The program of a library file under a set of defines, each line ending in a newline, made the first time. Host thread.</summary>
    protected FullscreenProgram Program(string file, string defines = "", bool source = true, BlendStateDescription? blend = null, bool lit = false)
        => Resources.Program(this, file, defines, source, blend, lit);

    protected static PixelFormat Hdr => PixelFormat.R16_G16_B16_A16_Float;
}

/// <summary>The resolution a screen space effect works at, as a part of the view's.</summary>
public enum EffectResolution
{
    Full,
    Half,
    Quarter,
}

internal static class EffectResolutionScale
{
    public static float Scale(this EffectResolution resolution) => resolution switch
    {
        EffectResolution.Half => 0.5f,
        EffectResolution.Quarter => 0.25f,
        _ => 1f,
    };
}

/// <summary>The Halton sequence term of an index in a radix, 0 to 1.</summary>
internal sealed class FrameCounter
{
    public int Index;
}

internal static class Halton
{
    public static float Of(int index, int radix)
    {
        float result = 0, fraction = 1f / radix;
        for (int i = index; i > 0; i /= radix)
        {
            result += i % radix * fraction;
            fraction /= radix;
        }
        return result;
    }
}

public enum TonemapOperator
{
    Aces,

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
/// The tonemapper, the Tonemap slot's exclusive feature: an operator into 0 to 1, then the sRGB encode. Later slots
/// read display encoded colour, which the present decodes for sRGB targets.
/// </summary>
public sealed class Tonemapper : PostEffect
{
    private static readonly PropertyID s_operator = "_TonemapOperator";

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private TonemapOperator _operator;

    public Tonemapper(Renderer renderer) : base(renderer, RenderSlot.Tonemap, FeatureGroup.Tonemap) => _program = Program("Tonemap");

    public TonemapOperator Operator { get; set; } = TonemapOperator.Aces;

    public override void Extract() => _operator = Operator;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.WriteColor();
        builder.EncodesForDisplay();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetInt(s_operator, (int)_operator);
        context.Fullscreen(_program, _properties);
    }
}

/// <summary>
/// Auto exposure with a histogram meter: the scene's log luminance measured at half resolution with its
/// pre-exposure undone, box downsampled, binned and averaged between <see cref="LowPercent"/> and
/// <see cref="HighPercent"/>, adapted over time, faster when brightening, then metered into the exposure its views take
/// from the next frame on, in place of their <see cref="RenderView.EV100"/>. It changes no colour itself. A
/// layered view measures its first layer and its layers share one exposure.
/// </summary>
public sealed class AutoExposure : PostEffect
{
    private static readonly PropertyID s_mainTex = "_MainTex", s_logMin = "_LogMin", s_logMax = "_LogMax", s_adaptedTex = "_AdaptedTex",
        s_lowPercent = "_LowPercent", s_highPercent = "_HighPercent", s_adaptSpeedUp = "_AdaptSpeedUp", s_adaptSpeedDown = "_AdaptSpeedDown",
        s_historyValid = "_HistoryValid", s_deltaTime = "_DeltaTime", s_exposureComp = "_ExposureComp", s_minEV100 = "_MinEV100",
        s_maxEV100 = "_MaxEV100";

    private readonly FullscreenProgram _extract, _downsample, _histogram, _adapt, _meter;
    private readonly PropertySet _properties = new();
    private (float Compensation, float Up, float Down, float Min, float Max, float Low, float High) _settings;

    private const int Bins = 64;
    private const float HistogramWidth = 64;

    private sealed class ViewState
    {
        public readonly List<TextureHandle> Levels = new();
        public TextureHandle Histogram;
        public TextureHandle Adapted;
        public TextureHandle Exposure;
    }

    public AutoExposure(Renderer renderer) : base(renderer, RenderSlot.HdrPost)
    {
        _extract = Program("AutoExposure", "#define AE_EXTRACT 1\n", source: false);
        _downsample = Program("AutoExposure", "#define AE_DOWNSAMPLE 1\n", source: false);
        _histogram = Program("AutoExposure", "#define AE_HISTOGRAM 1\n", source: false);
        _adapt = Program("AutoExposure", "#define AE_ADAPT 1\n", source: false);
        _meter = Program("AutoExposure", source: false);
        Order = 20;
    }

    /// <summary>
    /// Exposure compensation in stops, brighter when above 0. A K 12.5 meter alone puts the metered average at 0.104,
    /// 0.79 stops under the 0.18 mid grey the tonemappers expect, and metering the brighter share between the
    /// percentiles reads a daylight scene about 0.7 stops brighter than its whole average. The default adds both and a stop more, so a bright sky does not pull the ground dark.
    /// </summary>
    public float ExposureCompensation { get; set; } = 2.5f;

    /// <summary>How fast exposure follows the scene getting brighter, the gap in stops shrinking by exp(-speed * seconds).</summary>
    public float AdaptSpeedUp { get; set; } = 3;

    /// <summary>How fast exposure follows the scene getting darker, the gap in stops shrinking by exp(-speed * seconds).</summary>
    public float AdaptSpeedDown { get; set; } = 1.5f;

    /// <summary>The darkest scene metered, as the lowest EV100 the views take.</summary>
    public float MinEV100 { get; set; } = -6;

    /// <summary>The brightest scene metered, as the highest EV100 the views take.</summary>
    public float MaxEV100 { get; set; } = 20;

    /// <summary>The share of the darkest texels the meter leaves out, so a dark sky or a black backdrop does not pull it.</summary>
    public float LowPercent { get; set; } = 0.7f;

    /// <summary>The share of texels up to which the meter averages, leaving out the brightest highlights above it.</summary>
    public float HighPercent { get; set; } = 0.98f;

    internal override bool MeasuresExposure => true;

    public override void Extract()
    {
        float low = Math.Clamp(LowPercent, 0, 0.99f);
        _settings = (ExposureCompensation, Math.Max(0.01f, AdaptSpeedUp), Math.Max(0.01f, AdaptSpeedDown), MinEV100, Math.Max(MinEV100, MaxEV100), low, Math.Clamp(HighPercent, low + 0.01f, 1));
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadColor();
        ViewState state = builder.GetViewState<ViewState>();
        state.Levels.Clear();
        for (int level = 0; level == 0 || view.Width * 0.5f / (1 << (level - 1)) > HistogramWidth; level++)
            state.Levels.Add(builder.Temporary("Luminance", level, view.SharedTexture(PixelFormat.R16_Float, 0.5f / (1 << level))));
        state.Histogram = builder.Temporary("Histogram", FeatureView.Shared(Bins, 1, PixelFormat.R32_G32_Float));
        state.Adapted = builder.History("Adapted", FeatureView.Shared(1, 1, PixelFormat.R32_Float), usage: TextureState.Attachment | TextureState.Sampled);
        state.Exposure = builder.WriteExposure();
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        _properties.SetTexture(s_mainTex, context.Color!, LinearClamp);
        context.Fullscreen(_extract, context.Get(state.Levels[0]), _properties, shared: true);
        for (int i = 1; i < state.Levels.Count; i++)
        {
            _properties.SetTexture(s_mainTex, context.Get(state.Levels[i - 1]).ColorTextures[0], LinearClamp);
            context.Fullscreen(_downsample, context.Get(state.Levels[i]), _properties, shared: true);
        }

        // The bins span the metered range and a few stops past it, in the natural log luminance the levels hold
        RenderTexture histogram = context.Get(state.Histogram);
        _properties.SetTexture(s_mainTex, context.Get(state.Levels[^1]).ColorTextures[0], LinearClamp);
        _properties.SetFloat(s_logMin, (_settings.Min - 5) * MathF.Log(2));
        _properties.SetFloat(s_logMax, (_settings.Max - 1) * MathF.Log(2));
        context.Fullscreen(_histogram, histogram, _properties, shared: true);

        RenderTexture adapted = context.Get(state.Adapted);
        RenderTexture? previous = context.History(state.Adapted);
        _properties.SetTexture(s_mainTex, histogram.ColorTextures[0], LinearClamp);
        _properties.SetTexture(s_adaptedTex, (previous ?? histogram).ColorTextures[0], LinearClamp);
        _properties.SetFloat(s_lowPercent, _settings.Low);
        _properties.SetFloat(s_highPercent, _settings.High);
        _properties.SetFloat(s_adaptSpeedUp, _settings.Up);
        _properties.SetFloat(s_adaptSpeedDown, _settings.Down);
        _properties.SetFloat(s_historyValid, previous != null ? 1 : 0);
        _properties.SetFloat(s_deltaTime, context.Time.DeltaTime);
        context.Fullscreen(_adapt, adapted, _properties, shared: true);

        _properties.SetTexture(s_adaptedTex, adapted.ColorTextures[0], LinearClamp);
        _properties.SetFloat(s_exposureComp, _settings.Compensation);
        _properties.SetFloat(s_minEV100, _settings.Min);
        _properties.SetFloat(s_maxEV100, _settings.Max);
        context.Fullscreen(_meter, context.Get(state.Exposure), _properties, shared: true);
    }
}

/// <summary>
/// Dual filter bloom: bright colour past a soft knee threshold at half resolution, a chain of downsamples,
/// upsamples mixing each level with the blurred one below by scatter, added back to the colour.
/// </summary>
public sealed class Bloom : PostEffect
{
    private static readonly PropertyID s_threshold = "_Threshold", s_softKnee = "_SoftKnee", s_clamp = "_Clamp", s_antiFlicker = "_AntiFlicker",
        s_mainTex = "_MainTex", s_scatter = "_Scatter", s_highTex = "_HighTex", s_bloomTex = "_BloomTex", s_intensity = "_Intensity";

    private readonly FullscreenProgram _prefilter, _downsample, _upsample, _composite;
    private readonly PropertySet _properties = new();
    private (float Threshold, float Knee, float Clamp, bool AntiFlicker, float Scatter, float Intensity) _settings;
    private int _iterations = -1;

    private sealed class ViewState
    {
        public readonly List<TextureHandle> Down = new();
        public readonly List<TextureHandle> Up = new();
    }

    public Bloom(Renderer renderer) : base(renderer, RenderSlot.HdrPost)
    {
        _prefilter = Program("Bloom", "#define BLOOM_PREFILTER 1\n");
        _downsample = Program("Bloom", "#define BLOOM_DOWNSAMPLE 1\n", source: false);
        _upsample = Program("Bloom", "#define BLOOM_UPSAMPLE 1\n", source: false);
        _composite = Program("Bloom");
        Order = 30;
    }

    public float Intensity { get; set; } = 1.5f;

    /// <summary>Brightness above which colour blooms.</summary>
    public float Threshold { get; set; } = 1.2f;

    /// <summary>How gradually colour near the threshold fades in, 0 a hard cut to 1 the widest ramp.</summary>
    public float SoftKnee { get; set; } = 0.5f;

    /// <summary>The brightest colour fed into the bloom.</summary>
    public float Clamp { get; set; } = 65000;

    /// <summary>Weights the first blur by brightness so single pixel sparkles cannot flicker it, dimming small bright sources.</summary>
    public bool AntiFlicker { get; set; }

    /// <summary>Downsamples, each halving the size. More is wider.</summary>
    public int Iterations { get; set; } = 6;

    /// <summary>How far it spreads, 0 tight to 1 wide, keeping about the same brightness.</summary>
    public float Scatter { get; set; } = 0.7f;

    public override void Extract()
    {
        _settings = (Math.Max(0, Threshold), Math.Clamp(SoftKnee, 0, 1), Math.Max(0, Clamp), AntiFlicker, Math.Clamp(Scatter, 0, 1), Intensity);
        int iterations = Math.Clamp(Iterations, 0, 16);
        if (iterations != _iterations)
        {
            _iterations = iterations;
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        state.Down.Clear();
        state.Up.Clear();
        int count = _iterations + 1;
        for (int i = 0; i < count; i++)
            state.Down.Add(builder.Temporary("Down", i, view.Texture(Hdr, 0.5f / (1 << i))));
        for (int i = count - 2; i >= 0; i--)
            state.Up.Add(builder.Temporary("Up", i, view.Texture(Hdr, 0.5f / (1 << i))));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        _properties.Clear();
        _properties.SetFloat(s_threshold, _settings.Threshold);
        _properties.SetFloat(s_softKnee, _settings.Knee);
        _properties.SetFloat(s_clamp, _settings.Clamp);
        _properties.SetFloat(s_antiFlicker, _settings.AntiFlicker ? 1 : 0);
        context.Fullscreen(_prefilter, context.Get(state.Down[0]), _properties);
        for (int i = 1; i < state.Down.Count; i++)
        {
            _properties.SetTexture(s_mainTex, context.Get(state.Down[i - 1]).ColorTextures[0], LinearClamp);
            context.Fullscreen(_downsample, context.Get(state.Down[i]), _properties);
        }

        Texture lower = context.Get(state.Down[^1]).ColorTextures[0];
        _properties.SetFloat(s_scatter, _settings.Scatter);
        for (int i = 0; i < state.Up.Count; i++)
        {
            RenderTexture up = context.Get(state.Up[i]);
            _properties.SetTexture(s_mainTex, lower, LinearClamp);
            _properties.SetTexture(s_highTex, context.Get(state.Down[state.Down.Count - 2 - i]).ColorTextures[0], LinearClamp);
            context.Fullscreen(_upsample, up, _properties);
            lower = up.ColorTextures[0];
        }

        _properties.SetTexture(s_bloomTex, lower, LinearClamp);
        _properties.SetFloat(s_intensity, _settings.Intensity);
        context.Fullscreen(_composite, _properties);
    }
}

/// <summary>
/// GTAO as the view's screen lighting: visibility from a horizon search over slices of the depth, at a part of
/// the view's resolution, with blue noise scrolled per frame, accumulated over frames and blurred by depth. It occludes
/// indirect light only, read by every renderable drawn after. Normals are rebuilt from depth and history follows depth
/// and the camera, as the prepass leaves neither normals nor motion.
/// </summary>
public sealed class Gtao : ScreenLightingFeature
{
    private static readonly PropertyID s_cameraDepthTexture = "_CameraDepthTexture", s_noise = "_Noise", s_slices = "_Slices",
        s_directionSamples = "_DirectionSamples", s_radius = "_Radius", s_intensity = "_Intensity", s_noiseScale = "_NoiseScale",
        s_jitterOffset = "_JitterOffset", s_mainTex = "_MainTex", s_previousBuffer = "_PreviousBuffer", s_tResponse = "_TResponse",
        s_blurRadius = "_BlurRadius", s_screenSize = "_ScreenSize", s_blurDirection = "_BlurDirection";

    private readonly EffectResources _resources;
    private readonly RendererResources _shared;
    private readonly FullscreenProgram _depth, _ao, _temporal, _blur, _copy;
    private readonly PropertySet _properties = new();
    private Settings _settings;
    private (bool Temporal, bool Blur) _declared = (true, true);

    private readonly record struct Settings(int Slices, int DirectionSamples, float Radius, float Intensity, float BlurRadius, bool Temporal, float TemporalResponse);

    private sealed class ViewState
    {
        public TextureHandle Depth, Ao, History, Blur, Visibility;
    }

    public Gtao(Renderer renderer)
    {
        _resources = renderer.Effects;
        _shared = renderer.Resources;
        _depth = _resources.Program(this, "Gtao", "#define GTAO_DEPTH 1\n", source: false);
        _ao = _resources.Program(this, "Gtao", "#define GTAO_AO 1\n", source: false);
        _temporal = _resources.Program(this, "Gtao", "#define GTAO_TEMPORAL 1\n", source: false);
        _blur = _resources.Program(this, "Gtao", "#define GTAO_BLUR 1\n", source: false);
        _copy = _resources.Program(this, "Gtao", "", source: false);
    }

    /// <summary>Slices around each pixel.</summary>
    public int Slices { get; set; } = 6;

    /// <summary>Samples each way along a slice.</summary>
    public int DirectionSamples { get; set; } = 32;

    /// <summary>The world radius occlusion is searched over.</summary>
    public float Radius { get; set; } = 1;

    public float Intensity { get; set; } = 1;

    /// <summary>The bilateral blur's step, 0 for none.</summary>
    public float BlurRadius { get; set; } = 1;

    /// <summary>Accumulates over frames, so fewer slices and samples are needed.</summary>
    public bool UseTemporal { get; set; } = true;

    /// <summary>How much history is kept, 0 to 1.</summary>
    public float TemporalResponse { get; set; } = 0.9f;

    public EffectResolution Resolution { get; set; } = EffectResolution.Quarter;

    public override float Scale => Resolution.Scale();

    public override void Extract()
    {
        _settings = new Settings(Math.Max(1, Slices), Math.Max(1, DirectionSamples), Math.Max(0, Radius), Intensity, Math.Max(0, BlurRadius), UseTemporal, Math.Clamp(TemporalResponse, 0, 1));
        (bool, bool) declared = (UseTemporal, BlurRadius > 0.01f);
        if (declared != _declared)
        {
            _declared = declared;
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        ViewState state = builder.GetViewState<ViewState>();
        state.Visibility = builder.WriteScreenLighting(false).Visibility;
        state.Depth = builder.Temporary("Depth", view.Texture(PixelFormat.R32_Float, ScaleSnapshot));
        state.Ao = builder.Temporary("Ao", view.Texture(PixelFormat.R8_UNorm, ScaleSnapshot));
        if (_declared.Temporal)
            state.History = builder.History("History", view.Texture(PixelFormat.R8_UNorm, ScaleSnapshot), usage: TextureState.Attachment | TextureState.Sampled);
        if (_declared.Blur)
            state.Blur = builder.Temporary("Blur", view.Texture(PixelFormat.R8_UNorm, ScaleSnapshot));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        FrameCounter counter = context.GetViewState<FrameCounter>();
        counter.Index = (counter.Index + 1) % 64;
        RenderTexture depth = context.Get(state.Depth), ao = context.Get(state.Ao);

        _properties.SetTexture(s_cameraDepthTexture, context.Depth!, _shared.PointClamp);
        context.Fullscreen(_depth, depth, _properties);

        Float2 size = new(depth.ColorTextures[0].Width, depth.ColorTextures[0].Height);
        _properties.SetTexture(s_cameraDepthTexture, depth.ColorTextures[0], _shared.PointClamp);
        _properties.SetTexture(s_noise, context.Noise, _shared.LinearRepeat);
        _properties.SetInt(s_slices, _settings.Slices);
        _properties.SetInt(s_directionSamples, _settings.DirectionSamples);
        _properties.SetFloat(s_radius, _settings.Radius);
        _properties.SetFloat(s_intensity, _settings.Intensity);
        _properties.SetFloat2(s_noiseScale, size / 256f);
        _properties.SetFloat2(s_jitterOffset, new Float2(Halton.Of(counter.Index, 2), Halton.Of(counter.Index, 3)));
        context.Fullscreen(_ao, ao, _properties);

        RenderTexture result = ao;
        if (_declared.Temporal)
        {
            RenderTexture history = context.Get(state.History);
            RenderTexture? previous = context.History(state.History);
            _properties.SetTexture(s_mainTex, ao.ColorTextures[0], _shared.LinearClamp);
            if (previous != null && context.HasPreviousFrame)
            {
                _properties.SetTexture(s_previousBuffer, previous.ColorTextures[0], _shared.LinearClamp);
                _properties.SetFloat(s_tResponse, _settings.TemporalResponse);
                context.Fullscreen(_temporal, history, _properties);
            }
            else
            {
                context.Fullscreen(_copy, history, _properties);
            }
            result = history;
        }

        RenderTexture visibility = context.Get(state.Visibility);
        if (_declared.Blur)
        {
            RenderTexture blur = context.Get(state.Blur);
            _properties.SetFloat(s_blurRadius, _settings.BlurRadius);
            _properties.SetFloat2(s_screenSize, new Float2(context.View.Width, context.View.Height));
            _properties.SetTexture(s_mainTex, result.ColorTextures[0], _shared.LinearClamp);
            _properties.SetFloat2(s_blurDirection, new Float2(1, 0));
            context.Fullscreen(_blur, blur, _properties);
            _properties.SetTexture(s_mainTex, blur.ColorTextures[0], _shared.LinearClamp);
            _properties.SetFloat2(s_blurDirection, new Float2(0, 1));
            context.Fullscreen(_blur, visibility, _properties);
            return;
        }
        _properties.SetTexture(s_mainTex, result.ColorTextures[0], _shared.LinearClamp);
        context.Fullscreen(_copy, visibility, _properties);
    }
}

/// <summary>
/// Stochastic screen space reflections over the opaque scene: a GGX sampled ray per pixel at a part of the
/// resolution, resolved by reusing neighbouring rays with a pyramid level from a roughness cone, accumulated over frames
/// with last frame's result fed back for a bounce. It takes the place of the reflection probes in the top specular lobe,
/// weighted by the reflectance the material target holds.
/// </summary>
public sealed class ScreenSpaceReflections : PostEffect
{
    private static readonly PropertyID s_cameraDepthTexture = "_CameraDepthTexture", s_cameraNormalsTexture = "_CameraNormalsTexture",
        s_materialTexture = "_MaterialTexture", s_cameraMotionVectorsTexture = "_CameraMotionVectorsTexture", s_noise = "_Noise",
        s_noiseSize = "_NoiseSize", s_jitterSizeAndOffset = "_JitterSizeAndOffset", s_resolveSize = "_ResolveSize", s_screenSize = "_ScreenSize",
        s_numSteps = "_NumSteps", s_brdfBias = "_BRDFBias", s_edgeFactor = "_EdgeFactor", s_maxMipMap = "_MaxMipMap", s_rayReuse = "_RayReuse",
        s_useNormalization = "_UseNormalization", s_fireflies = "_Fireflies", s_reflectionVelocity = "_ReflectionVelocity", s_mainTex = "_MainTex",
        s_blurDir = "_BlurDir", s_rayCast = "_RayCast", s_previousBuffer = "_PreviousBuffer", s_tScale = "_TScale",
        s_tResponse = "_TResponse", s_reflectionBuffer = "_ReflectionBuffer";
    private static readonly PropertyID[] s_scenes = ["_Scene0", "_Scene1", "_Scene2", "_Scene3", "_Scene4"];

    private const int MaxMip = 5;
    private readonly FullscreenProgram _rayCast, _blur, _resolve, _temporal, _reproject, _combine, _copy;
    private readonly PropertySet _properties = new();
    private Settings _settings;
    private (bool MipMap, bool Temporal, float Scale) _declared = (true, true, 0.5f);

    private readonly record struct Settings(float RayDistance, float BrdfBias, bool RayReuse, bool Normalization, bool Fireflies, float TemporalScale, float TemporalResponse, bool ReflectionVelocity, float ScreenFade);

    private sealed class ViewState
    {
        public TextureHandle Source, Combined, ReflectionHistory, RayData, Reflection;
        public readonly TextureHandle[] Pyramid = new TextureHandle[4];
        public readonly TextureHandle[] Scratch = new TextureHandle[4];
    }

    public ScreenSpaceReflections(Renderer renderer) : base(renderer, RenderSlot.AfterOpaques, sceneReads: SceneTargets.Motion | SceneTargets.Normals | SceneTargets.Material)
    {
        _rayCast = Program("Ssr", "#define SSR_RAYCAST 1\n", source: false);
        _blur = Program("Ssr", "#define SSR_BLUR 1\n", source: false);
        _resolve = Program("Ssr", "#define SSR_RESOLVE 1\n", source: false);
        _temporal = Program("Ssr", "#define SSR_TEMPORAL 1\n", source: false);
        _reproject = Program("Ssr", "#define SSR_REPROJECT 1\n", source: false);
        _combine = Program("Ssr", lit: true, defines: "#define SSR_COMBINE 1\n");
        _copy = Program("Ssr", "", source: false);
    }

    /// <summary>The resolution rays are traced at.</summary>
    public EffectResolution RayResolution { get; set; } = EffectResolution.Half;

    /// <summary>Most ray march steps, 1 to 100.</summary>
    public float RayDistance { get; set; } = 70;

    /// <summary>Pulls rays toward the mirror direction, 0 the whole GGX lobe to 1 a mirror.</summary>
    public float BrdfBias { get; set; } = 0.7f;

    /// <summary>Reuses the neighbouring four rays of each pixel.</summary>
    public bool RayReuse { get; set; } = true;

    /// <summary>Weights reused rays by BRDF over pdf.</summary>
    public bool Normalization { get; set; } = true;

    /// <summary>Tonemaps rays as they resolve so bright ones do not sparkle.</summary>
    public bool ReduceFireflies { get; set; } = true;

    /// <summary>Blurs rough reflections through a convolved pyramid of the scene.</summary>
    public bool UseMipMap { get; set; } = true;

    /// <summary>Accumulates over frames and feeds the last result back for a bounce.</summary>
    public bool UseTemporal { get; set; } = true;

    /// <summary>The neighbourhood clamp box scale for the history.</summary>
    public float TemporalScale { get; set; } = 2;

    /// <summary>How much history is kept, 0 to 1.</summary>
    public float TemporalResponse { get; set; } = 0.85f;

    /// <summary>Reprojects history by the reflection hit depth rather than the surface motion.</summary>
    public bool ReflectionVelocity { get; set; } = true;

    /// <summary>The part of the screen border reflections fade out over, 0 to 1.</summary>
    public float ScreenFadeSize { get; set; } = 0.25f;

    public override void Extract()
    {
        _settings = new Settings(Math.Clamp(RayDistance, 1, 100), Math.Clamp(BrdfBias, 0, 1), RayReuse, Normalization, ReduceFireflies, TemporalScale, Math.Clamp(TemporalResponse, 0, 1), ReflectionVelocity, Math.Clamp(ScreenFadeSize, 0, 1));
        (bool, bool, float) declared = (UseMipMap, UseTemporal, RayResolution.Scale());
        if (declared != _declared)
        {
            _declared = declared;
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        builder.DrawsRenderables();
        ViewState state = builder.GetViewState<ViewState>();
        if (_declared.Temporal)
        {
            state.Source = builder.Temporary("Source", view.Texture(Hdr));
            state.Combined = builder.History("Combined", view.Texture(Hdr), usage: TextureState.Attachment | TextureState.Sampled);
            state.ReflectionHistory = builder.History("Reflection", view.Texture(Hdr), usage: TextureState.Attachment | TextureState.Sampled);
        }
        if (_declared.MipMap)
        {
            for (int level = 0; level < 4; level++)
            {
                state.Scratch[level] = builder.Temporary("Scratch", level, view.Texture(Hdr, 1f / (1 << level)));
                state.Pyramid[level] = builder.Temporary("Pyramid", level, view.Texture(Hdr, 1f / (1 << (level + 1))));
            }
        }
        state.RayData = builder.Temporary("Rays", view.Texture(Hdr, _declared.Scale));
        state.Reflection = builder.Temporary("Reflection", view.Texture(Hdr));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        FrameCounter counter = context.GetViewState<FrameCounter>();
        counter.Index = (counter.Index + 1) % 64;
        Float2 jitter = new(Halton.Of(counter.Index, 2), Halton.Of(counter.Index, 3));
        Float2 screen = new(context.View.Width, context.View.Height);
        RenderTexture rays = context.Get(state.RayData);
        Float2 raySize = new(rays.ColorTextures[0].Width, rays.ColorTextures[0].Height);

        _properties.SetTexture(s_cameraDepthTexture, context.Depth!, PointClamp);
        _properties.SetTexture(s_cameraNormalsTexture, context.Normals!, PointClamp);
        _properties.SetTexture(s_materialTexture, context.Material!, PointClamp);
        _properties.SetTexture(s_cameraMotionVectorsTexture, context.Motion!, PointClamp);
        _properties.SetTexture(s_noise, context.Noise, LinearRepeat);
        _properties.SetFloat2(s_noiseSize, new Float2(256, 256));
        _properties.SetFloat4(s_jitterSizeAndOffset, new Float4(raySize.X / 256f, raySize.Y / 256f, jitter.X, jitter.Y));
        _properties.SetFloat2(s_resolveSize, screen);
        _properties.SetFloat2(s_screenSize, screen);
        _properties.SetFloat(s_numSteps, _settings.RayDistance);
        _properties.SetFloat(s_brdfBias, _settings.BrdfBias);
        _properties.SetFloat(s_edgeFactor, _settings.ScreenFade);
        _properties.SetFloat(s_maxMipMap, MaxMip);
        _properties.SetInt(s_rayReuse, _settings.RayReuse ? 1 : 0);
        _properties.SetInt(s_useNormalization, _settings.Normalization ? 1 : 0);
        _properties.SetInt(s_fireflies, _settings.Fireflies ? 1 : 0);
        _properties.SetInt(s_reflectionVelocity, _settings.ReflectionVelocity ? 1 : 0);

        // The reflected scene: the last result moved by motion for a bounce, else these opaques
        RenderTexture? previousCombined = _declared.Temporal ? context.History(state.Combined) : null;
        Texture source = context.Color!;
        if (previousCombined != null && context.HasPreviousFrame)
        {
            RenderTexture reprojected = context.Get(state.Source);
            _properties.SetTexture(s_mainTex, previousCombined.ColorTextures[0], LinearClamp);
            context.Fullscreen(_reproject, reprojected, _properties);
            source = reprojected.ColorTextures[0];
        }

        _properties.SetTexture(s_scenes[0], source, LinearClamp);
        Texture level = source;
        for (int i = 0; i < 4; i++)
        {
            if (_declared.MipMap)
            {
                RenderTexture scratch = context.Get(state.Scratch[i]), next = context.Get(state.Pyramid[i]);
                _properties.SetTexture(s_mainTex, level, LinearClamp);
                _properties.SetFloat2(s_blurDir, new Float2(1, 0));
                context.Fullscreen(_blur, scratch, _properties);
                _properties.SetTexture(s_mainTex, scratch.ColorTextures[0], LinearClamp);
                _properties.SetFloat2(s_blurDir, new Float2(0, 1));
                context.Fullscreen(_blur, next, _properties);
                level = next.ColorTextures[0];
            }
            _properties.SetTexture(s_scenes[i + 1], level, LinearClamp);
        }

        context.Fullscreen(_rayCast, rays, _properties);
        _properties.SetTexture(s_rayCast, rays.ColorTextures[0], PointClamp);
        RenderTexture reflection = context.Get(state.Reflection);
        context.Fullscreen(_resolve, reflection, _properties);

        Texture final = reflection.ColorTextures[0];
        if (_declared.Temporal)
        {
            RenderTexture history = context.Get(state.ReflectionHistory);
            RenderTexture? previous = context.History(state.ReflectionHistory);
            _properties.SetTexture(s_mainTex, reflection.ColorTextures[0], LinearClamp);
            bool valid = previous != null && context.HasPreviousFrame;
            _properties.SetTexture(s_previousBuffer, (previous ?? reflection).ColorTextures[0], LinearClamp);
            _properties.SetFloat(s_tScale, _settings.TemporalScale);
            _properties.SetFloat(s_tResponse, valid ? _settings.TemporalResponse : 0);
            context.Fullscreen(_temporal, history, _properties);
            final = history.ColorTextures[0];
        }

        _properties.SetTexture(s_reflectionBuffer, final, LinearClamp);
        if (!_declared.Temporal)
        {
            context.Fullscreen(_combine, _properties);
            return;
        }
        RenderTexture combined = context.Get(state.Combined);
        context.Fullscreen(_combine, combined, _properties);
        _properties.SetTexture(s_mainTex, combined.ColorTextures[0], LinearClamp);
        context.Fullscreen(_copy, _properties);
    }
}

/// <summary>
/// Motion blur: the colour sampled along each pixel's motion, at most a largest radius in pixels, weighted by how
/// close each sample's depth is so background does not bleed over foreground.
/// </summary>
public sealed class MotionBlur : PostEffect
{
    private static readonly PropertyID s_motionVectorsTex = "_MotionVectorsTex", s_cameraDepthTexture = "_CameraDepthTexture",
        s_resolution = "_Resolution", s_intensity = "_Intensity", s_samples = "_Samples", s_maxBlurRadius = "_MaxBlurRadius";

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private (float Intensity, int Samples, float MaxRadius) _settings;

    public MotionBlur(Renderer renderer) : base(renderer, RenderSlot.HdrPost, sceneReads: SceneTargets.Motion) => _program = Program("MotionBlur");

    /// <summary>Blur strength, 1 physically correct.</summary>
    public float Intensity { get; set; } = 1;

    /// <summary>Samples along the motion, 1 to 32.</summary>
    public int Samples { get; set; } = 8;

    /// <summary>The longest blur in pixels.</summary>
    public float MaxBlurRadius { get; set; } = 40;

    public override void Extract() => _settings = (Intensity, Math.Clamp(Samples, 1, 32), Math.Max(1, MaxBlurRadius));

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture(s_motionVectorsTex, context.Motion!, PointClamp);
        _properties.SetTexture(s_cameraDepthTexture, context.Depth!, PointClamp);
        _properties.SetFloat2(s_resolution, new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat(s_intensity, _settings.Intensity);
        _properties.SetInt(s_samples, _settings.Samples);
        _properties.SetFloat(s_maxBlurRadius, _settings.MaxRadius);
        context.Fullscreen(_program, _properties);
    }
}

public enum DepthOfFieldResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4,
    Eighth = 8,
}

public enum DepthOfFieldQuality
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Bokeh depth of field: a signed circle of confusion splits the image into a far field behind focus and a near
/// field in front, each gathered with a disk, so sharp objects do not halo into a blurred background and a blurred
/// foreground spreads over what is behind it. The focus distance eases toward its target on the GPU, from the first
/// layer's center when automatic, so a layered view's layers share it.
/// </summary>
public sealed class DepthOfField : PostEffect
{
    private static readonly PropertyID s_cameraDepthTexture = "_CameraDepthTexture", s_prevFocusTex = "_PrevFocusTex",
        s_useAutoFocus = "_UseAutoFocus", s_manualFocusPoint = "_ManualFocusPoint", s_focusBlend = "_FocusBlend",
        s_focusHistoryValid = "_FocusHistoryValid", s_focusTex = "_FocusTex", s_focusStrength = "_FocusStrength", s_maxBlurRadius = "_MaxBlurRadius",
        s_resolution = "_Resolution", s_downscale = "_Downscale", s_maxCoc = "_MaxCoC", s_rings = "_Rings", s_cocTex = "_CoCTex",
        s_mainTex = "_MainTex", s_farTex = "_FarTex", s_nearTex = "_NearTex";

    private readonly FullscreenProgram _coc, _prefilter, _bokeh, _postfilter, _combine, _focus;
    private readonly PropertySet _properties = new();
    private Settings _settings;
    private int _divisor = 2;

    private readonly record struct Settings(bool AutoFocus, float ManualFocus, float Strength, float MaxBlur, int Rings, float Speed);

    private sealed class ViewState
    {
        public TextureHandle CoC, Prefiltered, Bokeh, Smoothed, Focus;
    }

    public DepthOfField(Renderer renderer) : base(renderer, RenderSlot.HdrPost)
    {
        BlendStateDescription two = new(default, BlendAttachmentDescription.OverrideBlend, BlendAttachmentDescription.OverrideBlend);
        _coc = Program("DepthOfField", "#define DOF_COC 1\n", source: false);
        _prefilter = Program("DepthOfField", "#define DOF_PREFILTER 1\n");
        _bokeh = Program("DepthOfField", "#define DOF_BOKEH 1\n", source: false, blend: two);
        _postfilter = Program("DepthOfField", "#define DOF_POSTFILTER 1\n", source: false, blend: two);
        _combine = Program("DepthOfField");
        _focus = Program("DepthOfField", "#define DOF_FOCUS 1\n", source: false);
        Order = 10;
    }

    public bool UseAutoFocus { get; set; } = true;

    /// <summary>The focus distance from the camera when not automatic.</summary>
    public float ManualFocusPoint { get; set; } = 10;

    /// <summary>The blur of something infinitely far, in percent of the view's height.</summary>
    public float FocusStrength { get; set; } = 2;

    /// <summary>The largest blur, in percent of the view's height.</summary>
    public float MaxBlurRadius { get; set; } = 2;

    public DepthOfFieldResolution Resolution { get; set; } = DepthOfFieldResolution.Half;
    public DepthOfFieldQuality Quality { get; set; } = DepthOfFieldQuality.Medium;

    /// <summary>How fast the focus follows its target, per second, 0 or less to jump to it.</summary>
    public float FocusSpeed { get; set; } = 4;

    public override void Extract()
    {
        int rings = Quality switch { DepthOfFieldQuality.Low => 2, DepthOfFieldQuality.Medium => 3, _ => 4 };
        _settings = new Settings(UseAutoFocus, MathF.Max(ManualFocusPoint, 0.01f), Math.Max(0, FocusStrength), Math.Max(0, MaxBlurRadius), rings, FocusSpeed);
        int divisor = Math.Max(1, (int)Resolution);
        if (divisor != _divisor)
        {
            _divisor = divisor;
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        float blur = 1f / _divisor;
        state.CoC = builder.Temporary("CoC", view.Texture(PixelFormat.R16_Float));
        state.Prefiltered = builder.Temporary("Prefiltered", view.Texture(Hdr, blur));
        state.Bokeh = builder.Temporary("Bokeh", view.Texture(Hdr, blur) with { ColorFormats = [Hdr, Hdr] });
        state.Smoothed = builder.Temporary("Smoothed", view.Texture(Hdr, blur) with { ColorFormats = [Hdr, Hdr] });
        state.Focus = builder.History("Focus", FeatureView.Shared(1, 1, PixelFormat.R32_Float), usage: TextureState.Attachment | TextureState.Sampled);
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        float width = context.View.Width, height = context.View.Height;
        RenderTexture prefiltered = context.Get(state.Prefiltered), bokeh = context.Get(state.Bokeh), smoothed = context.Get(state.Smoothed);
        float downscale = height / prefiltered.ColorTextures[0].Height;

        // The focus distance, eased from last frame's unless the view cut
        RenderTexture focus = context.Get(state.Focus);
        RenderTexture? previous = context.History(state.Focus);
        bool valid = previous != null && context.HasPreviousFrame;
        _properties.SetTexture(s_cameraDepthTexture, context.Depth!, PointClamp);
        _properties.SetTexture(s_prevFocusTex, previous?.ColorTextures[0] ?? context.Noise, PointClamp);
        _properties.SetFloat(s_useAutoFocus, _settings.AutoFocus ? 1 : 0);
        _properties.SetFloat(s_manualFocusPoint, _settings.ManualFocus);
        _properties.SetFloat(s_focusBlend, _settings.Speed > 0 ? 1 - MathF.Exp(-_settings.Speed * context.Time.DeltaTime) : 1);
        _properties.SetFloat(s_focusHistoryValid, valid ? 1 : 0);
        context.Fullscreen(_focus, focus, _properties, shared: true);

        _properties.SetTexture(s_focusTex, focus.ColorTextures[0], PointClamp);
        _properties.SetFloat(s_focusStrength, _settings.Strength);
        _properties.SetFloat(s_maxBlurRadius, _settings.MaxBlur);
        _properties.SetFloat2(s_resolution, new Float2(width, height));
        _properties.SetFloat(s_downscale, downscale);
        _properties.SetFloat(s_maxCoc, _settings.MaxBlur * 0.01f * height / downscale);
        _properties.SetInt(s_rings, _settings.Rings);
        RenderTexture coc = context.Get(state.CoC);
        context.Fullscreen(_coc, coc, _properties);

        _properties.SetTexture(s_cocTex, coc.ColorTextures[0], PointClamp);
        context.Fullscreen(_prefilter, prefiltered, _properties);
        _properties.SetTexture(s_mainTex, prefiltered.ColorTextures[0], LinearClamp);
        context.Fullscreen(_bokeh, bokeh, _properties);
        _properties.SetTexture(s_farTex, bokeh.ColorTextures[0], LinearClamp);
        _properties.SetTexture(s_nearTex, bokeh.ColorTextures[1], LinearClamp);
        context.Fullscreen(_postfilter, smoothed, _properties);

        _properties.SetTexture(s_farTex, smoothed.ColorTextures[0], LinearClamp);
        _properties.SetTexture(s_nearTex, smoothed.ColorTextures[1], LinearClamp);
        _properties.SetTexture(s_cocTex, coc.ColorTextures[0], LinearClamp);
        context.Fullscreen(_combine, _properties);
    }
}

/// <summary>
/// God rays as an HDR effect: bright sky along the line to the sun on screen, decaying each step, added as warm
/// shafts. The sun is the world's, so a world without a directional light draws none.
/// </summary>
public sealed class GodRays : PostEffect
{
    private static readonly PropertyID s_cameraDepthTexture = "_CameraDepthTexture", s_sunDirection = "_SunDirection",
        s_godRayIntensity = "_GodRayIntensity", s_godRayDecay = "_GodRayDecay", s_godRayDensity = "_GodRayDensity", s_godRayWeight = "_GodRayWeight",
        s_godRaySamples = "_GodRaySamples", s_godRayThreshold = "_GodRayThreshold";

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private (float Intensity, float Decay, float Density, float Weight, int Samples, float Threshold) _settings;

    public GodRays(Renderer renderer) : base(renderer, RenderSlot.HdrPost)
    {
        _program = Program("GodRays");
        Order = 40;
    }

    public float Intensity { get; set; } = 0.5f;

    /// <summary>How fast the rays fade away from the sun.</summary>
    public float Decay { get; set; } = 0.96f;

    /// <summary>The spacing of samples along a ray.</summary>
    public float Density { get; set; } = 0.5f;

    /// <summary>Each sample's brightness.</summary>
    public float Weight { get; set; } = 0.6f;

    /// <summary>Samples along a ray, 8 to 128.</summary>
    public int Samples { get; set; } = 64;

    /// <summary>The luminance below which sky adds no rays.</summary>
    public float Threshold { get; set; } = 0.8f;

    public override void Extract() => _settings = (Intensity, Decay, Density, Weight, Math.Clamp(Samples, 8, 128), Threshold);

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture(s_cameraDepthTexture, context.Depth!, PointClamp);
        _properties.SetFloat3(s_sunDirection, context.SunDirection ?? new Float3(0, -1, 0));
        _properties.SetFloat(s_godRayIntensity, context.SunDirection == null ? 0 : _settings.Intensity);
        _properties.SetFloat(s_godRayDecay, _settings.Decay);
        _properties.SetFloat(s_godRayDensity, _settings.Density);
        _properties.SetFloat(s_godRayWeight, _settings.Weight);
        _properties.SetInt(s_godRaySamples, _settings.Samples);
        _properties.SetFloat(s_godRayThreshold, _settings.Threshold);
        context.Fullscreen(_program, _properties);
    }
}

/// <summary>
/// One of the cinematic effects, a single pass over display referred colour in the DisplayPost slot. They run in
/// this order: pixelation, chromatic aberration, sharpening, edges, grading and the LUT, then anti aliasing, then film
/// grain and vignette so grain is not smoothed away.
/// </summary>
public abstract class DisplayEffect : PostEffect
{
    private static readonly PropertyID s_resolution = "_Resolution";

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();

    private protected DisplayEffect(Renderer renderer, string define, int order) : base(renderer, RenderSlot.DisplayPost)
    {
        _program = Program("Cinematic", $"#define CINEMATIC_{define} 1\n");
        Order = order;
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

    public override void Render(FeatureContext context)
    {
        _properties.SetFloat2(s_resolution, new Float2(context.View.Width, context.View.Height));
        SetProperties(context, _properties);
        context.Fullscreen(_program, _properties);
    }

    /// <summary>The effect's settings as copied at Extract. Render thread.</summary>
    private protected abstract void SetProperties(FeatureContext context, PropertySet properties);
}

/// <summary>Pixelation: the image in blocks of a size in pixels.</summary>
public sealed class Pixelation(Renderer renderer) : DisplayEffect(renderer, "PIXELATION", 0)
{
    private static readonly PropertyID s_pixelSize = "_PixelSize";

    private float _size;

    public float PixelSize { get; set; } = 4;

    public override void Extract() => _size = MathF.Max(1, PixelSize);

    private protected override void SetProperties(FeatureContext context, PropertySet properties) => properties.SetFloat(s_pixelSize, _size);
}

/// <summary>Chromatic aberration: red pushed outward and blue inward by a barrel distortion.</summary>
public sealed class ChromaticAberration(Renderer renderer) : DisplayEffect(renderer, "CHROMATIC_ABERRATION", 1)
{
    private static readonly PropertyID s_chromaticIntensity = "_ChromaticIntensity", s_chromaticDistortion = "_ChromaticDistortion";

    private (float Intensity, float Distortion) _settings;

    /// <summary>The largest channel offset in pixels.</summary>
    public float Intensity { get; set; } = 3;

    /// <summary>The barrel distortion of the offsets, 0 linear.</summary>
    public float Distortion { get; set; } = 0.5f;

    public override void Extract() => _settings = (Intensity, Distortion);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat(s_chromaticIntensity, _settings.Intensity);
        properties.SetFloat(s_chromaticDistortion, _settings.Distortion);
    }
}

/// <summary>Contrast adaptive sharpening.</summary>
public sealed class Sharpen(Renderer renderer) : DisplayEffect(renderer, "SHARPEN", 2)
{
    private static readonly PropertyID s_sharpenAmount = "_SharpenAmount", s_sharpenRadius = "_SharpenRadius";

    private (float Amount, float Radius) _settings;

    /// <summary>Sharpening, 0 least to 1 most.</summary>
    public float Amount { get; set; } = 0.5f;

    /// <summary>The sample radius in pixels, 1 or more.</summary>
    public float Radius { get; set; } = 1;

    public override void Extract() => _settings = (Amount, MathF.Max(1, Radius));

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat(s_sharpenAmount, _settings.Amount);
        properties.SetFloat(s_sharpenRadius, _settings.Radius);
    }
}

/// <summary>Sobel edge detection: edges drawn in a colour over the faded image.</summary>
public sealed class EdgeDetection(Renderer renderer) : DisplayEffect(renderer, "EDGE_DETECTION", 3)
{
    private static readonly PropertyID s_edgeIntensity = "_EdgeIntensity", s_edgeColor = "_EdgeColor", s_edgeBackgroundFade = "_EdgeBackgroundFade";

    private (float Intensity, Float4 Color, float Fade) _settings;

    public float Intensity { get; set; } = 1;
    public Float4 Color { get; set; } = new(0, 0, 0, 1);

    /// <summary>How much of the image stays under the edges, 0 edges only to 1 all of it.</summary>
    public float BackgroundFade { get; set; } = 1;

    public override void Extract() => _settings = (Intensity, Color, BackgroundFade);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat(s_edgeIntensity, _settings.Intensity);
        properties.SetFloat4(s_edgeColor, _settings.Color);
        properties.SetFloat(s_edgeBackgroundFade, _settings.Fade);
    }
}

/// <summary>Colour grading: exposure, contrast around mid grey, temperature, saturation, then lift, gamma and gain.</summary>
public sealed class ColorGrading(Renderer renderer) : DisplayEffect(renderer, "COLOR_GRADING", 4)
{
    private static readonly PropertyID s_postExposure = "_PostExposure", s_contrast = "_Contrast", s_saturation = "_Saturation",
        s_temperature = "_Temperature", s_lift = "_Lift", s_gamma = "_Gamma", s_gain = "_Gain";

    private (float Exposure, float Contrast, float Saturation, float Temperature, Float4 Lift, Float4 Gamma, Float4 Gain) _settings;

    /// <summary>Exposure after the tonemapper, in stops.</summary>
    public float PostExposure { get; set; }

    /// <summary>Contrast around mid grey, -1 to 1.</summary>
    public float Contrast { get; set; }

    /// <summary>Saturation, -1 grey to 1.</summary>
    public float Saturation { get; set; }

    /// <summary>A colour temperature shift, -1 cool to 1 warm.</summary>
    public float Temperature { get; set; }

    /// <summary>Added to shadows, 0 for none.</summary>
    public Float4 Lift { get; set; }

    /// <summary>Brightens midtones when positive, 0 for none.</summary>
    public Float4 Gamma { get; set; }

    /// <summary>Multiplies highlights by one plus it, 0 for none.</summary>
    public Float4 Gain { get; set; }

    public override void Extract() => _settings = (PostExposure, Contrast, Saturation, Temperature, Lift, Gamma, Gain);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat(s_postExposure, _settings.Exposure);
        properties.SetFloat(s_contrast, _settings.Contrast);
        properties.SetFloat(s_saturation, _settings.Saturation);
        properties.SetFloat(s_temperature, _settings.Temperature);
        properties.SetFloat4(s_lift, _settings.Lift);
        properties.SetFloat4(s_gamma, _settings.Gamma);
        properties.SetFloat4(s_gain, _settings.Gain);
    }
}

/// <summary>LUT grading from a strip texture, its slices side by side, 256 by 16 for a 16 cube. Without a texture it changes nothing.</summary>
public sealed class ColorLookup(Renderer renderer) : DisplayEffect(renderer, "LUT", 5)
{
    private static readonly PropertyID s_lutTex = "_LUTTex", s_lutContribution = "_LUTContribution", s_lutSize = "_LUTSize";

    private (Texture? Texture, float Contribution) _settings;

    public Texture? Texture { get; set; }

    /// <summary>How much of the LUT applies, 0 to 1.</summary>
    public float Contribution { get; set; } = 1;

    public override void Extract() => _settings = (Texture, Contribution);

    public override bool IsActive(in FeatureView view) => _settings.Texture is { IsDisposed: false };

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetTexture(s_lutTex, _settings.Texture!, LinearClamp);
        properties.SetFloat(s_lutContribution, _settings.Contribution);
        properties.SetFloat(s_lutSize, _settings.Texture!.Height);
    }
}

/// <summary>Film grain, changing every frame, optionally stronger in highlights.</summary>
public sealed class FilmGrain(Renderer renderer) : DisplayEffect(renderer, "FILM_GRAIN", 20)
{
    private static readonly PropertyID s_time = "_Time", s_grainIntensity = "_GrainIntensity", s_grainResponse = "_GrainResponse";

    private (float Intensity, float Response) _settings;

    public float Intensity { get; set; } = 0.15f;

    /// <summary>How far grain follows luminance, 0 even to 1 only in highlights.</summary>
    public float Response { get; set; } = 0.5f;

    public override void Extract() => _settings = (Intensity, Response);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat(s_time, (float)context.Time.Time);
        properties.SetFloat(s_grainIntensity, _settings.Intensity);
        properties.SetFloat(s_grainResponse, _settings.Response);
    }
}

/// <summary>Vignette, darkening toward the edges.</summary>
public sealed class Vignette(Renderer renderer) : DisplayEffect(renderer, "VIGNETTE", 21)
{
    private static readonly PropertyID s_vignetteIntensity = "_VignetteIntensity", s_vignetteSmoothness = "_VignetteSmoothness",
        s_vignetteRoundness = "_VignetteRoundness";

    private (float Intensity, float Smoothness, float Roundness) _settings;

    public float Intensity { get; set; } = 0.4f;
    public float Smoothness { get; set; } = 0.2f;

    /// <summary>0 follows the view's shape, 1 a circle.</summary>
    public float Roundness { get; set; } = 1;

    public override void Extract() => _settings = (Intensity, Smoothness, Roundness);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat(s_vignetteIntensity, _settings.Intensity);
        properties.SetFloat(s_vignetteSmoothness, _settings.Smoothness);
        properties.SetFloat(s_vignetteRoundness, _settings.Roundness);
    }
}

/// <summary>FXAA 3.11 on display referred colour, in the AntiAliasing group with TAA and SMAA, before film grain and vignette.</summary>
public sealed class Fxaa : PostEffect
{
    private static readonly PropertyID s_resolution = "_Resolution", s_edgeThresholdMax = "_EdgeThresholdMax",
        s_edgeThresholdMin = "_EdgeThresholdMin", s_subpixelQuality = "_SubpixelQuality";

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private (float Max, float Min, float Subpixel) _settings;

    public Fxaa(Renderer renderer) : base(renderer, RenderSlot.DisplayPost, FeatureGroup.AntiAliasing)
    {
        _program = Program("Fxaa");
        Order = 10;
    }

    /// <summary>The contrast an edge needs relative to its brightest pixel, 0.063 to 0.333, lower smoothing more.</summary>
    public float EdgeThresholdMax { get; set; } = 0.0625f;

    /// <summary>The contrast below which dark edges are left, 0.0312 to 0.0833.</summary>
    public float EdgeThresholdMin { get; set; } = 0.0312f;

    /// <summary>Subpixel smoothing, 0 to 1.</summary>
    public float SubpixelQuality { get; set; } = 0.75f;

    public override void Extract() => _settings = (EdgeThresholdMax, EdgeThresholdMin, SubpixelQuality);

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

    public override void Render(FeatureContext context)
    {
        _properties.SetFloat2(s_resolution, new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat(s_edgeThresholdMax, _settings.Max);
        _properties.SetFloat(s_edgeThresholdMin, _settings.Min);
        _properties.SetFloat(s_subpixelQuality, _settings.Subpixel);
        context.Fullscreen(_program, _properties);
    }
}

/// <summary>
/// SMAA 1x with luma edges on display referred colour: edges, blend weights from the reference's area and search
/// tables, then neighbourhood blending. In the AntiAliasing group with TAA and FXAA.
/// </summary>
public sealed class Smaa : PostEffect
{
    private static readonly PropertyID s_resolution = "_Resolution", s_edgeThreshold = "_EdgeThreshold", s_mainTex = "_MainTex",
        s_areaTex = "_AreaTex", s_searchTex = "_SearchTex", s_blendTex = "_BlendTex";

    private readonly FullscreenProgram _edges, _weights, _blend;
    private readonly PropertySet _properties = new();
    private float _threshold;

    private sealed class ViewState
    {
        public TextureHandle Edges, Weights;
    }

    public Smaa(Renderer renderer) : base(renderer, RenderSlot.DisplayPost, FeatureGroup.AntiAliasing)
    {
        _edges = Program("Smaa", "#define SMAA_EDGES 1\n", source: false);
        _weights = Program("Smaa", "#define SMAA_WEIGHTS 1\n", source: false);
        _blend = Program("Smaa");
        Order = 10;
    }

    /// <summary>Edge sensitivity, about 0.05 to 0.2, lower finding more edges.</summary>
    public float EdgeThreshold { get; set; } = 0.1f;

    public override void Extract() => _threshold = EdgeThreshold;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        state.Edges = builder.Temporary("Edges", view.Texture(PixelFormat.R8_G8_B8_A8_UNorm));
        state.Weights = builder.Temporary("Weights", view.Texture(PixelFormat.R8_G8_B8_A8_UNorm));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        RenderTexture edges = context.Get(state.Edges), weights = context.Get(state.Weights);
        (TextureView area, TextureView search) = context.SmaaTables;
        _properties.SetFloat2(s_resolution, new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat(s_edgeThreshold, _threshold);
        _properties.SetTexture(s_mainTex, context.Color!, LinearClamp);
        context.Fullscreen(_edges, edges, _properties, clear: true);
        _properties.SetTexture(s_mainTex, edges.ColorTextures[0], LinearClamp);
        _properties.SetTexture(s_areaTex, area, LinearClamp);
        _properties.SetTexture(s_searchTex, search, LinearClamp);
        context.Fullscreen(_weights, weights, _properties, clear: true);
        _properties.SetTexture(s_blendTex, weights.ColorTextures[0], LinearClamp);
        context.Fullscreen(_blend, _properties);
    }
}
