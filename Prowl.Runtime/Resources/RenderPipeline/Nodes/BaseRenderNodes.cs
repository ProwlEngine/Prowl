using Prowl.Runtime.NodeSystem;
using static Prowl.Runtime.MonoBehaviour;
using System;
using System.Diagnostics.Contracts;
using Raylib_cs;
using System.Runtime.ConstrainedExecution;

namespace Prowl.Runtime.Resources.RenderPipeline
{
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

    public class PBRDeferredNode : Node
    {
        public override string Title => "PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;

        [Output] public RenderTexture LightingRT;

        RenderTexture _lighting;
        long lastRenderedFrame = -1;
        Camera lastRenderedCam = null;

        public override object GetValue(NodePort port)
        {
            // If we already rendered this frame return that instead
            if (lastRenderedFrame == Time.frameCount && lastRenderedCam == Camera.Current)
                return _lighting;

            var gbuffer = GetInputValue<GBuffer>("CameraOutput");

            if(_lighting == null || (gbuffer.Width != _lighting.Width || gbuffer.Height != _lighting.Height))
            {
                _lighting?.Dispose();
                PixelFormat[] formats = [ PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32 ];
                _lighting = new RenderTexture(gbuffer.Width, gbuffer.Height, 1, false, formats);
            }

            // Start
            _lighting.Begin();

            // Clear then Draw
            Raylib.ClearBackground(new Color(0, 0, 0, 0));

            Rlgl.rlDisableDepthTest();
            Rlgl.rlSetCullFace(0); // Cull the front faces for the lighting pass
            Camera.Current.RenderAllOfOrder(RenderingOrder.Lighting);
            Rlgl.rlEnableDepthTest();
            Rlgl.rlSetCullFace(1);

            _lighting.End();

            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return _lighting;
        }
    }

    public class PostPBRDeferredNode : Node
    {
        public override string Title => "Post PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;
        [Input(ShowBackingValue.Never)] public RenderTexture LightingRT;

        [Output] public RenderTexture RenderTexture;

        public float Contrast = 1.1f;
        public float Saturation = 1.2f;
        public bool UseACES = true;
        public bool UseGammaCorrection = true;

        Material? CombineShader = null;

        RenderTexture _combined;
        long lastRenderedFrame = -1;
        Camera lastRenderedCam = null;

        public override object GetValue(NodePort port)
        {
            // If we already rendered this frame return that instead
            if (lastRenderedFrame == Time.frameCount && lastRenderedCam == Camera.Current)
                return _combined;

            var gbuffer = GetInputValue<GBuffer>("CameraOutput");
            var lighting = GetInputValue<RenderTexture>("LightingRT");

            if (_combined == null || (gbuffer.Width != _combined.Width || gbuffer.Height != _combined.Height))
            {
                _combined?.Dispose();
                PixelFormat[] formats = [ PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32 ];
                _combined = new RenderTexture((int)(gbuffer.Width / Camera.Current.RenderResolution), (int)(gbuffer.Height / Camera.Current.RenderResolution), 1, false, formats);
            }

            CombineShader ??= new(Shader.Find("Defaults/GBuffercombine.shader"));
            CombineShader.SetTexture("gAlbedoAO", gbuffer.AlbedoAO);
            CombineShader.SetTexture("gLighting", lighting.InternalTextures[0]);
            CombineShader.SetFloat("Contrast", Math.Clamp(Contrast, 0, 2));
            CombineShader.SetFloat("Saturation", Math.Clamp(Saturation, 0, 2));

            if (UseACES) CombineShader.EnableKeyword("ACESTONEMAP");
            else CombineShader.DisableKeyword("ACESTONEMAP");
            if (UseGammaCorrection) CombineShader.EnableKeyword("GAMMACORRECTION");
            else CombineShader.DisableKeyword("GAMMACORRECTION");


            _combined.Begin();

            Raylib.ClearBackground(new Color(0, 0, 0, 0));

            CombineShader.SetPass(0, true);
            Camera.Current.DrawFullScreenTexture(lighting.InternalTextures[0]);
            CombineShader.EndPass();

            _combined.End();

            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return _combined;
        }
    }

    public class DepthOfFieldNode : Node
    {
        public override string Title => "Depth Of Field";
        public override float Width => 125;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;
        [Input(ShowBackingValue.Never)] public RenderTexture RenderTexture;

        public float FocusStrength = 150f;
        public float Quality = 0.05f;
        public int BlurRadius = 10;

        [Output] public RenderTexture DofRT;

        Material Mat;
        RenderTexture _dof;
        long lastRenderedFrame = -1;
        Camera lastRenderedCam = null;

        public override object GetValue(NodePort port)
        {
            // If we already rendered this frame return that instead
            if (lastRenderedFrame == Time.frameCount && lastRenderedCam == Camera.Current)
                return _dof;

            var gbuffer = GetInputValue<GBuffer>("CameraOutput");
            var rt = GetInputValue<RenderTexture>("RenderTexture");

            if (_dof == null || (gbuffer.Width != _dof.Width || gbuffer.Height != _dof.Height))
            {
                _dof?.Dispose();
                PixelFormat[] formats = [PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32];
                _dof = new RenderTexture(gbuffer.Width, gbuffer.Height, 1, false, formats);
            }

            Mat ??= new Material(Shader.Find("Defaults/DOF.shader"));
            Mat.SetTexture("gCombined", rt.InternalTextures[0]);
            Mat.SetTexture("gDepth", gbuffer.Depth);

            Mat.SetFloat("u_Quality", Math.Clamp(Quality, 0.0f, 0.9f));
            Mat.SetFloat("u_BlurRadius", Math.Clamp(BlurRadius, 2, 40));
            Mat.SetFloat("u_FocusStrength", FocusStrength);

            Graphics.Blit(_dof, Mat, 0, true);

            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return _dof;
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
