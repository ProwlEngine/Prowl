// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>How a flat cloud layer looks.</summary>
public enum CloudLayerStyle
{
    /// <summary>Thin wispy streaks stretched along the wind, high up.</summary>
    Cirrus,

    /// <summary>Soft puffs drawn out into long streaks along the wind.</summary>
    Stratus,

    /// <summary>The red channel of <see cref="CloudLayer.Texture"/>, tiled over the sheet.</summary>
    Custom,
}

/// <summary>How many pixels the clouds are drawn at before they are scaled up over the scene.</summary>
public enum CloudResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4,
}

/// <summary>A flat cloud layer drawn as a curved sheet at one altitude, above or below the volumetric clouds.</summary>
[Serializable]
public sealed class CloudLayer
{
    public bool Enabled = true;

    public CloudLayerStyle Style = CloudLayerStyle.Cirrus;

    /// <summary>Height of the sheet above zero, in meters.</summary>
    public float Altitude = 8000f;

    [Range(0f, 1f)]
    public float Coverage = 0.5f;

    [Range(0f, 1f)]
    public float Opacity = 0.7f;

    /// <summary>Size of the pattern in meters, how far apart its features repeat.</summary>
    public float Scale = 12000f;

    /// <summary>How fast the layer drifts relative to the volumetric clouds' wind.</summary>
    public float WindMultiplier = 2f;

    public Color Tint = new(1f, 1f, 1f, 1f);

    /// <summary>A tiling texture whose red channel is the layer's pattern, scaled by <see cref="Scale"/>.</summary>
    [ShowIf(nameof(IsCustom))]
    public Texture2D? Texture;

    public bool IsCustom => Style == CloudLayerStyle.Custom;
}

/// <summary>
/// Volumetric clouds over the whole scene, with optional flat cloud layers above or below them.
/// <para/>
/// The cloud layer is not ray marched through the view. A grid of camera facing particles covers the sky around the
/// camera, each taking its density from a coverage map and eroding its edges with 3D noise, and only marching a few
/// steps toward the sun to light itself. Where there is no cloud the particles collapse and cost nothing. A second,
/// sparser grid with larger particles reaches further out. Everything is drawn at a reduced resolution and scaled
/// up over the scene.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Rendering/Volumetric Clouds")]
[ComponentIcon("")] // Cloud
public sealed class VolumetricClouds : MonoBehaviour
{
    /// <summary>Height of the cloud base above zero, in meters.</summary>
    [Header("Shape")]
    public float Altitude = 1500f;

    /// <summary>How tall the cloud layer is, in meters.</summary>
    public float Thickness = 1300f;

    /// <summary>How much of the sky is covered.</summary>
    [Range(0f, 1f)]
    public float Coverage = 0.45f;

    /// <summary>How quickly light dims inside the clouds. Higher looks heavier and darker underneath.</summary>
    public float Density = 1f;

    /// <summary>Size of the weather pattern in meters, how far apart clusters of clouds are.</summary>
    public float PatternScale = 24000f;

    /// <summary>How much the detail noise eats into cloud edges.</summary>
    [Range(0f, 1f)]
    public float Erosion = 0.5f;

    /// <summary>Size of the detail noise in meters.</summary>
    public float DetailScale = 1000f;

    /// <summary>
    /// How much billowy puffs drawn on each particle eat into thin cloud, detail at screen resolution on top of
    /// <see cref="Erosion"/>. Kept mild, since a particle's puffs are only an impression of the cloud behind it.
    /// </summary>
    [Range(0f, 1f)]
    public float PuffStrength = 0.5f;

    /// <summary>How many puffs fit across one particle.</summary>
    public float PuffScale = 10f;

    /// <summary>Wind speed in meters a second.</summary>
    [Header("Wind")]
    public float WindSpeed = 15f;

    /// <summary>Compass direction the wind blows toward, in degrees around Y.</summary>
    public float WindDirection = 30f;

    [Header("Lighting")]
    public Color Albedo = new(1f, 1f, 1f, 1f);

    /// <summary>Scales the sunlight the clouds receive.</summary>
    public float SunBrightness = 1f;

    /// <summary>Scales the sky light the clouds receive.</summary>
    public float AmbientBrightness = 1f;

    /// <summary>How much of the sky's color the sky light keeps. Lower greys it, so clouds do not turn blue.</summary>
    [Range(0f, 1f)]
    public float AmbientSaturation = 0.5f;

