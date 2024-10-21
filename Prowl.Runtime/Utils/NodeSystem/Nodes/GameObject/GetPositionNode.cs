// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Get Position")]
public class GetPositionNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Get Position";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Output] public Vector3 Position;

    public override object GetValue(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        return t != null ? t.Transform.position : null;
    }
}
