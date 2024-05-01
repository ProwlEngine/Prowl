using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Rendering.Primitives;
using System;
using static Prowl.Runtime.MonoBehaviour;

namespace Prowl.Runtime.Resources.RenderPipeline
{
    public abstract class RenderPassNode : Node
    {
        public abstract override string Title { get; }
        public abstract override float Width { get; }
        protected Runtime.RenderPipeline RP => (Runtime.RenderPipeline)this.graph;

        [Output, SerializeIgnore] public RenderTexture OutputRT;

        public override object GetValue(NodePort port)
        {
            return Render();
        }

        public virtual void Prepare(int width, int height) { }
        public abstract RenderTexture Render();

        protected RenderTexture GetRenderTexture(float scale, TextureImageFormat[] format)
        {
            var rt = RenderTexture.GetTemporaryRT((int)(RP.Width * scale), (int)(RP.Height * scale), format);
            RP.UsedRenderTextures.Add(rt);
            return rt;
        }

        protected void ReleaseRT(RenderTexture rt)
        {
            RP.UsedRenderTextures.Remove(rt);
            RenderTexture.ReleaseTemporaryRT(rt);
        }
    }

    public class PBRDeferredNode : RenderPassNode
    {
        public override string Title => "PBR Deferred Pass";
        public override float Width => 100;

        public TextureImageFormat Format = TextureImageFormat.Short3;

        public float Scale = 1.0f;

        public override RenderTexture Render()
        {
            var result = GetRenderTexture(Scale, [Format]);
            result.Begin();
            Graphics.Clear();
            Camera.Current.RenderAllOfOrder(RenderingOrder.Lighting);
            result.End();
            return result;
        }
    }

    public class PostPBRDeferredNode : RenderPassNode
    {
        public override string Title => "Post PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture LightingRT;

        Material? CombineShader = null;

        public override RenderTexture Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var source = GetInputValue<RenderTexture>("LightingRT");
            if (source == null) return null;

            CombineShader ??= new(Shader.Find("Defaults/GBuffercombine.shader"));
            CombineShader.SetTexture("gAlbedoAO", gbuffer.AlbedoAO);
            CombineShader.SetTexture("gLighting", source.InternalTextures[0]);

            var result = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            Graphics.Blit(result, CombineShader, 0, true);
            ReleaseRT(source);
            return result;
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

        public override RenderTexture Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var source = GetInputValue<RenderTexture>("RenderTexture");
            if (source == null) return null;

            Mat ??= new Material(Shader.Find("Defaults/DOF.shader"));
            Mat.SetTexture("gCombined", source.InternalTextures[0]);
            Mat.SetTexture("gDepth", gbuffer.Depth);

            Mat.SetFloat("u_Quality", Math.Clamp(Quality, 0.0f, 0.9f));
            Mat.SetFloat("u_BlurRadius", Math.Clamp(BlurRadius, 2, 40));
            Mat.SetFloat("u_FocusStrength", FocusStrength);

