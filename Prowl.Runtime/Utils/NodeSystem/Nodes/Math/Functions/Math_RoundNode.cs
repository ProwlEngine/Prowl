// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Round")]
public class Math_RoundNode : Math_SingleInOutNode
{
    public override string Title => "Round";
    public override double Execute() => MathD.Round(GetInput());
}
