// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Utils/Does Line Intersect Line")]
public class Math_DoesLineIntersectLineNode : Node
{
    public override string Title => "Does Line Intersect Line";
    public override float Width => 100;

    [Input] public Vector2 Line1Start;
    [Input] public Vector2 Line1End;
    [Input] public Vector2 Line2Start;
    [Input] public Vector2 Line2End;
    [Output, SerializeIgnore] public bool Hit;
    [Output, SerializeIgnore] public Vector2 Point;

    public override object GetValue(NodePort input)
    {
        Vector2 line1Start = GetInputValue(nameof(Line1Start), Line1Start);
        Vector2 line1End = GetInputValue(nameof(Line1End), Line1End);
        Vector2 line2Start = GetInputValue(nameof(Line2Start), Line2Start);
        Vector2 line2End = GetInputValue(nameof(Line2End), Line2End);

        bool hit = MathD.DoesLineIntersectLine(line1Start, line1End, line2Start, line2End, out var point);
        if (input.fieldName == nameof(Hit))
            return hit;
        return point;
    }
}
