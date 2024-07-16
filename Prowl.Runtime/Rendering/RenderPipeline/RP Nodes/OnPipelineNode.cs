using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/On Pipeline")]
    public class OnPipelineNode : OutFlowNode
    {
        public override string Title => "On Pipeline";
        public override float Width => 150;

        [Tooltip("Default Pipelines are 'Main' & 'Shadow'")]
        public string Name = "Main";

        public override void Execute(NodePort port)
        {
            ExecuteNext();
        }
    }
}
