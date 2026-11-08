using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Volumetric clouds over a world, with an optional faint high layer above them, in the Atmosphere slot over the opaques
/// and sky, under the transparents. It lights them with <see cref="World"/>'s sun and sky light.
/// <para/>
/// Each view draws a weather map around its camera holding how far each column's cloud rises from the layer's floor and
/// hangs from its ceiling, and the edges between cells of 3D noise carve lobes and billows into the clouds,
/// swirled into wisps by curl noise.
/// The clouds and the high layer are marched front to back up to the scene's depth and scaled up over the scene.
/// </summary>
public sealed class VolumetricCloudsFeature : RenderFeature, IDisposable
{
    private const int MapResolution = 512;
    private const int TemporalFrames = 4;
    private const int ShadowCascades = 4;

    private sealed class CloudSettings
    {
        public Float4 Shape, Pattern, Lighting, Albedo;
        public Float4 Heights;
        public Float2 Gap, Lobes;
        public float Dome;
        public Double3? ShadowCentre;
        public Float2 Coverages;
        public Float2 Wind, Scatter;
        public double WindX, WindZ;
        public Float3 ToSun, SunColor, SkyLight, ShadowRight, ShadowUp;
        public bool HasSun;
        public float AmbientSaturation, HorizonFade, Reach, DetailScale;
        public Float3 Wisps;
        public int Scale, ShadowResolution;
        public bool Temporal;
        public Texture? Noise2D;
        public int Epoch;
        public Float4 High;
        public Float2 HighWind;
    }

    private sealed class ViewState
    {
        public TextureHandle Maps, Target, Accumulated, ShadowMap, ShadowBlur;
        public bool Temporal, UsesShadows;
        public int Frame, Epoch;
        public Float2 Wind;
        public int MapEpoch;
        public float MapFormation;
        public long MapFirstX, MapFirstZ;
        public float MapTexel;
    }

    // The world's cloud shadow map, which every view reads: cascades side by side, each square to the sun around where a
    // camera was when it was drawn, the smallest first. The first camera drawn each frame draws half of them again, in
    // ShadowPass, the even cascades or the odd ones by Turn. Blur holds one cascade between the two passes of its blur
    private sealed class Shadows(RenderTexture map, RenderTexture blur, int resolution)
    {
        public readonly RenderTexture Map = map, Blur = blur;
        public readonly int Resolution = resolution;
        public readonly Float4[] Cascades = new Float4[ShadowCascades];
        public Float4 Heights;
        public readonly Float2[] Winds = new Float2[ShadowCascades];
        public int Epoch = -1;
        public int Turn;
        public readonly Float3[] Rights = new Float3[ShadowCascades], Ups = new Float3[ShadowCascades], ToSuns = new Float3[ShadowCascades];
    }

    private readonly FullscreenProgram _maps, _march, _composite, _temporal, _shadow, _shadowBlur;
    private readonly GraphicsDevice _device;
    private readonly CloudNoise _noise;
    private readonly Sampler _linearClamp, _pointClamp, _linearRepeat;
    private readonly PropertySet _properties = new();
    private readonly CloudSettings _settings = new();
    private int _scale = 1;
    private Shadows? _shadows;
    private bool _shadowsClaimed;
    private readonly DeviceBuffer _shadowData;

    // The settings the history was started with, so changing them enough starts it again
    private readonly List<float> _values = [], _tolerances = [];
    private float[]? _baseline;
    private Texture? _baselineNoise;

    public VolumetricCloudsFeature(Renderer renderer) : base(RenderSlot.Atmosphere, runsOn: ViewKinds.Cameras | ViewKinds.ReflectionProbe)
    {
        BlendStateDescription two = new(default, BlendAttachmentDescription.OverrideBlend, BlendAttachmentDescription.OverrideBlend);
        _maps = Program(renderer, "MAPS");
        _march = Program(renderer, "MARCH", two, lit: true);
        _composite = renderer.Effects.Program(this, "Clouds");
        _temporal = Program(renderer, "TEMPORAL");
        _shadow = Program(renderer, "SHADOW");
        _shadowBlur = Program(renderer, "SHADOWBLUR");
        _device = renderer.Device;
        _noise = renderer.Resources.CloudNoise;
        _linearClamp = renderer.Resources.LinearClamp;
        _pointClamp = renderer.Resources.PointClamp;
        _linearRepeat = renderer.Resources.LinearRepeat;
        _shadowData = _device.ResourceFactory.CreateBuffer(new BufferDescription(CloudShadowRecord.Size, BufferUsage.StructuredBufferReadOnly));
        _shadowData.Name = "Cloud Shadow Record";
        ShadowPass = new ShadowFeature(this);
    }

    /// <summary>
    /// Draws the cloud shadow map and the weather around it for the first camera each frame, before any view's surfaces,
    /// so every view's sun is shaded by the clouds, its volumetric fog and water included. Add it to the world's features
    /// beside this one.
    /// </summary>
    public RenderFeature ShadowPass { get; }

    private sealed class ShadowFeature(VolumetricCloudsFeature owner) : RenderFeature(RenderSlot.Shadows, runsOn: ViewKinds.Cameras | ViewKinds.ReflectionProbe)
    {
        public override bool IsActive(in FeatureView view) => owner.IsActive(view);

