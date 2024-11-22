// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Transform/Look At")]
public class GOLookAtNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Look At";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Vector3 WorldPosition;
    [Input] public Vector3 Up;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        Vector3 pos = GetInputValue("WorldPosition", WorldPosition);
        Vector3 up = GetInputValue("Up", Up);

        if (t != null)
            t.Transform.LookAt(pos, up);

        ExecuteNext();
    }
}
