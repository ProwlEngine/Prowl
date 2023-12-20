using Prowl.Runtime.NodeSystem;
using Silk.NET.OpenGL;
using System;
using static Prowl.Runtime.MonoBehaviour;

namespace Prowl.Runtime.Resources.RenderPipeline
{

    // 1. Move Buffers to exist on the Camera's, the camera will then be responsible for their width/height and clearing unused ones after X frames
    // 2. Combine PBR Deferred and Post PBR Deferred, they dont need to be two nodes

    public abstract class RenderPassNode : Node
    {
        public abstract override string Title { get; }
        public abstract override float Width { get; }

        [Output, SerializeIgnore] public RenderTexture OutputRT;
        public bool Clear = true;
        public virtual int Downsample { get; } = 1;
        public virtual int RTCount { get; } = 1;

        protected RenderTexture renderRT => renderRTs[0];
        protected RenderTexture[] renderRTs;
        long lastRenderedFrame = -1;
        Camera lastRenderedCam = null;

        public override object GetValue(NodePort port)
        {
            // If we already rendered this frame return that instead
            if (lastRenderedFrame == Time.frameCount && lastRenderedCam == Camera.Current)
                return renderRT;

            var gbuffer = Camera.Current.gBuffer;

            int width = gbuffer.Width / Downsample;
            int height = gbuffer.Height / Downsample;

            if (renderRTs == null || (width != renderRT.Width || height != renderRT.Height))
            {
                renderRTs ??= new RenderTexture[RTCount];
                for (int i = 0; i < RTCount; i++)
                {
                    renderRTs[i]?.Dispose();
                    Texture.TextureImageFormat[] formats = [Texture.TextureImageFormat.Float3];
                    renderRTs[i] = new RenderTexture(width, height, 1, false, formats);
                }
            }

            Render();

            lastRenderedFrame = Time.frameCount;
            lastRenderedCam = Camera.Current;

            return renderRT;
        }

        public virtual void PreRender(int width, int height) { }
        public abstract void Render();
    }

    public class PBRDeferredNode : RenderPassNode
    {
        public override string Title => "PBR Deferred Pass";
        public override float Width => 100;

        public override void Render()
        {
            renderRT.Begin();
            if (Clear) Graphics.Clear();
            using (Graphics.UseDepthTest(false)) {
                using (Graphics.UseFaceCull(TriangleFace.Front)) {
                    using (Graphics.UseBlendMode(BlendMode.Additive)) {
                        Camera.Current.RenderAllOfOrder(RenderingOrder.Lighting);
                    }
                }
            }
            renderRT.End();
        }
    }

    public class PostPBRDeferredNode : RenderPassNode
    {
        public override string Title => "Post PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture LightingRT;

        Material? CombineShader = null;

        public override void Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var lighting = GetInputValue<RenderTexture>("LightingRT");

            CombineShader ??= new(Shader.Find("Defaults/GBuffercombine.shader"));
            CombineShader.SetTexture("gAlbedoAO", gbuffer.AlbedoAO);
            CombineShader.SetTexture("gLighting", lighting.InternalTextures[0]);

