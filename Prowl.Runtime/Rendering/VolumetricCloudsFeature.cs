// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Scene = Prowl.Runtime.Resources.Scene;
using Texture2D = Prowl.Runtime.Resources.Texture2D;
using Texture3D = Prowl.Runtime.Resources.Texture3D;
using DefaultTexture = Prowl.Runtime.Resources.DefaultTexture;
using DefaultTexture3D = Prowl.Runtime.Resources.DefaultTexture3D;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Draws a scene's <see cref="VolumetricClouds"/> over its opaques and sky, under the transparents, as a feature of the
/// scene's world.
/// <para/>
/// Each view draws a weather map around its camera holding how far each column's cloud rises from the layer's floor and
/// hangs from its ceiling, and the edges between cells of 3D noise carve lobes, billows and puffs into the clouds.
/// The clouds and the high layer are marched front to back up to the scene's depth and scaled up over the scene.
/// </summary>
internal sealed class VolumetricCloudsFeature : RenderFeature, IDisposable
{
    private const int MapResolution = 512;
    private const int TemporalFrames = 4;
    private const int ShadowCascades = 4;

    internal sealed class CloudSettings
    {
        public Float4 Shape, Pattern, Lighting, Albedo;
        public Float4 Heights;
        public Float2 Gap, Lobes;
        public Double3? ShadowCentre;
        public Float2 Coverages;
        public Float2 Wind, Scatter;
        public double WindX, WindZ;
        public Float3 ToSun, SunColor, SkyLight;
        public float AmbientSaturation, HorizonFade, Reach, DetailScale;
        public int Scale, ShadowResolution;
        public bool Temporal;
        public Texture? Noise2D, Edges, Puffs, Slow, LobeNoise;
        public uint ErosionSize;
        public int Epoch;
        public Float4 High;
        public Float2 HighWind;

        public void CopyTo(CloudSettings other)
        {
            (other.Shape, other.Pattern, other.Lighting, other.Albedo, other.Heights, other.Gap, other.Coverages, other.Lobes, other.ShadowCentre) = (Shape, Pattern, Lighting, Albedo, Heights, Gap, Coverages, Lobes, ShadowCentre);
            (other.Wind, other.Scatter, other.WindX, other.WindZ) = (Wind, Scatter, WindX, WindZ);
            (other.ToSun, other.SunColor, other.SkyLight) = (ToSun, SunColor, SkyLight);
            (other.AmbientSaturation, other.HorizonFade, other.Reach, other.DetailScale) = (AmbientSaturation, HorizonFade, Reach, DetailScale);
            (other.Scale, other.Temporal, other.ShadowResolution) = (Scale, Temporal, ShadowResolution);
            (other.Noise2D, other.Edges, other.Puffs, other.Slow, other.LobeNoise, other.ErosionSize, other.Epoch) = (Noise2D, Edges, Puffs, Slow, LobeNoise, ErosionSize, Epoch);
            (other.High, other.HighWind) = (High, HighWind);
        }
    }

    private sealed class ViewState
    {
        public TextureHandle Maps, Target, Accumulated, ShadowMap;
        public bool Temporal, UsesShadows;
        public int Frame, Epoch;
        public Float2 Wind;
        public int MapEpoch;
        public float MapFormation;
        public long MapFirstX, MapFirstZ;
        public float MapTexel;
    }

    // The world's cloud shadow map, which every view reads: cascades side by side, each square to the sun around where a
    // camera was when it was drawn, the smallest first. The first camera drawn each frame draws the next one again
    private sealed class Shadows(RenderTexture map, int resolution)
    {
        public readonly RenderTexture Map = map;
        public readonly int Resolution = resolution;
        public readonly Float4[] Cascades = new Float4[ShadowCascades];
        public Float4 Heights;
        public readonly Float2[] Winds = new Float2[ShadowCascades];
        public int Next, Frame, Epoch = -1;
    }


    private readonly FullscreenProgram _maps, _march, _composite, _temporal, _shadow;
    private readonly GraphicsDevice _device;
    private readonly Sampler _linearClamp, _pointClamp, _linearRepeat;
    private readonly PropertySet _properties = new();
    private readonly CloudSettings _settings = new();
    private int _scale = 1;
    private Shadows? _shadows;
    private bool _shadowsDrawn;

    // The settings the history was started with, so changing them enough starts it again
    private readonly List<float> _values = [], _tolerances = [];
    private float[]? _baseline;

