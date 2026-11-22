// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// A CSG brush as an edit target: its polygons as faces, each tagged with the surface it had, and back again as points,
/// polygons and surfaces. Holes and stray faces are allowed, the brush repairs them when it builds. Folding through
/// itself is not.
/// </summary>
public sealed class CSGBrushTarget
{
    /// <summary>Face attribute holding the polygon index the face came from, plus one, so 0 means made from nothing.</summary>
    public const string SurfaceAttribute = "csg:surface";

    /// <summary>Why an edit was refused, as locale keys.</summary>
    public const string FoldsThroughItself = "csg.status_folds";
    public const string EnclosesNothing = "csg.status_encloses_nothing";

    private readonly SurfaceData[] _surfaces;
    private readonly Float3[] _normals;

    public CSGBrushTarget(CSGControlMesh mesh, SurfaceData[] surfaces, Float4x4? localToWorld = null)
    {
        GeometryData geometry = mesh.ToMesh(out int[] faceToPolygon);
        geometry.AddFaceAttribute(SurfaceAttribute, GeometryData.AttributeBaseType.Int, 1);

        int face = 0;
        foreach (GeometryData.Face f in geometry.Faces)
            f.Attributes[SurfaceAttribute] = new GeometryData.IntAttributeValue(faceToPolygon[face++] + 1);

        _surfaces = surfaces;
        _normals = new Float3[mesh.Polygons.Count];
        for (int p = 0; p < _normals.Length; p++) _normals[p] = mesh.PolygonNormal(p);

        Target = new EditTarget(geometry, localToWorld ?? Float4x4.Identity, Validate);
    }

    public EditTarget Target { get; }

    /// <summary>The faces that came from these polygons of the brush, before any edit.</summary>
    public List<GeometryData.Face> FacesOf(IEnumerable<int> polygons)
    {
        var wanted = new HashSet<int>();
        foreach (int polygon in polygons) wanted.Add(polygon + 1);

        var faces = new List<GeometryData.Face>();
        foreach (GeometryData.Face face in Target.Mesh.Faces)
            if (wanted.Contains(Tag(face))) faces.Add(face);
        return faces;
    }

    /// <summary>The edited mesh as the brush's points, polygons and surfaces.</summary>
    public (CSGControlMesh Mesh, SurfaceData[] Surfaces) ToBrush() => ToBrush(null, out _, out _);

    /// <summary>
    /// As <see cref="ToBrush()"/>, also giving where the elements of <paramref name="carry"/> end up: the points its
    /// vertices became, and the polygons its faces became part of.
    /// </summary>
    public (CSGControlMesh Mesh, SurfaceData[] Surfaces) ToBrush(ElementSelection? carry, out HashSet<int> points, out List<int> faces)
    {
        CSGControlMesh mesh = ToControlMesh(Target.Mesh, out List<GeometryData.Face> sources, out Dictionary<GeometryData.Vertex, int> pointOf,
            out Dictionary<GeometryData.Face, int> polygonOf);

        points = new HashSet<int>();
        faces = new List<int>();
        if (carry != null)
        {
            foreach (GeometryData.Vertex vertex in carry.Vertices)
                if (pointOf.TryGetValue(vertex, out int point)) points.Add(point);
            foreach (GeometryData.Face face in carry.Faces)
                if (polygonOf.TryGetValue(face, out int polygon) && !faces.Contains(polygon)) faces.Add(polygon);
        }

        var surfaces = new SurfaceData[mesh.Polygons.Count];
        for (int p = 0; p < surfaces.Length; p++)
        {
            int tag = Tag(sources[p]) - 1;
            surfaces[p] = tag >= 0 && tag < _surfaces.Length ? _surfaces[tag] : Nearest(mesh.PolygonNormal(p));
        }
        return (mesh, surfaces);
    }

    private static string? Validate(GeometryData geometry)
    {
        CSGControlMesh mesh = ToControlMesh(geometry, out _, out _, out _);
        CSGMeshProblem problem = mesh.Check();
        if (problem == CSGMeshProblem.None) return null;
        if (problem == CSGMeshProblem.SelfIntersecting) return FoldsThroughItself;
        return mesh.Repaired(out _) != null ? null : EnclosesNothing;
    }

