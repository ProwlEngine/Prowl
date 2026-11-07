// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// An ocean to the horizon at the GameObject's height. FFT waves on repeating tiles, a band of wavelengths each, drawn
/// as rings of tiles around the camera that grow with its height above the water, curving down with the planet.
/// Waves shrink in shallow water and foam along the shore, the floor captured once from above around the GameObject.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Rendering/Ocean")]
public sealed class Ocean : Component, IRenderSync
{
    /// <summary>Wind speed in km/h, which cuts off waves too long for it.</summary>
    [Header("Waves")]
    [Range(0f, 150f)] public float WindSpeed = 150f;

    /// <summary>The direction the wind blows toward, in degrees clockwise from +Z.</summary>
    public float WindDirection;

    /// <summary>How far waves spread from the wind's direction. At 1 they travel every way.</summary>
    [Range(0f, 1f)] public float Turbulence = 0.145f;

    /// <summary>Scales every wave's height.</summary>
    [Range(0f, 4f)] public float WaveScale = 1f;

    /// <summary>How far waves pinch sideways into sharp crests.</summary>
    [Range(0f, 2f)] public float Chop = 1.54f;

    /// <summary>Speeds the waves up or down.</summary>
    [Range(0f, 4f)] public float TimeScale = 1f;

    /// <summary>The largest wave tile in metres. Each next one is a quarter of the one before.</summary>
    public float TileSize = 1024f;

    /// <summary>The planet's radius in metres, which curves the water down toward the horizon. 0 is flat.</summary>
    [Header("Surface")]
    public float PlanetRadius = 6371000f;

    /// <summary>How many rings of tiles surround the camera.</summary>
    [Range(2, 12)] public int Rings = 7;

    /// <summary>How smooth the water is up close and far away.</summary>
    [Range(0f, 1f)] public float Roughness = 0.02f;
    [Range(0f, 1f)] public float FarRoughness = 0.12f;

    /// <summary>How fast the water absorbs red, green and blue light per metre. Higher is murkier.</summary>
    public Color Absorption = new(0.3f, 0.22f, 0.3f, 1f);

    /// <summary>The colour deep water scatters the sun and sky back in.</summary>
    public Color ScatterColor = new(0.02f, 0.09f, 0.12f, 1f);

    /// <summary>The glow through crests toward a sun behind them.</summary>
    public Color SubsurfaceColor = new(0.1f, 0.5f, 0.45f, 1f);

    /// <summary>Scales the absorption under water only. Lower sees further.</summary>
    [Range(0f, 1f)] public float UnderwaterDensity = 0.3f;

    /// <summary>How much the view bends through the surface.</summary>
    [Range(0f, 1f)] public float Refraction = 0.1f;

    /// <summary>How much of the crests foam, where the waves fold.</summary>
    [Range(0f, 1.5f)] public float FoamCoverage = 0.55f;

    /// <summary>The layers the floor is captured from, from straight above.</summary>
    [Header("Shore")]
    public LayerMask FloorLayers = LayerMask.Everything;

    /// <summary>The width of the square captured around the GameObject, in metres.</summary>
    public float FloorCaptureSize = 512f;

    /// <summary>Texels along each side of the floor capture. More keeps thin walls and steep shores sharp.</summary>
    public int FloorCaptureResolution = 512;

    /// <summary>How much waves shrink in water shallower than half their length.</summary>
    [Range(0f, 1f)] public float ShallowAttenuation = 0.95f;

    /// <summary>The water depth in metres shoreline foam reaches out to.</summary>
    public float ShorelineFoamDepth = 0.65f;

    /// <summary>How strongly the caustics light and darken what lies under the water.</summary>
    [Range(0f, 10f)] public float CausticsStrength = 3.2f;

    /// <summary>How far the wave normal map pushes the caustics about, and the metres it spans.</summary>
    [Range(0f, 0.25f)] public float CausticsDistortion = 0.1f;
    public float CausticsDistortionScale = 35f;

    [System.NonSerialized, NotHeld] private G.OceanFeature? _feature;
    [System.NonSerialized, NotHeld] private G.OceanSurface? _surface;
    [System.NonSerialized, NotHeld] private G.OceanDepthCapture? _capture;
    [System.NonSerialized, NotHeld] private G.OceanQueries? _queries;
    [System.NonSerialized, NotHeld] private G.OceanUnderwater? _underwater;
    [System.NonSerialized, NotHeld] private bool _captured;
    [System.NonSerialized, NotHeld] private readonly List<G.OceanQuery> _asked = [];

    /// <summary>The ocean last synced, which floaters ask about the water.</summary>
    public static Ocean? Active { get; private set; }

    /// <summary>Starts asking the water about points each frame. Answers arrive a few frames later.</summary>
    public G.OceanQuery CreateQuery(int points)
    {
        G.OceanQuery query = new(points);
        _asked.Add(query);
        return query;
    }

    public void RemoveQuery(G.OceanQuery query) => _asked.Remove(query);

    [System.NonSerialized, NotHeld] private readonly List<G.OceanInteractor> _interactors = [];

    /// <summary>Starts a sphere pushing ripples into the water, moved by its owner each frame.</summary>
    public void AddInteractor(G.OceanInteractor interactor) => _interactors.Add(interactor);

    public void RemoveInteractor(G.OceanInteractor interactor) => _interactors.Remove(interactor);

    [System.NonSerialized, NotHeld] private readonly List<G.OceanLevelInput> _levels = [];

