// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Acos")]
public class Math_AcosNode : Math_SingleInOutNode
{
    public override string Title => "Acos";
    public override double Execute() => MathD.Acos(GetInput());
}
