// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Prowl.Vector.Geometry.Operators;

namespace Prowl.Vector.Geometry;

/// <summary>
/// Defines the extrusion mode for face extrusion operations.
/// </summary>
public enum ExtrudeMode
{
    /// <summary>
    /// Each vertex moves along its own normal, averaged across all selected faces sharing that vertex.
    /// Vertices are shared between extruded faces.
    /// </summary>
    AlongNormals,

    /// <summary>
    /// All vertices move along the average normal of all selected faces.
    /// Vertices are shared between extruded faces.
    /// </summary>
    AverageNormal,

    /// <summary>
    /// Each face gets its own copy of vertices and moves along its own normal. Faces joined by hidden
    /// edges count as one face and move together.
    /// </summary>
    PerFace
}

/// <summary>Where an end of a drawn line sits: on a vertex of the mesh, on one of its edges, or free at a point.</summary>
public readonly record struct LineAnchor(Float3 Point, GeometryData.Vertex? Vertex = null, GeometryData.Edge? Edge = null)
{
    public bool Snapped => Vertex != null || Edge != null;
}

/// <summary>
/// Defines the inset mode for face inset operations.
/// </summary>
public enum InsetMode
{
    /// <summary>
    /// Vertices are shared between inset faces.
    /// Non-shared vertices move towards their face centers.
    /// Shared vertices (on edges between selected faces) move along their edge lines towards edge midpoints,
    /// keeping them on the surface while insetting.
    /// </summary>
    Shared,

    /// <summary>
    /// Each face gets its own copy of vertices and shrinks on its own. Faces joined by hidden edges count as
    /// one face and shrink together.
    /// </summary>
    PerFace
}

/// <summary>
/// How the thickness of an inset is measured.
/// </summary>
public enum InsetMeasure
{
    /// <summary>The part of the way each corner moves towards its face centre, from 0 to 1.</summary>
    Fraction,

    /// <summary>How far inside the old outline the new one runs, so every border is equally thick.</summary>
    Distance
}

/// <summary>
/// Settings for <see cref="GeometryOperators.Simplify"/>.
/// </summary>
public sealed class SimplifyOptions
{
    /// <summary>Fraction of triangles to keep, from 0 to 1. Ignored when <see cref="TargetTriangleCount"/> is set.</summary>
    public float TargetRatio = 0.5f;

    /// <summary>
    /// Number of triangles to stop at. Negative means use <see cref="TargetRatio"/>. A collapse removes two
    /// triangles, or one on a border, so the result can land one below.
    /// </summary>
    public int TargetTriangleCount = -1;

    /// <summary>
    /// Largest surface deviation allowed, in the mesh's own units, as estimated by the quadric of the
    /// vertex being collapsed. Collapses that would go past it are never taken, so the result can stop
    /// short of the target. Zero or less only allows collapses that keep the surface exactly in place.
    /// </summary>
    public float MaxError = float.PositiveInfinity;

    /// <summary>Keeps open borders exactly where they are, for shells and alpha cards.</summary>
    public bool LockBorders;

    /// <summary>
    /// Cost of changing each loop or vertex attribute, by name. Float attributes cost their weighted
    /// squared difference, int attributes cost their weight whenever they differ. Attributes left out
    /// cost nothing to change, but their seams are still kept.
    /// </summary>
    public Dictionary<string, float> AttributeWeights = new();

    /// <summary>
    /// Loop attributes whose discontinuities are seams that must survive. Null means every loop attribute.
    /// Discontinuities in other attributes are not protected, but every corner still keeps a value some
    /// original corner of its vertex had.
    /// </summary>
    public HashSet<string>? SeamAttributes;

    /// <summary>Face attributes that split the surface into regions whose outlines are kept, like a material index. Null means every face attribute.</summary>
    public HashSet<string>? RegionAttributes;

    /// <summary>Optional vertex attribute, any vertex with a non zero value never moves.</summary>
    public string? LockAttribute;
}

/// <summary>
/// What <see cref="GeometryOperators.Simplify"/> achieved.
/// </summary>
public readonly struct SimplifyResult
{
    public readonly int TrianglesBefore;
    public readonly int TrianglesAfter;

    /// <summary>
    /// Estimated surface deviation of the worst collapse taken, in the mesh's own units: the root mean square
    /// distance of the moved vertex from the planes it carried. An estimate, not a hard bound.
    /// </summary>
    public readonly float Error;

    public SimplifyResult(int trianglesBefore, int trianglesAfter, float error)
    {
        TrianglesBefore = trianglesBefore;
        TrianglesAfter = trianglesAfter;
        Error = error;
    }
}

/// <summary>
/// Static operators for manipulating GeometryData (BMesh-like) structures.
/// All operations modify the mesh in-place. Inspired by Blender's BMesh operators.
/// </summary>
public static class GeometryOperators
{
    #region General Operators

    /// <summary>
    /// Set all attributes in destination vertex to attr[v1] * (1 - t) + attr[v2] * t.
    /// Linearly interpolates all vertex attributes between two vertices.
    /// </summary>
    /// <param name="mesh">The mesh containing the vertices.</param>
    /// <param name="destination">The vertex to write interpolated attributes to.</param>
    /// <param name="v1">First source vertex.</param>
    /// <param name="v2">Second source vertex.</param>
    /// <param name="t">Interpolation factor (0 = v1, 1 = v2). Unclamped: values outside [0, 1] extrapolate.</param>
    public static void AttributeLerp(GeometryData mesh, GeometryData.Vertex destination,
        GeometryData.Vertex v1, GeometryData.Vertex v2, float t)
    {
        foreach (var attr in mesh.VertexAttributes)
        {
            if (!v1.Attributes.ContainsKey(attr.Name) || !v2.Attributes.ContainsKey(attr.Name))
                continue;

            switch (attr.Type.BaseType)
            {
                case GeometryData.AttributeBaseType.Float:
                    {
                        var val1 = v1.Attributes[attr.Name] as GeometryData.FloatAttributeValue;
                        var val2 = v2.Attributes[attr.Name] as GeometryData.FloatAttributeValue;
                        if (val1 == null || val2 == null) break;

                        int n = val1.Data.Length;
                        Debug.Assert(val2.Data.Length == n);
                        var val = new GeometryData.FloatAttributeValue { Data = new float[n] };
                        for (int i = 0; i < n; ++i)
                        {
                            val.Data[i] = Maths.LerpUnclamped(val1.Data[i], val2.Data[i], t);
                        }
                        destination.Attributes[attr.Name] = val;
                        break;
                    }
                case GeometryData.AttributeBaseType.Int:
                    {
                        var val1 = v1.Attributes[attr.Name] as GeometryData.IntAttributeValue;
                        var val2 = v2.Attributes[attr.Name] as GeometryData.IntAttributeValue;
                        if (val1 == null || val2 == null) break;

                        int n = val1.Data.Length;
                        Debug.Assert(val2.Data.Length == n);
                        var val = new GeometryData.IntAttributeValue { Data = new int[n] };
                        for (int i = 0; i < n; ++i)
                        {
                            val.Data[i] = (int)Math.Round(Maths.LerpUnclamped(val1.Data[i], val2.Data[i], t));
                        }
                        destination.Attributes[attr.Name] = val;
                        break;
                    }
            }
        }
    }

    public static (GeometryData.Vertex, GeometryData.Vertex) GetCanonicalEdge(GeometryData.Vertex v1, GeometryData.Vertex v2)
    {
        return v1.GetHashCode() < v2.GetHashCode() ? (v1, v2) : (v2, v1);
    }

