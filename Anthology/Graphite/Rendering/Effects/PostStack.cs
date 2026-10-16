using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The fullscreen programs and samplers of the stock effects, built from the library's Graphite/Internal files and
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
        LinearClamp.Dispose();
        PointClamp.Dispose();
        LinearRepeat.Dispose();
    }

    /// <summary>How many programs were built, for tests.</summary>
    internal int ProgramCount => _programs.Count;

    public EffectResources(Renderer renderer)
    {
        _renderer = renderer;
        SamplerDescription clamp = SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        };
        ResourceFactory factory = renderer.Device.ResourceFactory;
        LinearClamp = factory.CreateSampler(clamp);
        PointClamp = factory.CreateSampler(clamp with { MinFilter = FilterMode.Point, MagFilter = FilterMode.Point, MipFilter = FilterMode.Point });
        LinearRepeat = factory.CreateSampler(SamplerDescription.Linear);
    }

    public Sampler LinearClamp { get; }
    public Sampler PointClamp { get; }
    public Sampler LinearRepeat { get; }

    /// <summary>The program of a library file under a set of defines, each line ending in a newline, made the first time. Host thread.</summary>
    public FullscreenProgram Program(string file, string defines = "", bool source = true, BlendStateDescription? blend = null)
    {
        string key = file + "\n" + defines + (source ? "\n1" : "\n0");
        lock (_programs)
        {
            if (!_programs.TryGetValue(key, out FullscreenProgram? program))
            {
                program = _renderer.CreateFullscreenProgram(InternalShaders.PostFragment(file), $"Graphite/{file}", source, blend, variant: defines);
                _programs[key] = program;
            }
            return program;
        }
    }
}

/// <summary>
/// A stock effect: a port of one of Prowl's image effects, its programs built from the library's Graphite/Internal files
/// and its settings copied at Extract. Effects of one renderer share their programs, which the renderer owns.
/// </summary>
public abstract class PostEffect(Renderer renderer) : RenderFeature
{
    private protected EffectResources Resources { get; } = renderer.Effects;

    protected Sampler LinearClamp => Resources.LinearClamp;
    protected Sampler PointClamp => Resources.PointClamp;
    protected Sampler LinearRepeat => Resources.LinearRepeat;

    /// <summary>The program of a library file under a set of defines, each line ending in a newline, made the first time. Host thread.</summary>
    protected FullscreenProgram Program(string file, string defines = "", bool source = true, BlendStateDescription? blend = null)
        => Resources.Program(file, defines, source, blend);

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
    Melon,
    Aces,
    AcesSimple,
    AgX,
    ReinhardSimple,
    ReinhardLuma,
    ReinhardWhitePreserving,
    RomBinDaHouse,
    Uncharted2,
}

/// <summary>
/// Prowl's tonemapper, the Tonemap slot's exclusive feature: an operator into 0 to 1, display gamma, then contrast and
/// saturation in display space. Later slots read display encoded colour, which the present decodes for sRGB targets.
/// </summary>
public sealed class Tonemapper(Renderer renderer) : PostEffect(renderer)
{
    private FullscreenProgram _program = null!;
    private (float Contrast, float Saturation) _settings;
    private readonly PropertySet _properties = new();

    public TonemapOperator Operator { get; set; } = TonemapOperator.AgX;
    public float Contrast { get; set; } = 1.1f;
    public float Saturation { get; set; } = 1.1f;

    public override RenderSlot Slot => RenderSlot.Tonemap;
    public override string? ExclusiveGroup => "Tonemap";

    public override void Extract()
    {
        string define = Operator switch
        {
            TonemapOperator.Melon => "TONEMAP_MELON",
            TonemapOperator.Aces => "TONEMAP_ACES",
            TonemapOperator.AcesSimple => "TONEMAP_ACES_SIMPLE",
            TonemapOperator.AgX => "TONEMAP_AGX",
            TonemapOperator.ReinhardSimple => "TONEMAP_REINHARD_SIMPLE",
            TonemapOperator.ReinhardLuma => "TONEMAP_REINHARD_LUMA",
            TonemapOperator.ReinhardWhitePreserving => "TONEMAP_REINHARD_WHITE",
            TonemapOperator.RomBinDaHouse => "TONEMAP_ROMBINDAHOUSE",
            TonemapOperator.Uncharted2 => "TONEMAP_UNCHARTED2",
            _ => "TONEMAP_CLAMP",
        };
        _program = Program("Tonemap", $"#define {define} 1\n");
        _settings = (Contrast, Saturation);
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.WriteColor();
        builder.EncodesForDisplay();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetFloat("_Contrast", _settings.Contrast);
        _properties.SetFloat("_Saturation", _settings.Saturation);
        context.Fullscreen(_program, _properties);
    }
}

/// <summary>
/// Prowl's auto exposure: the colour's log luminance measured at half resolution and box downsampled, adapted over time
/// toward its geometric mean, faster when brightening, and applied as a multiply before bloom. A layered view measures its
/// first layer and its layers share one exposure.
/// </summary>
public sealed class AutoExposure : PostEffect
{
    private readonly FullscreenProgram _extract, _extractLayered, _downsample, _adapt, _apply;
    private readonly PropertySet _properties = new();
    private (float Compensation, float Up, float Down, float Min, float Max) _settings;

    private sealed class ViewState
    {
        public readonly List<TextureHandle> Levels = new();
        public TextureHandle Adapted;
    }

