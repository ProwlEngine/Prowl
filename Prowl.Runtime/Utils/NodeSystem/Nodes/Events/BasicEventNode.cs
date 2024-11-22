// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

public abstract class BasicEventNode : OutFlowNode
{
    public override bool ShowTitle => true;
    public override float Width => 100;
    public override void Execute(NodePort input)
    {
        ExecuteNext();
    }
}
