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
            typeof(CameraNode), 
            typeof(PBRDeferredNode),
            typeof(DepthOfFieldNode),
            typeof(AcesFittedNode),
            typeof(OutputNode),
            ];
    }
}