    public AutoExposure(Renderer renderer) : base(renderer)
    {
        _extract = Program("AutoExposure", "#define AE_EXTRACT 1\n", source: false);
        _extractLayered = Program("AutoExposure", "#define AE_EXTRACT 1\n#define AE_LAYERED_SOURCE 1\n", source: false);
        _downsample = Program("AutoExposure", "#define AE_DOWNSAMPLE 1\n", source: false);
        _adapt = Program("AutoExposure", "#define AE_ADAPT 1\n", source: false);
        _apply = Program("AutoExposure");
        Order = 20;
    }

    /// <summary>Exposure compensation in stops, 0 for none.</summary>
    public float ExposureCompensation { get; set; }

    /// <summary>How fast exposure follows the scene getting brighter, per second.</summary>
    public float AdaptSpeedUp { get; set; } = 3;

    /// <summary>How fast exposure follows the scene getting darker, per second.</summary>
    public float AdaptSpeedDown { get; set; } = 1.5f;

    public float MinExposure { get; set; } = 0.1f;
    public float MaxExposure { get; set; } = 10;

    public override RenderSlot Slot => RenderSlot.HdrPost;

    public override void Extract()
        => _settings = (ExposureCompensation, Math.Max(0.01f, AdaptSpeedUp), Math.Max(0.01f, AdaptSpeedDown), MinExposure, MaxExposure);

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        state.Levels.Clear();
        int width = (int)view.Width / 2, height = (int)view.Height / 2;
        state.Levels.Add(builder.Temporary("Luminance0", FeatureView.Shared(Math.Max(1, width), Math.Max(1, height), PixelFormat.R16_Float)));
        while (width > 2 || height > 2)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            state.Levels.Add(builder.Temporary("Luminance" + state.Levels.Count, FeatureView.Shared(width, height, PixelFormat.R16_Float)));
        }
        state.Adapted = builder.History("Adapted", FeatureView.Shared(1, 1, PixelFormat.R16_Float), usage: TextureState.Attachment | TextureState.Sampled);
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        _properties.SetTexture("_MainTex", context.Color!, LinearClamp);
        context.Fullscreen(context.View.LayerCount > 1 ? _extractLayered : _extract, context.Get(state.Levels[0]), _properties, shared: true);
        for (int i = 1; i < state.Levels.Count; i++)
        {
            _properties.SetTexture("_MainTex", context.Get(state.Levels[i - 1]).ColorTextures[0], LinearClamp);
            context.Fullscreen(_downsample, context.Get(state.Levels[i]), _properties, shared: true);
        }

        RenderTexture adapted = context.Get(state.Adapted);
        RenderTexture? previous = context.History(state.Adapted);
        _properties.SetTexture("_MainTex", context.Get(state.Levels[^1]).ColorTextures[0], LinearClamp);
        _properties.SetTexture("_AdaptedTex", (previous ?? context.Get(state.Levels[^1])).ColorTextures[0], LinearClamp);
        _properties.SetFloat("_AdaptSpeedUp", _settings.Up);
        _properties.SetFloat("_AdaptSpeedDown", _settings.Down);
        _properties.SetFloat("_HistoryValid", previous != null ? 1 : 0);
        _properties.SetFloat("_DeltaTime", context.Time.DeltaTime);
        context.Fullscreen(_adapt, adapted, _properties, shared: true);

        _properties.SetTexture("_AdaptedTex", adapted.ColorTextures[0], LinearClamp);
        _properties.SetFloat("_ExposureComp", _settings.Compensation);
        _properties.SetFloat("_MinExposure", _settings.Min);
        _properties.SetFloat("_MaxExposure", _settings.Max);
        context.Fullscreen(_apply, _properties);
    }
}

/// <summary>
/// Prowl's dual filter bloom: bright colour past a soft knee threshold at half resolution, a chain of downsamples,
/// upsamples mixing each level with the blurred one below by scatter, added back to the colour.
/// </summary>
public sealed class Bloom : PostEffect
{
    private readonly FullscreenProgram _prefilter, _downsample, _upsample, _composite;
    private readonly PropertySet _properties = new();
    private (float Threshold, float Knee, float Clamp, bool AntiFlicker, float Scatter, float Intensity) _settings;
    private int _iterations = -1;

    private sealed class ViewState
    {
        public readonly List<TextureHandle> Down = new();
        public readonly List<TextureHandle> Up = new();
    }

    public Bloom(Renderer renderer) : base(renderer)
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

    public override RenderSlot Slot => RenderSlot.HdrPost;

