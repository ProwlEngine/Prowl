// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Screen-Space Path Tracing (SSPT) effect for real-time global illumination.
/// Traces rays in screen space to sample indirect lighting from the light accumulation buffer.
/// Works directly with lighting data for physically accurate GI.
/// </summary>
public sealed class SSPTEffect : ImageEffect
{
    // Quality Settings

    /// <summary>Number of ray samples per pixel. Higher = better quality but much slower.</summary>
    public int SamplesPerPixel = 1; // [1, 2, 3, 4, 6, 8]

    /// <summary>Maximum number of bounces per ray. Higher = more accurate multi-bounce GI but slower.</summary>
    public int MaxBounces = 4; // [1, 2, 3, 4]

    /// <summary>Maximum number of steps per ray. Higher = better quality but slower.</summary>
    public int RaySteps = 64; // [8, 12, 16, 20, 24, 32]

    // Appearance Settings

    /// <summary>Maximum ray length in view space units. Larger = GI from more distant objects.</summary>
    public float RayLength = 10.0f;

    /// <summary>Overall intensity of the global illumination effect.</summary>
    public float Intensity = 2.0f;

    /// <summary>Resolution scale for GI calculation. 0.5 = half resolution (better performance).</summary>
    public float ResolutionScale = 1.0f;

    // Private fields
    private Material _mat;
    private RenderTexture _giRT;
    private RenderTexture _lightCopyRT;
    private uint _frameIndex = 0;

    /// <summary>
    /// SSPT runs during lighting to add indirect illumination to the light accumulation buffer.
    /// </summary>
    public override RenderStage Stage => RenderStage.DuringLighting;

    public override void OnRenderEffect(RenderContext context)
    {
        // Lazy initialize material
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.SSPT));

        // Calculate scaled resolution
        int width = (int)(context.Width * ResolutionScale);
        int height = (int)(context.Height * ResolutionScale);

        // Create light copy for reading (we read from this, write to original)
        if (_lightCopyRT.IsNotValid() || _lightCopyRT.Width != context.Width || _lightCopyRT.Height != context.Height)
        {
            _lightCopyRT?.Dispose();
            _lightCopyRT = new RenderTexture(context.Width, context.Height, false,
                [context.LightAccumulation.MainTexture.ImageFormat]);
        }

        // Ensure render texture is created
        EnsureRenderTexture(ref _giRT, width, height, TextureImageFormat.Float4);

        // Set common shader parameters
        _mat.SetTexture("_CameraDepthTexture", context.GBuffer.InternalDepth);
        _mat.SetTexture("_GBufferB", context.GBuffer.InternalTextures[1]);
        _mat.SetTexture("_GBufferA", context.GBuffer.InternalTextures[0]);
        _mat.SetInt("_SamplesPerPixel", SamplesPerPixel);
        _mat.SetInt("_RaySteps", RaySteps);
        _mat.SetFloat("_RayLength", RayLength);
        _mat.SetFloat("_Intensity", Intensity);
        _mat.SetInt("_FrameIndex", (int)_frameIndex);

        // Multi-bounce: Run SSPT pass multiple times, each bounce accumulates on the lighting buffer
        for (int bounce = 0; bounce < MaxBounces; bounce++)
        {
            // Copy current light accumulation state
            RenderPipeline.Blit(context.LightAccumulation, _lightCopyRT);

            // Run SSPT pass: trace rays and sample lighting from current accumulation
            _mat.SetTexture("_MainTex", _lightCopyRT.MainTexture);
            RenderPipeline.Blit(_lightCopyRT, _giRT, _mat, 0);

            // Add GI contribution back to light accumulation
            _mat.SetTexture("_MainTex", _lightCopyRT.MainTexture); // Original lighting
            _mat.SetTexture("_GITex", _giRT.MainTexture); // New GI bounce
            RenderPipeline.Blit(_lightCopyRT, context.LightAccumulation, _mat, 3); // Pass 3: Composite (additive)
        }

        _frameIndex++;
    }

    private void EnsureRenderTexture(ref RenderTexture rt, int width, int height, TextureImageFormat format)
    {
        if (rt.IsNotValid() || rt.Width != width || rt.Height != height || rt.MainTexture.ImageFormat != format)
        {
            rt?.Dispose();
            rt = new RenderTexture(width, height, false, [format]);
        }
    }

    public override void OnPostRender(Camera camera)
    {
        // Reset frame index on camera movement for clean temporal accumulation
    }
}
