using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Target Render Texture")]
    public class TargetRenderTextureNode : Node
    {
        public override string Title => "Target Render Texture";
        public override float Width => 100;
        [Output, SerializeIgnore] public NodeRenderTexture Target;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).Target;
    }
}
