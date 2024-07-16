using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class TargetCameraNode : Node
    {
        public override string Title => "Target Camera";
        public override float Width => 100;
        [Output, SerializeIgnore] public Camera.CameraData Camera;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).CurrentCamera;
    }
}
