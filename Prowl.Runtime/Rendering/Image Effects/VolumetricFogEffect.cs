// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Ray-marches per-pixel through global density + per-volume
/// density regions, accumulating shadowed light scattering from FogLight components.
/// </summary>
public sealed class VolumetricFogEffect : ImageEffect
{
    public override RenderStage Stage => RenderStage.AfterOpaques;

    // ── Global fog properties ──
    /// <summary>Base fog density everywhere in the world. Volumes can add more on top.</summary>
    public float GlobalDensity = 0.005f;

    /// <summary>Color tint applied to all scattering.</summary>
    public Color GlobalColorTint = new(1f, 1f, 1f, 1f);

    /// <summary>Henyey-Greenstein anisotropy (-0.99..0.99). 0 = isotropic, &gt;0 = forward-scattering.</summary>
    public float Scattering = 0.5f;

    /// <summary>Multiplier applied to density when computing extinction (light absorbed by fog along the view ray).
    /// 1.0 = physically matched scattering and extinction. Lower = brighter, longer-range fog.</summary>
    public float Extinction = 1.0f;

    /// <summary>Per-pixel random color shift to break up banding. 0 = none.</summary>
    public float Dithering = 0.02f;

    /// <summary>Ambient light added to every fog sample, regardless of shadows or light coverage.
    /// Use this so dense fog in shadowed areas still has visible color (sky bounce / GI approximation)
    /// rather than going to pure black.</summary>
    public Color AmbientColor = new(0.4f, 0.5f, 0.6f, 1f);

    /// <summary>Strength of the ambient term. 0 = no ambient (dense unlit fog goes black).</summary>
    public float AmbientIntensity = 0.3f;

    // ── Light toggles ──
    public bool EnableDirectional = true;
    public bool EnableDirectionalShadows = true;
    public bool EnablePointLights = true;
    public bool EnablePointLightShadows = true;
    public bool EnableSpotLights = true;
    public bool EnableSpotLightShadows = true;

    // ── Performance ──
    /// <summary>Maximum world-space distance the ray marches.</summary>
    public float MaxDistance = 100.0f;

    /// <summary>Number of ray-march samples per pixel.</summary>
    public int Steps = 48;

    /// <summary>Render-target downsample (1=full, 2=half, 4=quarter).</summary>
    public int DownsampleScale = 2;

    /// <summary>Bilateral upsample depth-similarity threshold.</summary>
    public float UpsampleDepthThreshold = 0.1f;

    private const int MaxFogLights = 5;     // 1 directional + 4 closest point/spot
    private const int MaxFogVolumes = 16;

