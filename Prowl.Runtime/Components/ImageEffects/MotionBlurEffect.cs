// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime;

[RequireComponent(typeof(Camera))]
[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Stopwatch}  MotionBlurEffect")]
[ImageEffectAllowedInSceneView]
public class MotionBlurEffect : MonoBehaviour
{
    private static Material s_motionblur;

    public float Intensity = 0.25f;
    public int SampleCount = 8;

    private Camera cam;

    public override void OnPreCull(Camera camera)
    {
        camera.DepthTextureMode |= DepthTextureMode.MotionVectors;
        cam = camera;
    }

    public override void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        s_motionblur ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/MotionBlur.shader"));

        s_motionblur.SetFloat("_Intensity", Intensity);
        s_motionblur.SetInt("_SampleCount", SampleCount);
        s_motionblur.SetVector("_ScreenParams", new Vector4(cam.PixelWidth, cam.PixelHeight, 1.0f + 1.0f / cam.PixelWidth, 1.0f + 1.0f / cam.PixelHeight));
        Graphics.Blit(src, dest, s_motionblur);
    }
}
