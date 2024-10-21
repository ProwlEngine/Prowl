// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Deg2Rad")]
public class Math_Deg2RadNode : Math_SingleInOutNode
{
    public override string Title => "Deg2Rad";
    public override double Execute() => GetInput().ToRad();
}
