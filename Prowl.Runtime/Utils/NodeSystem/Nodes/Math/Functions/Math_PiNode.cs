// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Pi")]
public class Math_PiNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Pi";
    public override float Width => 50;

    [Output, SerializeIgnore] public double Pi;

    public override object GetValue(NodePort input) => MathD.PI;
}
