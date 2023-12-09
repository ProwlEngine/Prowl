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

        [Output] public AssetRef<RenderTexture> LightingRT;

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
            Raylib.BeginTextureMode(new RenderTexture2D() { id = _lighting.fboId, texture = _lighting.InternalTextures[0] });
            Rlgl.rlActiveDrawBuffers(1);

            Rlgl.rlDisableDepthTest();
            Rlgl.rlSetCullFace(0); // Cull the front faces for the lighting pass

            // Clear then Draw
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            Camera.Current.RenderAllOfOrder(RenderingOrder.Lighting);

            Rlgl.rlEnableDepthTest();
            Rlgl.rlSetCullFace(1);

            Raylib.EndTextureMode();

            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return new AssetRef<RenderTexture>(_lighting);
        }
    }

    public class PostPBRDeferredNode : Node
    {
        public override string Title => "Post PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;
        [Input(ShowBackingValue.Never)] public RenderTexture LightingRT;

        [Output] public AssetRef<RenderTexture> RenderTexture;

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
            var lighting = GetInputValue<AssetRef<RenderTexture>>("LightingRT");

            if (_combined == null || (gbuffer.Width != _combined.Width || gbuffer.Height != _combined.Height))
            {
                _combined?.Dispose();
                PixelFormat[] formats = [ PixelFormat.PIXELFORMAT_UNCOMPRESSED_R32G32B32 ];
                _combined = new RenderTexture((int)(gbuffer.Width / Camera.Current.RenderResolution), (int)(gbuffer.Height / Camera.Current.RenderResolution), 1, false, formats);
            }

            Raylib.BeginTextureMode(new RenderTexture2D() { id = _combined.fboId, texture = _combined.InternalTextures[0] });
            Rlgl.rlActiveDrawBuffers(1); // Drawing only into Diffuse for the final Combine pass

            Rlgl.rlDisableDepthMask();
            Rlgl.rlDisableDepthTest();
            Rlgl.rlDisableBackfaceCulling();

            Raylib.ClearBackground(new Color(0, 0, 0, 0));


            CombineShader ??= new(Shader.Find("Defaults/GBuffercombine.shader"));
            //CombineShader.mpb.Clear();
            CombineShader.SetTexture("gAlbedoAO", gbuffer.AlbedoAO);
            CombineShader.SetTexture("gLighting", lighting.Res!.InternalTextures[0]);
            CombineShader.SetFloat("Contrast", Math.Clamp(Contrast, 0, 2));
            CombineShader.SetFloat("Saturation", Math.Clamp(Saturation, 0, 2));
            CombineShader.EnableKeyword("ACESTONEMAP");
            CombineShader.EnableKeyword("GAMMACORRECTION");
            CombineShader.SetPass(0, true);
            //CombineShader.Begin();
            Camera.Current.DrawFullScreenTexture(lighting.Res!.InternalTextures[0]);
            //CombineShader.End();
            CombineShader.EndPass();


            Rlgl.rlEnableDepthMask();
            Rlgl.rlEnableDepthTest();
            Rlgl.rlEnableBackfaceCulling();

            Raylib.EndTextureMode();


            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return new AssetRef<RenderTexture>(_combined);
        }
    }

    public class DepthOfFieldNode : Node
    {
        public override string Title => "Depth Of Field";
        public override float Width => 125;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;
        [Input(ShowBackingValue.Never)] public AssetRef<RenderTexture> RenderTexture;

        public float FocusStrength = 150f;
        public float Quality = 0.05f;
        public int BlurRadius = 10;

        [Output] public AssetRef<RenderTexture> DofRT;

        public override object GetValue(NodePort port)
        {
            return 0;
        }
    }

    [DisallowMultipleNodes]
    public class OutputNode : Node
    {
        public override string Title => "Output";
        public override float Width => 125;

        [Input] public AssetRef<RenderTexture> RenderTexture;

        public override object GetValue(NodePort port)
        {
            return GetInputValue<AssetRef<RenderTexture>>("RenderTexture");
        }
    }
}
