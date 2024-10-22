// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Get Parent")]
public class GetParentNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Get Parent";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Output, SerializeIgnore] public GameObject Parent;

    public override object GetValue(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        return t != null ? t.parent : null;
    }
}
