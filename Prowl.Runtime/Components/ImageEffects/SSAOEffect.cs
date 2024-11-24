// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

[RequireComponent(typeof(Camera))]
[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  SSAOEffect")]
[ImageEffectAllowedInSceneView]
[ImageEffectOpaque]
[ExecuteAlways]
public class SSAOEffect : MonoBehaviour
{
    private static Material s_ssao;

    public float Radius = 0.5f;
    public float Intensity = 1.25f;
    [Range(4, 32)]
    public int SampleCount = 16;
    public float MaxDistance = 100f;

    private Camera cam;

    public override void OnPreCull(Camera camera)
    {
        camera.DepthTextureMode |= DepthTextureMode.Depth;
        cam = camera;
    }

    public override void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (s_ssao == null)
            s_ssao = new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/SSAO.shader"));
        if (s_ssao == null) return;

        // Set shader parameters
        s_ssao.SetFloat("_Radius", Radius);
        s_ssao.SetFloat("_Intensity", Intensity);
        s_ssao.SetFloat("_MaxDistance", MaxDistance);
        s_ssao.SetInt("_SampleCount", SampleCount);
        s_ssao.SetMatrix("_ProjectionMatrix", cam.ProjectionMatrix.ToFloat());

        Graphics.Blit(src, dest, s_ssao);
    }
}
