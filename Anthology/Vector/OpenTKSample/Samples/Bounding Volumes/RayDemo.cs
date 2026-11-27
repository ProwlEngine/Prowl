// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample;

public class RayDemo : IDemo
{
    public string Name => "Ray";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float fastWave = Maths.Sin(timeInSeconds * 2.0f) * 0.1f;
        float slowWave = Maths.Sin(timeInSeconds * 0.5f) * 0.2f;

        Float3 rayDir = Float3.Normalize(new Float3(0.5f + fastWave * 10f, 1, 0.2f + slowWave * 10f));
        Ray ray = new Ray(position + new Float3(0, -1, 0), rayDir);

        Gizmo.DrawRay(ray, 3.0f, new Float4(1, 1, 0, 1));
    }

    public Float3 GetBounds() => new Float3(2.0f, 3.0f, 2.0f);
}
