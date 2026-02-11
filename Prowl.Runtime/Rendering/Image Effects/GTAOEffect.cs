// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Ground-Truth Ambient Occlusion (GTAO) effect for realistic ambient occlusion.
/// Based on "Practical Realtime Strategies for Accurate Indirect Occlusion" by Activision.
/// Reference: https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
/// </summary>
public sealed class GTAOEffect : ImageEffect
{
    // Quality Settings

    /// <summary>Number of angular slices to sample around each pixel. Higher = better quality but slower.</summary>
    public int Slices = 6; // [1, 2, 3, 4, 5, 6]

    /// <summary>Number of samples per direction. Higher = better quality but slower.</summary>
    public int DirectionSamples = 8; // [1, 2, 3, 4, 5, 6, 8]

    // Appearance Settings

    /// <summary>Sampling radius in world space units. Larger = more prominent occlusion from distant objects.</summary>
    public float Radius = 1.0f;

    /// <summary>Overall intensity of the ambient occlusion effect. Higher = darker occlusion.</summary>
    public float Intensity = 1.0f;

    /// <summary>Blur radius for denoising. 0 = no blur (noisy but sharp), higher = smoother but less detailed.</summary>
    public float BlurRadius = 1.0f;

    /// <summary>Resolution scale for AO calculation. 1.0 = full resolution, 0.5 = half resolution (better performance).</summary>
    public float ResolutionScale = 1.0f;

    // Private fields
    private Material _mat;

    public override RenderStage Stage => RenderStage.AfterLighting; // Run after deferred composition but before transparents

    public override void OnRenderEffect(RenderContext context)
    {
        // Lazy initialize material
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.GTAO));

        // Calculate scaled resolution
        int width = (int)(context.Width * ResolutionScale);
        int height = (int)(context.Height * ResolutionScale);

        // Allocate temporary render textures
        RenderTexture aoRT = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Color4b]);

        // Pass 0: Calculate GTAO
        _mat.SetInt("_Slices", Slices);
        _mat.SetInt("_DirectionSamples", DirectionSamples);
        _mat.SetFloat("_Radius", Radius);
        _mat.SetFloat("_Intensity", Intensity);
        _mat.SetVector("_NoiseScale", new Float2(width / 4.0f, height / 4.0f)); // Tile noise pattern
        RenderPipeline.Blit(context.SceneColor, aoRT, _mat, 0);

        // Pass 1: Blur Horizontal (if blur is enabled)
        if (BlurRadius > 0.01f)
        {
            RenderTexture blurTempRT = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Color4b]);

            _mat.SetVector("_BlurDirection", new Float2(1.0f, 0.0f));
            _mat.SetFloat("_BlurRadius", BlurRadius);
            RenderPipeline.Blit(aoRT, blurTempRT, _mat, 1);

            // Pass 1: Blur Vertical
            _mat.SetVector("_BlurDirection", new Float2(0.0f, 1.0f));
            _mat.SetFloat("_BlurRadius", BlurRadius);
            RenderPipeline.Blit(blurTempRT, aoRT, _mat, 1);

            RenderTexture.ReleaseTemporaryRT(blurTempRT);
        }

        // Pass 2: Composite - Apply AO to scene (in-place)
        _mat.SetTexture("_AOTex", aoRT.MainTexture);
        _mat.SetFloat("_Intensity", Intensity);
        RenderPipeline.Blit(context.SceneColor, context.SceneColor, _mat, 2);

        // Release temporary render textures
        RenderTexture.ReleaseTemporaryRT(aoRT);
    }

    public override void OnPostRender(Camera camera)
    {
        // Cleanup could be done here if needed
    }
}
