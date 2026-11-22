// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>Vertex, edge and face editing of a plane set brush. Edits that leave no valid solid are rejected.</summary>
public static class CSGMeshEdit
{
    /// <summary>How many times the residual of a corner drag is fed back into the plane fit.</summary>
    private const int FitIterations = 6;

    /// <summary>Moves a set of the solid's corners and rebuilds the planes around them.</summary>
    /// <returns>False when the edit does not produce a valid solid, leaving the brush untouched.</returns>
    public static bool MoveVertices(GeometryData shape, IReadOnlyList<int> faceToPlane, Plane[] planes,
                                    IReadOnlySet<int> vertexIndices, Float3 delta, out Plane[] result)
    {
        result = planes;
        if (vertexIndices.Count == 0 || Float3.LengthSquared(delta) < 1e-12f) return false;

        // Target per dragged corner, and the offset applied, which grows to make up for the fit falling short.
        var targets = new Dictionary<int, Float3>();
        var applied = new Dictionary<int, Float3>();
        foreach (int i in vertexIndices)
        {
            if (i < 0 || i >= shape.Vertices.Count) continue;
            targets[i] = shape.Vertices[i].Point + delta;
            applied[i] = delta;
        }

        if (targets.Count == 0) return false;

        Plane[]? best = null;

        for (int pass = 0; pass < FitIterations; pass++)
        {
            if (!FitPlanes(shape, faceToPlane, planes, applied, out Plane[] rebuilt)) break;

            ConvexSolidResult solid = GeometryGenerator.ConvexFromPlanes(rebuilt);
            if (!solid.IsValid) break;

            best = rebuilt;

            // Feed the shortfall back in, matching corners by nearest position.
            float worst = 0f;
            foreach (int i in targets.Keys)
            {
                Float3 actual = NearestCorner(solid.Solid!, targets[i]);
                Float3 residual = targets[i] - actual;

                applied[i] += residual;
                worst = MathF.Max(worst, Float3.Length(residual));
            }

            if (worst < 1e-4f) break;
        }

        if (best == null) return false;

        result = best;
        return true;
    }

    private static bool FitPlanes(GeometryData shape, IReadOnlyList<int> faceToPlane, Plane[] planes,
                                  Dictionary<int, Float3> offsets, out Plane[] result)
    {
        var moved = new Float3[shape.Vertices.Count];
        for (int i = 0; i < moved.Length; i++)
            moved[i] = offsets.TryGetValue(i, out Float3 offset) ? shape.Vertices[i].Point + offset : shape.Vertices[i].Point;

        result = new Plane[planes.Length];
        Array.Copy(planes, result, planes.Length);

        for (int face = 0; face < shape.Faces.Count && face < faceToPlane.Count; face++)
        {
            int plane = faceToPlane[face];
            if (plane < 0 || plane >= result.Length) continue;

            var ring = shape.Faces[face].NeighborVertices();
            if (ring.Count < 3) continue;

            var points = new Float3[ring.Count];
            for (int i = 0; i < ring.Count; i++) points[i] = moved[ring[i].Index];

            // Keep the old normal's sense so a reversed fit does not turn the face inside out.
            if (!FitPlane(points, planes[plane].Normal, out Plane fitted)) continue;
            result[plane] = fitted;
        }

        return true;
    }

    private static Float3 NearestCorner(GeometryData solid, Float3 target)
    {
        Float3 best = target;
        float bestDistance = float.MaxValue;

        foreach (GeometryData.Vertex v in solid.Vertices)
        {
            float d = Float3.DistanceSquared(v.Point, target);
            if (d >= bestDistance) continue;

            bestDistance = d;
            best = v.Point;
        }

        return best;
    }

    /// <summary>Slides one face along its own normal.</summary>
    public static bool MoveFace(Plane[] planes, int planeIndex, float distance, out Plane[] result)
    {
        result = planes;
        if (planeIndex < 0 || planeIndex >= planes.Length || MathF.Abs(distance) < 1e-6f) return false;

        var rebuilt = new Plane[planes.Length];
        Array.Copy(planes, rebuilt, planes.Length);
        rebuilt[planeIndex] = new Plane(planes[planeIndex].Normal, planes[planeIndex].D + distance);

        if (!GeometryGenerator.ConvexFromPlanes(rebuilt).IsValid) return false;

        result = rebuilt;
        return true;
    }

    /// <summary>Drops a face by removing its plane, the neighbouring faces grow to close the gap.</summary>
    public static bool RemoveFace(Plane[] planes, int planeIndex, out Plane[] result)
        => RemoveFace(planes, planeIndex, out result, out _, []);