        public override void Setup(FeatureBuilder builder, in FeatureView view) => owner.SetupShadows(builder, view);

        public override void Render(FeatureContext context) => owner.RenderShadows(context);
    }

    private FullscreenProgram Program(Renderer renderer, string pass, BlendStateDescription? blend = null, bool lit = false)
        => renderer.Effects.Program(this, "Clouds", $"#define CLOUD_{pass} 1\n", source: false, blend: blend, lit: lit);

    /// <summary>Height of the cloud layer's floor above zero, in meters.</summary>
    public float Altitude { get; set; } = 1500f;

    /// <summary>How tall the cloud layer is in meters, from its floor to its ceiling before the gap moves it.</summary>
    public float Thickness { get; set; } = 4000f;

    /// <summary>How far through the layer the tallest clouds rise in domes from flat bases on its floor, 0 to 1.</summary>
    public float BottomStrength { get; set; } = 0.45f;

    /// <summary>How far through the layer the top clouds dome down from its ceiling, 0 to 1. They dome up above it by half as far.</summary>
    public float TopStrength { get; set; } = 0.2f;

    /// <summary>How far up the top clouds dome above the ceiling at most, 0 to 2, as a share of their depth below it.</summary>
    public float DomeStrength { get; set; } = 2f;

    /// <summary>How far up the ceiling the top clouds hang from is, in layer thicknesses, so how far apart the two are.</summary>
    public float GapMultiplier { get; set; } = 0.4f;

    /// <summary>Scales the coverage of the clouds rising from the floor.</summary>
    public float BottomCoverage { get; set; } = 1.1f;

    /// <summary>Scales the coverage of the clouds hanging from the ceiling.</summary>
    public float TopCoverage { get; set; } = 0.9f;

    /// <summary>How far round lobes push each cloud's domed side in and out, as a share of its height, 0 to 1.</summary>
    public float Lobes { get; set; } = 0.25f;

    /// <summary>Size of the lobes in meters.</summary>
    public float LobeScale { get; set; } = 2000f;

    /// <summary>How much of the sky is covered, 0 to 1.</summary>
    public float Coverage { get; set; } = 0.69f;

    /// <summary>How quickly light dims inside the clouds. Higher looks heavier and darker underneath.</summary>
    public float Density { get; set; } = 1f;

    /// <summary>Size of the weather pattern in meters, how far apart clusters of clouds are.</summary>
    public float PatternScale { get; set; } = 12000f;

    /// <summary>How much the detail noise eats into cloud edges, 0 to 1.</summary>
    public float Erosion { get; set; } = 1f;

    /// <summary>Size of the detail noise in meters.</summary>
    public float DetailScale { get; set; } = 600f;

    /// <summary>How far curl noise swirls the clouds into wisps, 0 to 1, as a share of the swirls' size.</summary>
    public float Wisps { get; set; } = 0.5f;

    /// <summary>Size of the wisps' swirls in meters.</summary>
    public float WispScale { get; set; } = 140f;

    /// <summary>How far the wind has carried the clouds, in meters on the XZ plane.</summary>
    public Double2 Wind { get; set; }

    /// <summary>How far the clouds have drifted through the noise their shapes come from, 0 to 1.</summary>
    public float Formation { get; set; }

    /// <summary>The clouds' colour, which tints the light they scatter.</summary>
    public Float4 Albedo { get; set; } = Float4.One;

    /// <summary>Scales the sunlight the clouds receive.</summary>
    public float SunBrightness { get; set; } = 2f;

    /// <summary>Scales the sky light the clouds receive.</summary>
    public float AmbientBrightness { get; set; } = 2f;

    /// <summary>How much of the sky's color the sky light keeps, 0 to 1. Lower greys it, so clouds do not turn blue.</summary>
    public float AmbientSaturation { get; set; } = 1f;

    /// <summary>How bright the rim around the sun gets when looking through thin cloud toward it, 0 to 1.</summary>
    public float SilverLining { get; set; } = 0.6f;

    /// <summary>How much of the light that scatters more than once comes back out, 0 to 1.</summary>
    public float MultipleScattering { get; set; } = 0.4f;

    /// <summary>How much cloud light that scattered more than once still has to cross, 0 to 1.</summary>
    public float MultipleScatteringOcclusion { get; set; } = 0.2f;

    /// <summary>Around this distance in meters the clouds fade into the sky behind them. They are drawn out to twice this.</summary>
    public float HorizonFade { get; set; } = 32000f;

    /// <summary>Resolution the clouds are built at before they are scaled up over the scene.</summary>
    public EffectResolution Resolution { get; set; } = EffectResolution.Full;

    /// <summary>Marches in steps four times as long and blends each frame over the last, so four frames add up to the full march.</summary>
    public bool Temporal { get; set; } = true;

    /// <summary>Texels along each side of one cascade of the shadow map the clouds are lit from.</summary>
    public int ShadowResolution { get; set; } = 512;

    /// <summary>Where the shadow map is centred, null for the camera drawing it.</summary>
    public Double3? ShadowCentre { get; set; }