    public override void Extract()
    {
        _settings = (Math.Max(0, Threshold), Math.Clamp(SoftKnee, 0, 1), Math.Max(0, Clamp), AntiFlicker, Math.Clamp(Scatter, 0, 1), Intensity);
        int iterations = Math.Clamp(Iterations, 0, 16);
        if (iterations != _iterations)
        {
            _iterations = iterations;
            Invalidate();
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        state.Down.Clear();
        state.Up.Clear();
        List<(int Width, int Height)> sizes = new();
        int width = Math.Max(1, (int)view.Width / 2), height = Math.Max(1, (int)view.Height / 2);
        sizes.Add((width, height));
        for (int i = 0; i < _iterations; i++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            sizes.Add((width, height));
        }
        for (int i = 0; i < sizes.Count; i++)
            state.Down.Add(builder.Temporary("Down" + i, view.Texture(sizes[i].Width, sizes[i].Height, Hdr)));
        for (int i = sizes.Count - 2; i >= 0; i--)
            state.Up.Add(builder.Temporary("Up" + i, view.Texture(sizes[i].Width, sizes[i].Height, Hdr)));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        _properties.SetFloat("_Threshold", _settings.Threshold);
        _properties.SetFloat("_SoftKnee", _settings.Knee);
        _properties.SetFloat("_Clamp", _settings.Clamp);
        _properties.SetFloat("_AntiFlicker", _settings.AntiFlicker ? 1 : 0);
        context.Fullscreen(_prefilter, context.Get(state.Down[0]), _properties);
        for (int i = 1; i < state.Down.Count; i++)
        {
            _properties.SetTexture("_MainTex", context.Get(state.Down[i - 1]).ColorTextures[0], LinearClamp);
            context.Fullscreen(_downsample, context.Get(state.Down[i]), _properties);
        }

        Texture lower = context.Get(state.Down[^1]).ColorTextures[0];
        _properties.SetFloat("_Scatter", _settings.Scatter);
        for (int i = 0; i < state.Up.Count; i++)
        {
            RenderTexture up = context.Get(state.Up[i]);
            _properties.SetTexture("_MainTex", lower, LinearClamp);
            _properties.SetTexture("_HighTex", context.Get(state.Down[state.Down.Count - 2 - i]).ColorTextures[0], LinearClamp);
            context.Fullscreen(_upsample, up, _properties);
            lower = up.ColorTextures[0];
        }

        _properties.SetTexture("_BloomTex", lower, LinearClamp);
        _properties.SetFloat("_Intensity", _settings.Intensity);
        context.Fullscreen(_composite, _properties);
    }
}

/// <summary>
/// Prowl's GTAO as the view's screen lighting: visibility from a horizon search over slices of the depth, at a part of
/// the view's resolution, with blue noise scrolled per frame, accumulated over frames and blurred by depth. It occludes
/// indirect light only, read by every renderable drawn after. Normals are rebuilt from depth and history follows depth
/// and the camera, as the prepass leaves neither normals nor motion.
/// </summary>
public sealed class Gtao : ScreenLightingFeature
{
    private readonly EffectResources _resources;
    private readonly FullscreenProgram _depth, _ao, _temporal, _blur, _copy;
    private readonly PropertySet _properties = new();
    private Settings _settings;
    private (bool Temporal, bool Blur) _declared = (true, true);

    private readonly record struct Settings(int Slices, int DirectionSamples, float Radius, float Intensity, float BlurRadius, bool Temporal, float TemporalResponse);

    private sealed class ViewState
    {
        public TextureHandle Depth, Ao, History, Blur, Visibility;
        public int FrameIndex;
    }

    public Gtao(Renderer renderer)
    {
        _resources = renderer.Effects;
        _depth = _resources.Program("Gtao", "#define GTAO_DEPTH 1\n", source: false);
        _ao = _resources.Program("Gtao", "#define GTAO_AO 1\n", source: false);
        _temporal = _resources.Program("Gtao", "#define GTAO_TEMPORAL 1\n", source: false);
        _blur = _resources.Program("Gtao", "#define GTAO_BLUR 1\n", source: false);
        _copy = _resources.Program("Gtao", "", source: false);
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
            Invalidate();
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        ViewState state = builder.GetViewState<ViewState>();
        state.Visibility = builder.WriteScreenLighting(false).Visibility;
        int width = Math.Max(1, (int)(view.Width * ScaleSnapshot)), height = Math.Max(1, (int)(view.Height * ScaleSnapshot));
        state.Depth = builder.Temporary("Depth", view.Texture(width, height, PixelFormat.R32_Float));
        state.Ao = builder.Temporary("Ao", view.Texture(width, height, PixelFormat.R8_UNorm));
        if (_declared.Temporal)
            state.History = builder.History("History", view.Texture(width, height, PixelFormat.R8_UNorm), usage: TextureState.Attachment | TextureState.Sampled);
        if (_declared.Blur)
            state.Blur = builder.Temporary("Blur", view.Texture(width, height, PixelFormat.R8_UNorm));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        state.FrameIndex = (state.FrameIndex + 1) % 64;
        RenderTexture depth = context.Get(state.Depth), ao = context.Get(state.Ao);

        _properties.SetTexture("_CameraDepthTexture", context.Depth!, _resources.PointClamp);
        context.Fullscreen(_depth, depth, _properties);

        Float2 size = new(depth.ColorTextures[0].Width, depth.ColorTextures[0].Height);
        _properties.SetTexture("_CameraDepthTexture", depth.ColorTextures[0], _resources.PointClamp);
        _properties.SetTexture("_Noise", context.Noise, _resources.LinearRepeat);
        _properties.SetInt("_Slices", _settings.Slices);
        _properties.SetInt("_DirectionSamples", _settings.DirectionSamples);
        _properties.SetFloat("_Radius", _settings.Radius);
        _properties.SetFloat("_Intensity", _settings.Intensity);
        _properties.SetFloat2("_NoiseScale", size / 256f);
        _properties.SetFloat2("_JitterOffset", new Float2(Halton.Of(state.FrameIndex, 2), Halton.Of(state.FrameIndex, 3)));
        context.Fullscreen(_ao, ao, _properties);

        RenderTexture result = ao;
        if (_declared.Temporal)
        {
            RenderTexture history = context.Get(state.History);
            RenderTexture? previous = context.History(state.History);
            _properties.SetTexture("_MainTex", ao.ColorTextures[0], _resources.LinearClamp);
            if (previous != null && context.HasPreviousFrame)
            {
                _properties.SetTexture("_PreviousBuffer", previous.ColorTextures[0], _resources.LinearClamp);
                _properties.SetFloat("_TResponse", _settings.TemporalResponse);
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
            _properties.SetFloat("_BlurRadius", _settings.BlurRadius);
            _properties.SetFloat2("_ScreenSize", new Float2(context.View.Width, context.View.Height));
            _properties.SetTexture("_MainTex", result.ColorTextures[0], _resources.LinearClamp);
            _properties.SetFloat2("_BlurDirection", new Float2(1, 0));
            context.Fullscreen(_blur, blur, _properties);
            _properties.SetTexture("_MainTex", blur.ColorTextures[0], _resources.LinearClamp);
            _properties.SetFloat2("_BlurDirection", new Float2(0, 1));
            context.Fullscreen(_blur, visibility, _properties);
            return;
        }
        _properties.SetTexture("_MainTex", result.ColorTextures[0], _resources.LinearClamp);
        context.Fullscreen(_copy, visibility, _properties);
    }
}

/// <summary>
/// Prowl's stochastic screen space reflections over the opaque scene: a GGX sampled ray per pixel at a part of the
/// resolution, resolved by reusing neighbouring rays with a pyramid level from a roughness cone, accumulated over frames
/// with last frame's result fed back for a bounce, and added over the scene by Fresnel.
/// </summary>
public sealed class ScreenSpaceReflections : PostEffect
{
    public override SceneTargets SceneReads => SceneTargets.Motion | SceneTargets.Normals | SceneTargets.Material;

