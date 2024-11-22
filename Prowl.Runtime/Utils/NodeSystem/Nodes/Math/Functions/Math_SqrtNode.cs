// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Sqrt")]
public class Math_SqrtNode : Math_SingleInOutNode
{
    public override string Title => "Sqrt";
    public override double Execute() => MathD.Sqrt(GetInput());
}
