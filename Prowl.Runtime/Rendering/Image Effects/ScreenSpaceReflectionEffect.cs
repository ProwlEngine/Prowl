// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Screen Space Reflections (SSR) effect that renders realistic reflections using the visible geometry on screen.
/// Works best on smooth, metallic surfaces and automatically fades on rough surfaces.
/// </summary>
public sealed class ScreenSpaceReflectionEffect : ImageEffect
{
    // Quality Settings

    /// <summary>Maximum number of steps to march along a ray. Higher = better quality but slower.</summary>
    public float MaxSteps = 64f;

    /// <summary>Number of binary search iterations to refine ray hit position. 0 = no refinement.</summary>
    public float BinarySearchIterations = 8f;

    // Appearance Settings

    /// <summary>Overall intensity of reflections. 0 = no reflections, 1 = full strength.</summary>
    public float Intensity = 1.0f;

    /// <summary>How much reflections fade near screen edges. Higher = sharper fade.</summary>
    public float ScreenEdgeFade = 4.0f;

    /// <summary>Mip bias when sampling reflection color. Higher = blurrier reflections.</summary>
    public float MipBias = 0.0f;

    /// <summary>Blur radius applied to reflections. Higher = softer reflections.</summary>
    public float BlurRadius = 1.0f;

    /// <summary>Resolution scale for ray marching. 0.5 = half resolution (better performance).</summary>
    public float ResolutionScale = 1.0f;

    // Private fields
    private Material _mat;

    public override RenderStage Stage => RenderStage.AfterLighting; // Run after deferred composition but before transparents

    public override void OnRenderEffect(RenderContext context)
    {
        // Lazy initialize material
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.SSR));

        // Calculate scaled resolution
        int width = (int)(context.Width * ResolutionScale);
        int height = (int)(context.Height * ResolutionScale);

        // Allocate temporary render textures
        RenderTexture reflectionDataRT = RenderTexture.GetTemporaryRT(width, height, false, [TextureImageFormat.Short4]);
        RenderTexture resolvedRT = RenderTexture.GetTemporaryRT(width, height, false, [context.SceneColor.MainTexture.ImageFormat]);
        RenderTexture blurTempRT = RenderTexture.GetTemporaryRT(width, height, false, [context.SceneColor.MainTexture.ImageFormat]);

        // Pass 0: Ray March - Trace rays and output hit UVs + confidence
        _mat.SetFloat("_MaxSteps", MaxSteps);
        _mat.SetFloat("_BinarySearchIterations", BinarySearchIterations);
        _mat.SetFloat("_ScreenEdgeFade", ScreenEdgeFade);
        RenderPipeline.Blit(context.SceneColor, reflectionDataRT, _mat, 0);

        // Pass 1: Resolve - Sample scene color at hit points
        _mat.SetTexture("_ReflectionData", reflectionDataRT.MainTexture);
        _mat.SetFloat("_MipBias", MipBias);
        RenderPipeline.Blit(context.SceneColor, resolvedRT, _mat, 1);

        // Pass 2: Blur Horizontal
        _mat.SetVector("_BlurDirection", new Float2(1.0f, 0.0f));
        _mat.SetFloat("_BlurRadius", BlurRadius);
        RenderPipeline.Blit(resolvedRT, blurTempRT, _mat, 2);

        // Pass 3: Blur Vertical
        _mat.SetVector("_BlurDirection", new Float2(0.0f, 1.0f));
        _mat.SetFloat("_BlurRadius", BlurRadius);
        RenderPipeline.Blit(blurTempRT, resolvedRT, _mat, 2);

        // Pass 4: Composite - Blend reflections with scene (in-place)
        _mat.SetTexture("_ReflectionTex", resolvedRT.MainTexture);
        _mat.SetFloat("_Intensity", Intensity);
        RenderPipeline.Blit(context.SceneColor, context.SceneColor, _mat, 3);

        // Release temporary render textures
        RenderTexture.ReleaseTemporaryRT(reflectionDataRT);
        RenderTexture.ReleaseTemporaryRT(resolvedRT);
        RenderTexture.ReleaseTemporaryRT(blurTempRT);
    }

    public override void OnPostRender(Camera camera)
    {
        // Cleanup could be done here if needed
    }
}
