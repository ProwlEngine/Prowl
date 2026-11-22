// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>Geometric operations on a brush's plane set.</summary>
public static class CSGBrushGeometry
{
    /// <summary>Ray against the convex solid, in brush local space.</summary>
    public static bool Raycast(IReadOnlyList<Plane> planes, Float3 origin, Float3 direction, out float distance)
        => Raycast(planes, origin, direction, out distance, out _);

    /// <inheritdoc cref="Raycast(IReadOnlyList{Plane}, Float3, Float3, out float)"/>
    /// <param name="planeIndex">The plane the ray entered through, or -1 when it started inside.</param>
    public static bool Raycast(IReadOnlyList<Plane> planes, Float3 origin, Float3 direction,
                               out float distance, out int planeIndex)
    {
        distance = 0f;
        planeIndex = -1;
        if (planes.Count < 4) return false;

        float near = float.NegativeInfinity;
        float far = float.PositiveInfinity;
        int nearPlane = -1;

        for (int i = 0; i < planes.Count; i++)
        {
            float denom = Float3.Dot(planes[i].Normal, direction);
            float dist = planes[i].GetSignedDistanceToPoint(origin);

            if (MathF.Abs(denom) < 1e-9f)
            {
                if (dist > 0f) return false;   // parallel and outside
                continue;
            }

            float t = -dist / denom;
            if (denom < 0f)
            {
                if (t > near) { near = t; nearPlane = i; }   // entering
            }
            else far = MathF.Min(far, t);                    // leaving

            if (near > far) return false;
        }

        if (near >= 0f) { distance = near; planeIndex = nearPlane; }
        else distance = far;

        return distance >= 0f && far >= 0f;
    }

    /// <summary>A rotation whose local Y is <paramref name="normal"/>.</summary>
    public static Quaternion FrameFor(Float3 normal)
    {
        // A brush with a degenerate plane would otherwise build a rotation out of zero vectors.
        Float3 up = Float3.NormalizeSafe(normal, Float3.UnitY);

        // Any axis not nearly parallel to the normal gives a stable perpendicular to build from.
        Float3 helper = MathF.Abs(up.Y) > 0.99f ? Float3.UnitZ : Float3.UnitY;
        Float3 right = Float3.Normalize(Float3.Cross(helper, up));

        return Quaternion.LookRotation(Float3.Cross(up, right), up);
    }

    /// <summary>The box a draw then extrude drag describes, with corners and height in the work plane's frame.</summary>
    /// <returns>False when the drag has no volume.</returns>
    public static bool BoxFromDrag(Float3 cornerA, Float3 cornerB, float height,
                                   out Float3 center, out Float3 size)
    {
        size = new Float3(MathF.Abs(cornerB.X - cornerA.X), MathF.Abs(height),
                          MathF.Abs(cornerB.Z - cornerA.Z));

        center = new Float3((cornerA.X + cornerB.X) * 0.5f,
                            MathF.Min(cornerA.Y, cornerA.Y + height) + size.Y * 0.5f,
                            (cornerA.Z + cornerB.Z) * 0.5f);

        return size.X > 1e-4f && size.Y > 1e-4f && size.Z > 1e-4f;
    }

    /// <summary>Mirrors a plane set about its own origin along an <paramref name="axis"/> in the space <paramref name="frame"/> rotates the planes into.</summary>
    public static Plane[] Mirror(IReadOnlyList<Plane> planes, Float3 axis, Quaternion frame)
    {
        Float3 n = Float3.Normalize(axis);
        Quaternion inverse = Quaternion.Inverse(frame);
        var result = new Plane[planes.Count];

        for (int i = 0; i < planes.Count; i++)
        {
            Float3 outer = frame * planes[i].Normal;
            Float3 reflected = outer - n * (2f * Float3.Dot(outer, n));
            result[i] = new Plane(inverse * reflected, planes[i].D);
        }

        return result;
    }

