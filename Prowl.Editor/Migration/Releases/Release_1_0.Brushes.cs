// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0
{
    private const string BrushType = "Prowl.Runtime.CSG.CSGBrush";

    /// <summary>
    /// CSG brushes were planes, with points only once edited. They are now their points and faces, so a brush saved as
    /// planes gets the points of the solid its planes enclose, and its surfaces follow the faces they now belong to.
    /// </summary>
    internal static EchoObject? BrushesAsPoints(EchoObject echo)
    {
        bool changed = false;
        BrushesAsPoints(echo, ref changed);
        return changed ? echo : null;
    }

    private static void BrushesAsPoints(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List) BrushesAsPoints(item, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (TypeName(tag) == BrushType && tag.TryGet("planes", out EchoObject? planes))
        {
            if (!tag.TryGet("controlMesh", out EchoObject? mesh) || mesh!.TagType != EchoType.Compound)
                PointsFromPlanes(tag, ReadPlanes(planes!));
            tag.Remove("planes");
            changed = true;
        }

        foreach (string name in tag.GetNames())
            if (!name.StartsWith('$')) BrushesAsPoints(tag[name], ref changed);
    }

    private static List<Plane> ReadPlanes(EchoObject planes)
    {
        var result = new List<Plane>();
        if (!planes.TryGet("array", out EchoObject? array) || array!.TagType != EchoType.List) return result;

        foreach (EchoObject plane in array.List)
        {
            if (!plane.TryGet("Normal", out EchoObject? normal) || !plane.TryGet("D", out EchoObject? d)) continue;
            result.Add(new Plane(new Float3(Number(normal!, "X") ?? 0f, Number(normal!, "Y") ?? 0f, Number(normal!, "Z") ?? 0f), d!.FloatValue));
        }
        return result;
    }

    /// <summary>Writes the solid the planes enclose as points and faces, and lines the surfaces up with those faces.</summary>
    private static void PointsFromPlanes(EchoObject brush, List<Plane> planes)
    {
        if (GeometryGenerator.ConvexFromPlanes(planes).Solid is not GeometryData solid) return;

        var index = new Dictionary<GeometryData.Vertex, int>();
        EchoObject points = EchoObject.NewList();
        foreach (GeometryData.Vertex vertex in solid.Vertices)
        {
            index[vertex] = index.Count;
            EchoObject point = EchoObject.NewCompound();
            point["X"] = new EchoObject(vertex.Point.X);
            point["Y"] = new EchoObject(vertex.Point.Y);
            point["Z"] = new EchoObject(vertex.Point.Z);
            points.ListAdd(point);
        }

        EchoObject polygons = EchoObject.NewList();
        var planeOfFace = new List<int>();
        foreach (GeometryData.Face face in solid.Faces)
        {
            var ring = face.NeighborVertices();
            if (ring.Count < 3) continue;

            EchoObject corners = EchoObject.NewList();
            foreach (GeometryData.Vertex vertex in ring) corners.ListAdd(new EchoObject(index[vertex]));
            EchoObject polygon = EchoObject.NewCompound();
            polygon["array"] = corners;
            polygons.ListAdd(polygon);
            planeOfFace.Add(NearestPlane(planes, face.Normal(), face.Center()));
        }

        EchoObject mesh = EchoObject.NewCompound();
        mesh["points"] = Values(points);
        mesh["polygons"] = Values(polygons);
        brush["controlMesh"] = mesh;

        if (!brush.TryGet("surfaces", out EchoObject? surfaces) || !surfaces!.TryGet("array", out EchoObject? old) || old!.TagType != EchoType.List) return;

        EchoObject reordered = EchoObject.NewList();
        foreach (int plane in planeOfFace)
            reordered.ListAdd(plane < old.List.Count ? old.List[plane].Clone() : DefaultSurface());
        surfaces["array"] = reordered;
    }

    private static EchoObject Values(EchoObject list)
    {
        EchoObject wrapper = EchoObject.NewCompound();
        wrapper["$values"] = list;
        return wrapper;
    }

    private static EchoObject DefaultSurface()
    {
        EchoObject scale = EchoObject.NewCompound();
        scale["X"] = new EchoObject(1f);
        scale["Y"] = new EchoObject(1f);
        EchoObject surface = EchoObject.NewCompound();
        surface["Scale"] = scale;
        return surface;
    }

    private static int NearestPlane(List<Plane> planes, Float3 normal, Float3 centre)
    {
        int best = 0;
        float score = float.MaxValue;
        for (int i = 0; i < planes.Count; i++)
        {
            float s = (1f - Float3.Dot(normal, planes[i].Normal)) + MathF.Abs(planes[i].GetSignedDistanceToPoint(centre));
            if (s >= score) continue;
            score = s;
            best = i;
        }
        return best;
    }
}