    private const int MaxMip = 5;
    private readonly FullscreenProgram _rayCast, _blur, _resolve, _temporal, _reproject, _combine, _copy;
    private readonly PropertySet _properties = new();
    private Settings _settings;
    private (bool MipMap, bool Temporal, float Scale) _declared = (true, true, 0.5f);

    private readonly record struct Settings(float RayDistance, float BrdfBias, bool RayReuse, bool Normalization, bool Fireflies, float TemporalScale, float TemporalResponse, bool ReflectionVelocity, bool Fresnel, float ScreenFade);

    private sealed class ViewState
    {
        public TextureHandle Source, Combined, ReflectionHistory, RayData, Reflection;
        public readonly TextureHandle[] Pyramid = new TextureHandle[4];
        public readonly TextureHandle[] Scratch = new TextureHandle[4];
        public int FrameIndex;
    }

    public ScreenSpaceReflections(Renderer renderer) : base(renderer)
    {
        _rayCast = Program("Ssr", "#define SSR_RAYCAST 1\n", source: false);
        _blur = Program("Ssr", "#define SSR_BLUR 1\n", source: false);
        _resolve = Program("Ssr", "#define SSR_RESOLVE 1\n", source: false);
        _temporal = Program("Ssr", "#define SSR_TEMPORAL 1\n", source: false);
        _reproject = Program("Ssr", "#define SSR_REPROJECT 1\n", source: false);
        _combine = Program("Ssr", "#define SSR_COMBINE 1\n");
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

    /// <summary>Weights the reflection by Fresnel and the environment BRDF.</summary>
    public bool UseFresnel { get; set; } = true;

    /// <summary>The part of the screen border reflections fade out over, 0 to 1.</summary>
    public float ScreenFadeSize { get; set; } = 0.25f;

    public override RenderSlot Slot => RenderSlot.AfterOpaques;

    public override void Extract()
    {
        _settings = new Settings(Math.Clamp(RayDistance, 1, 100), Math.Clamp(BrdfBias, 0, 1), RayReuse, Normalization, ReduceFireflies, TemporalScale, Math.Clamp(TemporalResponse, 0, 1), ReflectionVelocity, UseFresnel, Math.Clamp(ScreenFadeSize, 0, 1));
        (bool, bool, float) declared = (UseMipMap, UseTemporal, RayResolution.Scale());
        if (declared != _declared)
        {
            _declared = declared;
            Invalidate();
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        int width = (int)view.Width, height = (int)view.Height;
        if (_declared.Temporal)
        {
            state.Source = builder.Temporary("Source", view.Texture(width, height, Hdr));
            state.Combined = builder.History("Combined", view.Texture(width, height, Hdr), usage: TextureState.Attachment | TextureState.Sampled);
            state.ReflectionHistory = builder.History("Reflection", view.Texture(width, height, Hdr), usage: TextureState.Attachment | TextureState.Sampled);
        }
        if (_declared.MipMap)
        {
            for (int level = 0; level < 4; level++)
            {
                state.Scratch[level] = builder.Temporary("Scratch" + level, view.Texture(Math.Max(1, width >> level), Math.Max(1, height >> level), Hdr));
                state.Pyramid[level] = builder.Temporary("Pyramid" + level, view.Texture(Math.Max(1, width >> (level + 1)), Math.Max(1, height >> (level + 1)), Hdr));
            }
        }
        state.RayData = builder.Temporary("Rays", view.Texture(Math.Max(1, (int)(width * _declared.Scale)), Math.Max(1, (int)(height * _declared.Scale)), Hdr));
        state.Reflection = builder.Temporary("Reflection", view.Texture(width, height, Hdr));
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        state.FrameIndex = (state.FrameIndex + 1) % 64;
        Float2 jitter = new(Halton.Of(state.FrameIndex, 2), Halton.Of(state.FrameIndex, 3));
        Float2 screen = new(context.View.Width, context.View.Height);
        RenderTexture rays = context.Get(state.RayData);
        Float2 raySize = new(rays.ColorTextures[0].Width, rays.ColorTextures[0].Height);

        _properties.SetTexture("_CameraDepthTexture", context.Depth!, PointClamp);
        _properties.SetTexture("_CameraNormalsTexture", context.Normals!, PointClamp);
        _properties.SetTexture("_MaterialTexture", context.Material!, PointClamp);
        _properties.SetTexture("_CameraMotionVectorsTexture", context.Motion!, PointClamp);
        _properties.SetTexture("_Noise", context.Noise, LinearRepeat);
        _properties.SetFloat2("_NoiseSize", new Float2(256, 256));
        _properties.SetFloat4("_JitterSizeAndOffset", new Float4(raySize.X / 256f, raySize.Y / 256f, jitter.X, jitter.Y));
        _properties.SetFloat2("_ResolveSize", screen);
        _properties.SetFloat2("_ScreenSize", screen);
        _properties.SetFloat("_NumSteps", _settings.RayDistance);
        _properties.SetFloat("_BRDFBias", _settings.BrdfBias);
        _properties.SetFloat("_EdgeFactor", _settings.ScreenFade);
        _properties.SetFloat("_MaxMipMap", MaxMip);
        _properties.SetInt("_RayReuse", _settings.RayReuse ? 1 : 0);
        _properties.SetInt("_UseNormalization", _settings.Normalization ? 1 : 0);
        _properties.SetInt("_Fireflies", _settings.Fireflies ? 1 : 0);
        _properties.SetInt("_UseFresnel", _settings.Fresnel ? 1 : 0);
        _properties.SetInt("_ReflectionVelocity", _settings.ReflectionVelocity ? 1 : 0);

        // The reflected scene: the last result moved by motion for a bounce, else these opaques
        RenderTexture? previousCombined = _declared.Temporal ? context.History(state.Combined) : null;
        Texture source = context.Color!;
        if (previousCombined != null && context.HasPreviousFrame)
        {
            RenderTexture reprojected = context.Get(state.Source);
            _properties.SetTexture("_MainTex", previousCombined.ColorTextures[0], LinearClamp);
            context.Fullscreen(_reproject, reprojected, _properties);
            source = reprojected.ColorTextures[0];
        }

        _properties.SetTexture("_Scene0", source, LinearClamp);
        Texture level = source;
        for (int i = 0; i < 4; i++)
        {
            if (_declared.MipMap)
            {
                RenderTexture scratch = context.Get(state.Scratch[i]), next = context.Get(state.Pyramid[i]);
                _properties.SetTexture("_MainTex", level, LinearClamp);
                _properties.SetFloat2("_BlurDir", new Float2(1, 0));
                context.Fullscreen(_blur, scratch, _properties);
                _properties.SetTexture("_MainTex", scratch.ColorTextures[0], LinearClamp);
                _properties.SetFloat2("_BlurDir", new Float2(0, 1));
                context.Fullscreen(_blur, next, _properties);
                level = next.ColorTextures[0];
            }
            _properties.SetTexture("_Scene" + (i + 1), level, LinearClamp);
        }

        context.Fullscreen(_rayCast, rays, _properties);
        _properties.SetTexture("_RayCast", rays.ColorTextures[0], PointClamp);
        RenderTexture reflection = context.Get(state.Reflection);
        context.Fullscreen(_resolve, reflection, _properties);

        Texture final = reflection.ColorTextures[0];
        if (_declared.Temporal)
        {
            RenderTexture history = context.Get(state.ReflectionHistory);
            RenderTexture? previous = context.History(state.ReflectionHistory);
            _properties.SetTexture("_MainTex", reflection.ColorTextures[0], LinearClamp);
            bool valid = previous != null && context.HasPreviousFrame;
            _properties.SetTexture("_PreviousBuffer", (previous ?? reflection).ColorTextures[0], LinearClamp);
            _properties.SetFloat("_TScale", _settings.TemporalScale);
            _properties.SetFloat("_TResponse", valid ? _settings.TemporalResponse : 0);
            context.Fullscreen(_temporal, history, _properties);
            final = history.ColorTextures[0];
        }

        _properties.SetTexture("_ReflectionBuffer", final, LinearClamp);
        if (!_declared.Temporal)
        {
            context.Fullscreen(_combine, _properties);
            return;
        }
        RenderTexture combined = context.Get(state.Combined);
        context.Fullscreen(_combine, combined, _properties);
        _properties.SetTexture("_MainTex", combined.ColorTextures[0], LinearClamp);
        context.Fullscreen(_copy, _properties);
    }
}

/// <summary>
/// Prowl's motion blur: the colour sampled along each pixel's motion, at most a largest radius in pixels, weighted by how
/// close each sample's depth is so background does not bleed over foreground.
/// </summary>
public sealed class MotionBlur : PostEffect
{
    public override SceneTargets SceneReads => SceneTargets.Motion;

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private (float Intensity, int Samples, float MaxRadius) _settings;

    public MotionBlur(Renderer renderer) : base(renderer) => _program = Program("MotionBlur");

    /// <summary>Blur strength, 1 physically correct.</summary>
    public float Intensity { get; set; } = 1;

    /// <summary>Samples along the motion, 1 to 32.</summary>
    public int Samples { get; set; } = 8;

    /// <summary>The longest blur in pixels.</summary>
    public float MaxBlurRadius { get; set; } = 40;

    public override RenderSlot Slot => RenderSlot.HdrPost;

    public override void Extract() => _settings = (Intensity, Math.Clamp(Samples, 1, 32), Math.Max(1, MaxBlurRadius));

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture("_MotionVectorsTex", context.Motion!, PointClamp);
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, PointClamp);
        _properties.SetFloat2("_Resolution", new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat("_Intensity", _settings.Intensity);
        _properties.SetInt("_Samples", _settings.Samples);
        _properties.SetFloat("_MaxBlurRadius", _settings.MaxRadius);
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
/// Prowl's bokeh depth of field: a signed circle of confusion splits the image into a far field behind focus and a near
/// field in front, each gathered with a disk, so sharp objects do not halo into a blurred background and a blurred
/// foreground spreads over what is behind it. The focus distance eases toward its target on the GPU, from the first
/// layer's center when automatic, so a layered view's layers share it.
/// </summary>
public sealed class DepthOfField : PostEffect
{
    private readonly FullscreenProgram _coc, _prefilter, _bokeh, _postfilter, _combine, _focus, _focusLayered;
    private readonly PropertySet _properties = new();
    private Settings _settings;
    private int _divisor = 2;

    private readonly record struct Settings(bool AutoFocus, float ManualFocus, float Strength, float MaxBlur, int Rings, float Speed);

    private sealed class ViewState
    {
        public TextureHandle CoC, Prefiltered, Bokeh, Smoothed, Focus;
    }

    public DepthOfField(Renderer renderer) : base(renderer)
    {
        BlendStateDescription two = new(default, BlendAttachmentDescription.OverrideBlend, BlendAttachmentDescription.OverrideBlend);
        _coc = Program("DepthOfField", "#define DOF_COC 1\n", source: false);
        _prefilter = Program("DepthOfField", "#define DOF_PREFILTER 1\n");
        _bokeh = Program("DepthOfField", "#define DOF_BOKEH 1\n", source: false, blend: two);
        _postfilter = Program("DepthOfField", "#define DOF_POSTFILTER 1\n", source: false, blend: two);
        _combine = Program("DepthOfField");
        _focus = Program("DepthOfField", "#define DOF_FOCUS 1\n", source: false);
        _focusLayered = Program("DepthOfField", "#define DOF_FOCUS 1\n#define DOF_LAYERED_SOURCE 1\n", source: false);
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

    public override RenderSlot Slot => RenderSlot.HdrPost;

    public override void Extract()
    {
        int rings = Quality switch { DepthOfFieldQuality.Low => 2, DepthOfFieldQuality.Medium => 3, _ => 4 };
        _settings = new Settings(UseAutoFocus, MathF.Max(ManualFocusPoint, 0.01f), Math.Max(0, FocusStrength), Math.Max(0, MaxBlurRadius), rings, FocusSpeed);
        int divisor = Math.Max(1, (int)Resolution);
        if (divisor != _divisor)
        {
            _divisor = divisor;
            Invalidate();
        }
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        int width = (int)view.Width, height = (int)view.Height;
        int blurWidth = Math.Max(1, width / _divisor), blurHeight = Math.Max(1, height / _divisor);
        uint layers = view.LayerCount > 1 ? (uint)view.LayerCount : 0;
        state.CoC = builder.Temporary("CoC", view.Texture(width, height, PixelFormat.R16_Float));
        state.Prefiltered = builder.Temporary("Prefiltered", view.Texture(blurWidth, blurHeight, Hdr));
        state.Bokeh = builder.Temporary("Bokeh", GraphTextureDesc.Sized(blurWidth, blurHeight, false, Hdr, Hdr) with { ArrayLayers = layers });
        state.Smoothed = builder.Temporary("Smoothed", GraphTextureDesc.Sized(blurWidth, blurHeight, false, Hdr, Hdr) with { ArrayLayers = layers });
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
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, PointClamp);
        _properties.SetTexture("_PrevFocusTex", previous?.ColorTextures[0] ?? context.Noise, PointClamp);
        _properties.SetFloat("_UseAutoFocus", _settings.AutoFocus ? 1 : 0);
        _properties.SetFloat("_ManualFocusPoint", _settings.ManualFocus);
        _properties.SetFloat("_FocusBlend", _settings.Speed > 0 ? 1 - MathF.Exp(-_settings.Speed * context.Time.DeltaTime) : 1);
        _properties.SetFloat("_FocusHistoryValid", valid ? 1 : 0);
        context.Fullscreen(context.View.LayerCount > 1 ? _focusLayered : _focus, focus, _properties, shared: true);

        _properties.SetTexture("_FocusTex", focus.ColorTextures[0], PointClamp);
        _properties.SetFloat("_FocusStrength", _settings.Strength);
        _properties.SetFloat("_MaxBlurRadius", _settings.MaxBlur);
        _properties.SetFloat2("_Resolution", new Float2(width, height));
        _properties.SetFloat("_Downscale", downscale);
        _properties.SetFloat("_MaxCoC", _settings.MaxBlur * 0.01f * height / downscale);
        _properties.SetInt("_Rings", _settings.Rings);
        RenderTexture coc = context.Get(state.CoC);
        context.Fullscreen(_coc, coc, _properties);

        _properties.SetTexture("_CoCTex", coc.ColorTextures[0], PointClamp);
        context.Fullscreen(_prefilter, prefiltered, _properties);
        _properties.SetTexture("_MainTex", prefiltered.ColorTextures[0], LinearClamp);
        context.Fullscreen(_bokeh, bokeh, _properties);
        _properties.SetTexture("_FarTex", bokeh.ColorTextures[0], LinearClamp);
        _properties.SetTexture("_NearTex", bokeh.ColorTextures[1], LinearClamp);
        context.Fullscreen(_postfilter, smoothed, _properties);

        _properties.SetTexture("_FarTex", smoothed.ColorTextures[0], LinearClamp);
        _properties.SetTexture("_NearTex", smoothed.ColorTextures[1], LinearClamp);
        _properties.SetTexture("_CoCTex", coc.ColorTextures[0], LinearClamp);
        context.Fullscreen(_combine, _properties);
    }
}

/// <summary>
/// Prowl's god rays as an HDR effect: bright sky along the line to the sun on screen, decaying each step, added as warm
/// shafts. The sun is the world's, so a world without a directional light draws none.
/// </summary>
public sealed class GodRays : PostEffect
{
    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private (float Intensity, float Decay, float Density, float Weight, int Samples, float Threshold) _settings;

    public GodRays(Renderer renderer) : base(renderer)
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

    public override RenderSlot Slot => RenderSlot.HdrPost;

    public override void Extract() => _settings = (Intensity, Decay, Density, Weight, Math.Clamp(Samples, 8, 128), Threshold);

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, PointClamp);
        _properties.SetFloat3("_SunDirection", context.SunDirection ?? new Float3(0, -1, 0));
        _properties.SetFloat("_GodRayIntensity", context.SunDirection == null ? 0 : _settings.Intensity);
        _properties.SetFloat("_GodRayDecay", _settings.Decay);
        _properties.SetFloat("_GodRayDensity", _settings.Density);
        _properties.SetFloat("_GodRayWeight", _settings.Weight);
        _properties.SetInt("_GodRaySamples", _settings.Samples);
        _properties.SetFloat("_GodRayThreshold", _settings.Threshold);
        context.Fullscreen(_program, _properties);
    }
}

/// <summary>
/// One of Prowl's cinematic effects, a single pass over display referred colour in the DisplayPost slot. They run in
/// Prowl's order: pixelation, chromatic aberration, sharpening, edges, grading and the LUT, then anti aliasing, then film
/// grain and vignette so grain is not smoothed away.
/// </summary>
public abstract class DisplayEffect : PostEffect
{
    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();

