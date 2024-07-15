using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime.RenderPipelines
{
    [CreateAssetMenu("GraphRenderPipeline")]
    public class RenderPipeline : NodeGraph
    {
        public override string[] NodeCategories => [
            "General",
            "Math",
            "Rendering",
        ];

        public override Type[] NodeTypes => [
            typeof(ParameterNode),
        ];

        private List<NodeRenderTexture> rts = [];

        public Vector2 Resolution { get; private set; }
        public Camera CurrentCamera { get; private set; }
        public RenderingContext Context { get; private set; }

        public NodeRenderTexture Target { get; internal set; }

        private Material blitMat;

        public NodeRenderTexture GetRT(RenderTextureDescription desc, RTBuffer[] colorFormats)
        {
            NodeRenderTexture rt = new(RenderTexture.GetTemporaryRT(desc), colorFormats);
            rts.Add(rt);
            return rt;
        }

        public void InitializeResources()
        {
        }

        public void Render(RenderingContext context, Camera[] cameras)
        {
            // Create and schedule a command to clear the current render target
            var rootBuffer = new CommandBuffer();
            rootBuffer.SetRenderTarget(context.TargetTexture);
            rootBuffer.ClearRenderTarget(true, true, Color.black);

            context.ExecuteCommandBuffer(rootBuffer);

            Context = context;

            foreach (var cam in cameras)
            {
                try
                {
                    // Update the value of built-in shader variables, based on the current Camera
                    Target = new NodeRenderTexture(context.SetupTargetCamera(cam, out var width, out var height));
                    Resolution = new Vector2(width, height);
                    CurrentCamera = cam;


                    var cmd = CommandBufferPool.Get("Camera Buffer");
                    cmd.SetRenderTarget(Target.RenderTexture);
                    //if (cam.DoClear)
                    //    cmd.ClearRenderTarget(true, true, cam.ClearColor);
                    cmd.ClearRenderTarget(true, true, Color.black);
                    context.ExecuteCommandBuffer(cmd);
                    //var outputNode = GetNode<OutputNode>();
                    //var outputTex = (outputNode.GetValue(null) as Texture2D) ?? throw new Exception($"Output Node must have a valid Texture2D!");

                    var pipelineNode = GetNode<OnPipelineNode>();
                    pipelineNode?.Execute();

                    // blit result into target
                    //cmd.SetRenderTarget(target);
                    //cmd.ClearRenderTarget(false, true, Color.black);
                    //cmd.SetTexture("_Texture", Result);
                    //blitMat ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Blit.shader"));
                    //cmd.SetMaterial(blitMat, 0);
                    //cmd.DrawSingle(Mesh.GetFullscreenQuad());
                    //
                    //context.ExecuteCommandBuffer(cmd);

                    CommandBufferPool.Release(cmd);

                }
                catch (Exception e)
                {
                    Debug.LogError($"Error rendering camera {cam.Name}: {e.Message}");
                }
                finally
                {
                    // Release all Temp Render Textures back into the RT Pool
                    foreach (var rt in rts)
                    {
                        rt.HasBeenReleased = true;
                        RenderTexture.ReleaseTemporaryRT(rt.RenderTexture);
                    }
                    rts.Clear();
                }
            }

            // Instruct the graphics API to perform all scheduled commands
            context.Submit();
        }

        public void ReleaseResources()
        {
        }
    }
}
