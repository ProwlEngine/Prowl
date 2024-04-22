using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Resources.RenderPipeline;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;

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

        public int Width { get; private set; }
        public int Height { get; private set; }

        public void Prepare(int width, int height)
        {
            Width = width;
            Height = height;
            foreach (var node in nodes)
            {
                if (node is RenderPassNode renderPass)
                    renderPass.Prepare(width, height);
            }
        }

        public RenderTexture? Render()
        {
            RenderTexture? result = null;
            try
            {
                result = GetNode<OutputNode>().GetValue(null) as RenderTexture;
            }
            catch (Exception e)
            {
                Debug.LogError("[RenderPipeline] " + e.Message + Environment.NewLine + e.StackTrace);
            }

            foreach (var rt in UsedRenderTextures)
                RenderTexture.ReleaseTemporaryRT(rt);
            UsedRenderTextures.Clear();
            return result;
        }
    }
}
