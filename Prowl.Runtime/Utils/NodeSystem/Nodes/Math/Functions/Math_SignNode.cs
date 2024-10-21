// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Sign")]
public class Math_SignNode : Math_SingleInOutNode
{
    public override string Title => "Sign";
    public override double Execute() => MathD.Sign(GetInput());
}
