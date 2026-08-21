// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Photonic.Raytracing;

/// <summary>Small shared math helpers for the ray-tracing / rasterization paths.</summary>
internal static class RayMath
{
    /// <summary>Transform a vector with implicit w (1 for points, 0 for directions). Column-major matrix.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Float3 Transform(Float4x4 m, Float3 v, float w)
    {
        float x = m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w;
        float y = m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w;
        float z = m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w;
        return new Float3(x, y, z);
    }

    /// <summary>
    /// Relative epsilon used to scale a ray offset with the magnitude of the coordinates it is
    /// applied to. A rounded FLT_EPSILON: <c>p + p * 2e-7</c> lands on (or just past) the next
    /// representable float, so the offset stays "just enough" however far the geometry sits from
    /// the world origin, where a fixed epsilon would either stop separating or start peter-panning.
    /// </summary>
    public const float FloatScaleEpsilon = 2e-7f;

    /// <summary>
    /// Ray origin for a ray leaving <paramref name="position"/> along <paramref name="normal"/>.
    /// Combines the caller's absolute bias with a magnitude-relative offset, so precision loss far
    /// from the origin cannot reintroduce self-intersection.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Float3 OffsetOrigin(Float3 position, Float3 normal, float bias)
    {
        return new Float3(
            position.X + normal.X * bias + System.MathF.CopySign(System.MathF.Abs(position.X) * FloatScaleEpsilon, normal.X),
            position.Y + normal.Y * bias + System.MathF.CopySign(System.MathF.Abs(position.Y) * FloatScaleEpsilon, normal.Y),
            position.Z + normal.Z * bias + System.MathF.CopySign(System.MathF.Abs(position.Z) * FloatScaleEpsilon, normal.Z));
    }
}
