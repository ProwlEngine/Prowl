// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Min")]
public class Math_MinNode : Math_DoubleInOutNode
{
    public override string Title => "Min";
    public override double Execute() => MathD.Min(GetInputA(), GetInputB());
}
