// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;

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
    public float Intensity = 1f;

    /// <summary>Luminance threshold for bright pixel extraction.</summary>
    public float Threshold = 0.6f;

    /// <summary>Number of downsample iterations. More = wider bloom but more GPU cost. 4-8 is typical.</summary>
    public int Iterations = 4;

    private Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.Bloom));

        int w = context.Width / 2;
        int h = context.Height / 2;
        var format = context.SceneColor.MainTexture.ImageFormat;

        // Pass 0: Threshold — extract bright pixels into half-res
        RenderTexture thresholdRT = RenderTexture.GetTemporaryRT(w, h, false, [format]);
        _mat.SetFloat("_Threshold", Threshold);
        RenderPipeline.Blit(context.SceneColor, thresholdRT, _mat, 0);

        // Downsample chain — each iteration halves resolution
        var mipChain = new List<RenderTexture>();
        mipChain.Add(thresholdRT);

        RenderTexture current = thresholdRT;
        for (int i = 0; i < Iterations; i++)
        {
            w = System.Math.Max(1, w / 2);
            h = System.Math.Max(1, h / 2);

            RenderTexture downRT = RenderTexture.GetTemporaryRT(w, h, false, [format]);
            RenderPipeline.Blit(current, downRT, _mat, 1); // Pass 1: Downsample
            mipChain.Add(downRT);
            current = downRT;
        }

        // Upsample chain — walk back up, each iteration doubles resolution
        for (int i = mipChain.Count - 1; i > 0; i--)
        {
            RenderTexture src = mipChain[i];
            RenderTexture dst = mipChain[i - 1];

            RenderPipeline.Blit(src, dst, _mat, 2); // Pass 2: Upsample (overwrites dst)
        }

        // Pass 3: Composite — add bloom to original scene
        _mat.SetTexture("_BloomTex", mipChain[0].MainTexture);
        _mat.SetFloat("_Intensity", Intensity);
        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [context.SceneColor.MainTexture.ImageFormat]);
        RenderPipeline.Blit(context.SceneColor, temp, _mat, 3);
        RenderPipeline.Blit(temp, context.SceneColor, null, 0);
        RenderTexture.ReleaseTemporaryRT(temp);

        // Release all mip chain RTs
        foreach (var rt in mipChain)
            RenderTexture.ReleaseTemporaryRT(rt);
    }
}
