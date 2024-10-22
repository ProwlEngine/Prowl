// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Atan")]
public class Math_AtanNode : Math_SingleInOutNode
{
    public override string Title => "Atan";
    public override double Execute() => MathD.Atan(GetInput());
}
