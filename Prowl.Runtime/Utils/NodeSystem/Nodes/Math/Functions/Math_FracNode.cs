// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Frac")]
public class Math_FracNode : Math_SingleInOutNode
{
    public override string Title => "Frac";
    public override double Execute() => MathD.Frac(GetInput());
}
