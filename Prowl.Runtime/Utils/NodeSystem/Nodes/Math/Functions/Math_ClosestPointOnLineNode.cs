// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Utils/Closest Point On Line")]
public class Math_ClosestPointOnLineNode : Node
{
    public override string Title => "Closest Point On Line";
    public override float Width => 100;

    [Input] public Vector2 Point;
    [Input] public Vector2 LineStart;
    [Input] public Vector2 LineEnd;
    [Output, SerializeIgnore] public Vector2 Result;

    public override object GetValue(NodePort input)
    {
        Vector2 point = GetInputValue(nameof(Point), Point);
        Vector2 lineStart = GetInputValue(nameof(LineStart), LineStart);
        Vector2 lineEnd = GetInputValue(nameof(LineEnd), LineEnd);

        return MathD.GetClosestPointOnLine(point, lineStart, lineEnd);
    }
}
