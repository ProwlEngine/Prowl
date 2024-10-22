// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Utils/Is Point In 3D Triangle")]
public class Math_IsPointIn3DTriangleNode : Node
{
    public override string Title => "Is Point In 3D Triangle";
    public override float Width => 100;

    [Input] public Vector3 Point;
    [Input] public Vector3 A;
    [Input] public Vector3 B;
    [Input] public Vector3 C;
    [Output, SerializeIgnore] public bool Inside;

    public override object GetValue(NodePort input)
    {
        Vector3 point = GetInputValue(nameof(Point), Point);
        Vector3 a = GetInputValue(nameof(A), A);
        Vector3 b = GetInputValue(nameof(B), B);
        Vector3 c = GetInputValue(nameof(C), C);

        return MathD.IsPointInTriangle(point, a, b, c);
    }
}
