// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

public class GJKConeAABBDemo : IDemo
{
    public string Name => "GJK: Cone-AABB";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f;

        // Animated cone
        float rotation = timeInSeconds * 0.5f;
        Float3 coneApex = position + new Float3(0, 0.7f, 0);
        Float3 axisDir = new Float3(
            Maths.Sin(rotation) * 0.5f,
            -1.0f,
            Maths.Cos(rotation) * 0.5f
        );

        float height = 1.0f;
        float baseRadius = 0.4f + Maths.Sin(timeInSeconds * 1.5f) * 0.1f;

        Cone cone = Cone.FromAxisDirection((Double3)coneApex, (Double3)axisDir, height, baseRadius);

        // Rotating AABB
        float aabbRot = timeInSeconds * 0.8f;
        float scale = 0.4f + Maths.Cos(timeInSeconds * 1.3f) * 0.1f;

        Float3 aabbOffset = new Float3(
            Maths.Cos(aabbRot) * 0.5f,
            Maths.Sin(aabbRot * 1.2f) * 0.3f,
            Maths.Sin(aabbRot) * 0.5f
        );

        AABB aabb = new AABB(
            (Double3)position + (Double3)aabbOffset - new Double3(scale, scale, scale),
            (Double3)position + (Double3)aabbOffset + new Double3(scale, scale, scale)
        );

        // Use GJK for collision detection
        bool intersects = GJK.Intersects(cone, aabb);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(0.8f, 0.2f, 0.8f, 1);

        Gizmo.DrawConeWireframe(cone, color, 16);
        Gizmo.DrawAABBSolid(aabb, new Float4(color.X, color.Y, color.Z, 0.4f));
        Gizmo.DrawAABB(aabb, color);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.5f, 2.5f);
}
