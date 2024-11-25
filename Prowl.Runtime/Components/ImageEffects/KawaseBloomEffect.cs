// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Sun}  KawaseBloomEffect")]
[ImageEffectAllowedInSceneView]
public class KawaseBloomEffect : MonoBehaviour
{
    private static Material s_bloomMaterial;

    public enum Resolution
    {
        Full = 0,
        Half = 1,
        Quarter = 2,
        Eighth = 3
    }

    public Resolution resolution = Resolution.Quarter;

    [Range(1, 8, true)]
    public int Iterations = 2;
    public float Radius = 16.0f;
    public float Threshold = 1.0f;
    public float Intensity = 1.0f;
    [Range(0, 1, true)]
    public float SoftKnee = 0.5f;

    public bool UseBlur = true;       // Toggle to enable/disable blur

    public override void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if(s_bloomMaterial == null)
            s_bloomMaterial = new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/KawaseBloom.shader"));
        if (s_bloomMaterial == null) return;

        // Set up material properties
        s_bloomMaterial.SetFloat("_Radius", Radius);
        s_bloomMaterial.SetFloat("_Threshold", Threshold);
        s_bloomMaterial.SetFloat("_SoftKnee", SoftKnee);
        s_bloomMaterial.SetFloat("_Intensity", Intensity);
        s_bloomMaterial.SetVector("_Resolution", new System.Numerics.Vector2(src.Width, src.Height));

        // Create temporary RTs
        float resScale = 1.0f;
        switch (resolution)
        {
            case Resolution.Half:
                resScale = 0.5f;
                break;
            case Resolution.Quarter:
                resScale = 0.25f;
                break;
            case Resolution.Eighth:
                resScale = 0.125f;
                break;
        }
        uint width = (uint)MathD.Max(1, src.Width * resScale);
        uint height = (uint)MathD.Max(1, src.Height * resScale);


        RenderTexture tempA = RenderTexture.GetTemporaryRT(width, height, [src.ColorBuffers[0].Format]);
        RenderTexture tempB = RenderTexture.GetTemporaryRT(width, height, [src.ColorBuffers[0].Format]);
        CommandBuffer tmpClear = CommandBufferPool.Get("Clear");
        tmpClear.SetRenderTarget(tempA);
        tmpClear.ClearRenderTarget(true, true, Color.clear);
        tmpClear.SetRenderTarget(tempB);
        tmpClear.ClearRenderTarget(true, true, Color.clear);
        Graphics.SubmitCommandBuffer(tmpClear);

        // blit the source into tempA with a threshold (Pass 0)
        Graphics.Blit(src, tempA, s_bloomMaterial, 0);


        // Calculate initial offset based on iterations (2^iterations)
        var initialOffset = MathD.Pow(2, Iterations);


        for (int i = 0; i < Iterations; i++)
        {
            // Calculate current offset
            double offset = initialOffset / MathD.Pow(2, i);
            s_bloomMaterial.SetFloat("_Offset", (float)offset);

            if (i % 2 == 0)
            {
                // Even iterations: tempA -> tempB
                Graphics.Blit(tempA, tempB, s_bloomMaterial, 1);
            }
            else
            {
                // Odd iterations: tempB -> tempA
                Graphics.Blit(tempB, tempA, s_bloomMaterial, 1);
            }
        }

        // If we ended on tempA, do one final blit to tempB
        if (Iterations % 2 == 0)
        {
            float finalOffset = 1.0f;
            s_bloomMaterial.SetFloat("_Offset", finalOffset);
            Graphics.Blit(tempA, tempB, s_bloomMaterial, 1);
        }

        // After the kawase blur iterations, apply blur if enabled
        if (UseBlur)
        {
            s_bloomMaterial.SetVector("_Resolution", new System.Numerics.Vector2(width, height));

            Graphics.Blit(tempB, tempA, s_bloomMaterial, 2); // Horizontal
            Graphics.Blit(tempA, tempB, s_bloomMaterial, 3); // Vertical

            // Original composite without blur
            s_bloomMaterial.SetTexture("_BloomTex", tempB);
            Graphics.Blit(src, dest, s_bloomMaterial, 4);
        }
        else
        {
            // Original composite without blur
            s_bloomMaterial.SetTexture("_BloomTex", tempB);
            Graphics.Blit(src, dest, s_bloomMaterial, 4);
        }

        // Release temporary RT
        RenderTexture.ReleaseTemporaryRT(tempA);
        RenderTexture.ReleaseTemporaryRT(tempB);
    }
}