    /// <summary>Radius of the planet in meters, which curves the cloud layers down toward the horizon.</summary>
    public float PlanetRadius { get; set; } = 6371000f;

    /// <summary>Tiling noise the high layer is built from, its red channel. Null uses the built in one.</summary>
    public Texture? Noise { get; set; }

    /// <summary>Draws a faint flat layer of noise high above the clouds, like thin cirrus.</summary>
    public bool HighLayer { get; set; }

    /// <summary>Height of the high layer above zero, in meters.</summary>
    public float HighLayerAltitude { get; set; } = 8000f;

    /// <summary>How opaque the high layer is where it is thickest, 0 to 1.</summary>
    public float HighLayerOpacity { get; set; } = 0.35f;

    /// <summary>Size of the high layer's pattern in meters.</summary>
    public float HighLayerScale { get; set; } = 20000f;

    /// <summary>The world whose sun and sky light the clouds take, set when the feature joins a world's features.</summary>
    public RenderWorld? World { get; set; }

    internal override void Attach(object set) => World = ((ExtensionSet<RenderFeature>)set).World ?? World;

    public override void Extract()
    {
        CloudSettings s = _settings;
        s.Noise2D = Noise ?? _noise.Flat;

        // The march covers up to the ceiling and the highest the top clouds dome above it, half their depth with lobes
        float thickness = MathF.Max(Thickness, 1f);
        float gap = MathF.Max(GapMultiplier, 0f);
        float domeStrength = Math.Clamp(DomeStrength, 0f, 2f);
        float above = domeStrength * Math.Clamp(TopStrength, 0f, 1f) * thickness * (1f + Math.Clamp(Lobes, 0f, 1f));
        float span = MathF.Max(thickness * gap + above, 1f);
        s.Shape = new Float4(Altitude, span, 1f / span, MathF.Max(PlanetRadius, 1000f));
        s.Gap = new Float2(thickness, gap);
        s.Dome = domeStrength;
        float coverage = Math.Clamp(Coverage, 0f, 1f);
        s.ShadowCentre = ShadowCentre;
        s.Lobes = new Float2(Math.Clamp(Lobes, 0f, 1f), 1f / MathF.Max(LobeScale, 1f));
        s.Coverages = new Float2(Math.Clamp(coverage * BottomCoverage, 0f, 1f), Math.Clamp(coverage * TopCoverage, 0f, 1f));
        s.Heights = new Float4(Formation, Math.Clamp(BottomStrength, 0f, 1f), Math.Clamp(TopStrength, 0f, 1f), 300f);
        s.Pattern = new Float4(1f / MathF.Max(PatternScale, 1f), coverage, 1f / MathF.Max(DetailScale, 1f), Math.Clamp(Erosion, 0f, 1f));
        s.Lighting = new Float4(MathF.Max(Density, 0f) * 0.004f, MathF.Max(SunBrightness, 0f), MathF.Max(AmbientBrightness, 0f), Math.Clamp(SilverLining, 0f, 1f));
        s.Albedo = Albedo;
        s.Scatter = new Float2(Math.Clamp(MultipleScattering, 0f, 1f), Math.Clamp(MultipleScatteringOcclusion, 0f, 1f));
        s.Wind = new Float2((float)Wind.X, (float)Wind.Y);
        (s.WindX, s.WindZ) = (Wind.X, Wind.Y);
        s.AmbientSaturation = Math.Clamp(AmbientSaturation, 0f, 1f);
        s.HorizonFade = 1f / MathF.Max(HorizonFade, 100f);
        s.Reach = MathF.Max(HorizonFade, 100f) * 2f;
        s.DetailScale = MathF.Max(DetailScale, 1f);
        float wispScale = MathF.Max(WispScale, 1f), wispFrequency = s.DetailScale / wispScale;
        s.Wisps = new Float3(Math.Clamp(Wisps, 0f, 1f) * 0.4f / wispFrequency, wispFrequency, MathF.Log2(wispFrequency * CloudNoise.CurlSize / CloudNoise.VolumeSize));
        s.Scale = (int)MathF.Round(1f / Resolution.Scale());
        s.Temporal = Temporal;
        s.ShadowResolution = Math.Clamp(ShadowResolution, 64, 2048);

        // Toward the world's sun, straight up without one so the shaders never normalize a zero vector
        s.ToSun = Float3.UnitY;
        s.SunColor = Float3.Zero;
        s.SkyLight = Float3.Zero;
        s.HasSun = false;
        if (World is { } world)
        {
            LightHandle sun = world.Environment.Sun;
            if (sun.IsValid && world.IsAlive(sun))
            {
                LightDesc light = world.GetLight(sun);
                float length = Float3.Length(light.Direction);
                if (length > 1e-6f && float.IsFinite(length)) s.ToSun = -light.Direction / length;
                s.SunColor = light.Color * (light.Intensity * s.Lighting.Y);
                s.HasSun = true;
            }

            // Sky light from above, the captured sky's when there is one, else the world's ambient light reaching an upward surface
            s.SkyLight = world.SkyAmbientVersion > 0 ? world.SkyAmbient.Evaluate(Float3.UnitY) : world.EvaluateAmbient(Float3.UnitY);
        }

        // The high layer drifts twice as fast as the clouds below it, as winds are stronger higher up
        s.High = new Float4(HighLayerAltitude, Math.Clamp(HighLayerOpacity, 0f, 1f), 1f / MathF.Max(HighLayerScale, 1f), HighLayer ? 1f : 0f);
        s.HighWind = s.Wind * 2f;

        Float3 axis = MathF.Abs(s.ToSun.Y) > 0.99f ? Float3.UnitX : Float3.UnitY;
        s.ShadowRight = Float3.Normalize(Float3.Cross(axis, s.ToSun));
        s.ShadowUp = Float3.Cross(s.ToSun, s.ShadowRight);

        if (SettingsChanged(s)) s.Epoch++;
        _scale = s.Scale;
        _shadowsClaimed = false;
    }

