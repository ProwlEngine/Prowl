// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Utils/Ray Intersects Triangle")]
public class Math_RayIntersectsTriangleNode : Node
{
    public override string Title => "Ray Intersects Triangle";
    public override float Width => 100;

    [Input] public Vector3 RayOrigin;
    [Input] public Vector3 RayDirection;
    [Input] public Vector3 V0;
    [Input] public Vector3 V1;
    [Input] public Vector3 V2;
    [Output, SerializeIgnore] public bool Hit;
    [Output, SerializeIgnore] public Vector3 Point;

    public override object GetValue(NodePort input)
    {
        Vector3 rayOrigin = GetInputValue(nameof(RayOrigin), RayOrigin);
        Vector3 rayDirection = GetInputValue(nameof(RayDirection), RayDirection);
        Vector3 v0 = GetInputValue(nameof(V0), V0);
        Vector3 v1 = GetInputValue(nameof(V1), V1);
        Vector3 v2 = GetInputValue(nameof(V2), V2);

        bool hit = MathD.RayIntersectsTriangle(rayOrigin, rayDirection, v0, v1, v2, out var point);
        if (input.fieldName == nameof(Hit))
            return hit;
        return point;
    }
}