    private Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.VolumetricFog));

        if (context.DepthNormals == null || !context.DepthNormals.IsValid())
            return;

        int scale = Math.Clamp(DownsampleScale, 1, 4);
        int lowW = Math.Max(1, context.Width / scale);
        int lowH = Math.Max(1, context.Height / scale);

        // Common uniforms
        _mat.SetVector("_Resolution", new Float2(context.Width, context.Height));
        _mat.SetVector("_LowResolution", new Float2(lowW, lowH));
        _mat.SetTexture("_CameraDepthTexture", context.DepthNormals.InternalDepth);

        _mat.SetFloat("_FogDensity", GlobalDensity);
        _mat.SetFloat("_FogScattering", Math.Clamp(Scattering, -0.99f, 0.99f));
        _mat.SetFloat("_FogExtinction", Math.Max(0f, Extinction));
        _mat.SetFloat("_FogMaxDistance", Math.Max(0.1f, MaxDistance));
        _mat.SetInt("_FogSteps", Math.Clamp(Steps, 8, 256));
        _mat.SetColor("_FogColorTint", GlobalColorTint);
        _mat.SetFloat("_FogDithering", Math.Max(0f, Dithering));
        _mat.SetFloat("_FogUpsampleThreshold", Math.Max(0.001f, UpsampleDepthThreshold));
        _mat.SetColor("_FogAmbientColor", AmbientColor);
        _mat.SetFloat("_FogAmbientIntensity", Math.Max(0f, AmbientIntensity));

        UploadFogLights(context);
        UploadFogVolumes(context);

        var format = context.SceneColor.MainTexture.ImageFormat;

        var lowRes = RenderTexture.GetTemporaryRT(lowW, lowH, false, [format]);
        RenderPipeline.Blit(context.SceneColor, lowRes, _mat, 0);

        _mat.SetTexture("_FogTex", lowRes.MainTexture);
        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [format]);
        RenderPipeline.Blit(context.SceneColor, temp, _mat, 1);
        RenderPipeline.Blit(temp, context.SceneColor, null, 0);

        RenderTexture.ReleaseTemporaryRT(lowRes);
        RenderTexture.ReleaseTemporaryRT(temp);
    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
    }

    /// <summary>
    /// Walk the scene's FogLight components and upload fog params to whichever
    /// shader slot the underlying Light occupies (per <see cref="Light.ForwardSlot"/>).
    /// Lights that aren't currently selected by the forward pipeline are skipped —
    /// they have no shadow data on the GPU this frame anyway.
    /// </summary>
    private void UploadFogLights(RenderContext context)
    {
        // Default-initialise all slots to "off" so any unused slot ignores fog contribution.
        for (int i = 0; i < MaxFogLights; i++)
        {
            _mat.SetInt($"_FogLightEnabled[{i}]", 0);
            _mat.SetInt($"_FogLightCastShadows[{i}]", 0);
            _mat.SetInt($"_FogLightUseOverride[{i}]", 0);
            _mat.SetColor($"_FogLightOverrideColor[{i}]", new Color(1, 1, 1, 1));
            _mat.SetFloat($"_FogLightIntensity[{i}]", 1f);
            _mat.SetFloat($"_FogLightScatBias[{i}]", 0f);
        }

        var scene = Resources.Scene.Current;
        if (scene == null)
        {
            _mat.SetInt("_FogLightCount", 0);
            return;
        }

        int active = 0;
        foreach (var go in scene.ActiveObjects)
        {
            var fl = go.GetComponent<FogLight>();
            if (fl == null || !fl.EnabledInHierarchy) continue;

            var lightComp = go.GetComponent<Light>();
            if (lightComp == null || !lightComp.EnabledInHierarchy) continue;

            int slot = lightComp.ForwardSlot;
            if (slot < 0 || slot >= MaxFogLights) continue;

            LightType type = lightComp.GetLightType();
            bool typeEnabled = type switch
            {
                LightType.Directional => EnableDirectional,
                LightType.Point => EnablePointLights,
                LightType.Spot => EnableSpotLights,
                _ => false
            };
            if (!typeEnabled) continue;

            bool shadowToggle = type switch
            {
                LightType.Directional => EnableDirectionalShadows,
                LightType.Point => EnablePointLightShadows,
                LightType.Spot => EnableSpotLightShadows,
                _ => false
            };

            _mat.SetInt($"_FogLightEnabled[{slot}]", 1);
            _mat.SetInt($"_FogLightCastShadows[{slot}]", (fl.CastFogShadows && shadowToggle) ? 1 : 0);
            _mat.SetInt($"_FogLightUseOverride[{slot}]", fl.UseOverrideColor ? 1 : 0);
            _mat.SetColor($"_FogLightOverrideColor[{slot}]", fl.OverrideColor);
            _mat.SetFloat($"_FogLightIntensity[{slot}]", fl.IntensityMultiplier);
            _mat.SetFloat($"_FogLightScatBias[{slot}]", fl.ScatteringBias);
            active++;
        }

        _mat.SetInt("_FogLightCount", active);
    }

    private void UploadFogVolumes(RenderContext context)
    {
        var scene = Resources.Scene.Current;
        var collected = new List<(FogVolume vol, float distSq)>();

        if (scene != null)
        {
            Float3 camPos = context.Camera.Transform.Position;
            foreach (var go in scene.ActiveObjects)
            {
                var v = go.GetComponent<FogVolume>();
                if (v == null || !v.EnabledInHierarchy) continue;
                float dSq = v.Shape == FogVolumeShape.Global
                    ? -1f // globals always go in
                    : (float)Float3.DistanceSquared(camPos, (Float3)v.Transform.Position);
                collected.Add((v, dSq));
            }
        }

        // Sort by distance — globals (negative) come first, then nearest shapes.
        collected.Sort((a, b) => a.distSq.CompareTo(b.distSq));

        int count = Math.Min(collected.Count, MaxFogVolumes);
        for (int i = 0; i < MaxFogVolumes; i++)
        {
            int shape = 0;
            Float3 pos = Float3.Zero;
            Float3 size = Float3.One;
            Float4x4 worldToLocal = Float4x4.Identity;
            float density = 0f;
            Color color = new Color(1, 1, 1, 1);
            float falloff = 0f;
            float coneAngle = 0f;

            if (i < count)
            {
                var v = collected[i].vol;
                shape = (int)v.Shape;
                pos = (Float3)v.Transform.Position;
                size = (Float3)v.Transform.LossyScale;
                worldToLocal = v.Transform.WorldToLocalMatrix;
                density = v.DensityMultiplier;
                color = v.ColorTint;
                falloff = Math.Clamp(v.Falloff, 0f, 1f);
                coneAngle = v.ConeAngle;
            }

            _mat.SetInt($"_FogVolumeShape[{i}]", shape);
            _mat.SetVector($"_FogVolumePosition[{i}]", pos);
            _mat.SetVector($"_FogVolumeSize[{i}]", size);
            _mat.SetMatrix($"_FogVolumeWorldToLocal[{i}]", worldToLocal);
            _mat.SetFloat($"_FogVolumeDensity[{i}]", density);
            _mat.SetColor($"_FogVolumeColor[{i}]", color);
            _mat.SetFloat($"_FogVolumeFalloff[{i}]", falloff);
            _mat.SetFloat($"_FogVolumeConeAngle[{i}]", coneAngle);
        }

        _mat.SetInt("_FogVolumeCount", count);
    }
}
