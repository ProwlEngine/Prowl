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

/// <summary>How many pixels the clouds are drawn at before they are scaled up over the scene.</summary>
public enum CloudResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4,
}

/// <summary>
/// Volumetric clouds over the whole scene, with an optional faint high layer above them.
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
    /// <summary>Height of the cloud layer's floor above zero, in meters.</summary>
    [Header("Shape")]
    public float Altitude = 1500f;

    /// <summary>How tall the cloud layer is in meters, from its floor to its ceiling before the gap moves it.</summary>
    public float Thickness = 4000f;

    /// <summary>How far through the layer the tallest clouds rise in domes from flat bases on its floor.</summary>
    [Range(0f, 1f)]
    public float BottomStrength = 0.3f;

    /// <summary>How far through the layer the top clouds dome down from its ceiling. They dome up above it by half as far.</summary>
    [Range(0f, 1f)]
    public float TopStrength = 0.3f;

    /// <summary>How far up the ceiling the top clouds hang from is, in layer thicknesses, so how far apart the two are.</summary>
    public float GapMultiplier = 0.4f;

    /// <summary>Scales the coverage of the clouds rising from the floor.</summary>
    public float BottomCoverage = 1.1f;

    /// <summary>Scales the coverage of the clouds hanging from the ceiling.</summary>
    public float TopCoverage = 0.9f;

    /// <summary>How far round lobes push each cloud's domed side in and out, as a share of its height.</summary>
    [Range(0f, 1f)]
    public float Lobes = 0.25f;

    /// <summary>Size of the lobes in meters.</summary>
    public float LobeScale = 2000f;

    /// <summary>How quickly the clouds change shape, in turns of the noise they drift through per second.</summary>
    public float FormationChangeSpeed = 0.002f;

    /// <summary>How much of the sky is covered.</summary>
    [Range(0f, 1f)]
    public float Coverage = 0.69f;

    /// <summary>How quickly light dims inside the clouds. Higher looks heavier and darker underneath.</summary>
    public float Density = 1f;

    /// <summary>Size of the weather pattern in meters, how far apart clusters of clouds are.</summary>
    public float PatternScale = 12000f;

    /// <summary>How much the detail noise eats into cloud edges.</summary>
    [Range(0f, 1f)]
    public float Erosion = 1f;

    /// <summary>Size of the detail noise in meters.</summary>
    public float DetailScale = 600f;

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

    /// <summary>Draws a faint flat layer of noise high above the clouds, like thin cirrus.</summary>
    [Header("High Layer")]
    public bool HighLayer = false;

    /// <summary>Height of the high layer above zero, in meters.</summary>
    public float HighLayerAltitude = 8000f;

    /// <summary>How opaque the high layer is where it is thickest.</summary>
    [Range(0f, 1f)]
    public float HighLayerOpacity = 0.35f;

    /// <summary>Size of the high layer's pattern in meters.</summary>
    public float HighLayerScale = 20000f;

    // Accumulated in double, since it only ever grows
    private double _windX, _windZ;

    /// <summary>How far the wind has carried the clouds, in meters on the XZ plane.</summary>
    public Float2 WindOffset => new((float)_windX, (float)_windZ);

    private double _formation;

    /// <summary>How far the clouds have drifted through the noise their shapes come from.</summary>
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
