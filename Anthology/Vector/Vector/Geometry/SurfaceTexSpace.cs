// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Vector.Geometry;

/// <summary>
/// A planar texture projection for one surface: two world-space axes that map a point to UV.
/// <c>u = dot(p, UAxis.xyz) + UAxis.w</c>, and likewise for V.
///
/// <para>CSG output polygons are planar, so projecting through a per-surface basis gives UVs that
/// stay put when the surface is re-cut - unlike interpolating corner UVs, which shift with the
/// fragmentation.</para>
/// </summary>
public struct SurfaceTexSpace : IEquatable<SurfaceTexSpace>
{
    /// <summary>World-space U axis in xyz, U offset in w.</summary>
    public Float4 UAxis;

    /// <summary>World-space V axis in xyz, V offset in w.</summary>
    public Float4 VAxis;

    public SurfaceTexSpace(Float4 uAxis, Float4 vAxis)
    {
        UAxis = uAxis;
        VAxis = vAxis;
    }

    /// <summary>Project a world point into this surface's texture space.</summary>
    public readonly Float2 Project(Float3 p) => new(
        p.X * UAxis.X + p.Y * UAxis.Y + p.Z * UAxis.Z + UAxis.W,
        p.X * VAxis.X + p.Y * VAxis.Y + p.Z * VAxis.Z + VAxis.W);

    /// <summary>Pick a basis from the face's dominant axis, then apply rotation, scale and offset.</summary>
    /// <param name="faceNormal">Chooses the projection basis.</param>
    /// <param name="offset">Texture offset in UV units.</param>
    /// <param name="scale">Larger means the texture repeats over a larger area.</param>
    /// <param name="rotationDegrees">Rotation about the face normal.</param>
    public static SurfaceTexSpace FromFace(Float3 faceNormal, Float2 offset = default,
                                           Float2 scale = default, float rotationDegrees = 0f)
    {
        if (scale.X == 0f) scale.X = 1f;
        if (scale.Y == 0f) scale.Y = 1f;

        BasisFor(faceNormal, out Float3 u, out Float3 v);

        if (rotationDegrees != 0f)
        {
            float r = rotationDegrees * (MathF.PI / 180f);
            float c = MathF.Cos(r), s = MathF.Sin(r);
            (u, v) = (u * c + v * s, v * c - u * s);
        }

        u /= scale.X;
        v /= scale.Y;

        return new SurfaceTexSpace(
            new Float4(u.X, u.Y, u.Z, offset.X),
            new Float4(v.X, v.Y, v.Z, offset.Y));
    }

    /// <summary>Axis-aligned basis chosen by the dominant normal component, so a wall and the floor
    /// it meets stay aligned.</summary>
    public static void BasisFor(Float3 normal, out Float3 u, out Float3 v)
    {
        float ax = MathF.Abs(normal.X), ay = MathF.Abs(normal.Y), az = MathF.Abs(normal.Z);

        if (az >= ax && az >= ay)      { u = Float3.UnitX; v = Float3.UnitY; }  // floor / ceiling
        else if (ax >= ay)             { u = Float3.UnitY; v = Float3.UnitZ; }  // wall facing X
        else                           { u = Float3.UnitX; v = Float3.UnitZ; }  // wall facing Y
    }

    public readonly bool Equals(SurfaceTexSpace other) => UAxis.Equals(other.UAxis) && VAxis.Equals(other.VAxis);
    public override readonly bool Equals(object? obj) => obj is SurfaceTexSpace o && Equals(o);
    public override readonly int GetHashCode() => HashCode.Combine(UAxis, VAxis);
}