    private protected DisplayEffect(Renderer renderer, string define, int order) : base(renderer)
    {
        _program = Program("Cinematic", $"#define CINEMATIC_{define} 1\n");
        Order = order;
    }

    public override RenderSlot Slot => RenderSlot.DisplayPost;

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

    public override void Render(FeatureContext context)
    {
        _properties.SetFloat2("_Resolution", new Float2(context.View.Width, context.View.Height));
        SetProperties(context, _properties);
        context.Fullscreen(_program, _properties);
    }

    /// <summary>The effect's settings as copied at Extract. Render thread.</summary>
    private protected abstract void SetProperties(FeatureContext context, PropertySet properties);
}

/// <summary>Prowl's pixelation: the image in blocks of a size in pixels.</summary>
public sealed class Pixelation(Renderer renderer) : DisplayEffect(renderer, "PIXELATION", 0)
{
    private float _size;

    public float PixelSize { get; set; } = 4;

    public override void Extract() => _size = MathF.Max(1, PixelSize);

    private protected override void SetProperties(FeatureContext context, PropertySet properties) => properties.SetFloat("_PixelSize", _size);
}

/// <summary>Prowl's chromatic aberration: red pushed outward and blue inward by a barrel distortion.</summary>
public sealed class ChromaticAberration(Renderer renderer) : DisplayEffect(renderer, "CHROMATIC_ABERRATION", 1)
{
    private (float Intensity, float Distortion) _settings;

