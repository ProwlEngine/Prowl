// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Numerics;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>The skies Graphite ships, each a shader a sky material can use.</summary>
public enum BuiltInSky
{
    /// <summary>Scattered sunlight lit by the scene's sun: sun size and glow, exposure, tint and ground colour.</summary>
    Procedural,

    /// <summary>Two colours blended from the ground up.</summary>
    Gradient,

    /// <summary>A cubemap texture, tinted, exposed and turned about the up axis.</summary>
    Cubemap,

    /// <summary>An equirectangular panorama texture, tinted, exposed and turned about the up axis.</summary>
    Panorama,

    /// <summary>Six separate images, one per side.</summary>
    SixSided,
}

/// <summary>How many pixels the clouds are drawn at before they are scaled up over the scene.</summary>
public enum CloudResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4,
}

/// <summary>
/// A scene's sky, ambient light and fog. The scene's rendering mirrors it onto its world's environment every frame, one
/// field to one, so what is set here is what Graphite draws.
/// </summary>
public sealed class SceneEnvironment
{
    /// <summary>
    /// The sky drawn behind everything and captured into the sky probe for reflections and sky ambient: any material
    /// whose shader has a Sky pass, usually one of <see cref="BuiltInSky"/>. Null shows <see cref="SkyColor"/>.
    /// </summary>
    public Material? SkyMaterial = DefaultSky();

    /// <summary>The background, and what the sky probe sees, where no sky material draws.</summary>
    public Color SkyColor = new(0.2f, 0.3f, 0.5f, 1f);

    public AmbientSettings Ambient = new();
    public FogSettings Fog = new();
    public VolumetricFogSettings VolumetricFog = new();
    public VolumetricCloudsSettings VolumetricClouds = new();
    public SkyCaptureSettings SkyCapture = new();

    public struct AmbientSettings
    {
        /// <summary>Uniform lights every surface with Color, Hemisphere blends GroundColor up to SkyColor, Sky takes the sky's own light.</summary>
        public AmbientMode Mode = AmbientMode.Sky;

        /// <summary>Nits the colours are scaled to for Uniform and Hemisphere, a multiplier of the sky's own light for Sky.</summary>
        public float Intensity = 1f;
        public Color Color = new(0.43f, 0.55f, 0.65f, 1f);
        public Color SkyColor = new(0.3f, 0.3f, 0.4f, 1f);
        public Color GroundColor = new(0.2f, 0.2f, 0.2f, 1f);

        public AmbientSettings() { }
    }

    public struct FogSettings
    {
        public FogMode Mode = FogMode.ExponentialSquared;
        public Color Color = new(0.5f, 0.5f, 0.5f, 1f);

        /// <summary>Nits the colour is scaled to.</summary>
        public float Intensity = 10000f;
        public float Density = 0.01f;

        /// <summary>Where linear fog starts and where it is full.</summary>
        public float Start = 20f;
        public float End = 100f;

        /// <summary>Colours the fog with the sky behind it, from the sky probe, instead of Color.</summary>
        public bool UseSky = true;

        public FogSettings() { }
    }

    /// <summary>
    /// Fog in the air lit by the scene's lights through their shadows, dimming what lies behind it, thinning with height
    /// above <see cref="BaseHeight"/>. <see cref="FogVolume"/>s add to it. Every fogged material reads it.
    /// </summary>
    public struct VolumetricFogSettings
    {
        public bool Enabled = false;

        /// <summary>How much of the light it takes out per metre at <see cref="BaseHeight"/>.</summary>
        public float Density = 0.02f;

        /// <summary>The height it is at <see cref="Density"/>, and the same below.</summary>
        public float BaseHeight = 0f;

        /// <summary>How fast it thins with height above <see cref="BaseHeight"/>, per metre.</summary>
        public float HeightFalloff = 0.1f;

        /// <summary>The share of the light it takes out that it scatters rather than absorbs.</summary>
        public Color Albedo = new(1f, 1f, 1f, 1f);

        /// <summary>0 scatters evenly, toward 1 forward, glowing around lights seen through it.</summary>
        public float Anisotropy = 0.6f;

        /// <summary>Scales the ambient light it scatters.</summary>
        public float AmbientScale = 1f;

        /// <summary>How far from the camera it reaches in metres.</summary>
        public float Distance = 128f;

        /// <summary>The depth slices it is lit in, more for sharper shafts far away.</summary>
        public int Slices = 128;

