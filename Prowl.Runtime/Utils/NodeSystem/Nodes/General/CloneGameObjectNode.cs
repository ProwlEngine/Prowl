// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Cloning;

namespace Prowl.Runtime.NodeSystem;

[Node("General/Clone GameObject")]
public class CloneGameObjectNode : InOutFlowNode
{
    public override string Title => "Clone GameObject";
    public override float Width => 150;

    [Input] public GameObject Target;
    [Output, SerializeIgnore, CloneField(CloneFieldFlags.Skip)] public GameObject Output;

    public override object GetValue(NodePort port) => Output;

    public override void Execute(NodePort input)
    {
        GameObject target = GetInputValue("Target", Target);

        if (target != null)
        {
            Output = target.DeepClone();
            SceneManagement.SceneManager.Scene.Add(Output);
        }

        ExecuteNext();
    }
}
