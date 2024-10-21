// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Utils/Is Point In 2D Triangle")]
public class Math_IsPointIn2DTriangleNode : Node
{
    public override string Title => "Is Point In 2D Triangle";
    public override float Width => 100;

    [Input] public Vector2 Point;
    [Input] public Vector2 A;
    [Input] public Vector2 B;
    [Input] public Vector2 C;
    [Output, SerializeIgnore] public bool Inside;

    public override object GetValue(NodePort input)
    {
        Vector2 point = GetInputValue(nameof(Point), Point);
        Vector2 a = GetInputValue(nameof(A), A);
        Vector2 b = GetInputValue(nameof(B), B);
        Vector2 c = GetInputValue(nameof(C), C);

        return MathD.IsPointInTriangle(point, a, b, c);
    }
}
