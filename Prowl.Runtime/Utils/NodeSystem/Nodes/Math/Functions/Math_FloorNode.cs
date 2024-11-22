// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Floor")]
public class Math_FloorNode : Math_SingleInOutNode
{
    public override string Title => "Floor";
    public override double Execute() => MathD.Floor(GetInput());
}