    /// <summary>
    /// Scale the entire mesh uniformly from the origin.
    /// </summary>
    /// <param name="mesh">The mesh to scale.</param>
    /// <param name="scale">The scale factor.</param>
    public static void Scale(GeometryData mesh, float scale)
    {
        foreach (var v in mesh.Vertices)
        {
            v.Point *= scale;
        }
    }


    /// <summary>
    /// Scale specified faces uniformly from their centers.
    /// </summary>
    /// <param name="mesh">The mesh to scale.</param>
    /// <param name="face">The face to scale.</param>
    /// <param name="scale">The scale factor.</param>
    public static void ScaleFace(GeometryData mesh, GeometryData.Face face, float scale)
    {
        // Calculate face center
        Float3 center = Float3.Zero;
        var verts = face.NeighborVertices();
        int count = verts.Count;
        foreach (var v in verts)
        {
            center += v.Point;
        }
        center /= count;
        // Scale each vertex away from the center
        foreach (var v in verts)
        {
            Float3 dir = v.Point - center;
            v.Point = center + dir * scale;
        }
    }


    /// <summary>
    /// Translate the entire mesh by an offset.
    /// </summary>
    /// <param name="mesh">The mesh to translate.</param>
    /// <param name="offset">The translation offset.</param>
    public static void Translate(GeometryData mesh, Float3 offset)
    {
        foreach (var v in mesh.Vertices)
        {
            v.Point += offset;
        }
    }

