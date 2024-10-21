// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Rad2Deg")]
public class Math_Rad2DegNode : Math_SingleInOutNode
{
    public override string Title => "Rad2Deg";
    public override double Execute() => GetInput().ToDeg();
}