    /// <summary>The largest channel offset in pixels.</summary>
    public float Intensity { get; set; } = 3;

    /// <summary>The barrel distortion of the offsets, 0 linear.</summary>
    public float Distortion { get; set; } = 0.5f;

    public override void Extract() => _settings = (Intensity, Distortion);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat("_ChromaticIntensity", _settings.Intensity);
        properties.SetFloat("_ChromaticDistortion", _settings.Distortion);
    }
}

/// <summary>Prowl's contrast adaptive sharpening.</summary>
public sealed class Sharpen(Renderer renderer) : DisplayEffect(renderer, "SHARPEN", 2)
{
    private (float Amount, float Radius) _settings;

    /// <summary>Sharpening, 0 least to 1 most.</summary>
    public float Amount { get; set; } = 0.5f;

    /// <summary>The sample radius in pixels, 1 or more.</summary>
    public float Radius { get; set; } = 1;

    public override void Extract() => _settings = (Amount, MathF.Max(1, Radius));

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat("_SharpenAmount", _settings.Amount);
        properties.SetFloat("_SharpenRadius", _settings.Radius);
    }
}

/// <summary>Prowl's Sobel edge detection: edges drawn in a colour over the faded image.</summary>
public sealed class EdgeDetection(Renderer renderer) : DisplayEffect(renderer, "EDGE_DETECTION", 3)
{
    private (float Intensity, Float4 Color, float Fade) _settings;

