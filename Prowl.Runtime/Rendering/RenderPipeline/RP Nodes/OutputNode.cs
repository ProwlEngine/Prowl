using Prowl.Runtime.NodeSystem;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    [DisallowMultipleNodes]
    public class OutputNode : Node
    {
        public override string Title => "Output";
        public override float Width => 150;

        [Input, SerializeIgnore] public Texture2D Result;

        public override object GetValue(NodePort port) => GetInputValue<Texture2D>("Result");
    }
}
