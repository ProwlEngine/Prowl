// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Scene = Prowl.Runtime.Resources.Scene;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Ray marches per pixel through global density and the scene's <see cref="FogVolume"/>s, accumulating light scattered
/// from the world's lights through their shadows, after the transparents. A light's <see cref="FogLight"/> tunes its share.
/// </summary>
[VolumeComponentMenu("Lighting/Volumetric Fog")]
public sealed class VolumetricFog : VolumeComponent<VolumetricFogFeature>
{
    public VolumeParameter<bool> Enabled = new(false);

    /// <summary>Base fog density everywhere in the world. Fog volumes add more on top.</summary>
    [Range(0f, 0.2f)] public VolumeParameter<float> GlobalDensity = new(0.005f);

    /// <summary>Color tint applied to all scattering.</summary>
    public VolumeParameter<Color> GlobalColorTint = new(new Color(1f, 1f, 1f, 1f));

    /// <summary>Henyey Greenstein anisotropy. 0 is isotropic, above 0 scatters forward.</summary>
    [Range(-0.99f, 0.99f)] public VolumeParameter<float> Scattering = new(0.5f);

    /// <summary>Multiplier on density for the light the fog absorbs along the view ray. Lower is brighter, longer range fog.</summary>
    [Range(0f, 5f)] public VolumeParameter<float> Extinction = new(1.0f);

    /// <summary>Per pixel random color shift to break up banding. 0 is none.</summary>
    [Range(0f, 0.2f)] public VolumeParameter<float> Dithering = new(0.02f);

    /// <summary>Ambient light added to every fog sample, whatever the shadows or light coverage, so dense fog in shadow keeps some color.</summary>
    [Header("Ambient")]
    public VolumeParameter<Color> AmbientColor = new(new Color(0.4f, 0.5f, 0.6f, 1f));

    /// <summary>Strength of the ambient term. 0 lets dense unlit fog go black.</summary>
    [Range(0f, 5f)] public VolumeParameter<float> AmbientIntensity = new(0.3f);

    [Header("Lights")]
    public VolumeParameter<bool> EnableDirectional = new(true);
    public VolumeParameter<bool> EnablePointLights = new(true);
    public VolumeParameter<bool> EnableSpotLights = new(true);

    /// <summary>Furthest world distance the ray marches.</summary>
    [Header("Quality")]
    [Range(1f, 500f)] public VolumeParameter<float> MaxDistance = new(100.0f);

    /// <summary>Depth slices of the grid the fog is lit in, closer together near the camera. Each slice lights one cell per 8 by 8 pixels.</summary>
    [Range(16f, 128f)] public VolumeParameter<int> Slices = new(64);

    /// <summary>Render target downsample, 1 full, 2 half, 4 quarter.</summary>
    [Range(1f, 4f)] public VolumeParameter<int> DownsampleScale = new(2);

    /// <summary>Bilateral upsample depth similarity threshold.</summary>
    [Range(0.001f, 1f)] public VolumeParameter<float> UpsampleDepthThreshold = new(0.1f);

    /// <summary>Blends the previous frame's fog into this frame, removing march noise at the cost of slight ghosting.</summary>
    public VolumeParameter<bool> EnableTemporalReprojection = new(true);

    /// <summary>How much of the reprojected history to keep.</summary>
    [Range(0f, 0.99f)] public VolumeParameter<float> TemporalBlendWeight = new(0.9f);

    public override bool IsActive => Enabled.Value;

    [SerializeIgnore]
    private readonly List<(FogVolume Volume, float Order)> _volumes = [];

    protected override VolumetricFogFeature CreateFeature(Renderer renderer) => new(renderer);