            Graphics.Blit(renderRT, CombineShader, 0, Clear);
        }
    }

    public class DepthOfFieldNode : RenderPassNode
    {
        public override string Title => "Depth Of Field";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

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

    public class ProceduralSkyboxNode : RenderPassNode
    {
        public override string Title => "Procedural Skybox Pass";
        public override float Width => 125;

        [Input(ShowBackingValue.Never)] public RenderTexture RenderTexture;

        public float FogDensity = 0.08f;

        Material Mat;

        public override void Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var rt = GetInputValue<RenderTexture>("RenderTexture");
            if (rt == null) return;

            Mat ??= new Material(Shader.Find("Defaults/ProcedualSkybox.shader"));
            Mat.SetTexture("gColor", rt.InternalTextures[0]);
            Mat.SetTexture("gPositionRoughness", gbuffer.PositionRoughness);
            Mat.SetFloat("fogDensity", FogDensity);

            // Find DirectionalLight
            DirectionalLight? light = EngineObject.FindObjectOfType<DirectionalLight>();
            if (light != null)
                Mat.SetVector("uSunPos", -light.GameObject.Forward);
            else // Fallback to a reasonable default
                Mat.SetVector("uSunPos", new Vector3(0.5f, 0.5f, 0.5f));

            Graphics.Blit(renderRT, Mat, 0, true);
        }
    }

    public class BloomNode : RenderPassNode
    {
        public override string Title => "Bloom Pass";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public override int Downsample => 1;
        public override int RTCount => 2;

        public float Radius = 1f;
        public float Threshold = 1f;
        public int Passes = 10;

        Material Mat;

        public override void Render()
        {
            var rt = GetInputValue<RenderTexture>("RenderTexture");
            if (rt == null) return;

            Mat ??= new Material(Shader.Find("Defaults/Bloom.shader"));

            RenderTexture[] rts = [renderRTs[0], renderRTs[1]];

            Mat.SetFloat("u_Alpha", 1.0f);
            Mat.SetTexture("gColor", rt.InternalTextures[0]);
            Mat.SetFloat("u_Radius", 1.5f);
            Mat.SetFloat("u_Threshold", Math.Clamp(Threshold, 0.0f, 8f));
            Graphics.Blit(renderRTs[0], Mat, 0, true);
            Graphics.Blit(renderRTs[1], Mat, 0, true);
            Mat.SetFloat("u_Threshold", 0.0f);

            Graphics.GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            for (int i = 1; i <= Passes; i++)
            {
                Mat.SetFloat("u_Alpha", 1.0f);
                Mat.SetTexture("gColor", renderRTs[0].InternalTextures[0]);
                Mat.SetFloat("u_Radius", Math.Clamp(Radius+i, 0.0f, 32f));
                Graphics.Blit(renderRTs[1], Mat, 0, false);

                var tmp = renderRTs[0];
                renderRTs[0] = renderRTs[1];
                renderRTs[1] = tmp;
            }

            // Final pass
            Graphics.Blit(renderRTs[0], rt.InternalTextures[0], false);



            //renderRT = rts[currentRenderTextureIndex];
        }
    }

    public class AcesTonemappingNode : RenderPassNode
    {
        public override string Title => "Aces Tonemapping Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public float Contrast = 1.1f;
        public float Saturation = 1.2f;
        public bool UseACES = true;
        public bool UseGammaCorrection = true;

        Material? AcesMat = null;

        public override void Render()
        {
            var rt = GetInputValue<RenderTexture>("RenderTexture");
            if (rt == null) return;

            AcesMat ??= new(Shader.Find("Defaults/AcesTonemapper.shader"));
            AcesMat.SetTexture("gAlbedo", rt.InternalTextures[0]);
            AcesMat.SetFloat("Contrast", Math.Clamp(Contrast, 0, 2));
            AcesMat.SetFloat("Saturation", Math.Clamp(Saturation, 0, 2));

            if (UseACES) AcesMat.EnableKeyword("ACESTONEMAP");
            else AcesMat.DisableKeyword("ACESTONEMAP");
            if (UseGammaCorrection) AcesMat.EnableKeyword("GAMMACORRECTION");
            else AcesMat.DisableKeyword("GAMMACORRECTION");

            Graphics.Blit(renderRT, AcesMat, 0, Clear);
        }
    }

    public class ScreenSpaceReflectionNode : RenderPassNode
    {
        public override string Title => "Screen Space Reflection";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public int Steps = 16;
        public int RefineSteps = 4;

        Material Mat;

        public override void Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var rt = GetInputValue<RenderTexture>("RenderTexture");
            if (rt == null) return;

            Mat ??= new Material(Shader.Find("Defaults/SSR.shader"));
            Mat.SetTexture("gColor", rt.InternalTextures[0]);
            Mat.SetTexture("gNormalMetallic", gbuffer.NormalMetallic);
            Mat.SetTexture("gPositionRoughness", gbuffer.PositionRoughness);
            Mat.SetTexture("gDepth", gbuffer.Depth);

            Mat.SetInt("SSR_STEPS", Math.Clamp(Steps, 16, 32));
            Mat.SetInt("SSR_BISTEPS", Math.Clamp(RefineSteps, 0, 16));

            Graphics.Blit(renderRT, Mat, 0, true);
        }
    }

    public class TAANode : RenderPassNode
    {
        public override string Title => "Temporal Anti-Aliasing";
        public override float Width => 125;
        public override int RTCount => 2;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        Material Mat;

        public override void PreRender(int width, int height)
        {
            // Apply jitter
            // Graphics class by default comes packed with a Halton16 sequence specifically for TAA jitter
            Graphics.UseJitter = true; // This applies the jitter to the Velocity Buffer/Motion Vectors
            Graphics.MatProjection.M31 += Graphics.Jitter.X / width;
            Graphics.MatProjection.M32 += Graphics.Jitter.Y / height;
        }

        public override void Render()
        {
            var rt = GetInputValue<RenderTexture>("RenderTexture");
            if (rt == null) return;

            Mat ??= new Material(Shader.Find("Defaults/TAA.shader"));
            Mat.SetTexture("gColor", rt.InternalTextures[0]);
            Mat.SetTexture("gHistory", renderRTs[1].InternalTextures[0]);
            Mat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);
            Mat.SetTexture("gVelocity", Camera.Current.gBuffer.Velocity);
            Mat.SetTexture("gDepth", Camera.Current.gBuffer.Depth);

            Mat.SetVector("Jitter", Graphics.Jitter);
            Mat.SetVector("PreviousJitter", Graphics.PreviousJitter);

            Graphics.Blit(renderRTs[0], Mat, 0, true);

            Graphics.Blit(renderRTs[1], renderRT.InternalTextures[0], true);
        }
    }

    [DisallowMultipleNodes]
    public class OutputNode : Node
    {
        public override string Title => "Output";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public override object GetValue(NodePort port)
        {
            return GetInputValue<RenderTexture>("RenderTexture");
        }
    }
}