    /// <summary>
    /// Translate specified faces by an offset.
    /// </summary>
    /// <param name="mesh">The mesh to translate.</param>
    /// <param name="faces">The faces to translate.</param>
    /// <param name="offset">The translation offset.</param>
    public static void TranslateFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces, Float3 offset)
    {
        var movedVertices = new HashSet<GeometryData.Vertex>();
        foreach (var face in faces)
        {
            foreach (var v in face.NeighborVerticesEnumerable())
            {
                if (!movedVertices.Contains(v))
                {
                    v.Point += offset;
                    movedVertices.Add(v);
                }
            }
        }
    }

    /// <summary>
    /// Transforms the mesh by a matrix: its points, the named vertex or corner attributes holding offsets by the
    /// matrix's turn and scale, and the named ones holding normals by its inverse transpose. A matrix that mirrors
    /// turns the faces round so they still face out.
    /// </summary>
    public static void Transform(GeometryData mesh, Float4x4 transform, IReadOnlyCollection<string>? offsets = null, IReadOnlyCollection<string>? normals = null)
    {
        foreach (var v in mesh.Vertices)
            v.Point = Float4x4.TransformPoint(v.Point, transform);

        Float3 origin = Float4x4.TransformPoint(Float3.Zero, transform);
        void Turn(GeometryData.Element element)
        {
            foreach (string name in offsets ?? [])
                if (element.TryGetAttribute(name, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue { Data.Length: >= 3 } offset)
                {
                    Float3 turned = Float4x4.TransformPoint(new Float3(offset.Data[0], offset.Data[1], offset.Data[2]), transform) - origin;
                    element.SetAttribute(name, new GeometryData.FloatAttributeValue(turned.X, turned.Y, turned.Z));
                }
            foreach (string name in normals ?? [])
                if (element.TryGetAttribute(name, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue { Data.Length: >= 3 } normal)
                {
                    Float3 turned = Float4x4.TransformNormal(new Float3(normal.Data[0], normal.Data[1], normal.Data[2]), transform);
                    element.SetAttribute(name, new GeometryData.FloatAttributeValue(turned.X, turned.Y, turned.Z));
                }
        }
        if (offsets is { Count: > 0 } || normals is { Count: > 0 })
        {
            foreach (var v in mesh.Vertices) Turn(v);
            foreach (var l in mesh.Loops) Turn(l);
        }

        if (Float4x4.Determinant(transform) < 0f) FlipFaces(mesh, [.. mesh.Faces]);
        mesh.MarkChanged();
    }

    /// <summary>
    /// Transform specified faces by a matrix.
    /// </summary>
    /// <param name="mesh">The mesh to transform.</param>
    /// <param name="faces">The faces to transform.</param>
    /// <param name="transform">The transformation matrix.</param>
    public static void TransformFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces, Float4x4 transform)
    {
        var transformedVertices = new HashSet<GeometryData.Vertex>();
        foreach (var face in faces)
        {
            foreach (var v in face.NeighborVerticesEnumerable())
            {
                if (!transformedVertices.Contains(v))
                {
                    v.Point = Float4x4.TransformPoint(v.Point, transform);
                    transformedVertices.Add(v);
                }
            }
        }
    }

    /// <summary>
    /// Remove all geometry on the positive side of a plane (where the normal points).
    /// Vertices exactly on the plane are kept.
    /// </summary>
    /// <param name="mesh">The mesh to modify.</param>
    /// <param name="plane">The plane to use for culling.</param>
    /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
    public static void RemoveVerticesOnPlanePositiveSide(GeometryData mesh, Plane plane, float epsilon = 0.0001f)
    {
        // Classify vertices
        var verticesToRemove = new List<GeometryData.Vertex>();

        foreach (var v in mesh.Vertices.ToArray())
        {
            float distance = plane.GetSignedDistanceToPoint(v.Point);
            if (distance > epsilon) // Positive side
            {
                verticesToRemove.Add(v);
            }
        }

        // Remove vertices (this will also remove connected edges and faces).
        // Each vertex is collected once and removing one never removes another, so no presence check is needed.
        foreach (var v in verticesToRemove)
            mesh.RemoveVertex(v);
    }

    /// <summary>
    /// Remove all geometry on the negative side of a plane (opposite to where the normal points).
    /// Vertices exactly on the plane are kept.
    /// </summary>
    /// <param name="mesh">The mesh to modify.</param>
    /// <param name="plane">The plane to use for culling.</param>
    /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
    public static void RemoveVerticesOnPlaneNegativeSide(GeometryData mesh, Plane plane, float epsilon = 0.0001f)
    {
        // Classify vertices
        var verticesToRemove = new List<GeometryData.Vertex>();

        foreach (var v in mesh.Vertices.ToArray())
        {
            float distance = plane.GetSignedDistanceToPoint(v.Point);
            if (distance < -epsilon) // Negative side
            {
                verticesToRemove.Add(v);
            }
        }

        // Remove vertices (this will also remove connected edges and faces).
        // Each vertex is collected once and removing one never removes another, so no presence check is needed.
        foreach (var v in verticesToRemove)
            mesh.RemoveVertex(v);
    }

    #endregion

    /// <summary>
    /// Subdivide all faces in the mesh without smoothing.
    /// After subdivision, all faces are quads. Attempts to interpolate all attributes.
    /// Note: Modifies edge IDs during operation.
    /// </summary>
    /// <param name="mesh">The mesh to subdivide.</param>
    public static void Subdivide(GeometryData mesh)
    {
        SubdivideOp.Subdivide(mesh);
    }

    /// <summary>
    /// Try to make quad faces as square as possible. This assumes the mesh is only made of quads.
    /// Can be called iteratively for better results.
    /// </summary>
    /// <param name="mesh">The mesh to squarify (must contain only quad faces).</param>
    /// <param name="rate">Speed at which faces are squarified. Higher is faster but may overshoot.</param>
    /// <param name="uniformLength">Whether to uniformize the size of all quads.</param>
    public static void SquarifyQuads(GeometryData mesh, float rate = 1.0f, bool uniformLength = false)
    {
        SquarifyOp.SquarifyQuads(mesh, rate, uniformLength);
    }

    /// <summary>
    /// Merge another mesh into this one. All vertices, edges, and faces from the other mesh
    /// are added to this mesh. Attributes are copied.
    /// Note: Modifies vertex IDs of the source mesh during operation.
    /// </summary>
    /// <param name="mesh">The destination mesh.</param>
    /// <param name="other">The source mesh to merge in.</param>
    public static void Merge(GeometryData mesh, GeometryData other)
    {
        MergeOp.Merge(mesh, other);
    }

    /// <summary>
    /// Copies part of a mesh into a new mesh: the vertices, edges and faces given, with the edges and corners of
    /// those faces and the ends of those edges, and all their attributes. The mesh itself is left as it was.
    /// </summary>
    public static GeometryData Extract(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices, IEnumerable<GeometryData.Edge> edges, IEnumerable<GeometryData.Face> faces)
    {
        return ExtractOp.Extract(mesh, vertices, edges, faces);
    }

    /// <summary>
    /// Split an edge at a given interpolation factor, creating a new vertex.
    /// </summary>
    /// <param name="mesh">The mesh containing the edge.</param>
    /// <param name="edge">The edge to split.</param>
    /// <param name="fromVertex">One of the edge's vertices (defines the "from" end).</param>
    /// <param name="factor">Interpolation factor (0.0 = at fromVertex, 1.0 = at the other vertex).</param>
    /// <param name="newEdge">Output: The newly created edge (from the new vertex to the non-fromVertex end).</param>
    /// <returns>The newly created vertex at the split point.</returns>
    public static GeometryData.Vertex SplitEdge(GeometryData mesh, GeometryData.Edge edge, GeometryData.Vertex fromVertex, float factor, out GeometryData.Edge newEdge)
    {
        return SplitEdgeOp.SplitEdge(mesh, edge, fromVertex, factor, out newEdge);
    }

    /// <summary>
    /// Split a face along two vertices, creating a new face and edge.
    /// </summary>
    /// <param name="mesh">The mesh containing the face.</param>
    /// <param name="face">The face to split.</param>
    /// <param name="vert1">First vertex of the split.</param>
    /// <param name="vert2">Second vertex of the split (must be different from vert1 and not adjacent).</param>
    /// <param name="newEdge">Output: The newly created edge connecting the two vertices.</param>
    /// <returns>The newly created face, or null if the split fails.</returns>
    public static GeometryData.Face? SplitFace(GeometryData mesh, GeometryData.Face face, GeometryData.Vertex vert1, GeometryData.Vertex vert2, out GeometryData.Edge? newEdge)
    {
        return SplitFaceOp.SplitFace(mesh, face, vert1, vert2, out newEdge);
    }

    /// <summary>Face attribute <see cref="Slice"/> writes: the index of the face it came from, or -1 for a cap.</summary>
    public const string SliceFaceAttribute = "slice:face";

    /// <summary>
    /// Cuts a closed mesh by a plane into the part behind it and the part in front, each closed with a flat cap where
    /// the cut was. Works on concave meshes, and caps the cut whatever shape it is. Each output face records the face
    /// of the original it came from, read with <see cref="SliceSourceFace"/>.
    /// </summary>
    public static void Slice(GeometryData closed, Plane plane, out GeometryData back, out GeometryData front)
    {
        back = SliceHalf(closed, plane);
        front = SliceHalf(closed, new Plane(-plane.Normal, -plane.D));
    }

    /// <summary>The face of the original a <see cref="Slice"/> output face came from, or -1 for a cap.</summary>
    public static int SliceSourceFace(GeometryData.Face face)
        => face.Attributes.TryGetValue(SliceFaceAttribute, out var value) && value is GeometryData.IntAttributeValue i ? i.Data[0] : -1;

    /// <summary>The mesh intersected with a box filling everything behind the plane and well past the mesh.</summary>
    private static GeometryData SliceHalf(GeometryData closed, Plane plane)
    {
        AABB bounds = closed.GetAABB();
        Float3 reach = (bounds.Max - bounds.Min) + new Float3(1f);
        Float3 min = bounds.Min - reach, max = bounds.Max + reach;

        Plane[] half =
        [
            plane,
            new(Float3.UnitX, max.X), new(-Float3.UnitX, -min.X),
            new(Float3.UnitY, max.Y), new(-Float3.UnitY, -min.Y),
            new(Float3.UnitZ, max.Z), new(-Float3.UnitZ, -min.Z),
        ];

        var result = new GeometryData();
        result.AddFaceAttribute(SliceFaceAttribute, GeometryData.AttributeBaseType.Int, 1);
        if (GeometryGenerator.ConvexFromPlanes(half).Solid is not GeometryData box) return result;

        var scene = new CSGScene();
        CSGBrushHandle source = scene.Add(closed);
        scene.Add(box, CSGOperation.Intersect);
        GeometryData built = scene.Build();

        var vertices = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        foreach (var face in built.Faces)
        {
            var ring = face.NeighborVertices().Select(v => vertices.TryGetValue(v, out var copy) ? copy : vertices[v] = result.AddVertex(v.Point)).ToArray();
            var added = result.AddFace(ring);
            if (added == null) continue;
            int index = CSGScene.GetBrushId(face) == source.Id ? CSGScene.GetSourceFaceIndex(face) : -1;
            added.Attributes[SliceFaceAttribute] = new GeometryData.IntAttributeValue(index);
        }
        return result;
    }

    /// <summary>
    /// Bisect a mesh with a plane, splitting edges and faces that cross the plane.
    /// </summary>
    /// <param name="mesh">The mesh to bisect.</param>
    /// <param name="plane">The plane to bisect with (Normal.X, Normal.Y, Normal.Z, Distance).</param>
    /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
    /// <param name="snapToPlane">If true, snap vertices very close to the plane onto it.</param>
    public static void BisectPlane(GeometryData mesh, Plane plane, float epsilon = 0.0001f, bool snapToPlane = true)
    {
        BisectPlaneOp.BisectPlane(mesh, plane, epsilon, snapToPlane);
    }

    #region Spatial Queries

    /// <summary>
    /// Find the vertex whose attribute value is closest to the given value.
    /// Uses Euclidean distance for comparison.
    /// </summary>
    /// <param name="mesh">The mesh to search.</param>
    /// <param name="value">The target attribute value.</param>
    /// <param name="attrName">The name of the attribute to compare.</param>
    /// <returns>The vertex with the closest attribute value, or null if attribute doesn't exist.</returns>
    public static GeometryData.Vertex? Nearpoint(GeometryData mesh, GeometryData.AttributeValue value, string attrName)
    {
        if (!mesh.HasVertexAttribute(attrName))
            return null;

        GeometryData.Vertex? argmin = null;
        float min = 0;

        foreach (var v in mesh.Vertices)
        {
            if (!v.Attributes.ContainsKey(attrName))
                continue;

            float d = Distance(v.Attributes[attrName], value);
            if (argmin == null || d < min)
            {
                argmin = v;
                min = d;
            }
        }

        return argmin;
    }

    /// <summary>
    /// Calculate Euclidean distance between two attribute values.
    /// </summary>
    private static float Distance(GeometryData.AttributeValue value1, GeometryData.AttributeValue value2)
    {
        if (value1 is GeometryData.IntAttributeValue ival1 && value2 is GeometryData.IntAttributeValue ival2)
        {
            int n = ival1.Data.Length;
            if (n != ival2.Data.Length) return float.PositiveInfinity;

            float s = 0;
            for (int i = 0; i < n; ++i)
            {
                float diff = ival1.Data[i] - ival2.Data[i];
                s += diff * diff;
            }
            return Maths.Sqrt(s);
        }

        if (value1 is GeometryData.FloatAttributeValue fval1 && value2 is GeometryData.FloatAttributeValue fval2)
        {
            int n = fval1.Data.Length;
            if (n != fval2.Data.Length) return float.PositiveInfinity;

            float s = 0;
            for (int i = 0; i < n; ++i)
            {
                float diff = fval1.Data[i] - fval2.Data[i];
                s += diff * diff;
            }
            return Maths.Sqrt(s);
        }

        return float.PositiveInfinity;
    }

    #endregion

    #region Additional Utility Operators

    /// <summary>
    /// Recalculate normals for all vertices based on adjacent face normals.
    /// Creates or updates a "normal" Float attribute (3 dimensions) on vertices.
    /// </summary>
    /// <param name="mesh">The mesh to calculate normals for.</param>
    public static void RecalculateNormals(GeometryData mesh)
    {
        if (!mesh.HasVertexAttribute("normal"))
            mesh.AddVertexAttribute("normal", GeometryData.AttributeBaseType.Float, 3);

        var sums = new Dictionary<GeometryData.Vertex, Float3>(mesh.Vertices.Count);
        foreach (var face in mesh.Faces)
        {
            Float3 normal = face.Normal();
            if (Float3.LengthSquared(normal) < 0.5f) continue;
            foreach (var v in face.NeighborVerticesEnumerable())
                sums[v] = (sums.TryGetValue(v, out var sum) ? sum : Float3.Zero) + normal;
        }

        foreach (var v in mesh.Vertices)
        {
            Float3 n = sums.TryGetValue(v, out var sum) && Float3.LengthSquared(sum) > 0f ? Float3.Normalize(sum) : Float3.Zero;
            v.Attributes["normal"] = new GeometryData.FloatAttributeValue(n.X, n.Y, n.Z);
        }
    }

    /// <summary>
    /// Splits every face with more than three corners into triangles, by ear clipping in the face's plane so
    /// concave faces come out right. Each triangle keeps the face attributes and the loop attributes of its corners.
    /// </summary>
    /// <param name="mesh">The mesh to triangulate.</param>
    public static void Triangulate(GeometryData mesh) => Triangulate(mesh, mesh.Faces);

    /// <summary>
    /// Splits the given faces with more than three corners into triangles, as <see cref="Triangulate(GeometryData)"/>
    /// does for every face.
    /// </summary>
    /// <returns>The triangles made.</returns>
    public static List<GeometryData.Face> Triangulate(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        var made = new List<GeometryData.Face>();
        foreach (var face in faces.Where(f => mesh.Contains(f) && f.VertCount > 3).ToList())
        {
            var snapshot = FaceSnapshot.Capture(mesh, face);
            var verts = snapshot.Vertices;
            Float3 normal = face.Normal();
            var frame = new PlaneFrame(Float3.LengthSquared(normal) > 0.5f ? normal : Float3.UnitY, verts[0].Point);
            List<int> triangles = Polygon2D.Triangulate(verts.Select(v => frame.To2D(v.Point)).ToList());
            if (triangles.Count < 3) continue;

            mesh.RemoveFace(face);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                var triangle = mesh.AddFace(verts[triangles[t]], verts[triangles[t + 1]], verts[triangles[t + 2]]);
                if (triangle == null) continue;
                snapshot.ApplyFaceAttributes(triangle);
                FaceSnapshot.ApplyLoops(triangle, snapshot.LoopOf);
                made.Add(triangle);
            }
        }
        return made;
    }

    /// <summary>
    /// Cuts faces with evenly spaced points along their edges: a four sided face becomes a grid of quads, any other face
    /// one quad per corner at a single cut or a fan to its centre at more. The faces beside them gain the edge points, so
    /// the surface stays closed.
    /// </summary>
    /// <returns>The faces made.</returns>
    public static List<GeometryData.Face> SubdivideFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces, int cuts = 1)
    {
        return SubdivideOp.Faces(mesh, faces, cuts);
    }

    /// <summary>
    /// Joins pairs of triangles into quads, best pairs first. A pair is joined when its two triangles turn less than the
    /// angle against each other and the quad they make has no corner bent further than that from a right angle. Seams
    /// and sharp edges are never joined across.
    /// </summary>
    /// <param name="faces">The triangles that may be joined, all when null.</param>
    /// <returns>The quads made.</returns>
    public static List<GeometryData.Face> TrisToQuads(GeometryData mesh, IEnumerable<GeometryData.Face>? faces, float degrees)
    {
        return TopologyOp.TrisToQuads(mesh, faces, degrees);
    }

    #endregion

    #region Face Operators

    /// <summary>
    /// Extrude a collection of faces outward (or inward) along their normals.
    /// Creates new geometry by duplicating the faces and connecting them with wall faces. Faces joined to a
    /// selected face by hidden edges are extruded with it, since they are one face.
    /// </summary>
    /// <param name="mesh">The mesh containing the faces.</param>
    /// <param name="facesToExtrude">The faces to extrude.</param>
    /// <param name="distance">The extrusion distance (positive = outward along normal, negative = inward).</param>
    /// <param name="mode">The extrusion mode:
    /// AlongNormals - vertices move along averaged normals (shared vertices),
    /// AverageNormal - all vertices move along average of all face normals (shared vertices),
    /// PerFace - each face gets its own vertices (no sharing).</param>
    public static void ExtrudeFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToExtrude, float distance, ExtrudeMode mode = ExtrudeMode.AlongNormals)
    {
        ExtrudeOp.ExtrudeFaces(mesh, facesToExtrude, distance, mode);
    }

    /// <summary>
    /// Inset a collection of faces, shrinking each inside its own outline and joining it to the old outline
    /// with wall faces. Faces joined by hidden edges are inset as one face.
    /// </summary>
    /// <param name="mesh">The mesh containing the faces.</param>
    /// <param name="facesToInset">The faces to inset.</param>
    /// <param name="thickness">How far to inset, as <paramref name="measure"/> says.</param>
    /// <param name="mode">Whether the faces are inset together as one region, or each on its own.</param>
    /// <param name="measure">Whether the thickness is a fraction of the way to the face centre, or a distance.</param>
    public static void InsetFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToInset, float thickness, InsetMode mode = InsetMode.Shared, InsetMeasure measure = InsetMeasure.Fraction)
    {
        InsetOp.InsetFaces(mesh, facesToInset, thickness, mode, measure);
    }

    /// <summary>
    /// Bevel edges between two faces, replacing each with a strip of faces. Any number of edges can meet at a
    /// corner, which is capped. Face, loop and edge attributes carry over to the new faces, with loop
    /// attributes such as texture coordinates sampled across the face they came from. Edges between faces in
    /// one plane are skipped. Throws <see cref="InvalidOperationException"/> when the width reaches other
    /// geometry or the surface is not manifold.
    /// </summary>
    /// <param name="mesh">The mesh containing the edges.</param>
    /// <param name="edges">The edges to bevel, each with a face on both sides.</param>
    /// <param name="width">How far from the edge the bevel reaches along each face.</param>
    /// <param name="segments">How many strips an edge becomes. One is a flat chamfer, more make a rounded edge.</param>
    /// <param name="profile">The shape of the rounding. 0 is flat, 0.5 a circle, higher is squarer.</param>
    /// <returns>How many strip faces were added.</returns>
    public static int BevelEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, float width, int segments = 1, float profile = 0.5f)
    {
        return BevelOp.Edges(mesh, edges.ToList(), new BevelOp.Options { Width = width, Segments = segments, Profile = profile });
    }

    /// <summary>
    /// Bevel one or more vertices by replacing each with a small face. A new vertex is placed on every edge
    /// around a beveled vertex and they are joined by the face. Neighbouring vertices can be beveled together,
    /// and vertices on an open border are cut too. Face, loop and edge attributes carry over to the new faces.
    /// Throws <see cref="InvalidOperationException"/> when the offset reaches past an edge or the surface is not manifold.
    /// </summary>
    /// <param name="mesh">The mesh containing the vertices.</param>
    /// <param name="verticesToBevel">The vertices to bevel.</param>
    /// <param name="offset">How far along each edge to place the new vertices.</param>
    /// <param name="relative">When true the offset is a fraction of each edge's length, from 0 to 1 exclusive. When false it is a distance.</param>
    /// <returns>How many vertices were beveled.</returns>
    public static int BevelVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> verticesToBevel, float offset = 0.3f, bool relative = true)
    {
        if (relative && (offset <= 0.0f || offset >= 1.0f))
            throw new ArgumentException("Offset must be between 0.0 and 1.0 when it is relative", nameof(offset));

        var targets = verticesToBevel.Where(v => mesh.Contains(v) && v.NeighborFaces().Count > 0).Distinct().ToList();
        return targets.Count == 0 ? 0 : BevelOp.Vertices(mesh, targets, offset, relative);
    }

    /// <summary>
    /// Weld vertices that are within a distance threshold, merging them into single vertices.
    /// This is useful for cleaning up geometry with duplicate or nearly-duplicate vertices.
    /// Attributes are averaged based on the merged vertices.
    /// </summary>
    /// <param name="mesh">The mesh to weld vertices in.</param>
    /// <param name="threshold">Maximum distance between vertices to be welded together.</param>
    /// <returns>The number of vertices that were welded (removed).</returns>
    public static int WeldVertices(GeometryData mesh, float threshold = 0.0001f)
    {
        return VertexWeldOp.WeldVertices(mesh, threshold);
    }

    /// <summary>
    /// Weld vertices at specific positions by merging all vertices within threshold distance of each target.
    /// This is useful for welding specific problem areas without affecting the entire mesh.
    /// </summary>
    /// <param name="mesh">The mesh to weld vertices in.</param>
    /// <param name="targetPositions">Positions around which to weld vertices.</param>
    /// <param name="threshold">Maximum distance from target positions to weld vertices.</param>
    /// <returns>The number of vertices that were welded (removed).</returns>
    public static int WeldVerticesAtPositions(GeometryData mesh, IEnumerable<Float3> targetPositions, float threshold)
    {
        return VertexWeldOp.WeldVerticesAtPositions(mesh, targetPositions, threshold);
    }

    /// <summary>
    /// Reduce the triangle count by collapsing vertices onto their neighbours, cheapest first by quadric
    /// error. No vertex is ever created or moved, so every vertex and loop attribute keeps an authored
    /// value. Seams (loops at one vertex with different attributes), borders and region outlines are
    /// only ever simplified along themselves. When anything collapses the mesh comes back as triangles,
    /// faces with more corners included. When nothing does it is left untouched.
    /// </summary>
    /// <param name="mesh">The mesh to simplify in place.</param>
    /// <param name="options">Target, error limit, attribute weights and what to preserve.</param>
    public static SimplifyResult Simplify(GeometryData mesh, SimplifyOptions options)
    {
        return SimplifyOp.Simplify(mesh, options);
    }

    /// <summary>
    /// Merges each connected patch of the faces into one face along the patch's outline. The new face carries the
    /// attributes of a face of its patch, its corners keep their loop attributes, and edges and vertices left
    /// inside are removed. A patch whose outline is not one simple loop, such as a ring, is left as it is.
    /// </summary>
    /// <returns>The faces that came of it.</returns>
    public static List<GeometryData.Face> DissolveFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        return DissolveOp.Faces(mesh, faces);
    }

    /// <summary>
    /// Removes edges by joining the two faces either side of each into one, then removes the vertices at their
    /// ends that are left joining only two edges. Edges without exactly two faces are left alone.
    /// </summary>
    /// <returns>How many edges were dissolved.</returns>
    public static int DissolveEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        return DissolveOp.Edges(mesh, edges);
    }

    /// <summary>
    /// Removes vertices and keeps the surface closed: a vertex joining two edges leaves one edge, and a vertex
    /// inside the surface leaves one face where the faces around it were. Vertices on a border with more than
    /// two edges are left alone.
    /// </summary>
    /// <returns>How many vertices were dissolved.</returns>
    public static int DissolveVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices)
    {
        return DissolveOp.Vertices(mesh, vertices);
    }

    #endregion

    #region Loop Operators

    /// <summary>
    /// The edges that run on from an edge end to end, as a modeller's edge loop: through a vertex where four edges
    /// meet it takes the edge across, and along a border it follows the border. In order, the edge given included.
    /// </summary>
    public static List<GeometryData.Edge> EdgeLoop(GeometryData.Edge edge)
    {
        return LoopOp.EdgeLoop(edge);
    }

    /// <summary>
    /// The edges facing an edge across four sided faces, one after another, as a modeller's edge ring. In order, the
    /// edge given included. It stops at a face that does not have four sides.
    /// </summary>
    public static List<GeometryData.Edge> EdgeRing(GeometryData.Edge edge)
    {
        return LoopOp.EdgeRing(edge, out _).Select(r => r.Edge).ToList();
    }

    /// <summary>
    /// The edge loop through an edge. Where it stops at once, at a corner of three edges or beside a face that is not
    /// four sided, it is the outline of the face on the given side instead.
    /// </summary>
    public static List<GeometryData.Edge> EdgeLoopOrOutline(GeometryData.Edge edge, GeometryData.Face? side = null)
    {
        return LoopOp.EdgeLoopOrOutline(edge, side);
    }

    /// <summary>
    /// The edge ring through an edge. Where it stops at once, beside a face that is not four sided, it walks the faces
    /// round taking the edge most nearly parallel to the last, as the upright edges of a prism.
    /// </summary>
    public static List<GeometryData.Edge> EdgeRingAcross(GeometryData.Edge edge)
    {
        return LoopOp.EdgeRingAcross(edge);
    }

    /// <summary>The faces in groups joined across edges to others of the faces given.</summary>
    public static List<List<GeometryData.Face>> FaceRegions(IEnumerable<GeometryData.Face> faces)
    {
        return LoopOp.FaceRegions(faces);
    }

    /// <summary>
    /// Cuts new edge loops across the ring of an edge. Every edge of the ring gets the given number of new vertices,
    /// evenly spaced, and the faces between are split through them. The slide moves the cuts along the ring's edges,
    /// from -1 to 1 for the whole gap either way. Vertex and loop attributes are interpolated.
    /// </summary>
    /// <returns>The new edges, the cuts.</returns>
    public static List<GeometryData.Edge> LoopCut(GeometryData mesh, GeometryData.Edge edge, int cuts = 1, float slide = 0f)
    {
        return LoopOp.Cut(mesh, edge, cuts, slide);
    }

    /// <summary>
    /// Slides the vertices of connected edges along the edges that leave them sideways. At 1 they reach the next
    /// vertices on the left of the direction the edges run in, at -1 the next on the right.
    /// </summary>
    /// <returns>How many vertices moved.</returns>
    public static int SlideEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, float factor)
    {
        return LoopOp.Slide(mesh, edges, factor);
    }

    /// <summary>
    /// Joins two runs of edges with a strip of four sided faces. The edges must fall into exactly two chains or
    /// two rings. Runs of different length are joined when they differ by an even number of vertices and the longer
    /// has at most three edges for every edge of the shorter: the strip narrows in evenly spread places, over two
    /// new vertices each. Throws <see cref="InvalidOperationException"/> when the runs do not fit.
    /// </summary>
    /// <returns>The new faces.</returns>
    public static List<GeometryData.Face> BridgeEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        return TopologyOp.Bridge(mesh, edges);
    }

    /// <summary>
    /// Moves vertices towards the middle of the vertices they share an edge with. A vertex on a border only follows
    /// its border neighbours, so it stays on the border.
    /// </summary>
    /// <param name="factor">How far each pass moves a vertex, from 0 to 1 for all the way.</param>
    /// <returns>How many vertices moved.</returns>
    public static int SmoothVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices, float factor = 0.5f, int iterations = 1)
    {
        return TopologyOp.Smooth(mesh, vertices, factor, iterations);
    }

    /// <summary>Moves vertices along their normals by a distance, out for positive and in for negative.</summary>
    /// <returns>How many vertices moved.</returns>
    public static int ShrinkFatten(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices, float distance)
    {
        return TopologyOp.ShrinkFatten(mesh, vertices, distance);
    }

    /// <summary>
    /// Pulls vertices towards a sphere round their middle, as big as their average distance from it, by a factor
    /// from 0 for not at all to 1 for onto it.
    /// </summary>
    /// <returns>How many vertices moved.</returns>
    public static int ToSphere(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices, float factor)
    {
        return TopologyOp.ToSphere(mesh, vertices, factor);
    }

    /// <summary>
    /// A copy of the mesh with its mirror image across a plane. Vertices within the merge distance of the plane
    /// are shared by both halves and put on it. The mesh given is not changed.
    /// </summary>
    /// <param name="axis">The axis the plane is across: 0, 1 or 2 for X, Y or Z.</param>
    /// <param name="offset">Where along the axis the plane stands, 0 for through the origin.</param>
    public static GeometryData MirrorCopy(GeometryData mesh, int axis = 0, float mergeDistance = 0.001f, float offset = 0f)
    {
        return TopologyOp.Mirror(mesh, axis, mergeDistance, offset, null);
    }

    /// <summary>
    /// A mirrored copy as <see cref="MirrorCopy(GeometryData, int, float, float)"/> makes, naming in
    /// <paramref name="mirrored"/> the faces of the copy that are mirror images, so their attributes can be turned too.
    /// </summary>
    public static GeometryData MirrorCopy(GeometryData mesh, int axis, float mergeDistance, float offset, List<GeometryData.Face> mirrored)
    {
        return TopologyOp.Mirror(mesh, axis, mergeDistance, offset, mirrored);
    }

    /// <summary>
    /// A copy of the mesh given thickness: an outer and an inner shell along the vertex normals, joined along open
    /// borders. The offset from -1 to 1 places the shells, -1 keeping the outer one on the surface. The mesh given is
    /// not changed.
    /// </summary>
    public static GeometryData Solidify(GeometryData mesh, float thickness, float offset = -1f)
    {
        return TopologyOp.Solidify(mesh, thickness, offset);
    }

    /// <summary>
    /// Catmull Clark subdivision as a new mesh: each level turns every face into one four sided face per corner and
    /// smooths the surface. Borders and edges flagged sharp keep their line. Attributes are carried over, with
    /// float loop attributes such as texture coordinates interpolated across each face. The mesh given is not changed.
    /// </summary>
    public static GeometryData SubdivideSmooth(GeometryData mesh, int levels = 1)
    {
        GeometryData result = mesh;
        for (int i = 0; i < levels; i++) result = TopologyOp.CatmullClark(result);
        return result;
    }

    /// <summary>
    /// Cuts faces along a plane. An edge of the faces that crosses the plane is split where it crosses, and a face
    /// with two such points is split between them. The test, when given, says which crossing points count, so a cut
    /// can be kept to a part of the plane. Faces the cut enters but does not cross are left whole.
    /// </summary>
    /// <returns>The new edges across the faces.</returns>
    public static List<GeometryData.Edge> KnifeCut(GeometryData mesh, IEnumerable<GeometryData.Face> faces, Float3 planePoint, Float3 planeNormal, Func<Float3, bool>? within = null)
    {
        return CutOp.Knife(mesh, faces, planePoint, planeNormal, within);
    }

    /// <summary>
    /// Cuts a mesh along a path of straight stretches, one between each point and the next. A stretch lies in a
    /// plane, and cuts every edge it crosses and runs through every vertex it meets. A point may sit on a vertex, on
    /// an edge, inside a face or off the mesh. A face is split wherever the path crosses it from outline to outline,
    /// also through points inside it and when it is crossed more than once. Where the path starts or stops inside a
    /// face, that end is left as edges lying on the face, which stays whole.
    /// </summary>
    /// <param name="planes">The plane of each stretch, one fewer than there are points.</param>
    /// <param name="along">How far along a stretch a point of its plane is, 0 at its start and 1 at its end. Null when the point is not part of the cut, as when it is past an end or out of sight.</param>
    /// <param name="cuts">Which faces may be cut, all when null.</param>
    /// <returns>The edges along the cut.</returns>
    public static List<GeometryData.Edge> KnifePath(GeometryData mesh, IReadOnlyList<KnifePoint> points, IReadOnlyList<(Float3 Point, Float3 Normal)> planes,
        Func<int, Float3, float?> along, Func<GeometryData.Face, bool>? cuts = null)
    {
        return KnifeOp.Cut(mesh, points, planes, along, cuts);
    }

    /// <summary>
    /// Pulls a surface apart along edges: every vertex on them is copied for each group of faces the edges separate
    /// around it. Where a run of edges ends inside the surface the end stays joined, which opens a slit.
    /// </summary>
    /// <returns>The new vertices.</returns>
    public static List<GeometryData.Vertex> SplitEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        return CutOp.SplitEdges(mesh, edges);
    }

    /// <summary>
    /// Splits faces off the rest of the mesh where they meet it along edges: every vertex on that outline is copied, so
    /// the faces keep their place but no longer share it.
    /// </summary>
    /// <returns>The faces split off, as they are after, since some are put back as new faces.</returns>
    public static List<GeometryData.Face> SplitFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        return CutOp.SplitFaces(mesh, faces);
    }

    /// <summary>
    /// Fills a closed ring of edges with a grid of four sided faces. The ring needs an even number of edges. Throws
    /// <see cref="InvalidOperationException"/> when the edges are not one such ring.
    /// </summary>
    /// <param name="span">How many faces run along the first side of the grid. The rest of the ring gives the other way.</param>
    /// <param name="offset">Turns the ring, moving where the grid's first corner is.</param>
    /// <returns>The new faces.</returns>
    public static List<GeometryData.Face> GridFill(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, int span = 1, int offset = 0)
    {
        return CutOp.GridFill(mesh, edges, span, offset);
    }

    /// <summary>
    /// Evens out the spacing of vertices without changing the shape: each moves towards the middle of its
    /// neighbours, but only along the surface.
    /// </summary>
    /// <returns>How many vertices moved.</returns>
    public static int RelaxVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices, float factor = 0.5f, int iterations = 1)
    {
        return CutOp.Relax(mesh, vertices, factor, iterations);
    }

    #endregion

    #region Modelling Operators

    // These keep a mesh the way a SketchUp style modeller wants it: faces stay flat, every plane's faces are
    // rebuilt from its edges after an edit, and faces joined by hidden edges act as one face. A face bent out
    // of flat by an edit is folded into triangles joined by hidden edges.

    /// <summary>
    /// Draws lines into the mesh. Any closed loop they make on a plane becomes a face, and lines across an
    /// existing face split it.
    /// </summary>
    /// <param name="mesh">The mesh to draw into.</param>
    /// <param name="segments">The lines, as pairs of end points.</param>
    /// <param name="planeNormal">The plane the lines were drawn on, when it is known.</param>
    /// <param name="viewDirection">Where the viewer looks, so a new face in empty space faces them.</param>
    /// <param name="material">The material for faces made in empty space.</param>
    public static void DrawSegments(GeometryData mesh, IReadOnlyList<(Float3 A, Float3 B)> segments, Float3? planeNormal, Float3 viewDirection, int material)
    {
        DrawSegmentsOp.DrawSegments(mesh, segments, planeNormal, viewDirection, material);
    }

    /// <summary>Joins vertices that share a face with new edges, in the order they run around it. Returns how many edges were drawn.</summary>
    public static int ConnectVertices(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, int material, Float3 viewDirection)
    {
        return DrawSegmentsOp.ConnectVertices(mesh, vertices, material, viewDirection);
    }

    /// <summary>
    /// Cuts faces where they pass through each other, adding the edges of the crossing. With a selection only
    /// those faces are cut against the rest. Returns how many crossing lines were added.
    /// </summary>
    public static int IntersectFaces(GeometryData mesh, IReadOnlyCollection<GeometryData.Face>? only = null)
    {
        return IntersectFacesOp.IntersectFaces(mesh, only);
    }

    /// <summary>
    /// Pushes or pulls a flat region along its normal. Walls are added around it, walls that line up with
    /// faces beside them merge into them, and faces that end up overlapping facing opposite ways cancel, so
    /// pushing through a solid cuts a hole. A region standing on its own keeps its base, making a solid.
    /// </summary>
    /// <param name="mesh">The mesh containing the region.</param>
    /// <param name="region">Faces in one plane, usually a whole face with the faces joined to it by hidden edges.</param>
    /// <param name="distance">How far to move it along its normal.</param>
    /// <param name="keepBase">Leave the region where it was too, so the result is a new solid on top of it.</param>
    public static void PushPull(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> region, float distance, bool keepBase = false)
    {
        PushPullOp.PushPull(mesh, region, distance, keepBase);
    }

    /// <summary>Moves vertices, then folds faces bent out of flat and rebuilds the planes they touch.</summary>
    public static void MoveVertices(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, Float3 delta)
    {
        MoveVerticesOp.MoveVertices(mesh, vertices, delta);
    }

    /// <summary>Moves vertices by any transform, then folds and rebuilds like <see cref="MoveVertices"/>.</summary>
    public static void TransformVertices(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, Func<Float3, Float3> transform)
    {
        MoveVerticesOp.TransformVertices(mesh, vertices, transform);
    }

    /// <summary>Scales vertices about a pivot, per axis of a rotated frame, then folds and rebuilds like <see cref="MoveVertices"/>.</summary>
    public static void ScaleVertices(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, Float3 pivot, Float3 factors, Quaternion frame)
    {
        MoveVerticesOp.Scale(mesh, vertices, pivot, factors, frame);
    }

    /// <summary>Joins vertices into one at a point. Faces that collapse away with them are removed.</summary>
    public static void MergeVertices(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, Float3 target)
    {
        MoveVerticesOp.MergeAt(mesh, vertices, target);
    }

    /// <summary>Collapses each connected group of edges to a single point at its centre. Returns how many vertices were joined.</summary>
    /// <param name="centres">Given the point each joined group went to.</param>
    public static int CollapseEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, List<Float3>? centres = null)
    {
        return MoveVerticesOp.CollapseEdges(mesh, edges, centres);
    }

    /// <summary>
    /// Adds a run of edges through points. An end on a vertex uses it, an end on an edge splits the edge there unless
    /// that is at one of its ends, and a free end gets a vertex of its own.
    /// </summary>
    /// <returns>The edges of the run.</returns>
    public static List<GeometryData.Edge> AddLine(GeometryData mesh, IReadOnlyList<Float3> points, LineAnchor start, LineAnchor end)
    {
        return DrawSegmentsOp.AddLine(mesh, points, start, end);
    }

    /// <summary>
    /// Adds a four sided face through corners, using the vertices given where there are some, turned to face against
    /// the view direction. Throws <see cref="InvalidOperationException"/> when the corners make no face.
    /// </summary>
    public static GeometryData.Face AddQuad(GeometryData mesh, IReadOnlyList<Float3> corners, IReadOnlyList<GeometryData.Vertex?> existing, Float3 viewDirection)
    {
        return DrawSegmentsOp.AddQuad(mesh, corners, existing, viewDirection);
    }

    /// <summary>
    /// A face through a chain or ring of edges, turned to face the way of a face already beside it. Throws
    /// <see cref="InvalidOperationException"/> when the edges are not one chain or ring.
    /// </summary>
    public static GeometryData.Face FillChain(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        return DrawSegmentsOp.FillChain(mesh, edges);
    }

    /// <summary>Pulls edges out into strips of faces joining each to a copy in the same place, for the copies to be moved off.</summary>
    /// <returns>The copied edges.</returns>
    public static List<GeometryData.Edge> ExtrudeEdgesInPlace(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        return ExtrudeEdgesOp.ExtrudeEdgesInPlace(mesh, edges);
    }

    /// <summary>Gives each vertex a copy in the same place joined to it by an edge, for the copies to be moved off.</summary>
    /// <returns>The copies.</returns>
    public static List<GeometryData.Vertex> ExtrudeVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices)
    {
        return ExtrudeEdgesOp.ExtrudeVertices(mesh, vertices);
    }

    /// <summary>
    /// Pulls the surface apart along edges as <see cref="SplitEdges"/> does and gives back one side's vertices: the
    /// copies, or with <paramref name="distance"/> the side whose faces are nearer by it.
    /// </summary>
    public static List<GeometryData.Vertex> Rip(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, Func<Float3, float>? distance = null)
    {
        return CutOp.Rip(mesh, edges, distance);
    }

    /// <summary>
    /// Moves vertices onto the nearest surface along their own normals, either way, within reach, then out by the
    /// offset. <paramref name="hit"/> gives how far along a world ray the nearest surface is, within a distance, or null.
    /// </summary>
    /// <returns>How many vertices moved.</returns>
    public static int Shrinkwrap(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices, Float4x4 toWorld, Func<Ray, float, float?> hit, float offset, float reach)
    {
        return MoveVerticesOp.Shrinkwrap(mesh, vertices, toWorld, hit, offset, reach);
    }

    /// <summary>
    /// Moves vertices onto one plane through their middle, facing the way the given faces do on average, or the
    /// vertices' own normals when no face is given.
    /// </summary>
    public static void FlattenVertices(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, IReadOnlyCollection<GeometryData.Face>? facing = null)
    {
        MoveVerticesOp.Flatten(mesh, vertices, facing);
    }

    /// <summary>
    /// Removes two faces and joins their outlines with a strip of faces, cut into segments along it. Outlines whose
    /// vertex counts do not fit get vertices added along the shorter one's longest edges first.
    /// </summary>
    /// <returns>The strip.</returns>
    public static List<GeometryData.Face> BridgeFaces(GeometryData mesh, GeometryData.Face first, GeometryData.Face second, int segments = 1)
    {
        return TopologyOp.BridgeFaces(mesh, first, second, segments);
    }

    /// <summary>Takes faces out of a mesh into a mesh of their own, with every attribute. Vertices left with no face are dropped.</summary>
    public static GeometryData Separate(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        return TopologyOp.Separate(mesh, faces);
    }

    /// <summary>Shades faces smooth, every edge between them soft, or flat, every edge of them sharp.</summary>
    /// <returns>The vertices of the faces, whose corner normals are now out of date.</returns>
    public static HashSet<GeometryData.Vertex> Shade(GeometryData mesh, IEnumerable<GeometryData.Face> faces, bool smooth)
    {
        return SoftenOp.Shade(mesh, faces, smooth);
    }

    /// <summary>
    /// Gives each corner a normal in a loop attribute: the faces round its vertex joined to its face across soft edges,
    /// averaged by area, so the surface is smooth across soft edges and creased across any other. With
    /// <paramref name="only"/> only the corners at those vertices.
    /// </summary>
    public static void CornerNormals(GeometryData mesh, string attribute, ISet<GeometryData.Vertex>? only = null)
    {
        SoftenOp.CornerNormals(mesh, attribute, only);
    }

    /// <summary>
    /// Joins vertices within a distance of each other onto their average and rebuilds the flat faces around
    /// them. Unlike <see cref="WeldVertices"/> clusters chain, so a row of close vertices becomes one. With no
    /// scope every vertex is considered. Returns how many vertices were joined.
    /// </summary>
    public static int MergeByDistance(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex>? scope, float distance)
    {
        return MoveVerticesOp.WeldByDistance(mesh, scope, distance);
    }

    /// <summary>
    /// Copies faces and loose edges through a transform. With weld, copies that land on existing faces merge into them
    /// like drawn geometry. Returns the new vertices.
    /// </summary>
    /// <param name="mesh">The mesh to copy within.</param>
    /// <param name="faces">The faces to copy, with any faces joined to them by hidden edges.</param>
    /// <param name="edges">Loose edges to copy.</param>
    /// <param name="transform">Where each point of the copy goes.</param>
    /// <param name="flip">Reverse the copies, for transforms that mirror.</param>
    public static List<GeometryData.Vertex> CopyFaces(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, IReadOnlyCollection<GeometryData.Edge> edges, Func<Float3, Float3> transform, bool flip = false, bool weld = true)
    {
        return CopyOp.Copy(mesh, faces, edges, transform, flip, weld);
    }

    /// <summary>A mirrored copy of faces and edges across a plane, wound to face the same way as the originals. Returns the new vertices.</summary>
    public static List<GeometryData.Vertex> Mirror(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, IReadOnlyCollection<GeometryData.Edge> edges, Plane plane)
    {
        return CopyOp.Mirror(mesh, faces, edges, plane);
    }

    /// <summary>Copies faces and edges several times, each copy by the transform for its step, starting at 1. With weld, copies merge into what they land on.</summary>
    public static void Array(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, IReadOnlyCollection<GeometryData.Edge> edges, int copies, Func<Float3, int, Float3> transform, bool weld = true)
    {
        CopyOp.Array(mesh, faces, edges, copies, transform, weld);
    }

    /// <summary>Deletes faces, with the rest of any face they are joined to by hidden edges. Their edges stay.</summary>
    public static void DeleteFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        DeleteOp.DeleteFaces(mesh, faces);
    }

    /// <summary>
    /// Deletes edges. An edge between two faces lying flat together merges them, any other edge takes the
    /// faces it bounds with it. Lines left running straight through a vertex are joined back up.
    /// </summary>
    public static void DeleteEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        DeleteOp.DeleteEdges(mesh, edges);
    }

    /// <summary>Deletes vertices with their faces. One sitting in the middle of a straight line just joins the line back up.</summary>
    public static void DeleteVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices)
    {
        DeleteOp.DeleteVertices(mesh, vertices);
    }

    /// <summary>Adds evenly spaced vertices along edges, splitting every face that uses them. Returns the new vertices.</summary>
    public static List<GeometryData.Vertex> SubdivideEdges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, int cuts)
    {
        return SubdivideEdgesOp.SubdivideEdges(mesh, edges, cuts);
    }

    /// <summary>Pushes edges out into a strip of new quads, joining each edge to a copy moved by the offset. Returns the copies of the vertices.</summary>
    public static List<GeometryData.Vertex> ExtrudeEdges(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, Float3 offset, int material)
    {
        return ExtrudeEdgesOp.ExtrudeEdges(mesh, edges, offset, material);
    }

    /// <summary>Closes a loop of edges with a face. A bent loop becomes triangles joined by hidden edges. Returns how many faces were made.</summary>
    public static int FillLoop(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, int material)
    {
        return FillLoopOp.FillLoop(mesh, edges, material);
    }

    /// <summary>Splits faces into triangles joined by hidden edges, so each piece can be bent on its own while it still reads as one face.</summary>
    public static void FoldFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        FoldOp.TriangulateFaces(mesh, faces);
    }

    /// <summary>Reverses the way faces point. Faces joined by hidden edges flip together.</summary>
    public static void FlipFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        OrientOp.FlipFaces(mesh, faces);
    }

    /// <summary>
    /// Turns faces round where neighbours disagree about which way is out, so each surface is consistent, and
    /// makes closed shells face outwards. Returns how many faces were flipped.
    /// </summary>
    public static int OrientFaces(GeometryData mesh)
    {
        return OrientOp.OrientFaces(mesh);
    }

    /// <summary>Marks the edges along a run of points as soft, the way a drawn circle is one smooth curve.</summary>
    public static void SoftenPolyline(GeometryData mesh, IReadOnlyList<Float3> points, bool closed)
    {
        SoftenOp.SoftenPolyline(mesh, points, closed);
    }

    /// <summary>Makes edges soft where their two faces meet at less than an angle in degrees, and hard where they do not. Returns how many changed.</summary>
    public static int SoftenByAngle(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, float degrees)
    {
        return SoftenOp.SoftenByAngle(mesh, edges, degrees);
    }

    /// <summary>The edges between two faces that turn against each other by more than the angle.</summary>
    public static List<GeometryData.Edge> EdgesSharperThan(GeometryData mesh, float degrees)
    {
        float limit = MathF.Cos(degrees * Maths.Deg2Rad);
        return [.. mesh.Edges.Where(e => e.DihedralCosine() is float cosine && cosine < limit)];
    }

    /// <summary>
    /// A new mesh of a closed mesh combined with another, placed by a matrix into its space: the other added, cut
    /// away, or kept only where both are. Attributes of both are carried, the first mesh's where they share a name.
    /// The meshes given are not changed.
    /// </summary>
    public static GeometryData Boolean(GeometryData mesh, GeometryData other, Float4x4 otherToMesh, CSGOperation operation)
    {
        var scene = new CSGScene();
        scene.Add(mesh, CSGOperation.Additive);
        scene.Add(other, operation, otherToMesh);
        GeometryData result = scene.Build();
        result.RemoveFaceAttribute(CSGScene.BrushIdAttribute);
        result.RemoveFaceAttribute(CSGScene.SourceFaceAttribute);
        return result;
    }

    /// <summary>Sets whether edges are soft.</summary>
    public static void SetSoft(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, bool soft)
    {
        SoftenOp.SetSoft(mesh, edges, soft);
    }

    /// <summary>Gives faces a material and drops any texture mapping they had, so the material's own projection applies.</summary>
    public static void Paint(GeometryData mesh, IEnumerable<GeometryData.Face> faces, int material)
    {
        foreach (var face in faces)
        {
            face.Material = material;
            face.Mapping = null;
        }
        mesh.MarkChanged();
    }

    /// <summary>
    /// Sweeps a flat region along a path of edges, like SketchUp's Follow Me. Corners are mitred so the walls
    /// stay flat, and the ends are capped unless the path loops. Returns how many faces were made.
    /// </summary>
    public static int FollowMe(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> profile, IReadOnlyCollection<GeometryData.Edge> path)
    {
        return FollowMeOp.FollowMe(mesh, profile, path);
    }

    #endregion
}
