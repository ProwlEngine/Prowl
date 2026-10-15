// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
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
/// Each view draws a coverage map around its camera holding how much cloud there is at every point of the ground plane,
/// and a sun map holding how much cloud the sunlight crosses. The cloud layer and the flat layers are marched front to
/// back at a reduced resolution, a quarter of its pixels a frame with temporal upscaling, and scaled up over the scene
/// with depth aware weights so the clouds stay behind geometry edges.
/// </summary>
internal sealed class VolumetricCloudsFeature : RenderFeature, IDisposable
{
    private const int CoverageResolution = 1024;
    private const int MaxLayers = 4;

    internal sealed class CloudSettings
    {
        public Float4 Shape, Pattern, Lighting, Albedo;
        public Float2 Wind, Puff;
        public double WindX, WindZ;
        public Float3 ToSun, SunColor, SkyLight;
        public float AmbientSaturation, HorizonFade, Reach, DetailScale;
        public int LightSamples, Scale;
        public bool Temporal;
        public Texture? Noise2D, Noise3D;
        public uint ErosionSize;
        public int Epoch;
        public readonly Layer[] Layers = new Layer[MaxLayers];
        public int LayerCount;

        public void CopyTo(CloudSettings other)
        {
            (other.Shape, other.Pattern, other.Lighting, other.Albedo) = (Shape, Pattern, Lighting, Albedo);
            (other.Wind, other.Puff, other.WindX, other.WindZ) = (Wind, Puff, WindX, WindZ);
            (other.ToSun, other.SunColor, other.SkyLight) = (ToSun, SunColor, SkyLight);
            (other.AmbientSaturation, other.HorizonFade, other.Reach, other.DetailScale) = (AmbientSaturation, HorizonFade, Reach, DetailScale);
            (other.LightSamples, other.Scale, other.Temporal) = (LightSamples, Scale, Temporal);
            (other.Noise2D, other.Noise3D, other.ErosionSize, other.Epoch) = (Noise2D, Noise3D, ErosionSize, Epoch);
            Array.Copy(Layers, other.Layers, MaxLayers);
            other.LayerCount = LayerCount;
        }
    }

    internal struct Layer
    {
        public Float4 Shape, Wind, Tint;
        public Texture? Texture;
    }

    private sealed class ViewState
    {
        public TextureHandle Coverage, Sun, Depth, Target, History, TargetDepth;
        public bool Temporal;
        public int OutWidth, OutHeight, Block;
        public int Frame, Epoch;
        public double PreviousWindX, PreviousWindZ;
    }

    // The order each frame's samples visit the pixels of a 2x2 block, diagonals first so any two frames cover it evenly
    private static readonly Int2[] s_jitterCells = [new(0, 0), new(1, 1), new(1, 0), new(0, 1)];

    private static readonly PropertyID[] s_layerShape = ["_LayerShape_0", "_LayerShape_1", "_LayerShape_2", "_LayerShape_3"];
    private static readonly PropertyID[] s_layerWind = ["_LayerWind_0", "_LayerWind_1", "_LayerWind_2", "_LayerWind_3"];
    private static readonly PropertyID[] s_layerTint = ["_LayerTint_0", "_LayerTint_1", "_LayerTint_2", "_LayerTint_3"];
    private static readonly PropertyID[] s_layerTexture = ["_LayerTexture_0", "_LayerTexture_1", "_LayerTexture_2", "_LayerTexture_3"];

    private readonly FullscreenProgram _coverage, _sun, _depth, _march, _temporal, _composite;
    private readonly Sampler _linearClamp, _pointClamp, _linearRepeat;
    private readonly PropertySet _properties = new();
    private readonly CloudSettings _settings = new();
    private (int Scale, bool Temporal, bool SunMap) _declared = (4, true, true);

    // The settings the history was started with, so changing them enough starts it again
    private readonly List<float> _values = [], _tolerances = [];
    private float[]? _baseline;

    /// <summary>The settings copied at the next Extract. Host thread.</summary>
    internal readonly CloudSettings Pending = new();

