// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Transform/Set Rotation")]
public class SetRotationNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Rotation";
    public override float Width => 100;

    [Input] public bool InLocalSpace;
    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Quaternion Rotation;

    public override void Execute(NodePort input)
    {
        bool local = GetInputValue("InLocalSpace", InLocalSpace);
        GameObject t = GetInputValue("Target", Target);
        Quaternion r = GetInputValue("Rotation", Rotation);

        if (t != null)
        {
            if (local)
                t.Transform.localRotation = r;
            else
                t.Transform.rotation = r;
        }

        ExecuteNext();
    }
}