        /// <summary>How much of last frame's light each frame keeps, smoothing it.</summary>
        public float TemporalBlend = 0.9f;

        public VolumetricFogSettings() { }
    }

    /// <summary>
    /// Volumetric clouds over the whole scene, with an optional faint high layer above them, ray marched through a coverage
    /// map shaped by a height profile and eroded by 3D noise. They shade the sun through a shadow map drawn once a frame,
    /// so the ground, the volumetric fog and the water under them are shaded too, and reflection probes see them.
    /// </summary>
    public sealed class VolumetricCloudsSettings
    {
        public bool Enabled = false;

        /// <summary>Height of the cloud layer's floor above zero, in meters.</summary>
        [Header("Shape")]
        public float Altitude = 1500f;

        /// <summary>How tall the cloud layer is in meters, from its floor to its ceiling before the gap moves it.</summary>
        public float Thickness = 4000f;

        /// <summary>How far through the layer the tallest clouds rise in domes from flat bases on its floor.</summary>
        [Range(0f, 1f)] public float BottomStrength = 0.45f;

        /// <summary>How far through the layer the top clouds dome down from its ceiling. They dome up above it by half as far.</summary>
        [Range(0f, 1f)] public float TopStrength = 0.2f;

        /// <summary>How far up the top clouds dome above the ceiling at most, as a share of their depth below it.</summary>
        [Range(0f, 2f)] public float DomeStrength = 2f;

        /// <summary>How far up the ceiling the top clouds hang from is, in layer thicknesses, so how far apart the two are.</summary>
        public float GapMultiplier = 0.4f;

        /// <summary>Scales the coverage of the clouds rising from the floor.</summary>
        public float BottomCoverage = 1.1f;

        /// <summary>Scales the coverage of the clouds hanging from the ceiling.</summary>
        public float TopCoverage = 0.9f;

        /// <summary>How far round lobes push each cloud's domed side in and out, as a share of its height.</summary>
        [Range(0f, 1f)] public float Lobes = 0.25f;

        /// <summary>Size of the lobes in meters.</summary>
        public float LobeScale = 2000f;

        /// <summary>How quickly the clouds change shape, in turns of the noise they drift through per second.</summary>
        public float FormationChangeSpeed = 0.002f;

        /// <summary>How much of the sky is covered.</summary>
        [Range(0f, 1f)] public float Coverage = 0.69f;

        /// <summary>How quickly light dims inside the clouds. Higher looks heavier and darker underneath.</summary>
        public float Density = 1f;

        /// <summary>Size of the weather pattern in meters, how far apart clusters of clouds are.</summary>
        public float PatternScale = 12000f;

        /// <summary>How much the detail noise eats into cloud edges.</summary>
        [Range(0f, 1f)] public float Erosion = 1f;

        /// <summary>Size of the detail noise in meters.</summary>
        public float DetailScale = 600f;

        /// <summary>How far curl noise swirls the clouds into wisps, as a share of the swirls' size.</summary>
        [Range(0f, 1f)] public float Wisps = 0.5f;

        /// <summary>Size of the wisps' swirls in meters.</summary>
        public float WispScale = 140f;

        /// <summary>Wind speed in meters a second.</summary>
        [Header("Wind")]
        public float WindSpeed = 15f;

        /// <summary>Compass direction the wind blows toward, in degrees around Y.</summary>
        public float WindDirection = 30f;

        [Header("Lighting")]
        public Color Albedo = new(1f, 1f, 1f, 1f);

        /// <summary>
        /// Scales the sunlight the clouds receive. Above 1 makes up for the light that scatters many times inside real
        /// clouds, which the march only roughly follows.
        /// </summary>
        public float SunBrightness = 2f;

        /// <summary>Scales the sky light the clouds receive.</summary>
        public float AmbientBrightness = 2f;

        /// <summary>How much of the sky's color the sky light keeps. Lower greys it, so clouds do not turn blue.</summary>
        [Range(0f, 1f)] public float AmbientSaturation = 1f;

        /// <summary>How bright the rim around the sun gets when looking through thin cloud toward it.</summary>
        [Range(0f, 1f)] public float SilverLining = 0.6f;

        /// <summary>
        /// How much of the light that scatters more than once comes back out. Higher fills the shaded parts of a cloud
        /// with light, lower leaves them darker.
        /// </summary>
        [Range(0f, 1f)] public float MultipleScattering = 0.4f;

