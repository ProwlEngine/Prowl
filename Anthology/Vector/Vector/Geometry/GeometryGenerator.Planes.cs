// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry;

/// <summary>Why a plane set failed to produce a solid.</summary>
public enum ConvexSolidStatus
{
    /// <summary>A closed, bounded solid was produced.</summary>
    Valid,
    /// <summary>Fewer than four planes were supplied; no volume can be enclosed.</summary>
    TooFewPlanes,
    /// <summary>The half-spaces enclose no volume at all.</summary>
    Empty,
    /// <summary>The half-spaces do not close - the solid runs to infinity in some direction.</summary>
    Unbounded,
}

/// <summary>Outcome of building a convex solid from half-spaces.</summary>
public readonly struct ConvexSolidResult
{
    /// <summary>The solid, or null unless <see cref="Status"/> is <see cref="ConvexSolidStatus.Valid"/>.</summary>
    public readonly GeometryData? Solid;

    public readonly ConvexSolidStatus Status;

    /// <summary>Indices into the input plane list that contributed no face, because other planes
    /// already cut away everything they would have.</summary>
    public readonly IReadOnlyList<int> RedundantPlanes;

    public bool IsValid => Status == ConvexSolidStatus.Valid && Solid != null;

    internal ConvexSolidResult(GeometryData? solid, ConvexSolidStatus status, IReadOnlyList<int> redundant)
    {
        Solid = solid;
        Status = status;
        RedundantPlanes = redundant;
    }
}

