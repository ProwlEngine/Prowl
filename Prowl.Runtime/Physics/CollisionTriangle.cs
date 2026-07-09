// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision;
using Jitter2.LinearMath;

namespace Prowl.Runtime;

/// <summary>
/// A triangle shape used for collision detection with terrain heightmaps.
/// Implements ISupportMappable for use with Jitter2's MPR-EPA collision algorithm.
/// Based on CollisionTriangle from Jitter Physics 2 demos.
/// </summary>
public struct CollisionTriangle : ISupportMappable
{
    public JVector A, B, C;

    public void SupportMap(in JVector direction, out JVector result)
    {
        float min = (float)JVector.Dot(A, direction);
        float dot = (float)JVector.Dot(B, direction);

        result = A;
        if (dot > min)
        {
            min = dot;
            result = B;
        }

        dot = (float)JVector.Dot(C, direction);
        if (dot > min)
        {
            result = C;
        }
    }

    public void GetCenter(out JVector point)
    {
        point = (1.0f / 3.0f) * (A + B + C);
    }
}
