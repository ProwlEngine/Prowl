// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>Shape edits: dropping a face of a convex brush, the plane a chamfer cuts along, and insetting faces.</summary>
public static class CSGMeshEdit
{
    private const string PolygonAttribute = "csg:polygon";

    /// <summary>
    /// Shrinks polygons inside their own outlines by <paramref name="distance"/>, joining each to its old outline with new
    /// faces that take its surface. Neighbouring polygons are inset as one region. False when the inset leaves no solid.
    /// </summary>
    public static bool Inset(CSGControlMesh mesh, SurfaceData[] surfaces, ICollection<int> polygons, float distance,
                             out CSGControlMesh result, out SurfaceData[] resultSurfaces)
    {
        result = mesh;
        resultSurfaces = surfaces;
        if (polygons.Count == 0 || distance <= 1e-5f) return false;

        GeometryData shape = mesh.ToMesh(out int[] faceToPolygon);
        shape.AddFaceAttribute(PolygonAttribute, GeometryData.AttributeBaseType.Int, 1);

        var picked = new List<GeometryData.Face>();
        int index = 0;
        foreach (GeometryData.Face face in shape.Faces)
        {
            int polygon = faceToPolygon[index++];
            face.Attributes[PolygonAttribute] = new GeometryData.IntAttributeValue(polygon);
            if (polygons.Contains(polygon)) picked.Add(face);
        }
        if (picked.Count == 0) return false;

        try { GeometryOperators.InsetFaces(shape, picked, distance, InsetMode.Shared, InsetMeasure.Distance); }
        catch (InvalidOperationException) { return false; }

        CSGControlMesh? inset = CSGControlMesh.FromMesh(shape);
        if (inset == null || (!inset.IsValid() && inset.Repaired(out _) == null)) return false;

        var kept = new List<SurfaceData>(inset.Polygons.Count);
        foreach (GeometryData.Face face in shape.Faces)
        {
            if (face.VertCount < 3) continue;
            int polygon = face.Attributes.TryGetValue(PolygonAttribute, out var value) && value is GeometryData.IntAttributeValue tag && tag.Data.Length > 0 ? tag.Data[0] : -1;
            kept.Add(polygon >= 0 && polygon < surfaces.Length ? surfaces[polygon] : SurfaceData.Default);
        }

        result = inset;
        resultSurfaces = [.. kept];
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

}