    private void Add(float value, float tolerance = 0.01f)
    {
        _values.Add(value);
        _tolerances.Add(tolerance);
    }

    // Whether the settings that shape the clouds moved more than about one percent since the history was started, or the
    // sun more than about ten. The sun moves slowly through a day and the history follows small changes in light by itself
    private bool SettingsChanged(CloudSettings s)
    {
        _values.Clear();
        _tolerances.Clear();
        Add(Altitude); Add(Thickness); Add(TopStrength); Add(DomeStrength); Add(BottomStrength); Add(GapMultiplier); Add(BottomCoverage); Add(TopCoverage); Add(Lobes); Add(LobeScale); Add(Coverage); Add(Density);
        Add(PatternScale); Add(Erosion); Add(DetailScale); Add(Wisps); Add(WispScale); Add(Albedo.X); Add(Albedo.Y); Add(Albedo.Z);
        Add(SunBrightness); Add(AmbientBrightness); Add(AmbientSaturation);
        Add(SilverLining); Add(MultipleScattering); Add(MultipleScatteringOcclusion); Add(HorizonFade);
        Add(PlanetRadius);
        Add(s.ToSun.X, 0.1f); Add(s.ToSun.Y, 0.1f); Add(s.ToSun.Z, 0.1f);
        Add(s.SunColor.X, 0.1f); Add(s.SunColor.Y, 0.1f); Add(s.SunColor.Z, 0.1f);
        Add(HighLayer ? 1f : 0f); Add(HighLayerAltitude); Add(HighLayerOpacity); Add(HighLayerScale);

        int count = _values.Count;
        bool changed = _baseline == null || _baseline.Length != count || _baselineNoise != s.Noise2D;
        for (int i = 0; !changed && i < count; i++)
        {
            float a = _values[i], b = _baseline![i];
            changed = MathF.Abs(a - b) > _tolerances[i] * MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
        }
        if (changed) (_baseline, _baselineNoise) = ([.. _values], s.Noise2D);
        return changed;
    }

    public override bool IsActive(in FeatureView view) => _settings.Noise2D is { IsDisposed: false };

    private static GraphTextureDesc Layered(in FeatureView view, int width, int height, PixelFormat format)
        => GraphTextureDesc.Sized(width, height, format) with { ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0 };

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        builder.DrawsRenderables();
        ViewState state = builder.GetViewState<ViewState>();
        TextureState usage = TextureState.Attachment | TextureState.Sampled;
        state.Maps = builder.History("Maps", GraphTextureDesc.Sized(MapResolution, MapResolution, PixelFormat.R16_G16_B16_A16_Float), usage: usage);

        int width = Math.Max(1, (int)view.Width / _scale), height = Math.Max(1, (int)view.Height / _scale);
        uint layers = view.LayerCount > 1 ? (uint)view.LayerCount : 0;

        state.Target = builder.Temporary("Target", GraphTextureDesc.Sized(width, height, false, PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R32_Float) with { ArrayLayers = layers });

        // A probe capture turns to new faces every render, so there is nothing to blend over
        state.Temporal = _settings.Temporal && view.Kind != ViewKind.ReflectionProbe;
        if (state.Temporal)
            state.Accumulated = builder.History("Accumulated", Layered(view, width, height, PixelFormat.R16_G16_B16_A16_Float), usage: usage);

