// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("General/Quit")]
public class QuitNode : InOutFlowNode
{
    public override string Title => "Quit";
    public override float Width => 100;

    public override void Execute(NodePort input)
    {
        Application.Quit();

        ExecuteNext();
    }
}
