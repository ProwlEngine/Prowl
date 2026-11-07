// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>How many pixels the clouds are drawn at before they are scaled up over the scene.</summary>
public enum CloudResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4,
}

/// <summary>
/// Volumetric clouds over the whole scene, with an optional faint high layer above them. Off at zero coverage.
/// <para/>
/// The cloud layer is ray marched through the view, taking its density from a coverage map around the camera shaped by
/// a height profile and eroded by 3D noise, and lit through a shadow map of the clouds toward the sun. The coverage map
/// is kept from frame to frame and only drawn where the camera or the wind uncovers it. Everything is scaled up over the
/// scene. Each camera draws its own, so reflection probes do not see them.
/// </summary>
[VolumeComponentMenu("Lighting/Volumetric Clouds")]
public sealed class VolumetricClouds : VolumeComponent<G.VolumetricCloudsFeature>
{
    /// <summary>Height of the cloud layer's floor above zero, in meters.</summary>
    [Header("Shape")]
    public VolumeParameter<float> Altitude = new(1500f);

    /// <summary>How tall the cloud layer is in meters, from its floor to its ceiling before the gap moves it.</summary>
    public VolumeParameter<float> Thickness = new(4000f);

    /// <summary>How far through the layer the tallest clouds rise in domes from flat bases on its floor.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> BottomStrength = new(0.45f);

    /// <summary>How far through the layer the top clouds dome down from its ceiling. They dome up above it by half as far.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> TopStrength = new(0.2f);

    /// <summary>How far up the top clouds dome above the ceiling at most, as a share of their depth below it.</summary>
    [Range(0f, 2f)] public VolumeParameter<float> DomeStrength = new(2f);

    /// <summary>How far up the ceiling the top clouds hang from is, in layer thicknesses, so how far apart the two are.</summary>
    public VolumeParameter<float> GapMultiplier = new(0.4f);

    /// <summary>Scales the coverage of the clouds rising from the floor.</summary>
    public VolumeParameter<float> BottomCoverage = new(1.1f);

    /// <summary>Scales the coverage of the clouds hanging from the ceiling.</summary>
    public VolumeParameter<float> TopCoverage = new(0.9f);

    /// <summary>How far round lobes push each cloud's domed side in and out, as a share of its height.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Lobes = new(0.25f);

    /// <summary>Size of the lobes in meters.</summary>
    public VolumeParameter<float> LobeScale = new(2000f);

    /// <summary>How quickly the clouds change shape, in turns of the noise they drift through per second.</summary>
    public VolumeParameter<float> FormationChangeSpeed = new(0.002f);

    /// <summary>How much of the sky is covered.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Coverage = new(0.69f);

    /// <summary>How quickly light dims inside the clouds. Higher looks heavier and darker underneath.</summary>
    public VolumeParameter<float> Density = new(1f);

    /// <summary>Size of the weather pattern in meters, how far apart clusters of clouds are.</summary>
    public VolumeParameter<float> PatternScale = new(12000f);

    /// <summary>How much the detail noise eats into cloud edges.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Erosion = new(1f);

    /// <summary>Size of the detail noise in meters.</summary>
    public VolumeParameter<float> DetailScale = new(600f);

    /// <summary>How far curl noise swirls the clouds into wisps, as a share of the swirls' size.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Wisps = new(0.5f);

    /// <summary>Size of the wisps' swirls in meters.</summary>
    public VolumeParameter<float> WispScale = new(140f);

    /// <summary>Wind speed in meters a second.</summary>
    [Header("Wind")]
    public VolumeParameter<float> WindSpeed = new(15f);

    /// <summary>Compass direction the wind blows toward, in degrees around Y.</summary>
    public VolumeParameter<float> WindDirection = new(30f);

    [Header("Lighting")]
    public VolumeParameter<Color> Albedo = new(new Color(1f, 1f, 1f, 1f));

    /// <summary>
    /// Scales the sunlight the clouds receive. Above 1 makes up for the light that scatters many times inside real
    /// clouds, which the march only roughly follows.
    /// </summary>
    public VolumeParameter<float> SunBrightness = new(2f);

    /// <summary>Scales the sky light the clouds receive.</summary>
    public VolumeParameter<float> AmbientBrightness = new(2f);

    /// <summary>How much of the sky's color the sky light keeps. Lower greys it, so clouds do not turn blue.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> AmbientSaturation = new(1f);

    /// <summary>How bright the rim around the sun gets when looking through thin cloud toward it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> SilverLining = new(0.6f);

    /// <summary>
    /// How much of the light that scatters more than once comes back out. Higher fills the shaded parts of a cloud
    /// with light, lower leaves them darker.
    /// </summary>
    [Range(0f, 1f)] public VolumeParameter<float> MultipleScattering = new(0.4f);