        // A camera draws the shadow map in ShadowPass, so a view sees the clouds unshadowed until one has
        state.UsesShadows = _shadows is { Epoch: >= 0 };
        if (state.UsesShadows)
            state.ShadowMap = builder.ImportRead(_shadows!.Map);
    }

    // The first camera each frame draws the weather and the shadow map and writes their record, every view reads them
    private void SetupShadows(FeatureBuilder builder, in FeatureView view)
    {
        int resolution = _settings.ShadowResolution;
        if (_shadows?.Resolution != resolution)
        {
            DisposeShadows();
            RenderTexture map = _device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription((uint)(resolution * ShadowCascades), (uint)resolution, PixelFormat.R16_G16_B16_A16_Float, false));
            map.Name = "Cloud Shadows";
            RenderTexture blur = _device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription((uint)resolution, (uint)resolution, PixelFormat.R16_G16_B16_A16_Float, false));
            blur.Name = "Cloud Shadow Blur";
            _shadows = new Shadows(map, blur, resolution);
        }

        ViewState state = builder.GetViewState<ViewState>();
        state.UsesShadows = !_shadowsClaimed && view.Kind != ViewKind.ReflectionProbe;
        if (state.UsesShadows)
        {
            _shadowsClaimed = true;
            TextureState usage = TextureState.Attachment | TextureState.Sampled;
            state.Maps = builder.History("Maps", GraphTextureDesc.Sized(MapResolution, MapResolution, PixelFormat.R16_G16_B16_A16_Float), usage: usage);
            state.ShadowMap = builder.Import(_shadows.Map);
            state.ShadowBlur = builder.Import(_shadows.Blur);
        }
        if (state.UsesShadows || _shadows.Epoch >= 0)
            builder.UseCloudShadow(_shadows.Map, _shadowData, state.UsesShadows);
    }

    private void RenderShadows(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        if (!state.UsesShadows || _shadows is not { } shadows)
            return;
        Double3 camera = context.View.Position;
        SetShape();
        DrawMaps(context, state, camera);
        _properties.SetFloat3("_CloudCamera", new Float3((float)camera.X, (float)camera.Y, (float)camera.Z));
        DrawShadows(context, state, camera, shadows);
        context.Command.UpdateBuffer(_shadowData, 0, ShadowRecord(shadows));
    }

    private CloudShadowRecord ShadowRecord(Shadows shadows)
    {
        CloudSettings s = _settings;
        Float2 w0 = s.Wind - shadows.Winds[0], w1 = s.Wind - shadows.Winds[1], w2 = s.Wind - shadows.Winds[2], w3 = s.Wind - shadows.Winds[3];
        return new CloudShadowRecord
        {
            Cascade0 = shadows.Cascades[0],
            Cascade1 = shadows.Cascades[1],
            Cascade2 = shadows.Cascades[2],
            Cascade3 = shadows.Cascades[3],
            Heights = shadows.Heights,
            WindA = new Float4(w0.X, w0.Y, w1.X, w1.Y),
            WindB = new Float4(w2.X, w2.Y, w3.X, w3.Y),
            Right0 = new Float4(shadows.Rights[0], 0f), Right1 = new Float4(shadows.Rights[1], 0f), Right2 = new Float4(shadows.Rights[2], 0f), Right3 = new Float4(shadows.Rights[3], 0f),
            Up0 = new Float4(shadows.Ups[0], 0f), Up1 = new Float4(shadows.Ups[1], 0f), Up2 = new Float4(shadows.Ups[2], 0f), Up3 = new Float4(shadows.Ups[3], 0f),
            ToSun0 = new Float4(shadows.ToSuns[0], 0f), ToSun1 = new Float4(shadows.ToSuns[1], 0f), ToSun2 = new Float4(shadows.ToSuns[2], 0f), ToSun3 = new Float4(shadows.ToSuns[3], 0f),
            Params = new Float4(shadows.Resolution, s.HasSun ? 1f : 0f, s.Lighting.X * 40f, 0f),
            Scatter = new Float4(s.Scatter.X, s.Scatter.Y, 0f, 0f),
        };
    }

    private void SetShape()
    {
        CloudSettings s = _settings;
        _properties.SetTexture("_CloudNoise2D", s.Noise2D!, _linearRepeat);
        _properties.SetTexture("_CloudEdges", _noise.Edges, _linearRepeat);
        _properties.SetTexture("_CloudSlow", _noise.Slow, _linearRepeat);
        _properties.SetTexture("_CloudLobeNoise", _noise.Lobes, _linearRepeat);
        _properties.SetTexture("_CloudCurl", _noise.Curl, _linearRepeat);
        _properties.SetFloat3("_CloudWisps", s.Wisps);
        _properties.SetFloat("_CloudErosionSize", CloudNoise.VolumeSize);
        _properties.SetFloat4("_CloudShape", s.Shape);
        _properties.SetFloat4("_CloudPattern", s.Pattern);
        _properties.SetFloat2("_CloudWind", s.Wind);
        _properties.SetFloat3("_CloudToSun", s.ToSun);
        _properties.SetFloat4("_CloudHeights", s.Heights);
        _properties.SetFloat2("_CloudGap", s.Gap);
        _properties.SetFloat2("_CloudLobes", s.Lobes);
        _properties.SetFloat2("_CloudCoverages", s.Coverages);
        _properties.SetFloat("_CloudDome", s.Dome);
        _properties.SetFloat("_CloudReach", s.Reach);
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        CloudSettings s = _settings;
        Double3 camera = context.View.Position;

        SetShape();
        DrawMaps(context, state, camera);
        SetLighting(s);

        RenderTexture target = context.Get(state.Target);
        Float2 low = new(target.ColorTextures[0].Width, target.ColorTextures[0].Height);
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, _pointClamp);
        _properties.SetFloat2("_CloudFullResolution", new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat2("_CloudLowResolution", low);

        _properties.SetFloat3("_CloudCamera", new Float3((float)camera.X, (float)camera.Y, (float)camera.Z));
        BindShadows(context, state);
        _properties.SetFloat("_CloudStepScale", state.Temporal ? TemporalFrames : 1f);
        _properties.SetFloat("_CloudFrame", state.Temporal ? state.Frame++ % 64 : 0f);
        _properties.SetTexture("_CloudBlueNoise", context.Noise, _pointClamp);
        context.Fullscreen(_march, target, _properties);

        Texture clouds = target.ColorTextures[0];
        if (state.Temporal)
            clouds = Accumulate(context, state, target);
        _properties.SetTexture("_CloudTex", clouds, _pointClamp);
        _properties.SetTexture("_CloudTexDistance", target.ColorTextures[1], _pointClamp);
        context.Fullscreen(_composite, _properties);
    }

    // The first camera draws half the cascades of the shadow map each frame, every other cascade in turn, so each is at
    // most a frame old and the wind carries it on from where it was drawn, read along the sun's direction it was drawn
    // with. All of them are drawn when the settings change.
    // They lie around the shadow centre, or around the camera without one. Each cascade reaches a third as far as the
    // next, the last as far as the clouds
    private void DrawShadows(FeatureContext context, ViewState state, Double3 camera, Shadows shadows)
    {
        CloudSettings s = _settings;
        Float3 right = s.ShadowRight, up = s.ShadowUp;
        _properties.SetFloat3("_CloudShadowRight", right);
        _properties.SetFloat3("_CloudShadowUp", up);
        _properties.SetFloat("_CloudShadowResolution", shadows.Resolution);

        // Each cascade covers its reach of cloud across the sun. Toward the sun's side of the sky a low sun's light slants
        // across the clouds, so the cascade there only needs its reach times the sun's height, plus the layer's height
        // seen at the slant, keeping its texels about square on the clouds at any time of day. Its far clouds sit lower
        // as the planet curves away, so the layer's height takes that drop as well
        float sunHigh = Math.Clamp(MathF.Abs(s.ToSun.Y), 0.01f, 1f), sunLow = MathF.Sqrt(1f - sunHigh * sunHigh);
        double cameraY = (s.ShadowCentre ?? camera).Y;
        float clouds = (float)Math.Max(Math.Abs(cameraY - s.Shape.X), Math.Abs(cameraY - s.Shape.X - s.Shape.Y));

        RenderTexture map = context.Get(state.ShadowMap);
        bool all = shadows.Epoch != s.Epoch;
        for (int c = 0; c < ShadowCascades; c++)
        {
            if (!all && (c & 1) != shadows.Turn)
                continue;
            float half = s.Reach / MathF.Pow(3f, ShadowCascades - 1 - c);
            float height = half * sunHigh + (clouds + half * half / (2f * s.Shape.W)) * sunLow;
            Double3 centre = SnapToTexels(s.ShadowCentre ?? camera, s.Wind, right, up, 2f * half / shadows.Resolution, 2f * height / shadows.Resolution);
            shadows.Cascades[c] = new Float4((float)centre.X, (float)centre.Y, (float)centre.Z, half);
            shadows.Heights[c] = height;
            shadows.Winds[c] = s.Wind;
            shadows.Rights[c] = right;
            shadows.Ups[c] = up;
            shadows.ToSuns[c] = s.ToSun;
            _properties.SetFloat4("_CloudShadowBuild", shadows.Cascades[c]);
            _properties.SetFloat("_CloudShadowBuildUp", height);
            context.Fullscreen(_shadow, map, c * shadows.Resolution, 0, shadows.Resolution, shadows.Resolution, _properties, shared: true);
            BlurCascade(context, map, context.Get(state.ShadowBlur), c, shadows.Resolution);
        }
        shadows.Epoch = s.Epoch;
        shadows.Turn ^= 1;
    }

    // Binds the shadow map for the march, every cascade's place and how far the wind carried it since it was drawn
    private void BindShadows(FeatureContext context, ViewState state)
    {
        CloudSettings s = _settings;
        if (!state.UsesShadows || _shadows is not { } shadows)
        {
            for (int c = 0; c < ShadowCascades; c++)
                _properties.SetFloat4(s_shadowCascade[c], Float4.Zero);
            _properties.SetFloat4("_CloudShadowHeights", Float4.One);
            _properties.SetTexture("_CloudShadowMap", s.Noise2D!, _linearClamp);
            return;
        }
        _properties.SetFloat("_CloudShadowResolution", shadows.Resolution);
        for (int c = 0; c < ShadowCascades; c++)
        {
            _properties.SetFloat4(s_shadowCascade[c], shadows.Cascades[c]);
            _properties.SetFloat3(s_shadowRight[c], shadows.Rights[c]);
            _properties.SetFloat3(s_shadowUp[c], shadows.Ups[c]);
            _properties.SetFloat3(s_shadowToSun[c], shadows.ToSuns[c]);
        }
        _properties.SetFloat4("_CloudShadowHeights", shadows.Heights);
        Float2 w0 = s.Wind - shadows.Winds[0], w1 = s.Wind - shadows.Winds[1], w2 = s.Wind - shadows.Winds[2], w3 = s.Wind - shadows.Winds[3];
        _properties.SetFloat4("_CloudShadowWindA", new Float4(w0.X, w0.Y, w1.X, w1.Y));
        _properties.SetFloat4("_CloudShadowWindB", new Float4(w2.X, w2.Y, w3.X, w3.Y));
        _properties.SetTexture("_CloudShadowMap", context.Get(state.ShadowMap).ColorTextures[0], _linearClamp);
    }

    // Blurs a cascade across into the scratch texture, then down back into its place
    private void BlurCascade(FeatureContext context, RenderTexture map, RenderTexture blur, int c, int resolution)
    {
        _properties.SetTexture("_CloudBlurSource", map.ColorTextures[0], _pointClamp);
        _properties.SetFloat4("_CloudBlurArea", new Float4(c * resolution, 0f, 1f, 0f));
        context.Fullscreen(_shadowBlur, blur, _properties, shared: true);
        _properties.SetTexture("_CloudBlurSource", blur.ColorTextures[0], _pointClamp);
        _properties.SetFloat4("_CloudBlurArea", new Float4(0f, c * resolution, 0f, 1f));
        context.Fullscreen(_shadowBlur, map, c * resolution, 0, resolution, resolution, _properties, shared: true);
    }

    // A cascade's centre moved onto its texel grid across the sun, the grid fixed to the clouds as the wind carries them,
    // so the cascade's texels keep covering the same cloud as the camera moves and its shadows do not shimmer
    private static Double3 SnapToTexels(Double3 centre, Float2 wind, Float3 right, Float3 up, float texelAcross, float texelUp)
    {
        double x = centre.X - wind.X, z = centre.Z - wind.Y;
        double across = x * right.X + centre.Y * right.Y + z * right.Z;
        double along = x * up.X + centre.Y * up.Y + z * up.Z;
        double moveAcross = Math.Floor(across / texelAcross) * texelAcross - across, moveAlong = Math.Floor(along / texelUp) * texelUp - along;
        return new Double3(centre.X + moveAcross * right.X + moveAlong * up.X, centre.Y + moveAcross * right.Y + moveAlong * up.Y,
            centre.Z + moveAcross * right.Z + moveAlong * up.Z);
    }

    private static readonly PropertyID[] s_shadowCascade = ["_CloudShadowCascade0", "_CloudShadowCascade1", "_CloudShadowCascade2", "_CloudShadowCascade3"];
    private static readonly PropertyID[] s_shadowRight = ["_CloudShadowRight0", "_CloudShadowRight1", "_CloudShadowRight2", "_CloudShadowRight3"];
    private static readonly PropertyID[] s_shadowUp = ["_CloudShadowUp0", "_CloudShadowUp1", "_CloudShadowUp2", "_CloudShadowUp3"];
    private static readonly PropertyID[] s_shadowToSun = ["_CloudShadowToSun0", "_CloudShadowToSun1", "_CloudShadowToSun2", "_CloudShadowToSun3"];

    // Blends this frame's march over last frame's result, which needs last frame's at this size and settings
    private Texture Accumulate(FeatureContext context, ViewState state, RenderTexture target)
    {
        CloudSettings s = _settings;
        RenderTexture accumulated = context.Get(state.Accumulated);
        RenderTexture? previous = context.History(state.Accumulated);
        bool held = previous != null && context.HasPreviousFrame && state.Epoch == s.Epoch
            && previous.ColorTextures[0].Width == accumulated.ColorTextures[0].Width && previous.ColorTextures[0].Height == accumulated.ColorTextures[0].Height;
        _properties.SetTexture("_CloudCurrent", target.ColorTextures[0], _pointClamp);
        _properties.SetTexture("_CloudCurrentDistance", target.ColorTextures[1], _pointClamp);
        _properties.SetTexture("_CloudHistory", previous?.ColorTextures[0] ?? target.ColorTextures[0], _linearClamp);
        _properties.SetFloat("_CloudHistoryValid", held ? 1f : 0f);
        _properties.SetFloat2("_CloudWindMoved", held ? s.Wind - state.Wind : Float2.Zero);
        context.Fullscreen(_temporal, accumulated, _properties);
        (state.Epoch, state.Wind) = (s.Epoch, s.Wind);
        return accumulated.ColorTextures[0];
    }

    private void SetLighting(CloudSettings s)
    {
        _properties.SetFloat4("_CloudLighting", s.Lighting);
        _properties.SetFloat3("_CloudSunColor", s.SunColor);
        _properties.SetFloat3("_CloudSkyLight", s.SkyLight);
        _properties.SetFloat4("_CloudAlbedo", s.Albedo);
        _properties.SetFloat2("_CloudScatter", s.Scatter);
        _properties.SetFloat("_CloudAmbientSaturation", s.AmbientSaturation);
        _properties.SetFloat("_CloudHorizonFade", s.HorizonFade);
        _properties.SetFloat4("_CloudHigh", s.High);
        _properties.SetFloat2("_CloudHighWind", s.HighWind);
    }

    // The map covers the clouds' reach around the view's camera, on a lattice the wind carries along. Each texel holds its
    // lattice point for as long as the point is in the square, wrapped around the map, so only the texels the square
    // newly covers are drawn
    private void DrawMaps(FeatureContext context, ViewState state, Double3 camera)
    {
        CloudSettings s = _settings;
        float span = s.Reach * 2f;
        float texel = span / MapResolution;
        long firstX = (long)Math.Round((camera.X - s.WindX) / texel) - MapResolution / 2;
        long firstZ = (long)Math.Round((camera.Z - s.WindZ) / texel) - MapResolution / 2;
        Float2 min = new((float)(s.WindX + firstX * (double)texel), (float)(s.WindZ + firstZ * (double)texel));
        _properties.SetFloat4("_CloudCoverageRect", new Float4(min.X, min.Y, 1f / span, span));

        RenderTexture maps = context.Get(state.Maps);
        RenderTexture? previous = context.History(state.Maps);
        bool held = previous != null && context.HasPreviousFrame && state.MapEpoch == s.Epoch && state.MapTexel == texel && state.MapFormation == s.Heights.X;
        _properties.SetTexture("_CloudMapsPrevious", previous?.ColorTextures[0] ?? s.Noise2D!, _pointClamp);
        _properties.SetFloat4("_CloudMapWindow", new Float4(firstX, firstZ, state.MapFirstX, state.MapFirstZ));
        _properties.SetFloat2("_CloudMapState", new Float2(held ? 1f : 0f, texel));
        _properties.SetFloat("_CloudMapSize", MapResolution);
        context.Fullscreen(_maps, maps, _properties, shared: true);
        (state.MapFirstX, state.MapFirstZ, state.MapEpoch, state.MapTexel, state.MapFormation) = (firstX, firstZ, s.Epoch, texel, s.Heights.X);

        _properties.SetTexture("_CloudCoverage", maps.ColorTextures[0], _linearRepeat);
    }

    private void DisposeShadows()
    {
        _shadows?.Map.Dispose();
        _shadows?.Blur.Dispose();
    }

    public void Dispose()
    {
        DisposeShadows();
        _shadowData.Dispose();
    }
}

