// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.NodeSystem;

[Node("Self/Self GameObject")]
public class SelfGameObjectNode : Node
{
    public override bool ShowTitle => true;
    public override string Title => "Self GameObject";
    public override float Width => 120;

    [Output, SerializeIgnore] public GameObject Self;

    public override object GetValue(NodePort port)
    {
        Blueprint blueprint = graph as Blueprint;
        return blueprint?.GameObject ?? throw new Exception("No GameObject assigned to Blueprint");
    }
}