    /// <summary>
    /// How much cloud light that scattered more than once still has to cross. Lower lets it reach deeper, so thick
    /// cloud stays brighter inside.
    /// </summary>
    [Range(0f, 1f)] public VolumeParameter<float> MultipleScatteringOcclusion = new(0.2f);

    /// <summary>
    /// Around this distance in meters the clouds fade into the sky behind them, little before it and quickly past it.
    /// They are drawn out to twice this, where nothing of them is left to see.
    /// </summary>
    public VolumeParameter<float> HorizonFade = new(32000f);

    /// <summary>Resolution the clouds are built at before they are scaled up over the scene.</summary>
    [Header("Quality")]
    public VolumeParameter<CloudResolution> Resolution = new(CloudResolution.Full);

    /// <summary>
    /// Marches in steps four times as long from a start that moves along the ray each frame, and blends each frame over
    /// the last, so four frames add up to the full march. Much cheaper, with a faint trail behind fast moving clouds.
    /// </summary>
    public VolumeParameter<bool> Temporal = new(true);

    /// <summary>
    /// Texels along each side of one cascade of the shadow map the clouds are lit from, cascades around the camera drawn
    /// one a frame.
    /// </summary>
    public VolumeParameter<int> ShadowResolution = new(512);

    /// <summary>Radius of the planet in meters, which curves the cloud layers down toward the horizon.</summary>
    public VolumeParameter<float> PlanetRadius = new(6371000f);

    /// <summary>Tiling noise the high layer is built from, its red channel. Empty uses the built in one.</summary>
    [Header("Noise")]
    public VolumeParameter<Texture2D?> Noise = new(null);

    /// <summary>Draws a faint flat layer of noise high above the clouds, like thin cirrus.</summary>
    [Header("High Layer")]
    public VolumeParameter<bool> HighLayer = new(false);

    /// <summary>Height of the high layer above zero, in meters.</summary>
    public VolumeParameter<float> HighLayerAltitude = new(8000f);

    /// <summary>How opaque the high layer is where it is thickest.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> HighLayerOpacity = new(0.35f);

    /// <summary>Size of the high layer's pattern in meters.</summary>
    public VolumeParameter<float> HighLayerScale = new(20000f);

    public override bool IsActive => Coverage.Value > 0f;

    protected override G.VolumetricCloudsFeature CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.VolumetricCloudsFeature feature, Camera camera)
    {
        (feature.Altitude, feature.Thickness, feature.BottomStrength, feature.TopStrength, feature.GapMultiplier) = (Altitude, Thickness, BottomStrength, TopStrength, GapMultiplier);
        feature.DomeStrength = DomeStrength;
        (feature.BottomCoverage, feature.TopCoverage, feature.Lobes, feature.LobeScale, feature.Coverage, feature.Density) = (BottomCoverage, TopCoverage, Lobes, LobeScale, Coverage, Density);
        (feature.PatternScale, feature.Erosion, feature.DetailScale) = (PatternScale, Erosion, DetailScale);
        (feature.Wisps, feature.WispScale) = (Wisps, WispScale);
        feature.Albedo = Albedo.Value.ToFloat4();
        (feature.SunBrightness, feature.AmbientBrightness, feature.AmbientSaturation, feature.SilverLining) = (SunBrightness, AmbientBrightness, AmbientSaturation, SilverLining);
        (feature.MultipleScattering, feature.MultipleScatteringOcclusion, feature.HorizonFade) = (MultipleScattering, MultipleScatteringOcclusion, HorizonFade);
        feature.Resolution = Resolution.Value switch
        {
            CloudResolution.Half => G.EffectResolution.Half,
            CloudResolution.Quarter => G.EffectResolution.Quarter,
            _ => G.EffectResolution.Full,
        };
        (feature.Temporal, feature.ShadowResolution, feature.PlanetRadius) = (Temporal, ShadowResolution, PlanetRadius);
        Texture2D? noise = Noise.Value;
        feature.Noise = noise.IsValid() ? noise.GpuTexture : null;
        (feature.HighLayer, feature.HighLayerAltitude, feature.HighLayerOpacity, feature.HighLayerScale) = (HighLayer, HighLayerAltitude, HighLayerOpacity, HighLayerScale);
        Scene? scene = camera.GameObject.Scene;
        feature.World = scene.IsValid() ? scene!.Rendering.WorldIfMade : null;

        float angle = WindDirection.Value * (MathF.PI / 180f);
        double travel = WindSpeed.Value * (double)Time.DeltaTime;
        feature.Wind += new Double2(MathF.Sin(angle) * travel, MathF.Cos(angle) * travel);
        feature.Formation = (feature.Formation + FormationChangeSpeed.Value * Time.DeltaTime) % 1f;
    }
}