    public VolumetricCloudsFeature(Renderer renderer)
    {
        BlendStateDescription two = new(default, BlendAttachmentDescription.OverrideBlend, BlendAttachmentDescription.OverrideBlend);
        _coverage = Program(renderer, "CLOUD_COVERAGE", "Coverage");
        _sun = Program(renderer, "CLOUD_SUN", "Sun");
        _depth = Program(renderer, "CLOUD_DEPTH", "Depth");
        _march = Program(renderer, "CLOUD_MARCH", "March", two);
        _temporal = Program(renderer, "CLOUD_TEMPORAL", "Temporal", two);
        _composite = renderer.CreateFullscreenProgram(Clouds, "Prowl/VolumetricCloudsComposite");

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

    public override RenderSlot Slot => RenderSlot.Atmosphere;
    public override ViewKinds RunsOn => ViewKinds.Cameras | ViewKinds.ReflectionProbe;

    /// <summary>Copies a component's settings, the world's sun and the scene's sky light into <see cref="Pending"/>. Main thread.</summary>
    internal bool Capture(VolumetricClouds clouds, RenderWorld world, Scene scene)
    {
        Texture2D noise = clouds.Noise.IsValid() ? clouds.Noise : Texture2D.LoadDefault(DefaultTexture.CloudNoise);
        Texture3D erosion = clouds.ErosionNoise.IsValid() ? clouds.ErosionNoise : Texture3D.LoadDefault(DefaultTexture3D.CloudNoise);
        CloudSettings s = Pending;
        s.Noise2D = noise.GpuTexture;
        s.Noise3D = erosion.GpuTexture;
        if (s.Noise2D == null || s.Noise3D == null) return false;
        s.ErosionSize = erosion.Width;

        float thickness = MathF.Max(clouds.Thickness, 1f);
        s.Shape = new Float4(clouds.Altitude, thickness, 1f / thickness, MathF.Max(clouds.PlanetRadius, 1000f));
        s.Pattern = new Float4(1f / MathF.Max(clouds.PatternScale, 1f), Math.Clamp(clouds.Coverage, 0f, 1f), 1f / MathF.Max(clouds.DetailScale, 1f), Math.Clamp(clouds.Erosion, 0f, 1f));
        s.Lighting = new Float4(MathF.Max(clouds.Density, 0f) * 0.004f, MathF.Max(clouds.SunBrightness, 0f), MathF.Max(clouds.AmbientBrightness, 0f), Math.Clamp(clouds.SilverLining, 0f, 1f));
        s.Albedo = new Float4(clouds.Albedo.R, clouds.Albedo.G, clouds.Albedo.B, clouds.Albedo.A);
        s.Wind = clouds.WindOffset;
        (s.WindX, s.WindZ) = (clouds.WindX, clouds.WindZ);
        s.Puff = new Float2(Math.Clamp(clouds.PuffStrength, 0f, 1f), MathF.Max(clouds.PuffScale, 0.01f));
        s.AmbientSaturation = Math.Clamp(clouds.AmbientSaturation, 0f, 1f);
        s.HorizonFade = 1f / MathF.Max(clouds.HorizonFade, 100f);
        s.Reach = MathF.Max(clouds.Reach, 200f);
        s.DetailScale = MathF.Max(clouds.DetailScale, 1f);
        s.LightSamples = clouds.MarchToSun ? Math.Clamp(clouds.LightSamples, 1, 16) : 0;
        s.Scale = Math.Max((int)clouds.Resolution, 1);
        s.Temporal = clouds.TemporalUpscale;

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

        Float2 windDirection = clouds.WindVector;
        s.LayerCount = 0;
        if (clouds.Layers != null)
        {
            foreach (CloudLayer layer in clouds.Layers)
            {
                if (s.LayerCount == MaxLayers) break;
                if (layer == null || !layer.Enabled || layer.Opacity <= 0f) continue;
                Texture? texture = null;
                if (layer.Style == CloudLayerStyle.Custom)
                {
                    if (layer.Texture.IsNotValid()) continue;
                    texture = layer.Texture.GpuTexture;
                    if (texture == null) continue;
                }
                Float2 wind = clouds.WindOffset * layer.WindMultiplier;
                s.Layers[s.LayerCount++] = new Layer
                {
                    Shape = new Float4(layer.Altitude, Math.Clamp(layer.Coverage, 0f, 1f), Math.Clamp(layer.Opacity, 0f, 1f), 1f / MathF.Max(layer.Scale, 1f)),
                    Wind = new Float4(wind.X, wind.Y, windDirection.X, windDirection.Y),
                    Tint = new Float4(layer.Tint.R, layer.Tint.G, layer.Tint.B, (int)layer.Style),
                    Texture = texture,
                };
            }
        }

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
        Add(clouds.Altitude); Add(clouds.Thickness); Add(clouds.Coverage); Add(clouds.Density);
        Add(clouds.PatternScale); Add(clouds.Erosion); Add(clouds.DetailScale); Add(clouds.PuffStrength);
        Add(clouds.PuffScale); Add(clouds.Albedo.R); Add(clouds.Albedo.G); Add(clouds.Albedo.B);
        Add(clouds.SunBrightness); Add(clouds.AmbientBrightness); Add(clouds.AmbientSaturation);
        Add(clouds.SilverLining); Add(clouds.HorizonFade); Add(clouds.Reach);
        Add(clouds.MarchToSun ? 1f : 0f); Add(clouds.LightSamples); Add(clouds.PlanetRadius);
        Add(clouds.Noise.IsValid() ? clouds.Noise.InstanceID : 0, 0f);
        Add(clouds.ErosionNoise.IsValid() ? clouds.ErosionNoise.InstanceID : 0, 0f);
        Add(s.ToSun.X, 0.1f); Add(s.ToSun.Y, 0.1f); Add(s.ToSun.Z, 0.1f);
        Add(s.SunColor.X, 0.1f); Add(s.SunColor.Y, 0.1f); Add(s.SunColor.Z, 0.1f);

        if (clouds.Layers != null)
        {
            foreach (CloudLayer layer in clouds.Layers)
            {
                if (layer == null) continue;
                Add(layer.Enabled ? 1f : 0f); Add((int)layer.Style); Add(layer.Altitude);
                Add(layer.Coverage); Add(layer.Opacity); Add(layer.Scale); Add(layer.WindMultiplier);
                Add(layer.Tint.R); Add(layer.Tint.G); Add(layer.Tint.B);
                Add(layer.Texture.IsValid() ? layer.Texture.InstanceID : 0, 0f);
            }
        }

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
        (int, bool, bool) declared = (_settings.Scale, _settings.Temporal, _settings.LightSamples == 0);
        if (declared != _declared)
        {
            _declared = declared;
            Invalidate();
        }
    }

    public override bool IsActive(in FeatureView view) => _settings.Noise2D is { IsDisposed: false } && _settings.Noise3D is { IsDisposed: false };

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
        ViewState state = builder.GetViewState<ViewState>();

        // A probe capture turns to new faces every render, so there is nothing to carry over between them
        state.Temporal = _declared.Temporal && view.Kind != ViewKind.ReflectionProbe;
        state.Block = state.Temporal ? 2 : 1;
        int outWidth = Math.Max(1, (int)view.Width / _declared.Scale), outHeight = Math.Max(1, (int)view.Height / _declared.Scale);
        if (state.Temporal)
        {
            outWidth = (outWidth + 1) & ~1;
            outHeight = (outHeight + 1) & ~1;
        }
        (state.OutWidth, state.OutHeight) = (outWidth, outHeight);
        int width = outWidth / state.Block, height = outHeight / state.Block;
        uint layers = view.LayerCount > 1 ? (uint)view.LayerCount : 0;

        state.Coverage = builder.Temporary("Coverage", FeatureView.Shared(CoverageResolution, CoverageResolution, PixelFormat.R16_Float));
        if (_declared.SunMap)
            state.Sun = builder.Temporary("Sun", FeatureView.Shared(CoverageResolution, CoverageResolution, PixelFormat.R16_G16_B16_A16_Float));
        state.Depth = builder.Temporary("Depth", view.Texture(width, height, PixelFormat.R32_Float));
        state.Target = builder.Temporary("Target", GraphTextureDesc.Sized(width, height, false, PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R32_Float) with { ArrayLayers = layers });
        if (state.Temporal)
        {
            TextureState usage = TextureState.Attachment | TextureState.Sampled;
            state.History = builder.History("History", GraphTextureDesc.Sized(outWidth, outHeight, false, PixelFormat.R16_G16_B16_A16_Float, PixelFormat.R32_Float) with { ArrayLayers = layers }, usage: usage);
            state.TargetDepth = builder.History("TargetDepth", view.Texture(outWidth, outHeight, PixelFormat.R32_Float), usage: usage);
        }
    }

