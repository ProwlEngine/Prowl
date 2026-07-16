// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Subpixel Morphological Anti-Aliasing (SMAA 1x) with luma edge detection.
/// A spatial post-process alternative to <see cref="FXAAEffect"/> with cleaner edges
/// and no temporal ghosting. Runs on LDR/perceptual color, so place it late in
/// <see cref="Camera.Effects"/> (after tonemapping), like FXAA. Don't stack it with
/// FXAA or TAA.
/// </summary>
public sealed class SMAAEffect : ImageEffect
{
    /// <summary>Edge detection sensitivity (SMAA_THRESHOLD). Lower values catch more
    /// edges at higher cost. Range ~[0.05, 0.2]; 0.1 is a good default.</summary>
    public float EdgeThreshold = 0.1f;

    private Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.SMAA));

        int w = context.Width;
        int h = context.Height;

        _mat.SetVector("_Resolution", new Float2(w, h));
        _mat.SetFloat("_EdgeThreshold", EdgeThreshold);
        _mat.SetTexture("_AreaTex", SMAALookupTextures.AreaTex);
        _mat.SetTexture("_SearchTex", SMAALookupTextures.SearchTex);

        // Edges (rg) and blend weights (rgba) are data buffers, kept at RGBA8; the
        // final blended result uses the scene color format.
        var edgesRT = RenderTexture.GetTemporaryRT(w, h, false, [PixelFormat.R8_G8_B8_A8_UNorm]);
        var blendRT = RenderTexture.GetTemporaryRT(w, h, false, [PixelFormat.R8_G8_B8_A8_UNorm]);
        var outRT = RenderTexture.GetTemporaryRT(w, h, false, [context.SceneColor.MainTexture.ImageFormat]);

        using var cmd = Graphics.GetCommandBuffer("SMAA");

        // Pass 0: luma edge detection. The shader discards non-edge pixels, so clear
        // the pooled RT to 0 first, otherwise stale pool data would leak into pass 1.
        cmd.Blit(context.SceneColor, edgesRT, _mat, 0, clear: true);

        // Pass 1: blend weight calculation (reads edges as _MainTex + Area/Search LUTs).
        cmd.Blit(edgesRT, blendRT, _mat, 1, clear: true);

        // Pass 2: neighborhood blending (reads scene color as _MainTex + weights).
        _mat.SetTexture("_BlendTex", blendRT.MainTexture);
        cmd.Blit(context.SceneColor, outRT, _mat, 2);
        cmd.Blit(outRT, context.SceneColor, null, 0);

        Graphics.Submit(cmd);

        RenderTexture.ReleaseTemporaryRT(edgesRT);
        RenderTexture.ReleaseTemporaryRT(blendRT);
        RenderTexture.ReleaseTemporaryRT(outRT);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
    }
}
