// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Clamp01")]
public class Math_Clamp01Node : Math_SingleInOutNode
{
    public override string Title => "Clamp01";
    public override double Execute() => MathD.Clamp01(GetInput());
}
