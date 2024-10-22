// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Asin")]
public class Math_AsinNode : Math_SingleInOutNode
{
    public override string Title => "Asin";
    public override double Execute() => MathD.Asin(GetInput());
}
