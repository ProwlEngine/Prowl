using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Target Resolution")]
    public class TargetResolutionNode : Node
    {
        public override string Title => "Target Resolution";
        public override float Width => 100;
        [Output, SerializeIgnore] public Vector2 Resolution;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).Resolution;
    }
}