    /// <summary>The settings copied at the next Extract. Host thread.</summary>
    internal readonly CloudSettings Pending = new();

    public VolumetricCloudsFeature(Renderer renderer) : base(RenderSlot.Atmosphere, runsOn: ViewKinds.Cameras | ViewKinds.ReflectionProbe)
    {
        BlendStateDescription two = new(default, BlendAttachmentDescription.OverrideBlend, BlendAttachmentDescription.OverrideBlend);
        _maps = Program(renderer, "CLOUD_MAPS", "Maps");
        _march = Program(renderer, "CLOUD_MARCH", "March", two);
        _composite = renderer.CreateFullscreenProgram(Clouds, "Prowl/VolumetricCloudsComposite");
        _temporal = Program(renderer, "CLOUD_TEMPORAL", "Temporal");
        _shadow = Program(renderer, "CLOUD_SHADOW", "Shadow");
        _device = renderer.Device;

        SamplerDescription clamp = SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        };
        _linearClamp = Graphics.GetSampler(clamp);
        _pointClamp = Graphics.GetSampler(clamp with { MinFilter = Graphite.FilterMode.Point, MagFilter = Graphite.FilterMode.Point, MipFilter = Graphite.FilterMode.Point });
        _linearRepeat = Graphics.GetSampler(SamplerDescription.Linear);
    }

    internal const string Clouds = "#include \"Prowl/VolumetricClouds.glsl\"\n";

    private static FullscreenProgram Program(Renderer renderer, string define, string name, BlendStateDescription? blend = null)
        => renderer.CreateFullscreenProgram(Clouds, $"Prowl/VolumetricClouds{name}", source: false, blend, variant: $"#define {define} 1\n");

    /// <summary>Copies a component's settings, the world's sun and the scene's sky light into <see cref="Pending"/>. Main thread.</summary>
    internal bool Capture(VolumetricClouds clouds, RenderWorld world, Scene scene)
    {
        Texture2D noise = clouds.Noise.IsValid() ? clouds.Noise : Texture2D.LoadDefault(DefaultTexture.CloudNoise);
        Texture3D edges = Texture3D.LoadDefault(DefaultTexture3D.CloudEdges);
        CloudSettings s = Pending;
        s.Noise2D = noise.GpuTexture;
        s.Edges = edges.GpuTexture;
        s.Puffs = Texture3D.LoadDefault(DefaultTexture3D.CloudPuffs).GpuTexture;
        s.Slow = Texture3D.LoadDefault(DefaultTexture3D.CloudSlow).GpuTexture;
        s.LobeNoise = Texture3D.LoadDefault(DefaultTexture3D.CloudLobes).GpuTexture;
        if (s.Noise2D == null || s.Edges == null || s.Puffs == null || s.Slow == null || s.LobeNoise == null) return false;
        s.ErosionSize = edges.Width;

        // The march covers up to the ceiling and the highest the top clouds dome above it, half their depth with lobes
        float thickness = MathF.Max(clouds.Thickness, 1f);
        float gap = MathF.Max(clouds.GapMultiplier, 0f);
        float above = 0.5f * Math.Clamp(clouds.TopStrength, 0f, 1f) * thickness * (1f + Math.Clamp(clouds.Lobes, 0f, 1f));
        float span = MathF.Max(thickness * gap + above, 1f);
        s.Shape = new Float4(clouds.Altitude, span, 1f / span, MathF.Max(clouds.PlanetRadius, 1000f));
        s.Gap = new Float2(thickness, gap);
        float coverage = Math.Clamp(clouds.Coverage, 0f, 1f);
        // The shadow map follows the scene's main camera, wherever the views that draw it are
        Camera? main = Camera.Main;
        s.ShadowCentre = main.IsValid() ? new Double3(main!.Transform.Position.X, main.Transform.Position.Y, main.Transform.Position.Z) : null;
        s.Lobes = new Float2(Math.Clamp(clouds.Lobes, 0f, 1f), 1f / MathF.Max(clouds.LobeScale, 1f));
        s.Coverages = new Float2(Math.Clamp(coverage * clouds.BottomCoverage, 0f, 1f), Math.Clamp(coverage * clouds.TopCoverage, 0f, 1f));
        s.Heights = new Float4(clouds.FormationOffset, Math.Clamp(clouds.BottomStrength, 0f, 1f), Math.Clamp(clouds.TopStrength, 0f, 1f), 300f);
        s.Pattern = new Float4(1f / MathF.Max(clouds.PatternScale, 1f), Math.Clamp(clouds.Coverage, 0f, 1f), 1f / MathF.Max(clouds.DetailScale, 1f), Math.Clamp(clouds.Erosion, 0f, 1f));
        s.Lighting = new Float4(MathF.Max(clouds.Density, 0f) * 0.004f, MathF.Max(clouds.SunBrightness, 0f), MathF.Max(clouds.AmbientBrightness, 0f), Math.Clamp(clouds.SilverLining, 0f, 1f));
        s.Albedo = new Float4(clouds.Albedo.R, clouds.Albedo.G, clouds.Albedo.B, clouds.Albedo.A);
        s.Scatter = new Float2(Math.Clamp(clouds.MultipleScattering, 0f, 1f), Math.Clamp(clouds.MultipleScatteringOcclusion, 0f, 1f));
        s.Wind = clouds.WindOffset;
        (s.WindX, s.WindZ) = (clouds.WindX, clouds.WindZ);
        s.AmbientSaturation = Math.Clamp(clouds.AmbientSaturation, 0f, 1f);
        s.HorizonFade = 1f / MathF.Max(clouds.HorizonFade, 100f);
        s.Reach = MathF.Max(clouds.HorizonFade, 100f) * 2f;
        s.DetailScale = MathF.Max(clouds.DetailScale, 1f);
        s.Scale = Math.Max((int)clouds.Resolution, 1);
        s.Temporal = clouds.Temporal;
        s.ShadowResolution = Math.Clamp(clouds.ShadowResolution, 64, 2048);

        // Toward the scene's sun, straight up without one so the shaders never normalize a zero vector
        s.ToSun = Float3.UnitY;
        s.SunColor = Float3.Zero;
        LightHandle sun = world.Environment.Sun;
        if (sun.IsValid && world.IsAlive(sun))
        {
            LightDesc light = world.GetLight(sun);
            float length = Float3.Length(light.Direction);
            if (length > 1e-6f && float.IsFinite(length)) s.ToSun = -light.Direction / length;
            s.SunColor = light.Color * (light.Intensity * s.Lighting.Y);
        }

        // Sky light from above, the captured sky's when there is one, else the scene's ambient light reaching an upward surface
        s.SkyLight = world.SkyAmbientVersion > 0 ? world.SkyAmbient.Evaluate(Float3.UnitY) : scene.Rendering.Ambient(Float3.UnitY);

        // The high layer drifts twice as fast as the clouds below it, as winds are stronger higher up
        s.High = new Float4(clouds.HighLayerAltitude, Math.Clamp(clouds.HighLayerOpacity, 0f, 1f), 1f / MathF.Max(clouds.HighLayerScale, 1f), clouds.HighLayer ? 1f : 0f);
        s.HighWind = clouds.WindOffset * 2f;

        if (SettingsChanged(clouds, s)) s.Epoch++;
        return true;
    }

    private void Add(float value, float tolerance = 0.01f)
    {
        _values.Add(value);
        _tolerances.Add(tolerance);
    }

    // Whether the settings that shape the clouds moved more than about one percent since the history was started, or the
    // sun more than about ten. The sun moves slowly through a day and the history follows small changes in light by itself
    private bool SettingsChanged(VolumetricClouds clouds, CloudSettings s)
    {
        _values.Clear();
        _tolerances.Clear();
        Add(clouds.Altitude); Add(clouds.Thickness); Add(clouds.TopStrength); Add(clouds.BottomStrength); Add(clouds.GapMultiplier); Add(clouds.BottomCoverage); Add(clouds.TopCoverage); Add(clouds.Lobes); Add(clouds.LobeScale); Add(clouds.Coverage); Add(clouds.Density);
        Add(clouds.PatternScale); Add(clouds.Erosion); Add(clouds.DetailScale); Add(clouds.Albedo.R);
        Add(clouds.Albedo.G); Add(clouds.Albedo.B);
        Add(clouds.SunBrightness); Add(clouds.AmbientBrightness); Add(clouds.AmbientSaturation);
        Add(clouds.SilverLining); Add(clouds.MultipleScattering); Add(clouds.MultipleScatteringOcclusion); Add(clouds.HorizonFade);
        Add(clouds.PlanetRadius);
        Add(clouds.Noise.IsValid() ? clouds.Noise.InstanceID : 0, 0f);
        Add(s.ToSun.X, 0.1f); Add(s.ToSun.Y, 0.1f); Add(s.ToSun.Z, 0.1f);
        Add(s.SunColor.X, 0.1f); Add(s.SunColor.Y, 0.1f); Add(s.SunColor.Z, 0.1f);

        Add(clouds.HighLayer ? 1f : 0f); Add(clouds.HighLayerAltitude); Add(clouds.HighLayerOpacity); Add(clouds.HighLayerScale);

        int count = _values.Count;
        bool changed = _baseline == null || _baseline.Length != count;
        for (int i = 0; !changed && i < count; i++)
        {
            float a = _values[i], b = _baseline![i];
            changed = MathF.Abs(a - b) > _tolerances[i] * MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
        }
        if (changed) _baseline = [.. _values];
        return changed;
    }

    public override void Extract()
    {
        Pending.CopyTo(_settings);
        _scale = _settings.Scale;
        _shadowsDrawn = false;
    }

    public override bool IsActive(in FeatureView view)
        => _settings.Noise2D is { IsDisposed: false } && _settings.Edges is { IsDisposed: false } && _settings.Puffs is { IsDisposed: false }
            && _settings.Slow is { IsDisposed: false } && _settings.LobeNoise is { IsDisposed: false };

    private static GraphTextureDesc Layered(in FeatureView view, int width, int height, PixelFormat format)
        => GraphTextureDesc.Sized(width, height, format) with { ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0 };

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();
        TextureState usage = TextureState.Attachment | TextureState.Sampled;
        state.Maps = builder.History("Maps", GraphTextureDesc.Sized(MapResolution, MapResolution, PixelFormat.R16_G16_Float), usage: usage);

        int width = Math.Max(1, (int)view.Width / _scale), height = Math.Max(1, (int)view.Height / _scale);
        uint layers = view.LayerCount > 1 ? (uint)view.LayerCount : 0;

        state.Target = builder.Temporary("Target", GraphTextureDesc.Sized(width, height, false, PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R32_Float) with { ArrayLayers = layers });

        // A probe capture turns to new faces every render, so there is nothing to blend over
        state.Temporal = _settings.Temporal && view.Kind != ViewKind.ReflectionProbe;
        if (state.Temporal)
            state.Accumulated = builder.History("Accumulated", Layered(view, width, height, PixelFormat.R16_G16_B16_A16_Float), usage: usage);

        // Cameras draw the shadow map, so a probe sees the clouds unshadowed until a camera has drawn it
        state.UsesShadows = view.Kind != ViewKind.ReflectionProbe || _shadows is { Epoch: >= 0 };
        if (state.UsesShadows)
        {
            int resolution = _settings.ShadowResolution;
            if (_shadows?.Resolution != resolution)
            {
                _shadows?.Map.Dispose();
                RenderTexture map = _device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription((uint)(resolution * ShadowCascades), (uint)resolution, PixelFormat.R16_G16_B16_A16_Float, false));
                map.Name = "Cloud Shadows";
                _shadows = new Shadows(map, resolution);
            }
            state.ShadowMap = builder.Import(_shadows.Map);
        }
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        CloudSettings s = _settings;
        Double3 camera = context.View.Position;

        _properties.SetTexture("_CloudNoise2D", s.Noise2D!, _linearRepeat);
        _properties.SetTexture("_CloudEdges", s.Edges!, _linearRepeat);
        _properties.SetTexture("_CloudPuffs", s.Puffs!, _linearRepeat);
        _properties.SetTexture("_CloudSlow", s.Slow!, _linearRepeat);
        _properties.SetTexture("_CloudLobeNoise", s.LobeNoise!, _linearRepeat);
        _properties.SetFloat("_CloudErosionSize", s.ErosionSize);
        _properties.SetFloat4("_CloudShape", s.Shape);
        _properties.SetFloat4("_CloudPattern", s.Pattern);
        _properties.SetFloat2("_CloudWind", s.Wind);
        _properties.SetFloat3("_CloudToSun", s.ToSun);
        _properties.SetFloat4("_CloudHeights", s.Heights);
        _properties.SetFloat2("_CloudGap", s.Gap);
        _properties.SetFloat2("_CloudLobes", s.Lobes);
        _properties.SetFloat2("_CloudCoverages", s.Coverages);
        _properties.SetFloat("_CloudReach", s.Reach);
        DrawMaps(context, state, camera);
        SetLighting(s);

        RenderTexture target = context.Get(state.Target);
        Float2 low = new(target.ColorTextures[0].Width, target.ColorTextures[0].Height);
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, _pointClamp);
        _properties.SetFloat2("_CloudFullResolution", new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat2("_CloudLowResolution", low);

        _properties.SetFloat3("_CloudCamera", new Float3((float)camera.X, (float)camera.Y, (float)camera.Z));
        DrawShadows(context, state, camera);
        _properties.SetFloat("_CloudStepScale", state.Temporal ? TemporalFrames : 1f);
        _properties.SetFloat("_CloudFrame", state.Temporal ? state.Frame++ % 64 : 0f);
        context.Fullscreen(_march, target, _properties);

        Texture clouds = target.ColorTextures[0];
        if (state.Temporal)
            clouds = Accumulate(context, state, target);
        _properties.SetTexture("_CloudTex", clouds, _pointClamp);
        _properties.SetTexture("_CloudTexDistance", target.ColorTextures[1], _pointClamp);
        context.Fullscreen(_composite, _properties);
    }

    // Every other frame the first camera draws the next cascade of the shadow map, every cascade when the settings or sun
    // changed, around the scene's main camera, or around itself without one. Binds them all for the march. Each cascade reaches a third as far as the next, the last as
    // far as the clouds
    private void DrawShadows(FeatureContext context, ViewState state, Double3 camera)
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

        Float3 axis = MathF.Abs(s.ToSun.Y) > 0.99f ? Float3.UnitX : Float3.UnitY;
        Float3 right = Float3.Normalize(Float3.Cross(axis, s.ToSun));
        Float3 up = Float3.Cross(s.ToSun, right);
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
        bool draws = !_shadowsDrawn && context.View.Kind != ViewKind.ReflectionProbe && (shadows.Epoch != s.Epoch || ++shadows.Frame % 2 == 0);
        bool all = shadows.Epoch != s.Epoch;
        for (int c = 0; c < ShadowCascades && draws; c++)
        {
            if (!all && c != shadows.Next)
                continue;
            float half = s.Reach / MathF.Pow(3f, ShadowCascades - 1 - c);
            float height = half * sunHigh + (clouds + half * half / (2f * s.Shape.W)) * sunLow;
            Double3 centre = SnapToTexels(s.ShadowCentre ?? camera, s.Wind, right, up, 2f * half / shadows.Resolution, 2f * height / shadows.Resolution);
            shadows.Cascades[c] = new Float4((float)centre.X, (float)centre.Y, (float)centre.Z, half);
            shadows.Heights[c] = height;
            shadows.Winds[c] = s.Wind;
            _properties.SetFloat4("_CloudShadowBuild", shadows.Cascades[c]);
            _properties.SetFloat("_CloudShadowBuildUp", height);
            context.Fullscreen(_shadow, map, c * shadows.Resolution, 0, shadows.Resolution, shadows.Resolution, _properties, shared: true);
        }
        if (draws)
        {
            shadows.Next = (shadows.Next + 1) % ShadowCascades;
            shadows.Epoch = s.Epoch;
            _shadowsDrawn = true;
        }

        for (int c = 0; c < ShadowCascades; c++)
            _properties.SetFloat4(s_shadowCascade[c], shadows.Cascades[c]);
        _properties.SetFloat4("_CloudShadowHeights", shadows.Heights);
        Float2 w0 = s.Wind - shadows.Winds[0], w1 = s.Wind - shadows.Winds[1], w2 = s.Wind - shadows.Winds[2], w3 = s.Wind - shadows.Winds[3];
        _properties.SetFloat4("_CloudShadowWindA", new Float4(w0.X, w0.Y, w1.X, w1.Y));
        _properties.SetFloat4("_CloudShadowWindB", new Float4(w2.X, w2.Y, w3.X, w3.Y));
        _properties.SetTexture("_CloudShadowMap", map.ColorTextures[0], _linearClamp);
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

    public void Dispose()
    {
        _maps.Dispose();
        _march.Dispose();
        _composite.Dispose();
        _temporal.Dispose();
        _shadow.Dispose();
        _shadows?.Map.Dispose();
    }
}
