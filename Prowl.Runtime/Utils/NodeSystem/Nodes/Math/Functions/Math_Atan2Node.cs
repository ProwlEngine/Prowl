// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Atan2")]
public class Math_Atan2Node : Math_DoubleInOutNode
{
    public override string Title => "Atan2";
    public override double Execute() => MathD.Atan(GetInputA(), GetInputB());
}