    public float Intensity { get; set; } = 1;
    public Float4 Color { get; set; } = new(0, 0, 0, 1);

    /// <summary>How much of the image stays under the edges, 0 edges only to 1 all of it.</summary>
    public float BackgroundFade { get; set; } = 1;

    public override void Extract() => _settings = (Intensity, Color, BackgroundFade);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat("_EdgeIntensity", _settings.Intensity);
        properties.SetFloat4("_EdgeColor", _settings.Color);
        properties.SetFloat("_EdgeBackgroundFade", _settings.Fade);
    }
}

/// <summary>Prowl's colour grading: exposure, contrast around mid grey, temperature, saturation, then lift, gamma and gain.</summary>
public sealed class ColorGrading(Renderer renderer) : DisplayEffect(renderer, "COLOR_GRADING", 4)
{
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
        properties.SetFloat("_PostExposure", _settings.Exposure);
        properties.SetFloat("_Contrast", _settings.Contrast);
        properties.SetFloat("_Saturation", _settings.Saturation);
        properties.SetFloat("_Temperature", _settings.Temperature);
        properties.SetFloat4("_Lift", _settings.Lift);
        properties.SetFloat4("_Gamma", _settings.Gamma);
        properties.SetFloat4("_Gain", _settings.Gain);
    }
}

