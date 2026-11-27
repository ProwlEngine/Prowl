// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample;

public class AABBDemo : IDemo
{
    public string Name => "AABB";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float scaleWave = Maths.Sin(timeInSeconds * 0.8f) * 0.3f;
        float scale = 1.0f + scaleWave * 0.9f;

        Gizmo.DrawAABB(new AABB(
            position + new Float3(-0.75f * scale, -0.75f, -0.75f * scale),
            position + new Float3(0.75f * scale, 0.75f, 0.75f * scale)
        ), new Float4(0, 1, 0, 1));
    }

    public Float3 GetBounds() => new Float3(2.0f, 1.5f, 2.0f);
}