        /// <summary>
        /// How much cloud light that scattered more than once still has to cross. Lower lets it reach deeper, so thick
        /// cloud stays brighter inside.
        /// </summary>
        [Range(0f, 1f)] public float MultipleScatteringOcclusion = 0.2f;

        /// <summary>
        /// Around this distance in meters the clouds fade into the sky behind them, little before it and quickly past it.
        /// They are drawn out to twice this, where nothing of them is left to see.
        /// </summary>
        public float HorizonFade = 32000f;

        /// <summary>Resolution the clouds are built at before they are scaled up over the scene.</summary>
        [Header("Quality")]
        public CloudResolution Resolution = CloudResolution.Full;

        /// <summary>
        /// Marches in steps four times as long from a start that moves along the ray each frame, and blends each frame over
        /// the last, so four frames add up to the full march. Much cheaper, with a faint trail behind fast moving clouds.
        /// </summary>
        public bool Temporal = true;

        /// <summary>
        /// Texels along each side of one cascade of the shadow map the clouds are lit from, cascades around the camera drawn
        /// one a frame.
        /// </summary>
        public int ShadowResolution = 512;

        /// <summary>Radius of the planet in meters, which curves the cloud layers down toward the horizon.</summary>
        public float PlanetRadius = 6371000f;

        /// <summary>Tiling noise the high layer is built from, its red channel. Empty uses the built in one.</summary>
        [Header("Noise")]
        public Texture2D? Noise;

        /// <summary>Draws a faint flat layer of noise high above the clouds, like thin cirrus.</summary>
        [Header("High Layer")]
        public bool HighLayer = false;

        /// <summary>Height of the high layer above zero, in meters.</summary>
        public float HighLayerAltitude = 8000f;

        /// <summary>How opaque the high layer is where it is thickest.</summary>
        [Range(0f, 1f)] public float HighLayerOpacity = 0.35f;

        /// <summary>Size of the high layer's pattern in meters.</summary>
        public float HighLayerScale = 20000f;

        /// <summary>Copies every setting onto the world's clouds and carries them on with the wind for a frame's time.</summary>
        internal void ApplyTo(Graphite.Rendering.VolumetricCloudsFeature feature, float deltaTime)
        {
            (feature.Altitude, feature.Thickness, feature.BottomStrength, feature.TopStrength, feature.GapMultiplier) = (Altitude, Thickness, BottomStrength, TopStrength, GapMultiplier);
            feature.DomeStrength = DomeStrength;
            (feature.BottomCoverage, feature.TopCoverage, feature.Lobes, feature.LobeScale, feature.Coverage, feature.Density) = (BottomCoverage, TopCoverage, Lobes, LobeScale, Coverage, Density);
            (feature.PatternScale, feature.Erosion, feature.DetailScale) = (PatternScale, Erosion, DetailScale);
            (feature.Wisps, feature.WispScale) = (Wisps, WispScale);
            feature.Albedo = Albedo.ToFloat4();
            (feature.SunBrightness, feature.AmbientBrightness, feature.AmbientSaturation, feature.SilverLining) = (SunBrightness, AmbientBrightness, AmbientSaturation, SilverLining);
            (feature.MultipleScattering, feature.MultipleScatteringOcclusion, feature.HorizonFade) = (MultipleScattering, MultipleScatteringOcclusion, HorizonFade);
            feature.Resolution = Resolution switch
            {
                CloudResolution.Half => Graphite.Rendering.EffectResolution.Half,
                CloudResolution.Quarter => Graphite.Rendering.EffectResolution.Quarter,
                _ => Graphite.Rendering.EffectResolution.Full,
            };
            (feature.Temporal, feature.ShadowResolution, feature.PlanetRadius) = (Temporal, ShadowResolution, PlanetRadius);
            feature.Noise = Noise.IsValid() ? Noise.GpuTexture : null;
            (feature.HighLayer, feature.HighLayerAltitude, feature.HighLayerOpacity, feature.HighLayerScale) = (HighLayer, HighLayerAltitude, HighLayerOpacity, HighLayerScale);

            float angle = WindDirection * (MathF.PI / 180f);
            double travel = WindSpeed * (double)deltaTime;
            feature.Wind += new Double2(MathF.Sin(angle) * travel, MathF.Cos(angle) * travel);
            feature.Formation = (feature.Formation + FormationChangeSpeed * deltaTime) % 1f;
        }
    }

    public struct SkyCaptureSettings
    {
        /// <summary>When the sky is captured into the sky probe that reflections, sky ambient and sky fog read.</summary>
        public SkyCaptureMode Mode = SkyCaptureMode.OnChange;