/// <summary>Prowl's LUT grading from a strip texture, its slices side by side, 256 by 16 for a 16 cube. Without a texture it changes nothing.</summary>
public sealed class ColorLookup(Renderer renderer) : DisplayEffect(renderer, "LUT", 5)
{
    private (Texture? Texture, float Contribution) _settings;

    public Texture? Texture { get; set; }

    /// <summary>How much of the LUT applies, 0 to 1.</summary>
    public float Contribution { get; set; } = 1;

    public override void Extract() => _settings = (Texture, Contribution);

    public override bool IsActive(in FeatureView view) => _settings.Texture is { IsDisposed: false };

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetTexture("_LUTTex", _settings.Texture!, LinearClamp);
        properties.SetFloat("_LUTContribution", _settings.Contribution);
        properties.SetFloat("_LUTSize", _settings.Texture!.Height);
    }
}

/// <summary>Prowl's film grain, changing every frame, optionally stronger in highlights.</summary>
public sealed class FilmGrain(Renderer renderer) : DisplayEffect(renderer, "FILM_GRAIN", 20)
{
    private (float Intensity, float Response) _settings;

    public float Intensity { get; set; } = 0.15f;

    /// <summary>How far grain follows luminance, 0 even to 1 only in highlights.</summary>
    public float Response { get; set; } = 0.5f;

    public override void Extract() => _settings = (Intensity, Response);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat("_Time", (float)context.Time.Time);
        properties.SetFloat("_GrainIntensity", _settings.Intensity);
        properties.SetFloat("_GrainResponse", _settings.Response);
    }
}

/// <summary>Prowl's vignette, darkening toward the edges.</summary>
public sealed class Vignette(Renderer renderer) : DisplayEffect(renderer, "VIGNETTE", 21)
{
    private (float Intensity, float Smoothness, float Roundness) _settings;

    public float Intensity { get; set; } = 0.4f;
    public float Smoothness { get; set; } = 0.2f;

    /// <summary>0 follows the view's shape, 1 a circle.</summary>
    public float Roundness { get; set; } = 1;

    public override void Extract() => _settings = (Intensity, Smoothness, Roundness);

    private protected override void SetProperties(FeatureContext context, PropertySet properties)
    {
        properties.SetFloat("_VignetteIntensity", _settings.Intensity);
        properties.SetFloat("_VignetteSmoothness", _settings.Smoothness);
        properties.SetFloat("_VignetteRoundness", _settings.Roundness);
    }
}

/// <summary>Prowl's FXAA 3.11 on display referred colour, in the AntiAliasing group with TAA and SMAA, before film grain and vignette.</summary>
public sealed class Fxaa : PostEffect
{
    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private (float Max, float Min, float Subpixel) _settings;

    public Fxaa(Renderer renderer) : base(renderer)
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

    public override RenderSlot Slot => RenderSlot.DisplayPost;
    public override string? ExclusiveGroup => "AntiAliasing";

    public override void Extract() => _settings = (EdgeThresholdMax, EdgeThresholdMin, SubpixelQuality);

    public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

    public override void Render(FeatureContext context)
    {
        _properties.SetFloat2("_Resolution", new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat("_EdgeThresholdMax", _settings.Max);
        _properties.SetFloat("_EdgeThresholdMin", _settings.Min);
        _properties.SetFloat("_SubpixelQuality", _settings.Subpixel);
        context.Fullscreen(_program, _properties);
    }
}

/// <summary>
/// Prowl's SMAA 1x with luma edges on display referred colour: edges, blend weights from the reference's area and search
/// tables, then neighbourhood blending. In the AntiAliasing group with TAA and FXAA.
/// </summary>
public sealed class Smaa : PostEffect
{
    private readonly FullscreenProgram _edges, _weights, _blend;
    private readonly PropertySet _properties = new();
    private float _threshold;

    private sealed class ViewState
    {
        public TextureHandle Edges, Weights;
    }

    public Smaa(Renderer renderer) : base(renderer)
    {
        _edges = Program("Smaa", "#define SMAA_EDGES 1\n", source: false);
        _weights = Program("Smaa", "#define SMAA_WEIGHTS 1\n", source: false);
        _blend = Program("Smaa");
        Order = 10;
    }

    /// <summary>Edge sensitivity, about 0.05 to 0.2, lower finding more edges.</summary>
    public float EdgeThreshold { get; set; } = 0.1f;

    public override RenderSlot Slot => RenderSlot.DisplayPost;
    public override string? ExclusiveGroup => "AntiAliasing";

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
        (Texture area, Texture search) = context.SmaaTables;
        _properties.SetFloat2("_Resolution", new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat("_EdgeThreshold", _threshold);
        _properties.SetTexture("_MainTex", context.Color!, LinearClamp);
        context.Fullscreen(_edges, edges, _properties, clear: true);
        _properties.SetTexture("_MainTex", edges.ColorTextures[0], LinearClamp);
        _properties.SetTexture("_AreaTex", area, LinearClamp);
        _properties.SetTexture("_SearchTex", search, LinearClamp);
        context.Fullscreen(_weights, weights, _properties, clear: true);
        _properties.SetTexture("_BlendTex", weights.ColorTextures[0], LinearClamp);
        context.Fullscreen(_blend, _properties);
    }
}