/// <summary>The cloud shadow as shaders read it, 336 bytes, matching Graphite_CloudShadowRecord.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CloudShadowRecord
{
    public const uint Size = 336;

    public Float4 Cascade0, Cascade1, Cascade2, Cascade3;
    public Float4 Heights;
    public Float4 WindA, WindB;
    public Float4 Right0, Right1, Right2, Right3;
    public Float4 Up0, Up1, Up2, Up3;
    public Float4 ToSun0, ToSun1, ToSun2, ToSun3;
    public Float4 Params, Scatter;
}

/// <summary>The built in noise of <see cref="VolumetricCloudsFeature"/>: the high layer's 2D noise and the 3D noise's channels apart, each in the smallest format holding it.</summary>
internal sealed class CloudNoise : IDisposable
{
    public const uint VolumeSize = 64;
    public const uint CurlSize = 32;

    public CloudNoise(GraphicsDevice device)
    {
        Flat = RendererResources.LoadData(device, "CloudNoise.bin", 256, 256, mipmapped: true);
        byte[] rgba = RendererResources.ReadData("CloudNoise3D.bin", VolumeSize * VolumeSize * VolumeSize * 4);
        Edges = Volume(device, "Cloud Edges", PixelFormat.R8_UNorm, Repack(rgba, 1, (texel, i, _) => texel[i]));
        Slow = Volume(device, "Cloud Slow", PixelFormat.R8_G8_UNorm, Repack(rgba, 2, (texel, i, channel) => texel[i + 2 + channel]));
        Lobes = Volume(device, "Cloud Lobes", PixelFormat.R8_UNorm, Repack(rgba, 1, (texel, i, _) => (byte)((texel[i + 2] + texel[i + 3] + 1) / 2)));
        Curl = Volume(device, "Cloud Curl", PixelFormat.R8_G8_B8_A8_UNorm, RendererResources.ReadData("CloudCurl.bin", CurlSize * CurlSize * CurlSize * 4), CurlSize);
    }

