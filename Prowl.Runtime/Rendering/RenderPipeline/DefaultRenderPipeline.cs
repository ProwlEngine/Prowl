using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;


namespace Prowl.Runtime.RenderPipelines
{
    [CreateAssetMenu("RenderPipeline")]
    public sealed class DefaultRenderPipeline : RenderPipeline
    {
        public float Contrast = 1f;
        public float Saturation = 1.15f;
        public enum Tonemapper { Melon, Aces, Reinhard, Uncharted2, Filmic, None }
        public Tonemapper UseTonemapper = Tonemapper.Melon;
        Material? toneMapperMat;
        Tonemapper? prevTonemapper = null;


        private Material screenMat;
        private Material blitMat;

        private Dictionary<Camera, RenderTexture> gBuffers = [];



        public override void Render(RenderingContext context, Camera[] cameras)
        {
            // Create and schedule a command to clear the current render target
            var rootBuffer = new CommandBuffer();
            rootBuffer.SetRenderTarget(context.TargetFramebuffer);
            rootBuffer.ClearRenderTarget(true, true, Color.black);

            context.ExecuteCommandBuffer(rootBuffer);

            // Create and schedule a command to clear the current render target
            foreach (var cam in cameras)
            {
                var camBuffer = CommandBufferPool.Get("Camera Buffer");

                // Update the value of built-in shader variables, based on the current Camera
                var target = context.SetupTargetCamera(cam, out var width, out var height);

                // GBuffer Format:
                // Albedo - RGB8U
                // Normal (Pack with Octahedron mapping) - RG8 - Maybe RG16?
                // Emissive - RGB8U
                // AO + Roughness + Metallic - RGB8U
                // Object ID - R32U
                // Depth and Stencil - D24S8
                // TODO: Is there no R8_G8_B8_UNorm Format? Using RGBA8u instead

                if (!gBuffers.TryGetValue(cam, out var gBuffer))
                {
                    RenderTextureDescription desc = new(
                        width, height,
                        Veldrid.PixelFormat.D24_UNorm_S8_UInt, // Depth
                        [
                            Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, // Albedo
                            Veldrid.PixelFormat.R16_G16_B16_A16_Float, // Position
                            Veldrid.PixelFormat.R16_G16_B16_A16_Float, // Normal
                            Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, // Emissive
                            Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, // AO + Roughness + Metallic
                            Veldrid.PixelFormat.R32_UInt // Object ID
                        ],
                        true,
                        false,
                        Veldrid.TextureSampleCount.Count1
                    );

                    gBuffer = RenderTexture.GetTemporaryRT(desc);
                    gBuffers.Add(cam, gBuffer);
                }

                camBuffer.SetRenderTarget(gBuffer);

                //if (cam.DoClear)
                //    camBuffer.ClearRenderTarget(true, true, cam.ClearColor);
                camBuffer.ClearRenderTarget(true, true, Color.black);

                context.ExecuteCommandBuffer(camBuffer);
                CommandBufferPool.Release(camBuffer);

                // Get the culling parameters from the current Camera
                var camFrustrum = cam.GetFrustrum(width, height);

                // Use the culling parameters to perform a cull operation, and store the results
                var cullingResults = context.Cull(camFrustrum);

                // Sort renderables
                var sorted = context.SortRenderables(cullingResults, SortMode.FrontToBack);

                context.DrawRenderers(sorted, new("Opaque"), cam.LayerMask);

                // Draw Lighting effects
                var lightingRT = Pass_Lighting(context, cam, width, height, gBuffer, sorted);

                var combined = Pass_Combine(context, gBuffer.ColorBuffers[0], lightingRT.ColorBuffers[0], width, height);
                RenderTexture.ReleaseTemporaryRT(lightingRT);

                Pass_ToneMapping(context, combined.ColorBuffers[0], target, width, height);

                RenderTexture.ReleaseTemporaryRT(combined);
            }

            // Instruct the graphics API to perform all scheduled commands
            context.Submit();
        }

        private void Pass_ToneMapping(RenderingContext context, Texture2D toTonemap, Veldrid.Framebuffer target, uint width, uint height)
        {
            CommandBuffer cmd = CommandBufferPool.Get("ToneMapping Pass");

            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(false, true, Color.black);

            cmd.SetTexture("_Texture", toTonemap);
            //cmd.SetFloat("_Contrast", (float)MathD.Clamp(Contrast, 0, 2));
            //cmd.SetFloat("_Saturation", (float)MathD.Clamp(Saturation, 0, 2));
            cmd.SetFloat("_Contrast", 1.0f);
            cmd.SetFloat("_Saturation", 1.15f);
            // Because we always Reset the tonemappers to disabled then re-enable them
            // this will trigger a Uniform Location Cache clear every single frame
            // As the shader could be changing, so we do a previous check to see if we need to do this
            //if (prevTonemapper != UseTonemapper)
            //{
            //    prevTonemapper = UseTonemapper;
            //    toneMapperMat.SetKeyword("MODE", ((int)UseTonemapper).ToString());
            //}

            toneMapperMat ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ToneMapper.shader"));
            cmd.SetMaterial(toneMapperMat, 0);
            cmd.DrawSingle(Mesh.GetFullscreenQuad());

            context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);
        }

        private RenderTexture Pass_Lighting(RenderingContext context, Camera cam, uint width, uint height, RenderTexture? gBuffer, SortedList<double, List<Renderable>> sorted)
        {
            CommandBuffer lightCmd = CommandBufferPool.Get("Lighting Pass");
            lightCmd.SetTexture("Camera_Albedo", gBuffer.ColorBuffers[0]);
            lightCmd.SetTexture("Camera_Position", gBuffer.ColorBuffers[1]);
            lightCmd.SetTexture("Camera_Normal", gBuffer.ColorBuffers[2]);
            lightCmd.SetTexture("Camera_Emissive", gBuffer.ColorBuffers[3]);
            lightCmd.SetTexture("Camera_Surface", gBuffer.ColorBuffers[4]);
            lightCmd.SetTexture("Camera_ObjectID", gBuffer.ColorBuffers[5]);
            lightCmd.SetTexture("Camera_Depth", gBuffer.DepthBuffer);

            RenderTextureDescription lightingDesc = new(width, height, null, [Veldrid.PixelFormat.R16_G16_B16_A16_Float]);
            var lightingRT = RenderTexture.GetTemporaryRT(lightingDesc);
            lightCmd.SetRenderTarget(lightingRT);
            lightCmd.ClearRenderTarget(false, true, Color.black);

            context.ExecuteCommandBuffer(lightCmd);

            context.DrawRenderers(sorted, new("Lighting"), cam.LayerMask);

            CommandBufferPool.Release(lightCmd);

            return lightingRT;
        }

        private RenderTexture Pass_Combine(RenderingContext context, Texture2D albedo, Texture2D lighting, uint width, uint height)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Combine Pass");

            var final = RenderTexture.GetTemporaryRT(width, height, null, [Veldrid.PixelFormat.R16_G16_B16_A16_Float]);
            cmd.SetRenderTarget(final);
            cmd.ClearRenderTarget(false, true, Color.black);

            cmd.SetTexture("_AlbedoTex", albedo);
            cmd.SetTexture("_LightTex", lighting);

            screenMat ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Screen.shader"));
            cmd.SetMaterial(screenMat, 0);
            cmd.DrawSingle(Mesh.GetFullscreenQuad());

            context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);

            return final;
        }
    }
}