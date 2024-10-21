// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Cos")]
public class Math_CosNode : Math_SingleInOutNode
{
    public override string Title => "Cos";
    public override double Execute() => MathD.Cos(GetInput());
}
