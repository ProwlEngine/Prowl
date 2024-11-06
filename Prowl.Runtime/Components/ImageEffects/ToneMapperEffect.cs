// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

[RequireComponent(typeof(Camera))]
[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.EyeLowVision}  ToneMapperEffect")]
[ImageEffectTransformsToLDR]
[ImageEffectAllowedInSceneView]
public class ToneMapperEffect : MonoBehaviour
{
    private static Material s_tonemapper;

    public float Contrast = 1f;
    public float Saturation = 1f;

    public override void OnRenderImage(RenderTexture src, RenderTexture dest)
    {

        s_tonemapper ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ToneMapper.shader"));

        // Final tonemapping
        s_tonemapper.SetFloat("_Contrast", Contrast);
        s_tonemapper.SetFloat("_Saturation", Saturation);
        Graphics.Blit(src, dest, s_tonemapper);
    }
}