    protected override void Apply(VolumetricFogFeature feature, Camera camera)
    {
        float maxDistance = MathF.Max(0.1f, MaxDistance.Value);
        feature.Settings = new VolumetricFogFeature.FogSettings(
            GlobalDensity, GlobalColorTint.Value.ToFloat4(), Math.Clamp(Scattering, -0.99f, 0.99f), MathF.Max(0f, Extinction),
            MathF.Max(0f, Dithering), AmbientColor.Value.ToFloat4(), MathF.Max(0f, AmbientIntensity), maxDistance,
            Math.Clamp(Slices, 16, 128), Math.Clamp(DownsampleScale, 1, 4), MathF.Max(0.001f, UpsampleDepthThreshold),
            EnableTemporalReprojection, Math.Clamp(TemporalBlendWeight, 0f, 0.99f),
            new Float3(EnableDirectional ? 1f : 0f, EnablePointLights ? 1f : 0f, EnableSpotLights ? 1f : 0f));

        feature.PendingVolumes.Clear();
        Scene? scene = camera.GameObject.Scene;
        if (scene.IsNotValid()) return;

        Float3 cameraPosition = camera.Transform.Position;
        _volumes.Clear();
        foreach (GameObject go in scene.ActiveObjects)
        {
            FogVolume? volume = go.GetComponent<FogVolume>();
            if (volume.IsValid() && volume.EnabledInHierarchy)
                _volumes.Add((volume, volume.Shape == FogVolumeShape.Global ? -1f : Float3.DistanceSquared(cameraPosition, volume.Transform.Position)));
        }

        // Globals first, then the nearest
        _volumes.Sort((a, b) => a.Order.CompareTo(b.Order));

        for (int i = 0; i < Math.Min(_volumes.Count, VolumetricFogFeature.MaxVolumes); i++)
            feature.PendingVolumes.Add(Describe(_volumes[i].Volume));
    }

    private static VolumetricFogFeature.Volume Describe(FogVolume volume)
    {
        Float4x4 m = volume.Transform.WorldToLocalMatrix;
        float cone = volume.ConeAngle * (MathF.PI / 180f);
        Color tint = volume.ColorTint;
        return new VolumetricFogFeature.Volume
        {
            Row0 = new Float4(m.c0.X, m.c1.X, m.c2.X, m.c3.X),
            Row1 = new Float4(m.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y),
            Row2 = new Float4(m.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z),
            Params = new Float4((int)volume.Shape, volume.DensityMultiplier, Math.Clamp(volume.Falloff, 0f, 1f), MathF.Cos(cone)),
            Color = new Float4(tint.R, tint.G, tint.B, MathF.Sin(cone)),
        };
    }
}

/// <summary>
/// The volumetric fog of a <see cref="VolumetricFog"/>: the world's lights and shadows lit once per cell of a grid fitted
/// to the view, then marched through per pixel at a reduced resolution, blended over frames and upsampled over the
/// colour by depth, after the transparents.
/// </summary>
public sealed class VolumetricFogFeature : RenderFeature, IDisposable
{
    public const int MaxVolumes = 16;

    internal readonly record struct FogSettings(
        float Density, Float4 Tint, float Scattering, float Extinction, float Dithering, Float4 AmbientColor,
        float AmbientIntensity, float MaxDistance, int Slices, int Downsample, float UpsampleThreshold, bool Temporal,
        float TemporalBlend, Float3 LightTypes);

    internal struct Volume
    {
        public Float4 Row0, Row1, Row2, Params, Color;
    }

    private sealed class ViewState
    {
        public TextureHandle Froxels, March, History;
        public Float4 Grid;
        public Float2 Atlas;
    }

    private static readonly PropertyID[] s_volumeRow0 = Names("_FogVolumeRow0_", MaxVolumes);
    private static readonly PropertyID[] s_volumeRow1 = Names("_FogVolumeRow1_", MaxVolumes);
    private static readonly PropertyID[] s_volumeRow2 = Names("_FogVolumeRow2_", MaxVolumes);
    private static readonly PropertyID[] s_volumeParams = Names("_FogVolumeParams_", MaxVolumes);
    private static readonly PropertyID[] s_volumeColor = Names("_FogVolumeColor_", MaxVolumes);

    private static PropertyID[] Names(string prefix, int count)
    {
        var names = new PropertyID[count];
        for (int i = 0; i < count; i++) names[i] = prefix + i;
        return names;
    }

    private readonly SharedPrograms _programs;
    private const int CellPixels = 8;