    /// <inheritdoc cref="RemoveFace(Plane[], int, out Plane[])"/>
    /// <param name="surfaces">The brush's surface data, returned without the removed face so the rest keep their faces.</param>
    public static bool RemoveFace(Plane[] planes, int planeIndex, out Plane[] result, out SurfaceData[] keptSurfaces, SurfaceData[] surfaces)
    {
        result = planes;
        keptSurfaces = surfaces;
        if (planeIndex < 0 || planeIndex >= planes.Length || planes.Length <= 4) return false;

        var rebuilt = new List<Plane>(planes.Length - 1);
        var kept = new List<SurfaceData>(planes.Length - 1);
        for (int i = 0; i < planes.Length; i++)
        {
            if (i == planeIndex) continue;
            rebuilt.Add(planes[i]);
            if (i < surfaces.Length) kept.Add(surfaces[i]);
        }

        Plane[] array = rebuilt.ToArray();
        if (!GeometryGenerator.ConvexFromPlanes(array).IsValid) return false;

        result = array;
        keptSurfaces = kept.ToArray();
        return true;
    }

    /// <summary>Best fit plane through a ring of points by Newell's method, oriented to agree with <paramref name="reference"/>.</summary>
    public static bool FitPlane(IReadOnlyList<Float3> points, Float3 reference, out Plane plane)
    {
        plane = default;
        if (points.Count < 3) return false;

        Float3 normal = Float3.Zero;
        Float3 centroid = Float3.Zero;

        for (int i = 0; i < points.Count; i++)
        {
            Float3 a = points[i], b = points[(i + 1) % points.Count];
            normal.X += (a.Y - b.Y) * (a.Z + b.Z);
            normal.Y += (a.Z - b.Z) * (a.X + b.X);
            normal.Z += (a.X - b.X) * (a.Y + b.Y);
            centroid += a;
        }

        if (Float3.LengthSquared(normal) < 1e-12f) return false;

        normal = Float3.Normalize(normal);
        centroid /= points.Count;

        if (Float3.Dot(normal, reference) < 0f) normal = -normal;

        plane = new Plane(normal, Float3.Dot(normal, centroid));
        return true;
    }

    /// <summary>Cuts a corner or an edge off by adding one plane along the average of the meeting faces' normals.</summary>
    /// <param name="normals">Outward normals of the faces meeting at the corner or edge.</param>
    /// <param name="distance">How far back along the average normal to cut.</param>
    public static bool ChamferPlane(IReadOnlyList<Float3> normals, Float3 corner, float distance, out Plane plane)
    {
        plane = default;
        if (normals.Count < 2 || distance <= 1e-5f) return false;

        Float3 average = Float3.Zero;
        foreach (Float3 n in normals) average += n;

        if (Float3.LengthSquared(average) < 1e-10f) return false;
        average = Float3.Normalize(average);

        plane = new Plane(average, Float3.Dot(average, corner - average * distance));
        return true;
    }

    /// <summary>The plane set of a prism standing on a face, used to pull a face out into its own brush.</summary>
    /// <param name="ring">The face's corners, wound to match <paramref name="normal"/>.</param>
    /// <param name="height">How far to pull it out. Negative pushes in.</param>
    public static Plane[]? ExtrudePrism(IReadOnlyList<Float3> ring, Float3 normal, float height)
    {
        if (ring.Count < 3 || MathF.Abs(height) < 1e-4f) return null;

        // The sides face out from the ring's own winding, whichever way it is pulled
        Float3 wound = Float3.NormalizeSafe(normal, Float3.UnitY);
        Float3 up = height < 0f ? -wound : wound;
        height = MathF.Abs(height);

        Float3 basePoint = ring[0];

        var planes = new List<Plane>(ring.Count + 2)
        {
            new(up, Float3.Dot(up, basePoint + up * height)),   // the pulled-out cap
            new(-up, Float3.Dot(-up, basePoint)),               // sitting on the original face
        };

        for (int i = 0; i < ring.Count; i++)
        {
            Float3 a = ring[i], b = ring[(i + 1) % ring.Count];

            Float3 side = Float3.Cross(b - a, wound);
            if (Float3.LengthSquared(side) < 1e-12f) continue;

            side = Float3.Normalize(side);
            planes.Add(new Plane(side, Float3.Dot(side, a)));
        }

        Plane[] result = planes.ToArray();
        return GeometryGenerator.ConvexFromPlanes(result).IsValid ? result : null;
    }

    /// <summary>Corner positions of the solid, in the order the tool indexes them by.</summary>
    public static List<Float3> Corners(GeometryData? shape)
    {
        var result = new List<Float3>();
        if (shape == null) return result;

        foreach (GeometryData.Vertex v in shape.Vertices) result.Add(v.Point);
        return result;
    }

    /// <summary>The solid's edges as index pairs into <see cref="Corners"/>.</summary>
    public static List<(int a, int b)> Edges(GeometryData? shape)
    {
        var result = new List<(int, int)>();
        if (shape == null) return result;

        foreach (GeometryData.Edge edge in shape.Edges)
            result.Add((edge.Vert1.Index, edge.Vert2.Index));

        return result;
    }
}
