// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Ceiling")]
public class Math_CeilingNode : Math_SingleInOutNode
{
    public override string Title => "Ceiling";
    public override double Execute() => MathD.Ceil(GetInput());
}