    /// <summary>Tiling 2D noise, broad gradient noise in red, 256 square.</summary>
    public Texture Flat { get; }

    /// <summary>Tiling 3D noise, 64 cubed: cell edges, smooth noise and cell centres, and those two averaged.</summary>
    public Texture Edges { get; }
    public Texture Slow { get; }
    public Texture Lobes { get; }

    /// <summary>Tiling 3D curl noise of unit length, 32 cubed, its xyz from -1 to 1 stored 0 to 1.</summary>
    public Texture Curl { get; }

    private static Texture Volume(GraphicsDevice device, string name, PixelFormat format, byte[] texels, uint size = VolumeSize)
    {
        uint mips = (uint)Math.Log2(size) + 1;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture3D(size, size, size, mips, format, TextureUsage.Sampled));
        texture.Name = name;
        device.UpdateTexture<byte>(texture, texels, TextureRegion.Whole(texture, 0, 0));
        device.Record(cmd => cmd.GenerateMipmaps(texture)).Wait();
        return texture;
    }

    // RGBA8 texels rebuilt with channels per texel, each picked from the texel's RGBA bytes starting at the given index
    private static byte[] Repack(byte[] rgba, int channels, Func<byte[], int, int, byte> pick)
    {
        byte[] result = new byte[rgba.Length / 4 * channels];
        for (int texel = 0; texel < rgba.Length / 4; texel++)
            for (int channel = 0; channel < channels; channel++)
                result[texel * channels + channel] = pick(rgba, texel * 4, channel);
        return result;
    }

    public void Dispose()
    {
        Flat.Dispose();
        Edges.Dispose();
        Slow.Dispose();
        Lobes.Dispose();
        Curl.Dispose();
    }
}