            var result = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            Graphics.Blit(result, Mat, 0, true);
            ReleaseRT(source);
            return result;
        }
    }

    public class ProceduralSkyboxNode : RenderPassNode
    {
        public override string Title => "Procedural Skybox Pass";
        public override float Width => 125;

        [Input(ShowBackingValue.Never)] public RenderTexture RenderTexture;

        public float FogDensity = 0.08f;

        Material Mat;

        public override RenderTexture Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var source = GetInputValue<RenderTexture>("RenderTexture");
            if (source == null) return null;

            Mat ??= new Material(Shader.Find("Defaults/ProcedualSkybox.shader"));
            Mat.SetTexture("gColor", source.InternalTextures[0]);
            Mat.SetTexture("gPositionRoughness", gbuffer.PositionRoughness);
            Mat.SetFloat("fogDensity", FogDensity);

            // Find DirectionalLight
            DirectionalLight? light = EngineObject.FindObjectOfType<DirectionalLight>();
            if (light != null)
                Mat.SetVector("uSunPos", -light.GameObject.Transform.forward);
            else // Fallback to a reasonable default
                Mat.SetVector("uSunPos", new Vector3(0.5f, 0.5f, 0.5f));

            var result = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            Graphics.Blit(result, Mat, 0, true);
            ReleaseRT(source);
            return result;
        }
    }

    public class BloomNode : RenderPassNode
    {
        public override string Title => "Bloom Pass";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public float Radius = 10f;
        public float Threshold = 0.5f;
        public int Passes = 10;

        Material Mat;

        public override RenderTexture Render()
        {
            var source = GetInputValue<RenderTexture>("RenderTexture");
            if (source == null) return null;

            Mat ??= new Material(Shader.Find("Defaults/Bloom.shader"));

            var front = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            var back = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            RenderTexture[] rts = [front, back];

            Mat.SetFloat("u_Alpha", 1.0f);
            Mat.SetTexture("gColor", source.InternalTextures[0]);
            Mat.SetFloat("u_Radius", 1.5f);
            Mat.SetFloat("u_Threshold", Math.Clamp(Threshold, 0.0f, 8f));
            Graphics.Blit(rts[0], Mat, 0, true);
            Graphics.Blit(rts[1], Mat, 0, true);
            Mat.SetFloat("u_Threshold", 0.0f);

            for (int i = 1; i <= Passes; i++)
            {
                Mat.SetFloat("u_Alpha", 1.0f);
                Mat.SetTexture("gColor", rts[0].InternalTextures[0]);
                Mat.SetFloat("u_Radius", Math.Clamp(Radius, 0.0f, 32f) + i);
                Graphics.Blit(rts[1], Mat, 0, false);

                (rts[1], rts[0]) = (rts[0], rts[1]);
            }

            // Final pass
            Graphics.Blit(rts[0], source.InternalTextures[0], false);
            ReleaseRT(rts[1]);
            ReleaseRT(source);
            return rts[0];
        }
    }

    public class TonemappingNode : RenderPassNode
    {
        public override string Title => "Tonemapping Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public float Contrast = 1.05f;
        public float Saturation = 1.15f;
        public enum Tonemapper { Melon, Aces, Reinhard, Uncharted2, Filmic, None }
        public Tonemapper UseTonemapper = Tonemapper.Melon;
        public bool UseGammaCorrection = true;

        Material? AcesMat = null;
        Tonemapper? prevTonemapper = null;

        public override RenderTexture Render()
        {
            var source = GetInputValue<RenderTexture>("RenderTexture");
            if (source == null) return null;

            AcesMat ??= new(Shader.Find("Defaults/Tonemapper.shader"));
            AcesMat.SetTexture("gAlbedo", source.InternalTextures[0]);
            AcesMat.SetFloat("Contrast", Math.Clamp(Contrast, 0, 2));
            AcesMat.SetFloat("Saturation", Math.Clamp(Saturation, 0, 2));

            // Because we always Reset the tonemappers to disabled then re-enable them
            // this will trigger a Uniform Location Cache clear every single frame
            // As the shader could be changing, so we do a previous check to see if we need to do this
            if (prevTonemapper != UseTonemapper)
            {
                prevTonemapper = UseTonemapper;
                AcesMat.DisableKeyword("MELON");
                AcesMat.DisableKeyword("ACES");
                AcesMat.DisableKeyword("REINHARD");
                AcesMat.DisableKeyword("UNCHARTED");
                AcesMat.DisableKeyword("FILMIC");

                if (UseTonemapper == Tonemapper.Melon)
                    AcesMat.EnableKeyword("MELON");
                else if (UseTonemapper == Tonemapper.Aces)
                    AcesMat.EnableKeyword("ACES");
                else if (UseTonemapper == Tonemapper.Reinhard)
                    AcesMat.EnableKeyword("REINHARD");
                else if (UseTonemapper == Tonemapper.Uncharted2)
                    AcesMat.EnableKeyword("UNCHARTED");
                else if (UseTonemapper == Tonemapper.Filmic)
                    AcesMat.EnableKeyword("FILMIC");
            }

            if (UseGammaCorrection) AcesMat.EnableKeyword("GAMMACORRECTION");
            else AcesMat.DisableKeyword("GAMMACORRECTION");

            var result = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            Graphics.Blit(result, AcesMat, 0, true);
            ReleaseRT(source);
            return result;
        }
    }

    public class ScreenSpaceReflectionNode : RenderPassNode
    {
        public override string Title => "Screen Space Reflection";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public float Threshold = 0.15f;
        public int Steps = 16;
        public int RefineSteps = 4;

        Material Mat;

        public override RenderTexture Render()
        {
            var gbuffer = Camera.Current.gBuffer;
            var source = GetInputValue<RenderTexture>("RenderTexture");
            if (source == null) return null;

            Mat ??= new Material(Shader.Find("Defaults/SSR.shader"));
            Mat.SetTexture("gColor", source.InternalTextures[0]);
            Mat.SetTexture("gNormalMetallic", gbuffer.NormalMetallic);
            Mat.SetTexture("gPositionRoughness", gbuffer.PositionRoughness);
            Mat.SetTexture("gDepth", gbuffer.Depth);

            Mat.SetFloat("SSR_THRESHOLD", Math.Clamp(Threshold, 0.0f, 1.0f));
            Mat.SetInt("SSR_STEPS", Math.Clamp(Steps, 16, 32));
            Mat.SetInt("SSR_BISTEPS", Math.Clamp(RefineSteps, 0, 16));

            var result = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            Graphics.Blit(result, Mat, 0, true);
            ReleaseRT(source);
            return result;
        }

        public override void OnValidate()
        {
            Threshold = Math.Clamp(Threshold, 0.0f, 1.0f);
            Steps = Math.Clamp(Steps, 16, 32);
            RefineSteps = Math.Clamp(RefineSteps, 0, 16);
        }
    }

    public class TAANode : RenderPassNode
    {
        public override string Title => "Temporal Anti-Aliasing";
        public override float Width => 125;

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public bool Jitter2X = false;

        Material Mat;
        Vector2 Jitter;
        Vector2 PreviousJitter;

        readonly static Vector2[] Halton16 =
        [
            new Vector2(0.5f, 0.333333f),
            new Vector2(0.25f, 0.666667f),
            new Vector2(0.75f, 0.111111f),
            new Vector2(0.125f, 0.444444f),
            new Vector2(0.625f, 0.777778f),
            new Vector2(0.375f, 0.222222f),
            new Vector2(0.875f, 0.555556f),
            new Vector2(0.0625f, 0.888889f),
            new Vector2(0.5625f, 0.037037f),
            new Vector2(0.3125f, 0.370370f),
            new Vector2(0.8125f, 0.703704f),
            new Vector2(0.1875f, 0.148148f),
            new Vector2(0.6875f, 0.481481f),
            new Vector2(0.4375f, 0.814815f),
            new Vector2(0.9375f, 0.259259f),
            new Vector2(0.03125f, 0.592593f),
        ];

        public override void Prepare(int width, int height)
        {
            // Apply Halton jitter
            long n = Time.frameCount % 16;
            var halton = Halton16[n];
            PreviousJitter = Jitter;
            Jitter = new Vector2((halton.x - 0.5f), (halton.y - 0.5f)) * 2.0;
            if (Jitter2X)
                Jitter *= 2.0;

            Graphics.MatProjection.M31 += Jitter.x / width;
            Graphics.MatProjection.M32 += Jitter.y / height;

            Graphics.UseJitter = true; // This applies the jitter to the Velocity Buffer/Motion Vectors
            Graphics.Jitter = Jitter / new Vector2(width, height);
            Graphics.PreviousJitter = PreviousJitter / new Vector2(width, height);
        }

        public override RenderTexture Render()
        {
            var source = GetInputValue<RenderTexture>("RenderTexture");
            if(source == null) return null;

            var history = Camera.Current.GetCachedRT("TAA_HISTORY", RP.Width, RP.Height, [TextureImageFormat.Short3]);

            Mat ??= new Material(Shader.Find("Defaults/TAA.shader"));
            Mat.SetTexture("gColor", source.InternalTextures[0]);
            Mat.SetTexture("gHistory", history.InternalTextures[0]);
            Mat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);
            Mat.SetTexture("gVelocity", Camera.Current.gBuffer.Velocity);
            Mat.SetTexture("gDepth", Camera.Current.gBuffer.Depth);

            Mat.SetInt("ClampRadius", Jitter2X ? 2 : 1);

            Mat.SetVector("Jitter", Graphics.Jitter);
            Mat.SetVector("PreviousJitter", Graphics.PreviousJitter);

            var result = GetRenderTexture(1f, [TextureImageFormat.Short3]);
            Graphics.Blit(result, Mat, 0, true);
            Graphics.Blit(history, result.InternalTextures[0], true);
            return result;
        }
    }

    [DisallowMultipleNodes]
    public class OutputNode : Node
    {
        public override string Title => "Output";
        public override float Width => 125;

        public string Pipeline = "Deferred";

        [Input(ShowBackingValue.Never), SerializeIgnore] public RenderTexture RenderTexture;

        public override object GetValue(NodePort port)
        {
            return GetInputValue<RenderTexture>("RenderTexture");
        }
    }
}
