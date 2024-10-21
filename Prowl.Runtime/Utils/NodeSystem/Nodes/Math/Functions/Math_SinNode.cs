// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Sin")]
public class Math_SinNode : Math_SingleInOutNode
{
    public override string Title => "Sin";
    public override double Execute() => MathD.Sin(GetInput());
}