public static partial class GeometryGenerator
{
    /// <summary>
    /// Build the convex solid enclosed by a set of half-spaces.
    ///
    /// <para>Planes face <b>outward</b>: a point is inside when
    /// <c>Plane.GetSignedDistanceToPoint(p) &lt;= 0</c> for every plane. Moving a face is moving one
    /// plane, so convexity holds by construction and a face that stops contributing drops out.</para>
    ///
    /// <para>Each face is a large quad in its own plane clipped against every other plane: O(n^2),
    /// irrelevant at brush scale, and free of the degeneracies vertex enumeration hits when three
    /// planes nearly meet.</para>
    /// </summary>
    /// <param name="planes">Outward-facing half-spaces.</param>
    /// <param name="weldTolerance">Distance under which corner points from different faces are treated
    /// as the same vertex.</param>
    public static ConvexSolidResult ConvexFromPlanes(IReadOnlyList<Plane> planes, float weldTolerance = 1e-4f)
    {
        var redundant = new List<int>();
        if (planes == null || planes.Count < 4)
            return new ConvexSolidResult(null, ConvexSolidStatus.TooFewPlanes, redundant);

        // The starting quad has to be big enough to reach past the solid in every direction, so size
        // it from how far the planes themselves sit from the origin.
        float extent = 1f;
        for (int i = 0; i < planes.Count; i++)
            extent = MathF.Max(extent, MathF.Abs(planes[i].D));
        extent *= 8f;

        // A duplicate would clip to a full face of its own, leaving two coincident faces sharing
        // every edge - non-manifold output CSG cannot categorise against.
        var usable = new bool[planes.Count];
        for (int i = 0; i < planes.Count; i++)
        {
            Float3 n = planes[i].Normal;
            if (!float.IsFinite(n.X) || !float.IsFinite(n.Y) || !float.IsFinite(n.Z)
                || !float.IsFinite(planes[i].D) || Float3.LengthSquared(n) < 0.5f)
            {
                redundant.Add(i);   // no usable orientation
                continue;
            }

            bool duplicate = false;
            for (int j = 0; j < i; j++)
            {
                if (!usable[j]) continue;
                if (Float3.Dot(n, planes[j].Normal) > 1f - 1e-4f
                    && MathF.Abs(planes[i].D - planes[j].D) < 1e-4f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate) redundant.Add(i);
            else usable[i] = true;
        }

        var faces = new List<List<Float3>>(planes.Count);
        bool unbounded = false;

        for (int i = 0; i < planes.Count; i++)
        {
            if (!usable[i]) { faces.Add(new List<Float3>()); continue; }

            List<Float3>? poly = BuildFaceQuad(planes[i], extent);

            for (int j = 0; j < planes.Count && poly != null; j++)
            {
                if (j == i || !usable[j]) continue;
                poly = ClipPolygonByPlane(poly, planes[j]);
            }

            if (poly == null || poly.Count < 3)
            {
                redundant.Add(i);
                faces.Add(new List<Float3>());
                continue;
            }

            // A corner still sitting out at the starting extent means nothing clipped it, so the
            // half-spaces never closed.
            foreach (Float3 v in poly)
            {
                if (Float3.Length(v) >= extent * 0.99f) { unbounded = true; break; }
            }

            faces.Add(poly);
        }

        redundant.Sort();

        if (unbounded)
            return new ConvexSolidResult(null, ConvexSolidStatus.Unbounded, redundant);

        int realFaces = 0;
        foreach (var f in faces) if (f.Count >= 3) realFaces++;
        if (realFaces < 4)
            return new ConvexSolidResult(null, ConvexSolidStatus.Empty, redundant);

        return new ConvexSolidResult(BuildSolid(faces, weldTolerance), ConvexSolidStatus.Valid, redundant);
    }

    /// <inheritdoc cref="ConvexFromPlanes(IReadOnlyList{Plane}, float)"/>
    public static ConvexSolidResult ConvexFromPlanes(params Plane[] planes) => ConvexFromPlanes(planes, 1e-4f);

    /// <summary>
    /// The six outward planes of an axis-aligned box.
    /// </summary>
    public static Plane[] BoxPlanes(Float3 size, Float3 center = default)
    {
        Float3 h = size * 0.5f;
        return
        [
            new Plane(Float3.UnitX,  Float3.Dot(Float3.UnitX,  center + new Float3(h.X, 0, 0))),
            new Plane(-Float3.UnitX, Float3.Dot(-Float3.UnitX, center - new Float3(h.X, 0, 0))),
            new Plane(Float3.UnitY,  Float3.Dot(Float3.UnitY,  center + new Float3(0, h.Y, 0))),
            new Plane(-Float3.UnitY, Float3.Dot(-Float3.UnitY, center - new Float3(0, h.Y, 0))),
            new Plane(Float3.UnitZ,  Float3.Dot(Float3.UnitZ,  center + new Float3(0, 0, h.Z))),
            new Plane(-Float3.UnitZ, Float3.Dot(-Float3.UnitZ, center - new Float3(0, 0, h.Z))),
        ];
    }

    // ================================================================
    //  Internals
    // ================================================================

    /// <summary>A quad lying in the plane, wound so its normal matches the plane's.</summary>
    private static List<Float3> BuildFaceQuad(in Plane plane, float extent)
    {
        Float3 n = plane.Normal;
        Float3 helper = MathF.Abs(n.Y) > 0.99f ? Float3.UnitX : Float3.UnitY;
        Float3 u = Float3.Normalize(Float3.Cross(helper, n));
        Float3 v = Float3.Cross(n, u);
        Float3 origin = n * plane.D;

        return
        [
            origin - u * extent - v * extent,
            origin + u * extent - v * extent,
            origin + u * extent + v * extent,
            origin - u * extent + v * extent,
        ];
    }

    /// <summary>Sutherland-Hodgman clip, keeping the half-space behind <paramref name="plane"/>.
    /// Returns null when nothing survives.</summary>
    private static List<Float3>? ClipPolygonByPlane(List<Float3> poly, in Plane plane, float epsilon = 1e-5f)
    {
        var result = new List<Float3>(poly.Count + 1);

        for (int i = 0; i < poly.Count; i++)
        {
            Float3 a = poly[i];
            Float3 b = poly[(i + 1) % poly.Count];
            float da = plane.GetSignedDistanceToPoint(a);
            float db = plane.GetSignedDistanceToPoint(b);

            bool aIn = da <= epsilon;
            bool bIn = db <= epsilon;

            if (aIn) result.Add(a);

            // Straddles the plane: add the crossing point.
            if (aIn != bIn && MathF.Abs(db - da) > 1e-12f)
                result.Add(a + (b - a) * (da / (da - db)));
        }

        return result.Count < 3 ? null : result;
    }

    /// <summary>Weld the per-face corner rings into one mesh.</summary>
    private static GeometryData BuildSolid(List<List<Float3>> faces, float weldTolerance)
    {
        var mesh = new GeometryData();
        var weld = new Dictionary<(int, int, int), GeometryData.Vertex>();
        float inv = 1f / MathF.Max(weldTolerance, 1e-6f);

        (int, int, int) Key(Float3 p) => (
            (int)MathF.Round(p.X * inv),
            (int)MathF.Round(p.Y * inv),
            (int)MathF.Round(p.Z * inv));

        foreach (var ring in faces)
        {
            if (ring.Count < 3) continue;

            var verts = new List<GeometryData.Vertex>(ring.Count);
            foreach (Float3 p in ring)
            {
                var key = Key(p);
                if (!weld.TryGetValue(key, out var vert))
                {
                    vert = mesh.AddVertex(p);
                    weld[key] = vert;
                }
                // Clipping can leave near-duplicate corners; a face must not reference one twice.
                if (verts.Count == 0 || !ReferenceEquals(verts[^1], vert))
                    verts.Add(vert);
            }

            if (verts.Count > 2 && ReferenceEquals(verts[0], verts[^1]))
                verts.RemoveAt(verts.Count - 1);

            if (verts.Count >= 3)
                mesh.AddFace(verts.ToArray());
        }

        return mesh;
    }
}