    public override void Render(FeatureContext context)
    {
        ViewState state = context.GetViewState<ViewState>();
        CloudSettings s = _settings;
        Double3 camera = context.View.Position;

        _properties.SetTexture("_CloudNoise2D", s.Noise2D!, _linearRepeat);
        _properties.SetTexture("_CloudNoise3D", s.Noise3D!, _linearRepeat);
        _properties.SetFloat("_CloudErosionSize", s.ErosionSize);
        _properties.SetFloat4("_CloudShape", s.Shape);
        _properties.SetFloat4("_CloudPattern", s.Pattern);
        _properties.SetFloat2("_CloudWind", s.Wind);
        _properties.SetFloat3("_CloudToSun", s.ToSun);
        DrawMaps(context, state, camera);

        // With temporal upscaling the clouds draw one pixel of each 2x2 block a frame in turn, and the history fills in the rest
        Int2 cell = default;
        Float2 jitter = Float2.Zero;
        if (state.Temporal)
        {
            cell = s_jitterCells[state.Frame++ & 3];
            jitter = new Float2((cell.X + 0.5f) / state.Block - 0.5f, (cell.Y + 0.5f) / state.Block - 0.5f);
        }

        RenderTexture depth = context.Get(state.Depth), target = context.Get(state.Target);
        Float2 low = new(depth.ColorTextures[0].Width, depth.ColorTextures[0].Height);
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, _pointClamp);
        _properties.SetFloat2("_CloudFullResolution", new Float2(context.View.Width, context.View.Height));
        _properties.SetFloat2("_CloudLowResolution", low);
        _properties.SetFloat2("_CloudJitter", jitter);
        _properties.SetInt("_CloudDownsample", _declared.Scale);
        _properties.SetInt("_CloudDepthMode", state.Temporal ? 1 : 0);
        context.Fullscreen(_depth, depth, _properties);

