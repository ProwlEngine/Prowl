// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Golden Ratio")]
public class Math_GoldenRatioNode : Node
{
    public override bool ShowTitle => false;
    public override string Title => "Golden Ratio";
    public override float Width => 50;

    [Output, SerializeIgnore] public double GoldenRatio;

    public override object GetValue(NodePort input) => MathD.GOLDEN_RATIO;
}
