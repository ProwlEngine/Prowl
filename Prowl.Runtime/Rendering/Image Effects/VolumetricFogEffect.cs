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

    // ── Temporal reprojection ──
    /// <summary>
    /// Blend the previous frame's low-res fog into this frame. Drops a LOT of the
    /// ray-march noise at the cost of a small amount of ghosting on fast-moving lights.
    /// </summary>
    public bool EnableTemporalReprojection = true;

    /// <summary>How much of the reprojected history to keep (0..0.99). Higher is smoother
    /// but ghosts more. 0.9 is a good default.</summary>
    public float TemporalBlendWeight = 0.9f;

    private const int MaxFogVolumes = 16;

    private Material _mat;

    // Persistent low-res history for temporal reprojection. Recreated on resolution change.
    private RenderTexture _history;
    private bool _historyValid;

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

        UploadFogToggles();
        UploadFogVolumes(context);

        var format = context.SceneColor.MainTexture.ImageFormat;

        // Drop history if the low-res size changed reprojecting against a
        // differently-sized history is garbage and a one-frame flash is fine.
        if (_history != null && (_history.Width != lowW || _history.Height != lowH))
        {
            _history.Dispose();
            _history = null;
            _historyValid = false;
        }

        using var cmd = Graphics.GetCommandBuffer("VolumetricFog");

        // Pass 0 Ray march into low-res.
        var currentLow = RenderTexture.GetTemporaryRT(lowW, lowH, false, [format]);
        cmd.Blit(context.SceneColor, currentLow, _mat, 0);

        RenderTexture blendedLow;
        if (EnableTemporalReprojection)
        {
            _history ??= new RenderTexture(lowW, lowH, false, [format]);

            blendedLow = RenderTexture.GetTemporaryRT(lowW, lowH, false, [format]);
            _mat.SetTexture("_FogCurrentTex", currentLow.MainTexture);
            _mat.SetTexture("_FogHistoryTex", _history.MainTexture);
            _mat.SetFloat("_FogHistoryValid", _historyValid ? 1f : 0f);
            _mat.SetFloat("_FogTemporalBlend", Math.Clamp(TemporalBlendWeight, 0f, 0.99f));
            cmd.Blit(currentLow, blendedLow, _mat, 1);

            cmd.Blit(blendedLow, _history, null, 0);
            _historyValid = true;
        }
        else
        {
            blendedLow = currentLow;
            _historyValid = false;
        }

        // Pass 2 Bilateral upsample + composite onto scene color.
        _mat.SetTexture("_FogTex", blendedLow.MainTexture);
        var fullRes = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [format]);
        cmd.Blit(context.SceneColor, fullRes, _mat, 2);
        cmd.Blit(fullRes, context.SceneColor, null, 0);
        Graphics.Submit(cmd);

        if (!ReferenceEquals(blendedLow, currentLow))
            RenderTexture.ReleaseTemporaryRT(blendedLow);
        RenderTexture.ReleaseTemporaryRT(currentLow);
        RenderTexture.ReleaseTemporaryRT(fullRes);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
        _history?.Dispose();
        _history = null;
        _historyValid = false;
    }

    /// <summary>
    /// Push the type-level enable / shadow toggles. With the BVH refactor every BVH light
    /// contributes to fog automatically; the per-light overrides that the old FogLight
    /// component carried are gone (no stable forward slot to attach them to). The global
    /// <c>Scattering</c> / <c>Extinction</c> uniforms still apply to everyone.
    /// </summary>
    private void UploadFogToggles()
    {
        _mat.SetInt("_FogEnableDirectional", EnableDirectional ? 1 : 0);
        _mat.SetInt("_FogEnableDirectionalShadows", EnableDirectionalShadows ? 1 : 0);
        _mat.SetInt("_FogEnablePointLights", EnablePointLights ? 1 : 0);
        _mat.SetInt("_FogEnablePointShadows", EnablePointLightShadows ? 1 : 0);
        _mat.SetInt("_FogEnableSpotLights", EnableSpotLights ? 1 : 0);
        _mat.SetInt("_FogEnableSpotShadows", EnableSpotLightShadows ? 1 : 0);
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

        // Sort by distance globals (negative) come first, then nearest shapes.
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
