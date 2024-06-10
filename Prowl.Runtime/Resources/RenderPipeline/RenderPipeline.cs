using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Resources.RenderPipeline;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime
{


    [CreateAssetMenu("RenderPipeline")]
    public class RenderPipeline : NodeGraph
    {
        public override Type[] NodeTypes => [ 
            typeof(PBRDeferredNode),
            typeof(PostPBRDeferredNode),
            typeof(ProceduralSkyboxNode),
            typeof(ScreenSpaceReflectionNode),
            typeof(DepthOfFieldNode),
            typeof(BloomNode),
            typeof(TonemappingNode),
            typeof(TAANode),
            typeof(OutputNode),
            ];

        public readonly List<RenderTexture> UsedRenderTextures = [];

        public int Width { get; private set; } = 0;
        public int Height { get; private set; } = 0;
        public string Pipeline { get; private set; } = "Deferred";

        private OutputNode? outputNode = null;

        public void Prepare(string pipeline, int width, int height)
        {
            Width = width;
            Height = height;
            Pipeline = pipeline;

            var pipelineNodes = GetNodes<OutputNode>();
            outputNode = pipelineNodes.Where(n => n.Pipeline == Pipeline).FirstOrDefault();
            Internal_Prepare(outputNode);
        }
         
        private void Internal_Prepare(Node start)
        {
            if (start is RenderPassNode renderPass)
                renderPass.Prepare(Width, Height);

            foreach (var port in start.Inputs)
            {
                if (port.node != null && port.node != start)
                    Internal_Prepare(port.node);
                else if (port.Connection.node != null && port.Connection.node != start)
                    Internal_Prepare(port.Connection.node);
            }
        }

        public RenderTexture? Render()
        {
            if (outputNode == null)
            {
                Debug.LogError("[RenderPipeline] Output node not assigned, Did you call Prepare() first?");
                return null;
            }
            RenderTexture? result = null;
            try
            {
                result = outputNode.GetValue(null) as RenderTexture;
            }
            catch (Exception e)
            {
                Debug.LogError("[RenderPipeline] " + e.Message + Environment.NewLine + e.StackTrace);
            }
            outputNode = null;
            foreach (var rt in UsedRenderTextures)
                RenderTexture.ReleaseTemporaryRT(rt);
            UsedRenderTextures.Clear();
            return result;
        }
    }
}
