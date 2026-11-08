// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
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

    /// <summary>Seconds before the waves repeat, 0 for never.</summary>
    public float LoopPeriod = 600f;

    /// <summary>Gravity in m/s2, which sets how fast waves of each length travel.</summary>
    public float Gravity = 9.81f;

    /// <summary>The planet's radius in metres, which curves the water down toward the horizon. 0 is flat.</summary>
    [Header("Surface")]
    public float PlanetRadius = 6371000f;

    /// <summary>How many rings of tiles surround the camera.</summary>
    [Range(2, 12)] public int Rings = 7;

    /// <summary>The smallest and largest a tile of the innermost ring gets in metres, near and high above the water.</summary>
    public float MinTileScale = 8f;
    public float MaxTileScale = 256f;

    /// <summary>The distances each wave cascade fades out by, from the longest waves to the shortest.</summary>
    public Float4 CascadeFade = new(1000000f, 6000f, 1500f, 400f);

    /// <summary>How smooth the water is up close and far away, and the distance it reaches the far value.</summary>
    [Range(0f, 1f)] public float Roughness = 0.02f;
    [Range(0f, 1f)] public float FarRoughness = 0.12f;
    public float FarRoughnessDistance = 2000f;

    /// <summary>A normal map of small waves over the surface, null for the library's, its size in metres and strength.</summary>
    public Texture2D? NormalsTexture;
    [Range(0.01f, 200f)] public float NormalsScale = 40f;
    [Range(0f, 2f)] public float NormalsStrength = 0.36f;

    /// <summary>Reflects what is on screen in the water.</summary>
    public bool ScreenReflections = true;

    /// <summary>How much the view bends through the surface.</summary>
    [Range(0f, 1f)] public float Refraction = 0.1f;

    /// <summary>How fast the water absorbs red, green and blue light per metre. Higher is murkier.</summary>
    [Header("Colour")]
    public Color Absorption = new(0.3f, 0.22f, 0.3f, 1f);

    /// <summary>Scales the absorption for sunlight reaching the floor.</summary>
    [Range(0f, 2f)] public float DepthLightScale = 0.3f;

    /// <summary>The colour deep water scatters the sun and sky back in.</summary>
    public Color ScatterColor = new(0.02f, 0.09f, 0.12f, 1f);

    /// <summary>The glow through crests toward a sun behind them, how bright and how tightly it gathers around the sun.</summary>
    public Color SubsurfaceColor = new(0.1f, 0.5f, 0.45f, 1f);
    [Range(0f, 10f)] public float SubsurfaceSun = 2f;
    [Range(0f, 32f)] public float SubsurfaceFalloff = 5f;

    /// <summary>Scales the absorption under water only. Lower sees further.</summary>
    [Range(0f, 1f)] public float UnderwaterDensity = 0.3f;

    /// <summary>How folded the crests must be before they foam: foam builds where the Jacobian falls under this.</summary>
    [Header("Foam")]
    [Range(0f, 1.5f)] public float FoamCoverage = 0.55f;

    /// <summary>How white the foam on folding crests is, 0 for none.</summary>
    [Range(0f, 4f)] public float FoamStrength = 1f;

    /// <summary>The share of foam that fades each second.</summary>
    [Range(0f, 4f)] public float FoamFade = 0.8f;

    /// <summary>How hard ripples and wakes pull the water toward their crests, folding them into foam.</summary>
    [Range(0f, 4f)] public float RippleFoam = 1f;

    /// <summary>The share of ripple motion lost each second.</summary>
    [Range(0f, 1f)] public float RippleDamping = 0.05f;

    /// <summary>A texture whose red channel shows through as foam thickens, null for the library's, the metres it spans and the foam's colour.</summary>
    public Texture2D? FoamTexture;
    [Range(0.1f, 50f)] public float FoamScale = 3f;
    public Color FoamColor = new(1f, 1f, 1f, 1f);

    /// <summary>How soft the foam's edges are.</summary>
    [Range(0.01f, 1f)] public float FoamFeather = 0.4f;

    /// <summary>How the foam takes the light: the sun's share, how far its texture bends its normal, its highlight's tightness and brightness.</summary>
    [Range(0f, 2f)] public float FoamLightScale = 1.35f;
    [Range(0f, 30f)] public float FoamNormalStrength = 3.5f;
    [Range(1f, 512f)] public float FoamSpecularFalloff = 293f;
    [Range(0f, 16f)] public float FoamSpecularBoost = 0.15f;

    /// <summary>The layers the floor is captured from, from straight above.</summary>
    [Header("Shore")]
    public LayerMask FloorLayers = LayerMask.Everything;

    /// <summary>The width of the square captured around the GameObject, in metres.</summary>
    public float FloorCaptureSize = 512f;

    /// <summary>Texels along each side of the floor capture. More keeps thin walls and steep shores sharp.</summary>
    public int FloorCaptureResolution = 512;

    /// <summary>The width in metres of the first depth slice kept around the camera, each next twice as wide.</summary>
    public float DepthSliceSize = 64f;

    /// <summary>How much waves shrink in water shallower than half their length.</summary>
    [Range(0f, 1f)] public float ShallowAttenuation = 0.95f;

    /// <summary>The water depth in metres shoreline foam reaches out to, and how thick it gets.</summary>
    public float ShorelineFoamDepth = 0.65f;
    [Range(0f, 8f)] public float ShorelineFoamStrength = 2f;

    /// <summary>How thick the water behind a pixel must be for its foam to show in full.</summary>
    public float ShorelineFoamMinDepth = 0.27f;

    /// <summary>A caustics texture, null for the library's, and the metres it spans.</summary>
    [Header("Caustics")]
    public Texture2D? CausticsTexture;
    [Range(0.1f, 25f)] public float CausticsScale = 5f;

    /// <summary>The caustics texture's average brightness, which neither lights nor darkens.</summary>
    [Range(0f, 1f)] public float CausticsGreyPoint = 0.07f;

    /// <summary>How strongly the caustics light and darken what lies under the water.</summary>
    [Range(0f, 10f)] public float CausticsStrength = 3.2f;

    /// <summary>The depth in metres the caustics are sharpest at, and how far either side they stay sharp.</summary>
    [Range(0f, 250f)] public float CausticsFocalDepth = 2f;
    public float CausticsDepthOfField = 0.33f;

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
        _feature.LoopPeriod = LoopPeriod;
        _feature.Gravity = Gravity;
        _feature.RippleDamping = RippleDamping;
        _feature.DepthSliceSize = DepthSliceSize;
        _feature.FoamCoverage = FoamCoverage;
        _feature.FoamStrength = FoamStrength;
        _feature.FoamFade = FoamFade;
        _feature.RippleFoam = RippleFoam;
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
        surface.MinScale = MinTileScale;
        surface.MaxScale = MaxTileScale;
        surface.MaxVerticalDisplacement = 10f * WaveScale;
        surface.CascadeFade = CascadeFade;
        surface.ShorelineFoamDepth = ShorelineFoamDepth;
        surface.ShorelineFoamStrength = ShorelineFoamStrength;
        surface.ShorelineFoamMinDepth = ShorelineFoamMinDepth;
        surface.Absorption = Absorption.ToFloat4();
        surface.DepthLightScale = DepthLightScale;
        surface.ScatterColor = ScatterColor.ToFloat4();
        surface.CausticsScale = CausticsScale;
        surface.CausticsGreyPoint = CausticsGreyPoint;
        surface.CausticsStrength = CausticsStrength;
        surface.CausticsFocalDepth = CausticsFocalDepth;
        surface.CausticsDepthOfField = CausticsDepthOfField;
        surface.CausticsDistortion = CausticsDistortion;
        surface.CausticsDistortionScale = CausticsDistortionScale;
        (Graphite.Texture? foam, Graphite.SamplerDescription foamSampler) = Gpu(FoamTexture);
        surface.SetFoamTexture(foam, foamSampler);
        (Graphite.Texture? caustics, Graphite.SamplerDescription causticsSampler) = Gpu(CausticsTexture);
        surface.SetCausticsTexture(caustics, causticsSampler);
        (Graphite.Texture? normals, Graphite.SamplerDescription normalsSampler) = Gpu(NormalsTexture);
        surface.SetNormalsTexture(normals, normalsSampler);
        _underwater!.DensityFactor = UnderwaterDensity;

        G.Material material = surface.Material;
        material.SetFloat("_Roughness", Roughness);
        material.SetFloat("_FarRoughness", FarRoughness);
        material.SetFloat("_FarRoughnessDistance", FarRoughnessDistance);
        material.SetFloat("_NormalsScale", NormalsScale);
        material.SetFloat("_NormalsStrength", NormalsStrength);
        material.SetFloat("_ScreenReflections", ScreenReflections ? 1f : 0f);
        material.SetFloat("_RefractionStrength", Refraction);
        material.SetColor("_SubsurfaceColor", SubsurfaceColor.ToFloat4());
        material.SetFloat("_SubsurfaceSun", SubsurfaceSun);
        material.SetFloat("_SubsurfaceFalloff", SubsurfaceFalloff);
        material.SetColor("_FoamColor", FoamColor.ToFloat4());
        material.SetFloat("_FoamScale", FoamScale);
        material.SetFloat("_FoamFeather", FoamFeather);
        material.SetFloat("_FoamLightScale", FoamLightScale);
        material.SetFloat("_FoamNormalStrength", FoamNormalStrength);
        material.SetFloat("_FoamSpecularFalloff", FoamSpecularFalloff);
        material.SetFloat("_FoamSpecularBoost", FoamSpecularBoost);
        return false;
    }

    // A texture's GPU copy and sampler once loaded, starting its load when it has not
    private static (Graphite.Texture?, Graphite.SamplerDescription) Gpu(Texture2D? texture)
    {
        if (!texture.IsValid())
            return (null, Graphite.SamplerDescription.Linear);
        if (texture.GpuIfLoaded == null && texture.State == AssetState.Unloaded)
            _ = texture.LoadAsync();
        return (texture.GpuIfLoaded, texture.SamplerUnchecked);
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