        /// <summary>Edge length of the sky probe's cube, rounded to a power of two from 16 to 2048.</summary>
        public int Resolution = 128;

        /// <summary>Frames at least between two captures when capturing on change.</summary>
        public int Interval = 2;

        public SkyCaptureSettings() { }
    }

    /// <summary>The shader of one of Graphite's skies, shared by every material of it.</summary>
    public static Shader SkyShader(BuiltInSky sky) => Shader.LoadDefault(sky switch
    {
        BuiltInSky.Gradient => DefaultShader.GradientSky,
        BuiltInSky.Cubemap => DefaultShader.CubemapSky,
        BuiltInSky.Panorama => DefaultShader.PanoramaSky,
        BuiltInSky.SixSided => DefaultShader.SixSidedSky,
        _ => DefaultShader.ProceduralSky,
    });

    /// <summary>A new material of one of Graphite's skies, at the sky's defaults.</summary>
    public static Material CreateSkyMaterial(BuiltInSky sky) => new(SkyShader(sky)) { Name = $"{sky} Sky" };

    private static Material? DefaultSky()
    {
        try { return CreateSkyMaterial(BuiltInSky.Procedural); }
        catch (Exception e)
        {
            Debug.LogErrorOnce("SceneEnvironment.DefaultSky", $"The procedural sky could not be made: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The ambient light a surface facing <paramref name="normal"/> receives, intensity included, the way the shaders
    /// light it without a lightmap or probe. Sky reads the sky's light as last read back from <paramref name="world"/>,
    /// and the sky colour until the first read back arrives or when there is no world.
    /// </summary>
    public Float3 EvaluateAmbient(Float3 normal, RenderWorld? world)
    {
        Float3 light = Ambient.Mode switch
        {
            AmbientMode.Sky when world != null && world.SkyAmbientVersion > 0 => world.SkyAmbient.Evaluate(normal),
            AmbientMode.Sky => Rgb(SkyColor),
            AmbientMode.Hemisphere => Rgb(Ambient.GroundColor) + (Rgb(Ambient.SkyColor) - Rgb(Ambient.GroundColor)) * (normal.Y * 0.5f + 0.5f),
            _ => Rgb(Ambient.Color),
        };
        return light * Ambient.Intensity;
    }

    /// <summary>Copies every setting onto a world's environment. The sky material's Graphite material is null headless.</summary>
    internal void ApplyTo(EnvironmentDesc env)
    {
        env.Ambient = (Graphite.Rendering.AmbientMode)Ambient.Mode;
        env.AmbientColor = Ambient.Color.ToFloat4();
        env.AmbientSky = Ambient.SkyColor.ToFloat4();
        env.AmbientGround = Ambient.GroundColor.ToFloat4();
        env.AmbientIntensity = Ambient.Intensity;
        env.Fog = new FogDesc
        {
            Mode = (Graphite.Rendering.FogMode)Fog.Mode,
            Color = new Float4(Fog.Color.R * Fog.Intensity, Fog.Color.G * Fog.Intensity, Fog.Color.B * Fog.Intensity, Fog.Color.A),
            Density = Fog.Density,
            Start = Fog.Start,
            End = Fog.End,
            UseSky = Fog.UseSky,
        };
        env.VolumetricFog = new VolumetricFogDesc
        {
            Enabled = VolumetricFog.Enabled,
            Density = VolumetricFog.Density,
            BaseHeight = VolumetricFog.BaseHeight,
            HeightFalloff = VolumetricFog.HeightFalloff,
            Albedo = VolumetricFog.Albedo.ToFloat4(),
            Anisotropy = VolumetricFog.Anisotropy,
            AmbientScale = VolumetricFog.AmbientScale,
            Distance = VolumetricFog.Distance,
            Slices = VolumetricFog.Slices,
            TemporalBlend = VolumetricFog.TemporalBlend,
        };
        env.SkyColor = SkyColor.ToFloat4();
        env.Sky = SkyMaterial.IsValid() ? SkyMaterial.GpuMaterial : null;
        env.SkyCapture = (Graphite.Rendering.SkyCaptureMode)SkyCapture.Mode;
        env.SkyProbeResolution = BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(Math.Min(SkyCapture.Resolution, QualitySettings.Active.MaxProbeResolution), 16, 2048));
        env.SkyCaptureInterval = Math.Max(SkyCapture.Interval, 1);
    }

    private static Float3 Rgb(Color c) => new(c.R, c.G, c.B);
}
