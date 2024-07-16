using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class TargetNode : Node
    {
        public override string Title => "Target";
        public override float Width => 100;
        [Output, SerializeIgnore] public NodeRenderTexture Target;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).Target;
    }
}
