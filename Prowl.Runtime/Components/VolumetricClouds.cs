// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
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
/// The cloud layer is ray marched through the view, taking its density from a coverage map around the camera shaped by
/// a height profile and eroded by 3D noise. Each point of cloud marches a short ray toward the sun for its shading. The
/// coverage map is kept from frame to frame and only drawn where the camera or the wind uncovers it. Everything is
/// scaled up over the scene. The scene's first enabled clouds are drawn, by a
/// <see cref="VolumetricCloudsFeature"/> in the scene's world.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Rendering/Volumetric Clouds")]
[ComponentIcon("")] // Cloud
public sealed class VolumetricClouds : Component, IRenderSync
{
    /// <summary>Height of the cloud base above zero, in meters.</summary>
    [Header("Shape")]
    public float Altitude = 1500f;

    /// <summary>How tall the cloud layer is, in meters.</summary>
    public float Thickness = 1300f;

    /// <summary>How much of the sky is covered.</summary>
    [Range(0f, 1f)]
    public float Coverage = 0.5f;

    /// <summary>How quickly light dims inside the clouds. Higher looks heavier and darker underneath.</summary>
    public float Density = 1f;

    /// <summary>Size of the weather pattern in meters, how far apart clusters of clouds are.</summary>
    public float PatternScale = 12000f;

    /// <summary>How much the detail noise eats into cloud edges.</summary>
    [Range(0f, 1f)]
    public float Erosion = 1f;

    /// <summary>
    /// How much the erosion leans toward each cloud's base. 0 erodes evenly, 1 fades it linearly to none at the very
    /// top, so tops stay smooth.
    /// </summary>
    [Range(0f, 1f)]
    public float ErosionWeight = 0.7f;

    /// <summary>Size of the detail noise in meters.</summary>
    public float DetailScale = 600f;

    /// <summary>How far smooth noise bends the clouds' outlines, so they wind rather than sit as clumps.</summary>
    [Range(0f, 2f)]
    public float Warp = 0.4f;

    /// <summary>How much the bend changes from a cloud's base to its top, so clouds twist as they rise.</summary>
    [Range(0f, 1f)]
    public float WarpHeightShift = 0.25f;

    /// <summary>How quickly the clouds change shape as they drift, in turns of the detail noise per second.</summary>
    public float FormationChangeSpeed = 0.002f;

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
    [Range(0f, 1f)]
    public float AmbientSaturation = 1f;

    /// <summary>How bright the rim around the sun gets when looking through thin cloud toward it.</summary>
    [Range(0f, 1f)]
    public float SilverLining = 0.6f;

    /// <summary>
    /// How much of the light that scatters more than once comes back out. Higher fills the shaded parts of a cloud
    /// with light, lower leaves them darker.
    /// </summary>
    [Range(0f, 1f)]
    public float MultipleScattering = 0.4f;

    /// <summary>
    /// How much cloud light that scattered more than once still has to cross. Lower lets it reach deeper, so thick
    /// cloud stays brighter inside.
    /// </summary>
    [Range(0f, 1f)]
    public float MultipleScatteringOcclusion = 0.2f;

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

    /// <summary>
    /// Tiling noise the clouds and flat layers are built from: R broad gradient noise, G billowy cells, B fine gradient
    /// noise, A fine cells. Empty uses the built in one.
    /// </summary>
    [Header("Noise")]
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

    private double _formation;

    /// <summary>How far the formations have changed, along the 3D noise's third axis.</summary>
    public float FormationOffset => (float)(_formation % 1.0);

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

    [NonSerialized, NotHeld] private VolumetricCloudsFeature? _feature;
    [NonSerialized, NotHeld] private RenderWorld? _world;

    public override void OnEnable() => JoinScene(GameObject.Scene);

    public override void OnDisable()
    {
        LeaveScene(_registeredScene);
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

    bool IRenderSync.Sync(RenderWorld world)
    {
        bool drawn = _registeredScene.IsValid() && _registeredScene.Clouds.Count > 0 && _registeredScene.Clouds[0] == this;
        if (!drawn || (_world != null && _world != world))
        {
            Release();
            if (!drawn) return false;
        }

        _feature ??= new VolumetricCloudsFeature(Graphics.Renderer!);
        _feature.Enabled = _feature.Capture(this, world, _registeredScene!);
        if (_world == null)
        {
            world.Features.Add(_feature);
            _world = world;
        }
        return false;
    }

    void IRenderSync.Detach(RenderWorld world) => Release();

    private void Release()
    {
        if (_feature == null) return;
        _world?.Features.Remove(_feature);
        Graphics.DeferDispose(_feature);
        _feature = null;
        _world = null;
    }

    public override void Update()
    {
        Float2 wind = WindVector * (WindSpeed * Time.DeltaTime);
        _windX += wind.X;
        _windZ += wind.Y;
        _formation += FormationChangeSpeed * Time.DeltaTime;
    }
}