    /// <summary>How bright the rim around the sun gets when looking through thin cloud toward it.</summary>
    [Range(0f, 1f)]
    public float SilverLining = 0.6f;

    /// <summary>Beyond about this distance in meters the clouds fade into the sky behind them.</summary>
    public float HorizonFade = 40000f;

    /// <summary>Radius in meters of the detailed particle grid around the camera.</summary>
    [Header("Quality")]
    public float Radius = 16000f;

    /// <summary>Particles along each side of the detailed grid. The far grid uses the same count.</summary>
    [Range(32, 256)]
    public int ParticleGrid = 128;

    /// <summary>How much further than <see cref="Radius"/> the far grid reaches.</summary>
    [Range(2f, 8f)]
    public float FarRadiusMultiplier = 4f;

    /// <summary>How much bigger far grid particles are than the detailed ones.</summary>
    [Range(1f, 8f)]
    public float FarParticleSize = 2.5f;

    /// <summary>
    /// Marches toward the sun for every pixel of cloud instead of reading the sun map, which is drawn once a frame
    /// from the same march. Sharper self shadowing from the detail noise, at <see cref="LightSamples"/> extra reads of
    /// the coverage and noise per pixel.
    /// </summary>
    public bool MarchToSun = false;

    /// <summary>Samples taken toward the sun for each pixel of cloud, when <see cref="MarchToSun"/> is on.</summary>
    [Range(1, 12)]
    public int LightSamples = 5;

    /// <summary>
    /// Resolution the clouds are built at before they are scaled up over the scene. With
    /// <see cref="TemporalUpscale"/> on, only a quarter of these pixels are drawn each frame.
    /// </summary>
    public CloudResolution Resolution = CloudResolution.Quarter;

    /// <summary>
    /// Draws a quarter of the clouds' pixels each frame, a different one of every 2x2 block in turn, and fills in the
    /// rest from earlier frames moved to where the clouds are now. About a quarter of the cost for the same sharpness
    /// once settled, a little soft for a few frames after fast camera turns.
    /// </summary>
    public bool TemporalUpscale = true;


    /// <summary>Radius of the planet in meters, which curves the cloud layers down toward the horizon.</summary>
    public float PlanetRadius = 6371000f;

    [Header("Noise")]
    /// <summary>
    /// Tiling noise the clouds and flat layers are built from: R broad gradient noise, G billowy cells, B fine gradient
    /// noise, A fine cells. Empty uses the built in one.
    /// </summary>
    public Texture2D? Noise;

    /// <summary>
    /// Tiling 3D noise that erodes the clouds' edges: R at the base scale, G three times finer. Empty uses the built in
    /// one.
    /// </summary>
    public Texture3D? ErosionNoise;

    [Header("Flat Layers")]
    public List<CloudLayer> Layers = [new CloudLayer()];

    // Accumulated in double, since it only ever grows
    private double _windX, _windZ;

    /// <summary>How far the wind has carried the clouds, in meters on the XZ plane.</summary>
    public Float2 WindOffset => new((float)_windX, (float)_windZ);

    internal double WindX => _windX;
    internal double WindZ => _windZ;

    /// <summary>The direction the wind blows toward on the XZ plane.</summary>
    public Float2 WindVector
    {
        get
        {
            float angle = WindDirection * (MathF.PI / 180f);
            return new Float2(MathF.Sin(angle), MathF.Cos(angle));
        }
    }

    private Scene? _registeredScene;

    /// <summary>The coverage and sun maps the renderer draws once a frame for these clouds.</summary>
    internal CloudRenderer.CloudMaps? Maps;

    public override void OnEnable() => JoinScene(GameObject.Scene);

    public override void OnDisable()
    {
        LeaveScene(_registeredScene);
        Maps?.Dispose();
        Maps = null;
    }

    internal override void JoinScene(Scene? scene)
    {
        _registeredScene = scene;
        if (scene.IsValid()) scene!.Clouds.Add(this);
    }

    internal override void LeaveScene(Scene? scene)
    {
        if (_registeredScene.IsValid()) _registeredScene!.Clouds.Remove(this);
        _registeredScene = null;
    }

    public override void Update()
    {
        Float2 wind = WindVector * (WindSpeed * Time.DeltaTime);
        _windX += wind.X;
        _windZ += wind.Y;
    }
}
