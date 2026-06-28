// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Per-pixel motion blur using the motion vectors buffer.
/// Samples the scene color along each pixel's motion vector to simulate camera
/// and object motion blur. Uses depth-aware weighting to reduce background
/// bleeding through foreground objects.
///
/// Motion vectors come from the unified prepass (always produced).
/// </summary>
public sealed class MotionBlurEffect : ImageEffect
{
    public override RenderStage Stage => RenderStage.PostProcess;

    /// <summary>Blur intensity multiplier. 1.0 = physically correct, higher = exaggerated.</summary>
    public float Intensity = 1.0f;

    /// <summary>Number of samples along the motion vector (4-16 typical). More = smoother but slower.</summary>
    public int Samples = 8;

    /// <summary>Maximum blur radius in pixels. Prevents extreme streaking.</summary>
    public float MaxBlurRadius = 40.0f;

    private Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.MotionBlur));

        _mat.SetVector("_Resolution", new Float2(context.Width, context.Height));
        _mat.SetFloat("_Intensity", Intensity);
        _mat.SetInt("_Samples", Maths.Clamp(Samples, 1, 32));
        _mat.SetFloat("_MaxBlurRadius", Math.Max(1.0f, MaxBlurRadius));

        if (context.MotionVectors != null)
            _mat.SetTexture("_MotionVectorsTex", context.MotionVectors);
        _mat.SetTexture("_CameraDepthTexture", context.DepthNormals.InternalDepth);

        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false,
            [context.SceneColor.MainTexture.ImageFormat]);
        var cmd = Graphics.GetCommandBuffer("MotionBlur");
        cmd.Blit(context.SceneColor, temp, _mat, 0);
        cmd.Blit(temp, context.SceneColor, null, 0);
        Graphics.Submit(cmd);
        RenderTexture.ReleaseTemporaryRT(temp);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
    }
}
