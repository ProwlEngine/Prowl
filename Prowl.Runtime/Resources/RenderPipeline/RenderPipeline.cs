using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Resources.RenderPipeline;
using Prowl.Runtime.Utils;
using System;

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
    }
}
