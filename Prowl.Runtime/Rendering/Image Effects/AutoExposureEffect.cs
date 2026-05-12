// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Auto exposure (eye adaptation) effect. Measures scene luminance via a progressive
/// downsample chain and smoothly adapts exposure over time using a persistent 1x1
/// render target. Works entirely in fragment shaders - no compute or SSBOs required.
///
/// Place this effect BEFORE Bloom and Tonemapper in the Camera's effect list so the
/// exposure adjustment feeds into downstream effects.
/// </summary>
public sealed class AutoExposureEffect : ImageEffect
{
    /// <summary>Exposure compensation in EV stops. 0 = auto, positive = brighter, negative = darker.</summary>
    public float ExposureCompensation = 0f;

    /// <summary>Adaptation speed when scene gets brighter (higher = faster snap to bright).</summary>
    public float AdaptSpeedUp = 3.0f;

    /// <summary>Adaptation speed when scene gets darker (higher = faster snap to dark).</summary>
    public float AdaptSpeedDown = 1.5f;

    /// <summary>Minimum allowed exposure multiplier. Prevents over-darkening in very bright scenes.</summary>
    public float MinExposure = 0.1f;

    /// <summary>Maximum allowed exposure multiplier. Prevents over-brightening in very dark scenes.</summary>
    public float MaxExposure = 10.0f;

    private Material _mat;
    private RenderTexture _adaptedLuminance;
    private bool _historyValid;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.AutoExposure));

        int w = context.Width / 2;
        int h = context.Height / 2;

        // ---- Step 1: Extract log-luminance at half resolution ----
        var lumRT = RenderTexture.GetTemporaryRT(w, h, false, [TextureImageFormat.Short]);
        RenderPipeline.Blit(context.SceneColor, lumRT, _mat, 0);

        // ---- Step 2: Downsample chain until we reach a small enough size ----
        var mipChain = new List<RenderTexture>();
        mipChain.Add(lumRT);

        var current = lumRT;
        while (w > 2 || h > 2)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);

            var downRT = RenderTexture.GetTemporaryRT(w, h, false, [TextureImageFormat.Short]);
            RenderPipeline.Blit(current, downRT, _mat, 1);
            mipChain.Add(downRT);
            current = downRT;
        }

        // ---- Step 3: Temporal adaptation ----
        // Ensure the persistent 1x1 adaptation RT exists
        if (_adaptedLuminance != null && _adaptedLuminance.IsDisposed)
        {
            _adaptedLuminance = null;
            _historyValid = false;
        }

        _adaptedLuminance ??= new RenderTexture(1, 1, false, [TextureImageFormat.Short]);

        // Adapt: blend measured luminance with previous adapted value
        var newAdapted = RenderTexture.GetTemporaryRT(1, 1, false, [TextureImageFormat.Short]);
        _mat.SetTexture("_AdaptedTex", _adaptedLuminance.MainTexture);
        _mat.SetFloat("_AdaptSpeedUp", Math.Max(0.01f, AdaptSpeedUp));
        _mat.SetFloat("_AdaptSpeedDown", Math.Max(0.01f, AdaptSpeedDown));
        _mat.SetFloat("_HistoryValid", _historyValid ? 1.0f : 0.0f);
        RenderPipeline.Blit(current, newAdapted, _mat, 2);

        // Store adapted luminance for next frame
        RenderPipeline.Blit(newAdapted, _adaptedLuminance, null, 0);
        RenderTexture.ReleaseTemporaryRT(newAdapted);
        _historyValid = true;

        // ---- Step 4: Apply exposure to scene color ----
        _mat.SetTexture("_AdaptedTex", _adaptedLuminance.MainTexture);
        _mat.SetFloat("_ExposureComp", ExposureCompensation);
        _mat.SetFloat("_MinExposure", MinExposure);
        _mat.SetFloat("_MaxExposure", MaxExposure);

        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [context.SceneColor.MainTexture.ImageFormat]);
        RenderPipeline.Blit(context.SceneColor, temp, _mat, 3);
        RenderPipeline.Blit(temp, context.SceneColor, null, 0);
        RenderTexture.ReleaseTemporaryRT(temp);

        // ---- Cleanup ----
        foreach (var rt in mipChain)
            RenderTexture.ReleaseTemporaryRT(rt);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
        _adaptedLuminance?.Dispose();
        _adaptedLuminance = null;
        _historyValid = false;
    }
}
