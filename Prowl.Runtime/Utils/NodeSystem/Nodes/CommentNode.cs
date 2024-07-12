using Prowl.Runtime.NodeSystem;
using System.Diagnostics;

namespace Prowl.Runtime.NodeSystem
{
    public class CommentNode : Node
    {
        public override bool ShowTitle => false;
        public override string Title => "CommentNode";
        public override float Width => 50;

        public string Header;
        public string Desc;

        public override object GetValue(NodePort port) => null;
    }
}
