// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Converts vectors and rotations between the engine's types and the physics engine's.</summary>
internal static class JitterConvert
{
    public static JVector ToJitter(this Float3 v) => new(v.X, v.Y, v.Z);

    public static Float3 ToProwl(this JVector v) => new(v.X, v.Y, v.Z);

    public static JQuaternion ToJitter(this Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public static Quaternion ToProwl(this JQuaternion q) => new(q.X, q.Y, q.Z, q.W);
}
