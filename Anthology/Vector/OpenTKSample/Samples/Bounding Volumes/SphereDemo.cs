// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample;

// Basic Shape Demos
public class SphereDemo : IDemo
{
    public string Name => "Sphere";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float bounce = Maths.Abs(Maths.Sin(timeInSeconds * 1.5f)) * 0.3f;
        float sizeWave = Maths.Sin(timeInSeconds * 0.5f) * 0.2f;

        Float3 spherePos = position + new Float3(0, bounce, 0);
        Gizmo.DrawSphereWireframe(
            new Sphere((Double3)spherePos, 1.0f + sizeWave * 0.2f),
            new Float4(0, 0, 1, 1),
            12
        );
    }

    public Float3 GetBounds() => new Float3(2.4f, 2.6f, 2.4f);
}
