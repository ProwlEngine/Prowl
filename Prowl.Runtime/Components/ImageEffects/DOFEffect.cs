using Prowl.Runtime.Resources;
using Raylib_cs;
using System;

namespace Prowl.Runtime.Components.ImageEffects
{
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class DOFEffect : MonoBehaviour
    {
        public float focusStrength = 40f;
        public float quality = 0.05f;
        public int blurRadius = 10;

        Camera _cam;
        Camera Cam 
        { 
            get 
            { 
                _cam ??= GetComponent<Camera>();
                return _cam; 
            } 
        }

        Resources.Material? _mat;
        Resources.Material Mat 
        { 
            get 
            {
                _mat ??= new Resources.Material(Resources.Shader.Find("Defaults/DOF.shader"));
                return _mat; 
            } 
        }

        RenderTexture dof;

        public void OnEnable()
        {
            Cam.PostProcessStagePostCombine += ApplyEffect;
            Cam.Resize += OnResize;
        }

        public void OnDisable()
        {
            Cam.PostProcessStagePostCombine -= ApplyEffect;
        }

        private void OnResize(int width, int height)
        {
            dof?.Destroy();
            dof = new RenderTexture(width, height, 1, false, [Raylib_cs.PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32A32]);
        }

        private void ApplyEffect(GBuffer gBuffer)
        {
            if (dof == null) OnResize(gBuffer.Width, gBuffer.Height);

            Mat.SetTexture("gCombined", gBuffer.Combined);
            Mat.SetTexture("gDepth", gBuffer.Depth);

            Mat.SetFloat("u_Quality", Math.Clamp(quality, 0.0f, 0.9f));
            Mat.SetFloat("u_BlurRadius", Math.Clamp(blurRadius, 2, 40));
            Mat.SetFloat("u_FocusStrength", focusStrength);

            Rlgl.rlDisableDepthMask();
            Rlgl.rlDisableDepthTest();
            Rlgl.rlDisableBackfaceCulling();
            Graphics.Blit(dof, Mat, 0, true);
            Rlgl.rlEnableDepthMask();
            Rlgl.rlEnableDepthTest();
            Rlgl.rlEnableBackfaceCulling();

            gBuffer.BeginCombine();
            Cam.DrawFullScreenTexture(dof.InternalTextures[0]);
            gBuffer.EndCombine();
        }

    }
}
