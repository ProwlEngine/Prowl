// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

public class CommentNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "CommentNode";
    public override float Width => 50;

    public string Header;
    public string Desc;

    public override object GetValue(NodePort port) => null;
}
