// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Tan")]
public class Math_TanNode : Math_SingleInOutNode
{
    public override string Title => "Tan";
    public override double Execute() => MathD.Tan(GetInput());
}