    /// <summary>
    /// Mirrors a plane set along a world <paramref name="axis"/> through the brush's origin, taking the brush's whole
    /// transform into account so a scaled and rotated brush still reflects in world space.
    /// </summary>
    public static Plane[] Mirror(IReadOnlyList<Plane> planes, Float3 axis, Float4x4 localToWorld)
    {
        // A flattened transform has no inverse and nothing to mirror
        if (!TryMirrorMatrix(axis, localToWorld, out Float4x4 local)) return [.. planes];

        var result = new Plane[planes.Count];
        for (int i = 0; i < planes.Count; i++)
        {
            Float3 p = planes[i].Normal;
            Float3 normal = new(Float3.Dot(local.c0.XYZ, p), Float3.Dot(local.c1.XYZ, p), Float3.Dot(local.c2.XYZ, p));
            float length = Float3.Length(normal);
            result[i] = new Plane(normal / length, planes[i].D / length);
        }

        return result;
    }

    /// <summary>
    /// The reflection along a world <paramref name="axis"/> through the brush's origin, as seen in the brush's own space.
    /// It is its own inverse. False when the transform is flattened and has no inverse.
    /// </summary>
    public static bool TryMirrorMatrix(Float3 axis, Float4x4 localToWorld, out Float4x4 local)
    {
        Float4x4 linear = localToWorld;
        linear.c3 = new Float4(0, 0, 0, 1);
        local = Float4x4.Identity;
        if (MathF.Abs(Float4x4.Determinant(linear)) < 1e-12f) return false;

        Float3 n = Float3.Normalize(axis);
        Float4x4 reflect = Float4x4.Identity;
        reflect.c0 = new Float4(1 - 2 * n.X * n.X, -2 * n.Y * n.X, -2 * n.Z * n.X, 0);
        reflect.c1 = new Float4(-2 * n.X * n.Y, 1 - 2 * n.Y * n.Y, -2 * n.Z * n.Y, 0);
        reflect.c2 = new Float4(-2 * n.X * n.Z, -2 * n.Y * n.Z, 1 - 2 * n.Z * n.Z, 0);

        local = linear.Invert() * reflect * linear;
        return true;
    }

    /// <summary>Puts a plane set through an affine transform of its points, scale and shear included.</summary>
    public static Plane[] Transform(IReadOnlyList<Plane> planes, Float4x4 pointTransform)
    {
        if (MathF.Abs(Float4x4.Determinant(pointTransform)) < 1e-12f) return [.. planes];

        // A point q of the result came from p = inverse * q, so n.p = d becomes (inverse transposed n).q = d - n.t
        Float4x4 inverse = pointTransform.Invert();
        Float3 t = inverse.c3.XYZ;

        var result = new Plane[planes.Count];
        for (int i = 0; i < planes.Count; i++)
        {
            Float3 n = planes[i].Normal;
            Float3 normal = new(Float3.Dot(inverse.c0.XYZ, n), Float3.Dot(inverse.c1.XYZ, n), Float3.Dot(inverse.c2.XYZ, n));
            float length = Float3.Length(normal);
            result[i] = new Plane(normal / length, (planes[i].D - Float3.Dot(n, t)) / length);
        }
        return result;
    }

    /// <summary>Shifts every plane so the solid moves by <paramref name="offset"/> in local space.</summary>
    public static Plane[] Translate(IReadOnlyList<Plane> planes, Float3 offset)
    {
        var result = new Plane[planes.Count];
        for (int i = 0; i < planes.Count; i++)
            result[i] = new Plane(planes[i].Normal, planes[i].D + Float3.Dot(planes[i].Normal, offset));
        return result;
    }

    /// <summary>Centre of the solid's corners in local space, or zero when it has none.</summary>
    public static Float3 Centroid(GeometryData? shape)
        => shape == null ? Float3.Zero : GeometryData.Centroid(shape.Vertices);

    /// <summary>Local space bounds of the solid.</summary>
    public static AABB Bounds(GeometryData? shape)
        => shape == null || shape.Vertices.Count == 0 ? new AABB(Float3.Zero, Float3.Zero) : shape.GetAABB();
}
