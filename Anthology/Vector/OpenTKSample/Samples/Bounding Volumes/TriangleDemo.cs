// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample;

public class TriangleDemo : IDemo
{
    public string Name => "Triangle";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float rotation = timeInSeconds * 0.3f;
        float cosRot = Maths.Cos(rotation);
        float sinRot = Maths.Sin(rotation);

        Triangle triangle = new Triangle(
            (Double3)position + new Double3(cosRot * 0 - sinRot * (-1), -1, cosRot * (-0.5f) - sinRot * 0),
            (Double3)position + new Double3(cosRot * 1 - sinRot * 1, 1, cosRot * (-0.5f) - sinRot * 1),
            (Double3)position + new Double3(cosRot * (-1) - sinRot * 1, 1, cosRot * 0.5f - sinRot * (-1))
        );

        Gizmo.DrawTriangle(triangle, new Float4(0.5f, 0.5f, 1, 1f), true);
        Gizmo.DrawTriangle(triangle, new Float4(1, 0, 0, 1), false);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}