        _properties.SetTexture("_CloudSceneDepth", depth.ColorTextures[0], _pointClamp);
        _properties.SetFloat3("_CloudCamera", new Float3((float)camera.X, (float)camera.Y, (float)camera.Z));
        _properties.SetFloat4("_CloudLighting", s.Lighting);
        _properties.SetFloat3("_CloudSunColor", s.SunColor);
        _properties.SetFloat3("_CloudSkyLight", s.SkyLight);
        _properties.SetFloat4("_CloudAlbedo", s.Albedo);
        _properties.SetFloat("_CloudAmbientSaturation", s.AmbientSaturation);
        _properties.SetFloat("_CloudHorizonFade", s.HorizonFade);
        _properties.SetInt("_CloudLightSamples", s.LightSamples);
        _properties.SetFloat2("_CloudPuff", s.Puff);
        SetLayers(s);
        _properties.SetFloat("_CloudReach", s.Reach);
        context.Fullscreen(_march, target, _properties);

        RenderTexture result = target, resultDepth = depth;
        if (state.Temporal)
        {
            // Scene depth at the full target resolution, which the history is checked against and the composite matches
            RenderTexture outDepth = context.Get(state.TargetDepth);
            RenderTexture? previousDepth = context.History(state.TargetDepth);
            _properties.SetFloat2("_CloudLowResolution", new Float2(state.OutWidth, state.OutHeight));
            _properties.SetInt("_CloudDepthMode", 0);
            context.Fullscreen(_depth, outDepth, _properties);

            RenderTexture history = context.Get(state.History);
            RenderTexture? previous = context.History(state.History);
            bool valid = previous != null && previousDepth != null && context.HasPreviousFrame && state.Epoch == s.Epoch;
            _properties.SetTexture("_CloudTargetDepth", outDepth.ColorTextures[0], _pointClamp);
            _properties.SetTexture("_CloudPreviousTargetDepth", (previousDepth ?? outDepth).ColorTextures[0], _pointClamp);
            _properties.SetTexture("_CloudLowDepth", depth.ColorTextures[0], _pointClamp);
            _properties.SetFloat2("_CloudLowResolution", low);
            _properties.SetTexture("_CloudCurrent", target.ColorTextures[0], _pointClamp);
            _properties.SetTexture("_CloudCurrentDistance", target.ColorTextures[1], _pointClamp);
            _properties.SetTexture("_CloudHistory", (previous ?? target).ColorTextures[0], _linearClamp);
            _properties.SetTexture("_CloudHistoryDistance", (previous ?? target).ColorTextures[1], _linearClamp);
            _properties.SetFloat2("_CloudHistoryResolution", new Float2(state.OutWidth, state.OutHeight));
            _properties.SetFloat2("_CloudJitterCell", new Float2(cell.X, cell.Y));
            _properties.SetInt("_CloudBlock", state.Block);
            _properties.SetFloat("_CloudHistoryValid", valid ? 1f : 0f);
            _properties.SetFloat2("_CloudWindStep", new Float2((float)(s.WindX - state.PreviousWindX), (float)(s.WindZ - state.PreviousWindZ)));
            context.Fullscreen(_temporal, history, _properties);

            (state.PreviousWindX, state.PreviousWindZ, state.Epoch) = (s.WindX, s.WindZ, s.Epoch);
            (result, resultDepth) = (history, outDepth);
        }

