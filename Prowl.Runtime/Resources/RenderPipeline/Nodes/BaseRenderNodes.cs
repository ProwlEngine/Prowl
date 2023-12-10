using Prowl.Runtime.NodeSystem;
using Raylib_cs;
using System;
using static Prowl.Runtime.MonoBehaviour;

namespace Prowl.Runtime.Resources.RenderPipeline
{

    // 1. Move Buffers to exist on the Camera's, the camera will then be responsible for their width/height and clearing unused ones after X frames
    // 2. 

    [DisallowMultipleNodes]
    public class CameraNode : Node
    {
        public override string Title => "Camera";
        public override float Width => 50;

        [Output] public GBuffer CameraOutput;

        public override object GetValue(NodePort port)
        {
            return Camera.Current.gBuffer;
        }
    }

    public abstract class RenderPassNode : Node
    {
        public abstract override string Title { get; }
        public abstract override float Width { get; }

        [Output] public RenderTexture OutputRT;
        public bool Clear = true;

        protected RenderTexture renderRT;
        long lastRenderedFrame = -1;
        Camera lastRenderedCam = null;

        public override object GetValue(NodePort port)
        {
            // If we already rendered this frame return that instead
            if (lastRenderedFrame == Time.frameCount && lastRenderedCam == Camera.Current)
                return renderRT;

            var gbuffer = Camera.Current.gBuffer;

            if (renderRT == null || (gbuffer.Width != renderRT.Width || gbuffer.Height != renderRT.Height))
            {
                renderRT?.Dispose();
                PixelFormat[] formats = [PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32];
                renderRT = new RenderTexture(gbuffer.Width, gbuffer.Height, 1, false, formats);
            }

            Render();

            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return renderRT;
        }

        public abstract void Render();
    }

    public class PBRDeferredNode : RenderPassNode
    {
        public override string Title => "PBR Deferred Pass";
        public override float Width => 100;

        public override void Render()
        {
            renderRT.Begin();
            if(Clear) Raylib.ClearBackground(Color.clear);
            Rlgl.rlDisableDepthTest();
            Rlgl.rlSetCullFace(0); // Cull the front faces for the lighting pass
            Camera.Current.RenderAllOfOrder(RenderingOrder.Lighting);
            Rlgl.rlEnableDepthTest();
            Rlgl.rlSetCullFace(1);
            renderRT.End();
        }
    }

    public class PostPBRDeferredNode : RenderPassNode
    {
        public override string Title => "Post PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never)] public RenderTexture LightingRT;

        public float Contrast = 1.1f;
        public float Saturation = 1.2f;
        public bool UseACES = true;
        public bool UseGammaCorrection = true;

        Material? CombineShader = null;

        public override void Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var lighting = GetInputValue<RenderTexture>("LightingRT");

            SetupMaterial(gbuffer, lighting);

            Graphics.Blit(renderRT, CombineShader, 0, Clear);
            //Rlgl.rlDisableDepthMask();
            //Rlgl.rlDisableDepthTest();
            //Rlgl.rlDisableBackfaceCulling();
            ////Camera.Current.DrawFullScreenTexture(lighting.InternalTextures[0]);
            //var t = lighting.InternalTextures[0];
            //CombineShader.SetPass(0, true);
            //Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.width, -t.height), new Rectangle(0, 0, renderRT.Width, renderRT.Height), System.Numerics.Vector2.Zero, 0.0f, Color.white);
            //CombineShader.EndPass();
            //Rlgl.rlEnableDepthMask();
            //Rlgl.rlEnableDepthTest();
            //Rlgl.rlEnableBackfaceCulling();

            //Graphics.Blit(renderRT, CombineShader, 0, Clear);
        }

        private void SetupMaterial(GBuffer gbuffer, RenderTexture lighting)
        {
            CombineShader ??= new(Shader.Find("Defaults/GBuffercombine.shader"));
            CombineShader.SetTexture("gAlbedoAO", gbuffer.AlbedoAO);
            CombineShader.SetTexture("gLighting", lighting.InternalTextures[0]);
            CombineShader.SetFloat("Contrast", Math.Clamp(Contrast, 0, 2));
            CombineShader.SetFloat("Saturation", Math.Clamp(Saturation, 0, 2));

            if (UseACES) CombineShader.EnableKeyword("ACESTONEMAP");
            else CombineShader.DisableKeyword("ACESTONEMAP");
            if (UseGammaCorrection) CombineShader.EnableKeyword("GAMMACORRECTION");
            else CombineShader.DisableKeyword("GAMMACORRECTION");
        }
    }

    public class DepthOfFieldNode : RenderPassNode
    {
        public override string Title => "Depth Of Field";
        public override float Width => 125;

        [Input(ShowBackingValue.Never)] public RenderTexture RenderTexture;

        public float FocusStrength = 150f;
        public float Quality = 0.05f;
        public int BlurRadius = 10;

        Material Mat;

        public override void Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var rt = GetInputValue<RenderTexture>("RenderTexture");
            if (rt == null) return;

            Mat ??= new Material(Shader.Find("Defaults/DOF.shader"));
            Mat.SetTexture("gCombined", rt.InternalTextures[0]);
            Mat.SetTexture("gDepth", gbuffer.Depth);

            Mat.SetFloat("u_Quality", Math.Clamp(Quality, 0.0f, 0.9f));
            Mat.SetFloat("u_BlurRadius", Math.Clamp(BlurRadius, 2, 40));
            Mat.SetFloat("u_FocusStrength", FocusStrength);

            Graphics.Blit(renderRT, Mat, 0, true);
        }
    }

    [DisallowMultipleNodes]
    public class OutputNode : Node
    {
        public override string Title => "Output";
        public override float Width => 125;

        [Input] public RenderTexture RenderTexture;

        public override object GetValue(NodePort port)
        {
            return GetInputValue<RenderTexture>("RenderTexture");
        }
    }
}
