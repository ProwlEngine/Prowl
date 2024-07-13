namespace Prowl.Runtime.RenderPipelines
{
    [DisallowMultipleNodes]
    public class OutputNode : InFlowNode
    {
        public override string Title => "Output";
        public override float Width => 150;

        [Input, SerializeIgnore] public Texture2D Result;

        public override void Execute()
        {
            (graph as RenderPipeline).Result = GetInputValue<Texture2D>("Result");
        }
    }
}