    /// <summary>Starts holding the water at a level over an area, a lake, moved by its owner each frame.</summary>
    public void AddLevel(G.OceanLevelInput level) => _levels.Add(level);

    public void RemoveLevel(G.OceanLevelInput level) => _levels.Remove(level);

    [System.NonSerialized, NotHeld] private readonly List<G.OceanFlowInput> _flows = [];

    /// <summary>Starts a current carrying the water along over an area, moved by its owner each frame.</summary>
    public void AddFlow(G.OceanFlowInput flow) => _flows.Add(flow);

    public void RemoveFlow(G.OceanFlowInput flow) => _flows.Remove(flow);

    [System.NonSerialized, NotHeld] private readonly List<G.OceanClipInput> _clips = [];

    /// <summary>Starts cutting the surface away inside a shape, moved by its owner each frame.</summary>
    public void AddClip(G.OceanClipInput clip) => _clips.Add(clip);

    public void RemoveClip(G.OceanClipInput clip) => _clips.Remove(clip);

    /// <summary>Captures the floor again next frame, after the scene around the ocean changed.</summary>
    public void RecaptureFloor() => _captured = false;

    bool IRenderSync.Sync(G.RenderWorld world)
    {
        if (Graphics.Renderer is not { } renderer)
            return false;
        if (_feature == null)
        {
            _feature = new G.OceanFeature(renderer);
            _surface = new G.OceanSurface(renderer, _feature);
            _queries = new G.OceanQueries(renderer, _feature);
            _underwater = new G.OceanUnderwater(renderer, _surface);
        }
        Active = this;
        world.FrameFeatures.Add(_feature);
        world.FrameFeatures.Add(_feature.LocalPass);
        foreach (G.FrameFeature feature in _queries!.Features)
            world.FrameFeatures.Add(feature);
        _queries.Queries.Clear();
        _queries.Queries.AddRange(_asked);
        _feature.Interactors.Clear();
        _feature.Interactors.AddRange(_interactors);
        _feature.LevelInputs.Clear();
        _feature.LevelInputs.AddRange(_levels);
        _feature.FlowInputs.Clear();
        _feature.FlowInputs.AddRange(_flows);
        _feature.ClipInputs.Clear();
        _feature.ClipInputs.AddRange(_clips);
        world.Providers.Add(_surface!);
        world.Features.Add(_surface!.DrawFeature);
        world.Features.Add(_underwater!);

        _feature.WindSpeed = WindSpeed / 3.6f;
        _feature.WindDirection = (90f - WindDirection) * (MathF.PI / 180f);
        _feature.Turbulence = Turbulence;
        _feature.TileSize = TileSize;
        _feature.Spectrum.Multiplier = WaveScale;
        _feature.Spectrum.Chop = Chop;
        _feature.Spectrum.TimeScale = TimeScale;
        _feature.FoamCoverage = FoamCoverage;
        _feature.SeaLevel = Transform.Position.Y;
        _feature.PlanetRadius = PlanetRadius;
        _feature.ShallowAttenuation = ShallowAttenuation;
        if (Camera.Main is { } main)
        {
            Float3 eye = main.Transform.Position;
            _feature.Viewer = new Double3(eye.X, eye.Y, eye.Z);
        }

        if (!_captured)
        {
            _capture ??= new G.OceanDepthCapture(renderer, world, Math.Clamp(FloorCaptureResolution, 64, 4096));
            Float3 centre = Transform.Position;
            _capture.Aim(new Double3(centre.X, centre.Y, centre.Z), FloorCaptureSize, layers: (uint)FloorLayers.Mask);
            if (!_feature.DepthInputs.Contains(_capture.Input))
                _feature.DepthInputs.Add(_capture.Input);
            Graphics.AddView(_capture.View);
            _captured = true;
        }

        G.OceanSurface surface = _surface!;
        surface.Rings = Rings;
        surface.MaxVerticalDisplacement = 10f * WaveScale;
        surface.ShorelineFoamDepth = ShorelineFoamDepth;
        surface.CausticsStrength = CausticsStrength;
        surface.CausticsDistortion = CausticsDistortion;
        surface.CausticsDistortionScale = CausticsDistortionScale;
        _underwater!.DensityFactor = UnderwaterDensity;
        G.Material material = surface.Material;
        material.SetFloat("_Roughness", Roughness);
        material.SetFloat("_FarRoughness", FarRoughness);
        surface.Absorption = Absorption.ToFloat4();
        surface.ScatterColor = ScatterColor.ToFloat4();
        material.SetColor("_SubsurfaceColor", SubsurfaceColor.ToFloat4());
        material.SetFloat("_RefractionStrength", Refraction);
        return false;
    }

    void IRenderSync.Detach(G.RenderWorld world)
    {
        if (_feature != null)
            world.FrameFeatures.Remove(_feature);
        if (_queries != null)
        {
            foreach (G.FrameFeature feature in _queries.Features)
                world.FrameFeatures.Remove(feature);
            _queries.Dispose();
            _queries = null;
        }
        if (Active == this)
            Active = null;
        if (_surface != null)
        {
            world.Providers.Remove(_surface);
            world.Features.Remove(_surface.DrawFeature);
        }
        if (_underwater != null)
        {
            world.Features.Remove(_underwater);
            _underwater.Dispose();
            _underwater = null;
        }
        _surface?.Dispose();
        _feature?.Dispose();
        _capture?.Dispose();
        (_surface, _feature, _capture) = (null, null, null);
        _captured = false;
    }
}