    /// <summary>
    /// Points and polygons from the faces: faces joined by hidden edges become the one polygon they read as, and points
    /// no face uses are left out. Points keep the order of the mesh's vertices, so an edit that only moves points keeps
    /// every index. <paramref name="sources"/> names a face each polygon came from.
    /// </summary>
    private static CSGControlMesh ToControlMesh(GeometryData geometry, out List<GeometryData.Face> sources,
                                                out Dictionary<GeometryData.Vertex, int> index, out Dictionary<GeometryData.Face, int> polygonOf)
    {
        index = new Dictionary<GeometryData.Vertex, int>();
        var points = new List<Float3>();
        var used = new HashSet<GeometryData.Vertex>();
        foreach (GeometryData.Face face in geometry.Faces)
            foreach (GeometryData.Vertex vertex in face.NeighborVertices())
                used.Add(vertex);
        foreach (GeometryData.Vertex vertex in geometry.Vertices)
        {
            if (!used.Contains(vertex)) continue;
            index[vertex] = points.Count;
            points.Add(vertex.Point);
        }

        var polygons = new List<int[]>();
        var from = new List<GeometryData.Face>();
        var done = new HashSet<GeometryData.Face>();
        polygonOf = new Dictionary<GeometryData.Face, int>();
        Dictionary<GeometryData.Face, int> owners = polygonOf;
        Dictionary<GeometryData.Vertex, int> pointIndex = index;

        foreach (GeometryData.Face face in geometry.Faces)
        {
            if (done.Contains(face)) continue;
            List<GeometryData.Face> group = face.LogicalFace();
            foreach (GeometryData.Face member in group) done.Add(member);

            List<GeometryData.Vertex>? outline = group.Count == 1 ? face.NeighborVertices() : Outline(group);
            if (outline != null)
            {
                if (Emit(outline, face))
                    foreach (GeometryData.Face member in group) owners[member] = polygons.Count - 1;
                continue;
            }

            // An outline that does not close into one ring keeps its pieces as they are
            foreach (GeometryData.Face member in group)
                if (Emit(member.NeighborVertices(), member)) owners[member] = polygons.Count - 1;
        }

        sources = from;
        return CSGControlMesh.FromPolygons(points, polygons);

        bool Emit(List<GeometryData.Vertex> ring, GeometryData.Face source)
        {
            if (ring.Count < 3) return false;
            var polygon = new int[ring.Count];
            for (int i = 0; i < ring.Count; i++) polygon[i] = pointIndex[ring[i]];
            polygons.Add(polygon);
            from.Add(source);
            return true;
        }
    }

    /// <summary>The ring round a group of faces, leaving out the edges the group shares inside itself, or null.</summary>
    private static List<GeometryData.Vertex>? Outline(List<GeometryData.Face> group)
    {
        var members = new HashSet<GeometryData.Face>(group);
        var onward = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

        foreach (GeometryData.Face face in group)
        {
            GeometryData.Loop? first = face.Loop;
            if (first == null) continue;
            GeometryData.Loop loop = first;
            do
            {
                bool inside = true;
                int sides = 0;
                foreach (GeometryData.Face other in loop.Edge.NeighborFaces())
                {
                    sides++;
                    if (!members.Contains(other)) inside = false;
                }

                if (!(inside && sides == 2))
                {
                    if (onward.ContainsKey(loop.Vert)) return null;
                    onward[loop.Vert] = loop.Next!.Vert;
                }
                loop = loop.Next!;
            } while (loop != first);
        }

        if (onward.Count < 3) return null;

        var ring = new List<GeometryData.Vertex>();
        GeometryData.Vertex start = default!;
        foreach (GeometryData.Vertex v in onward.Keys) { start = v; break; }

        GeometryData.Vertex at = start;
        do
        {
            ring.Add(at);
            if (!onward.TryGetValue(at, out at!) || ring.Count > onward.Count) return null;
        } while (at != start);

        return ring.Count == onward.Count ? ring : null;
    }

    private static int Tag(GeometryData.Face face)
        => face.Attributes.TryGetValue(SurfaceAttribute, out var value) && value is GeometryData.IntAttributeValue tag && tag.Data.Length > 0 ? tag.Data[0] : 0;

    private SurfaceData Nearest(Float3 normal)
    {
        int best = -1;
        float bestDot = float.MinValue;
        for (int p = 0; p < _normals.Length && p < _surfaces.Length; p++)
        {
            float dot = Float3.Dot(_normals[p], normal);
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = p;
        }
        return best >= 0 ? _surfaces[best] : SurfaceData.Default;
    }
}
