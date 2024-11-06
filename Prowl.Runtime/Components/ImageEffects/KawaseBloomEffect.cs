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

    public int Iterations = 4;
    public float Radius = 1.0f;
    public float Threshold = 1.0f;
    public float Intensity = 1.0f;
    public float SoftKnee = 0.5f;

    public override void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        s_bloomMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/KawaseBloom.shader"));

        // Set up material properties
        s_bloomMaterial.SetFloat("_Radius", Radius);
        s_bloomMaterial.SetFloat("_Threshold", Threshold);
        s_bloomMaterial.SetFloat("_SoftKnee", SoftKnee);
        s_bloomMaterial.SetFloat("_Intensity", Intensity);
        s_bloomMaterial.SetVector("_Resolution", new System.Numerics.Vector2(src.Width, src.Height));

        // Create temporary RTs
        RenderTexture tempA = RenderTexture.GetTemporaryRT(src.Width, src.Height, [src.ColorBuffers[0].Format]);
        RenderTexture tempB = RenderTexture.GetTemporaryRT(src.Width, src.Height, [src.ColorBuffers[0].Format]);

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
        if ((Iterations - 1) % 2 == 0)
        {
            float finalOffset = 1.0f;
            s_bloomMaterial.SetFloat("_Offset", finalOffset);
            Graphics.Blit(tempA, tempB, s_bloomMaterial, 1);
        }

        //s_bloomMaterial.SetFloat("_Offset", 4.0f); Graphics.Blit(tempA, tempB, s_bloomMaterial, 1);
        //s_bloomMaterial.SetFloat("_Offset", 2.0f); Graphics.Blit(tempB, tempA, s_bloomMaterial, 1);
        //s_bloomMaterial.SetFloat("_Offset", 1.0f); Graphics.Blit(tempA, tempB, s_bloomMaterial, 1);

        s_bloomMaterial.SetTexture("_BloomTex", tempB);
        Graphics.Blit(src, dest, s_bloomMaterial, 2);
        //Graphics.Blit(tempB, dest);

        // Release temporary RT
        RenderTexture.ReleaseTemporaryRT(tempA);
        RenderTexture.ReleaseTemporaryRT(tempB);
    }
}
