using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.RenderPipelines
{
    [CreateAssetMenu("GraphRenderPipeline")]
    public class RenderPipeline : NodeGraph
    {
        public override string[] NodeCategories => [
            "General",
            "Flow Control",
            "Operations",
            "Rendering",
        ];

        public override (string, Type)[] NodeTypes => [
            ("Parameter", typeof(ParameterNode)),
        ];

        //public override (string, Type)[] NodeReflectionTypes => [
        //    ("Reflection/Vector3", typeof(Vector3)),
        //];

        private List<NodeRenderTexture> rts = [];

        public Vector2 Resolution { get; private set; }
        public Camera.CameraData CurrentCamera { get; private set; }
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

        public void Render(RenderingContext context, Camera.CameraData[] cameras)
        {
            // Sort the cameras by their render order
            cameras = cameras.OrderBy(c => c.RenderOrder).ToArray();

            //// Create and schedule a command to clear the current render target
            //var rootBuffer = new CommandBuffer();
            //rootBuffer.SetRenderTarget(context.TargetTexture);
            //rootBuffer.ClearRenderTarget(context.TargetTexture.ColorBuffers.Length > 0, context.TargetTexture.DepthBuffer != null, Color.black);
            //
            //context.ExecuteCommandBuffer(rootBuffer);

            Context = context;

            foreach (var cam in cameras)
            {
                // Get Width and Height an the target RenderTexture
                var target = context.TargetTexture;
                uint width = context.TargetTexture.Width;
                uint height = context.TargetTexture.Height;

                if (cam.Target.IsAvailable)
                {
                    target = cam.Target.Res!;
                    width = cam.Target.Res!.Width;
                    height = cam.Target.Res!.Height;
                }

                context.PushCamera(cam);

                try
                {
                    // Update the value of built-in shader variables, based on the current Camera
                    Target = new NodeRenderTexture(target);
                    Resolution = new Vector2(width, height);
                    CurrentCamera = cam;

                    context.SetRenderTarget(Target.RenderTexture);
                    var viewX = (int)(cam.Viewrect.x * width);
                    var viewY = (int)(cam.Viewrect.y * height);
                    var viewWidth = (int)(cam.Viewrect.width * width);
                    var viewHeight = (int)(cam.Viewrect.height * height);
                    context.SetViewports(viewX, viewY, viewWidth, viewHeight, 0f, 1f);
                    if (cam.DoClear)
                        context.ClearRenderTarget(Target.HasDepth, Target.HasColors, cam.ClearColor);

                    var pipelineNode = GetNodes<OnPipelineNode>().FirstOrDefault(n => n.Name == context.PipelineName);
                    if (pipelineNode == null)
                    {
                        //Debug.LogError($"Pipeline Node {context.PipelineName} not found!");
                        return;
                    }

                    pipelineNode.Execute(null);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error rendering camera: {e.Message}");
                }
                finally
                {

                    context.PopCamera();

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