        _properties.SetTexture("_CloudTex", result.ColorTextures[0], _pointClamp);
        _properties.SetTexture("_CloudTexDistance", result.ColorTextures[1], _pointClamp);
        _properties.SetTexture("_CloudSceneDepth", resultDepth.ColorTextures[0], _pointClamp);
        _properties.SetFloat2("_CloudLowResolution", new Float2(result.ColorTextures[0].Width, result.ColorTextures[0].Height));
        context.Fullscreen(_composite, _properties);
    }

    // The maps cover the clouds' reach around the view's camera. Their texels ride the wind like the clouds do, so a point
    // of cloud reads the same coverage every frame rather than one interpolated afresh as it slides across fixed texels
    private void DrawMaps(FeatureContext context, ViewState state, Double3 camera)
    {
        CloudSettings s = _settings;
        float span = s.Reach * 2f;
        float texel = span / CoverageResolution;
        Float2 min = LatticeOrigin(camera.X, camera.Z, s.Wind, texel) - new Float2(span * 0.5f, span * 0.5f);
        _properties.SetFloat4("_CloudCoverageRect", new Float4(min.X, min.Y, 1f / span, span));

        RenderTexture coverage = context.Get(state.Coverage);
        context.Fullscreen(_coverage, coverage, _properties, shared: true);
        _properties.SetTexture("_CloudCoverage", coverage.ColorTextures[0], _linearClamp);

        if (!_declared.SunMap)
        {
            _properties.SetTexture("_CloudSunDepth", coverage.ColorTextures[0], _linearClamp);
            return;
        }

        // Its noise is blurred to the map's texel size
        RenderTexture sun = context.Get(state.Sun);
        _properties.SetFloat("_CloudSunMapLod", MathF.Max(MathF.Log2(texel / s.DetailScale * s.ErosionSize), 0f));
        context.Fullscreen(_sun, sun, _properties, shared: true);
        _properties.SetTexture("_CloudSunDepth", sun.ColorTextures[0], _linearClamp);
    }

    private void SetLayers(CloudSettings s)
    {
        _properties.SetInt("_CloudLayerCount", s.LayerCount);
        for (int i = 0; i < MaxLayers; i++)
        {
            Layer layer = i < s.LayerCount ? s.Layers[i] : default;
            _properties.SetFloat4(s_layerShape[i], layer.Shape);
            _properties.SetFloat4(s_layerWind[i], layer.Wind);
            _properties.SetFloat4(s_layerTint[i], layer.Tint);
            _properties.SetTexture(s_layerTexture[i], layer.Texture is { IsDisposed: false } texture ? texture : s.Noise2D!, _linearRepeat);
        }
    }

    /// <summary>The lattice point nearest the camera, on a lattice of <paramref name="cellSpan"/> the wind carries along.</summary>
    private static Float2 LatticeOrigin(double x, double z, Float2 wind, float cellSpan)
        => new((float)(wind.X + Math.Round((x - wind.X) / cellSpan) * cellSpan), (float)(wind.Y + Math.Round((z - wind.Y) / cellSpan) * cellSpan));

    public void Dispose()
    {
        _coverage.Dispose();
        _sun.Dispose();
        _depth.Dispose();
        _march.Dispose();
        _temporal.Dispose();
        _composite.Dispose();
    }
}
