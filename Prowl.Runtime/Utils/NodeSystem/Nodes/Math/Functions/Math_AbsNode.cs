// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Abs")]
public class Math_AbsNode : Math_SingleInOutNode
{
    public override string Title => "Abs";
    public override double Execute() => MathD.Abs(GetInput());
}