    private readonly FullscreenProgram _froxel, _march, _temporal, _composite;
    private bool _disposed;
    private readonly Sampler _linearClamp, _pointClamp;
    private readonly PropertySet _properties = new();
    private readonly Volume[] _volumes = new Volume[MaxVolumes];
    private int _volumeCount;
    private FogSettings _settings;
    private (int Downsample, bool Temporal, int Slices) _declared = (2, true, 64);

    /// <summary>The settings copied at the next Extract. Host thread.</summary>
    internal FogSettings Settings;

    /// <summary>The volumes copied at the next Extract, positions absolute. Host thread.</summary>
    internal readonly List<Volume> PendingVolumes = [];

    public VolumetricFogFeature(Renderer renderer)
    {
        _programs = SharedPrograms.Acquire(renderer);
        (_froxel, _march, _temporal, _composite) = (_programs.Froxel, _programs.March, _programs.Temporal, _programs.Composite);
        SamplerDescription clamp = SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        };
        _linearClamp = Graphics.GetSampler(clamp);
        _pointClamp = Graphics.GetSampler(clamp with { MinFilter = Graphite.FilterMode.Point, MagFilter = Graphite.FilterMode.Point, MipFilter = Graphite.FilterMode.Point });
    }

    public override RenderSlot Slot => RenderSlot.AfterTransparents;

    public override void Extract()
    {
        _settings = Settings;
        _volumeCount = Math.Min(PendingVolumes.Count, MaxVolumes);
        for (int i = 0; i < _volumeCount; i++) _volumes[i] = PendingVolumes[i];

        (int, bool, int) declared = (_settings.Downsample, _settings.Temporal, _settings.Slices);
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
        builder.DrawsRenderables();
        ViewState state = builder.GetViewState<ViewState>();
        int width = Math.Max(1, (int)view.Width / _declared.Downsample), height = Math.Max(1, (int)view.Height / _declared.Downsample);
        // The slices tiled in rows of one texture, as near square as they fit
        int cellsX = ((int)view.Width + CellPixels - 1) / CellPixels, cellsY = ((int)view.Height + CellPixels - 1) / CellPixels;
        int slices = _declared.Slices, perRow = (int)MathF.Ceiling(MathF.Sqrt(slices)), rows = (slices + perRow - 1) / perRow;
        state.Grid = new Float4(cellsX, cellsY, slices, perRow);
        state.Atlas = new Float2(cellsX * perRow, cellsY * rows);
        state.Froxels = builder.Temporary("Froxels", view.Texture(cellsX * perRow, cellsY * rows, PixelFormat.R16_G16_B16_A16_Float));
        state.March = builder.Temporary("March", view.Texture(width, height, PixelFormat.R16_G16_B16_A16_Float));
        if (_declared.Temporal)
            state.History = builder.History("History", view.Texture(width, height, PixelFormat.R16_G16_B16_A16_Float), usage: TextureState.Attachment | TextureState.Sampled);
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        RenderTexture march = context.Get(state.March);
        Texture marchColor = march.ColorTextures[0];
        FogSettings s = _settings;

        _properties.SetTexture("_CameraDepthTexture", context.Depth!, _pointClamp);
        _properties.SetFloat2("_LowResolution", new Float2(marchColor.Width, marchColor.Height));
        _properties.SetFloat("_FogDensity", s.Density);
        _properties.SetFloat("_FogScattering", s.Scattering);
        _properties.SetFloat("_FogExtinction", s.Extinction);
        _properties.SetFloat("_FogMaxDistance", s.MaxDistance);
        _properties.SetFloat4("_FroxelGrid", state.Grid);
        _properties.SetFloat2("_FroxelAtlas", state.Atlas);
        _properties.SetFloat4("_FogColorTint", s.Tint);
        _properties.SetFloat("_FogDithering", s.Dithering);
        _properties.SetFloat4("_FogAmbientColor", s.AmbientColor);
        _properties.SetFloat("_FogAmbientIntensity", s.AmbientIntensity);
        _properties.SetFloat("_FogTime", (float)context.Time.Time);
        _properties.SetFloat("_FogUpsampleThreshold", s.UpsampleThreshold);
        _properties.SetFloat3("_FogLightTypes", s.LightTypes);
        SetVolumes(context.View.Position);
        RenderTexture froxels = context.Get(state.Froxels);
        context.Fullscreen(_froxel, froxels, _properties);
        _properties.SetTexture("_FroxelTex", froxels.ColorTextures[0], _linearClamp);
        context.Fullscreen(_march, march, _properties);

        Texture fog = marchColor;
        if (_declared.Temporal)
        {
            RenderTexture history = context.Get(state.History);
            RenderTexture? previous = context.History(state.History);
            _properties.SetTexture("_FogCurrentTex", marchColor, _linearClamp);
            _properties.SetTexture("_FogHistoryTex", (previous ?? march).ColorTextures[0], _linearClamp);
            _properties.SetFloat("_FogHistoryValid", previous != null && context.HasPreviousFrame ? 1f : 0f);
            _properties.SetFloat("_FogTemporalBlend", s.TemporalBlend);
            context.Fullscreen(_temporal, history, _properties);
            fog = history.ColorTextures[0];
        }

        _properties.SetTexture("_FogTex", fog, _linearClamp);
        context.Fullscreen(_composite, _properties);
    }

    // Positions become relative to the view's first layer, in double so far from the origin keeps its precision
    private void SetVolumes(Double3 camera)
    {
        _properties.SetInt("_FogVolumeCount", _volumeCount);
        for (int i = 0; i < _volumeCount; i++)
        {
            ref Volume v = ref _volumes[i];
            _properties.SetFloat4(s_volumeRow0[i], Relative(v.Row0, camera));
            _properties.SetFloat4(s_volumeRow1[i], Relative(v.Row1, camera));
            _properties.SetFloat4(s_volumeRow2[i], Relative(v.Row2, camera));
            _properties.SetFloat4(s_volumeParams[i], v.Params);
            _properties.SetFloat4(s_volumeColor[i], v.Color);
        }

    }

    private static Float4 Relative(Float4 row, Double3 camera)
        => new(row.X, row.Y, row.Z, (float)(row.W + row.X * camera.X + row.Y * camera.Y + row.Z * camera.Z));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _programs.Release();
    }

    // The fog programs of a renderer, shared by every camera's feature so a new one compiles nothing
    private sealed class SharedPrograms
    {
        private static readonly Dictionary<Renderer, SharedPrograms> s_shared = [];
        private readonly Renderer _renderer;
        private int _users;

        public readonly FullscreenProgram Froxel, March, Temporal, Composite;

        private SharedPrograms(Renderer renderer)
        {
            _renderer = renderer;
            Froxel = renderer.CreateFullscreenProgram("#define FOG_FROXEL 1\n#include \"Prowl/VolumetricFog.glsl\"\n", "Prowl/VolumetricFogFroxel", source: false, lit: true);
            March = renderer.CreateFullscreenProgram("#define FOG_MARCH 1\n#include \"Prowl/VolumetricFog.glsl\"\n", "Prowl/VolumetricFogMarch", source: false);
            Temporal = renderer.CreateFullscreenProgram("#define FOG_TEMPORAL 1\n#include \"Prowl/VolumetricFog.glsl\"\n", "Prowl/VolumetricFogTemporal", source: false);
            Composite = renderer.CreateFullscreenProgram("#include \"Prowl/VolumetricFog.glsl\"\n", "Prowl/VolumetricFogComposite");
        }

        public static SharedPrograms Acquire(Renderer renderer)
        {
            if (!s_shared.TryGetValue(renderer, out SharedPrograms? programs))
            {
                programs = new SharedPrograms(renderer);
                s_shared[renderer] = programs;
            }
            programs._users++;
            return programs;
        }

        public void Release()
        {
            if (--_users > 0) return;
            s_shared.Remove(_renderer);
            Froxel.Dispose();
            March.Dispose();
            Temporal.Dispose();
            Composite.Dispose();
        }
    }
}
