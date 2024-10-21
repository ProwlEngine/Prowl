// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Utils/Are Lines Parallel")]
public class Math_AreLinesParallelNode : Node
{
    public override string Title => "Are Lines Parallel";
    public override float Width => 100;

    [Input] public Vector2 Line1Start;
    [Input] public Vector2 Line1End;
    [Input] public Vector2 Line2Start;
    [Input] public Vector2 Line2End;
    [Output, SerializeIgnore] public bool Parallel;

    public override object GetValue(NodePort input)
    {
        Vector2 line1Start = GetInputValue(nameof(Line1Start), Line1Start);
        Vector2 line1End = GetInputValue(nameof(Line1End), Line1End);
        Vector2 line2Start = GetInputValue(nameof(Line2Start), Line2Start);
        Vector2 line2End = GetInputValue(nameof(Line2End), Line2End);

        return MathD.AreLinesParallel(line1Start, line1End, line2Start, line2End, MathD.Small);
    }
}
