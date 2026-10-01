// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Bloom effect using dual filtering (downsample + upsample) for fast, high-quality blur.
/// Much faster than separable Gaussian or Kawase ping-pong at equivalent quality.
/// </summary>
public sealed class BloomEffect : ImageEffect
{
    /// <summary>Bloom intensity multiplier.</summary>
    public float Intensity = 1.5f;

    /// <summary>Brightness above which pixels bloom.</summary>
    public float Threshold = 0.8f;

    /// <summary>How gradually pixels near the threshold fade into the bloom (0 = hard cut, 1 = widest ramp).</summary>
    public float SoftKnee = 0.5f;

    /// <summary>Brightest color fed into the bloom, so a few extreme HDR pixels can't blow it out.</summary>
    public float Clamp = 65000f;

    /// <summary>Weights the first blur by brightness so single pixel sparkles can't flicker the bloom.
    /// Also dims small bright sources noticeably, so leave it off unless sparkles are a problem.</summary>
    public bool AntiFlicker = false;

    /// <summary>Number of downsample iterations. More = wider bloom but more GPU cost. 4-8 is typical.</summary>
    public int Iterations = 6;

    /// <summary>How far the bloom spreads (0..1). Low keeps it tight around bright spots, high favors
    /// the wide, soft levels. Brightness stays about the same either way.</summary>
    public float Scatter = 0.7f;

    private Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        if (_mat.IsNotValid()) _mat = new Material(Shader.LoadDefault(DefaultShader.Bloom));

        int w = context.Width / 2;
        int h = context.Height / 2;
        var format = context.SceneColor.MainTexture.ImageFormat;

        using var cmd = Graphics.GetCommandBuffer("Bloom");

        // Pass 0: Threshold extract bright pixels into half-res
        RenderTexture thresholdRT = RenderTexture.GetTemporaryRT(w, h, false, [format]);
        _mat.SetFloat("_Threshold", System.MathF.Max(0f, Threshold));
        _mat.SetFloat("_SoftKnee", Maths.Clamp(SoftKnee, 0f, 1f));
        _mat.SetFloat("_Clamp", System.MathF.Max(0f, Clamp));
        _mat.SetFloat("_AntiFlicker", AntiFlicker ? 1f : 0f);
        cmd.Blit(context.SceneColor, thresholdRT, _mat, 0);

        // Downsample chain each iteration halves resolution
        var mipChain = new List<RenderTexture>();
        mipChain.Add(thresholdRT);

        RenderTexture current = thresholdRT;
        for (int i = 0; i < Iterations; i++)
        {
            w = System.Math.Max(1, w / 2);
            h = System.Math.Max(1, h / 2);

            RenderTexture downRT = RenderTexture.GetTemporaryRT(w, h, false, [format]);
            cmd.Blit(current, downRT, _mat, 1); // Pass 1: Downsample
            mipChain.Add(downRT);
            current = downRT;
        }

        // Upsample chain: walk back up, each level mixing its own downsample with the blurred level below
        // into a fresh target. The smallest level is its own result.
        _mat.SetFloat("_Scatter", Maths.Clamp(Scatter, 0f, 1f));
        var upChain = new List<RenderTexture>();
        RenderTexture lower = mipChain[^1];
        for (int i = mipChain.Count - 2; i >= 0; i--)
        {
            RenderTexture high = mipChain[i];
            RenderTexture upRT = RenderTexture.GetTemporaryRT(high.Width, high.Height, false, [format]);
            _mat.SetTexture("_HighTex", high.MainTexture);
            cmd.Blit(lower, upRT, _mat, 2); // Pass 2: Upsample
            upChain.Add(upRT);
            lower = upRT;
        }

        // Pass 3: Composite add bloom to original scene
        _mat.SetTexture("_BloomTex", lower.MainTexture);
        _mat.SetFloat("_Intensity", Intensity);
        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [context.SceneColor.MainTexture.ImageFormat]);
        cmd.Blit(context.SceneColor, temp, _mat, 3);
        cmd.Blit(temp, context.SceneColor, null, 0);
        Graphics.Submit(cmd);
        RenderTexture.ReleaseTemporaryRT(temp);

        foreach (var rt in mipChain)
            RenderTexture.ReleaseTemporaryRT(rt);
        foreach (var rt in upChain)
            RenderTexture.ReleaseTemporaryRT(rt);
    }

    public override void OnDisable()
    {
        if (_mat.IsValid()) _mat.Dispose();
        _mat = null;
    }
}
