// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/MoveTowards")]
public class Math_MoveTowardsNode : Node
{
    public override string Title => "Move Towards";
    public override float Width => 100;

    [Input] public double Current;
    [Input] public double Target;
    [Input] public double MaxDelta;
    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort input)
    {
        double current = GetInputValue(nameof(Current), Current);
        double target = GetInputValue(nameof(Target), Target);
        double maxDelta = GetInputValue(nameof(MaxDelta), MaxDelta);

        return MathD.MoveTowards(current, target, maxDelta);
    }
}
