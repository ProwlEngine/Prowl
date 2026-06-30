// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// CSG (Constructive Solid Geometry) operations for <see cref="GeometryData"/>.
    ///
    /// This is a polygon-based, plane-driven implementation (after Sander van Rossen's Realtime-CSG):
    /// each input solid is reduced to its set of face planes, rebuilt into a clean convex half-edge
    /// mesh, and booleaned by splitting polygons against the other solid's planes and categorizing the
    /// pieces as inside/outside/aligned. No triangulation is involved, so coplanar faces stay aligned
    /// and the output is clean n-gon geometry.
    ///
    /// Attributes (vertex, edge, loop, and face) are carried through: every output polygon is traced
    /// back to the source face it lies on, and per-corner/per-vertex values are reconstructed by
    /// interpolating that source face at the output vertex's position. Where both inputs define an
    /// attribute of the same name, the schema of <c>meshA</c> wins.
    ///
    /// IMPORTANT: Both inputs MUST be convex closed solids. A solid is treated purely as the
    /// intersection of the half-spaces behind its (outward-facing) face planes; concave geometry is
    /// not representable this way and will produce incorrect results. Face windings must be consistent
    /// so that face normals point outward.
    /// </summary>
    public static class GeometryCSG
    {
        public enum Operation
        {
            Union,
            Intersection,
            Subtraction
        }

        // Matches the epsilons from the original Realtime-CSG; tuned for roughly unit-scale brushes.
        private const float DistanceEpsilon = 0.0001f;
        private const float WeldTolerance = 0.0001f;

        #region Public API

        /// <summary>
        /// Performs a CSG operation on two convex solids and returns the resulting geometry.
        /// </summary>
        /// <param name="operation">The CSG operation to perform.</param>
        /// <param name="meshA">First convex solid.</param>
        /// <param name="meshB">Second convex solid.</param>
        public static GeometryData PerformOperation(Operation operation, GeometryData meshA, GeometryData meshB)
        {
            var brushA = Brush.FromConvexGeometry(meshA);
            var brushB = Brush.FromConvexGeometry(meshB);

            var result = new GeometryData();
            var ctx = new EmitContext(result, meshA, meshB);

            // Degenerate inputs (not enough planes to bound a solid) fall back gracefully.
            if (brushA == null)
            {
                if (brushB != null && operation == Operation.Union)
                    Emit(ctx, brushB, brushB.Polygons, flip: false);
                return result;
            }
            if (brushB == null)
            {
                if (operation != Operation.Intersection)
                    Emit(ctx, brushA, brushA.Polygons, flip: false);
                return result;
            }

            // Categorize each brush's polygons against the other convex solid.
            brushA.CategorizeAgainst(brushB, out var insideA, out var alignedA, out _, out var outsideA);
            brushB.CategorizeAgainst(brushA, out var insideB, out _, out _, out var outsideB);

            // A's faces are emitted last so that A wins on shared (welded) per-vertex attributes.
            switch (operation)
            {
                case Operation.Union:
                    // Surface of A outside B, plus surface of B outside A. Coincident (aligned) faces are
                    // kept once (from A) to avoid overlapping duplicates.
                    Emit(ctx, brushB, outsideB, flip: false);
                    Emit(ctx, brushA, outsideA, flip: false);
                    Emit(ctx, brushA, alignedA, flip: false);
                    break;

                case Operation.Intersection:
                    // Surface of A inside B, plus surface of B inside A.
                    Emit(ctx, brushB, insideB, flip: false);
                    Emit(ctx, brushA, insideA, flip: false);
                    Emit(ctx, brushA, alignedA, flip: false);
                    break;

                case Operation.Subtraction:
                    // A minus B: keep A's surface outside B, and B's surface inside A flipped inward to
                    // cap the cavity.
                    Emit(ctx, brushB, insideB, flip: true);
                    Emit(ctx, brushA, outsideA, flip: false);
                    break;
            }

            return result;
        }

        /// <summary>Computes the union of two convex solids.</summary>
        public static GeometryData Union(GeometryData meshA, GeometryData meshB)
            => PerformOperation(Operation.Union, meshA, meshB);

        /// <summary>Computes the intersection of two convex solids.</summary>
        public static GeometryData Intersect(GeometryData meshA, GeometryData meshB)
            => PerformOperation(Operation.Intersection, meshA, meshB);

        /// <summary>Subtracts <paramref name="meshB"/> from <paramref name="meshA"/> (meshA - meshB).</summary>
        public static GeometryData Subtraction(GeometryData meshA, GeometryData meshB)
            => PerformOperation(Operation.Subtraction, meshA, meshB);

        #endregion

        #region Result assembly

        /// <summary>
        /// Holds the output mesh, the vertex weld cache, and whether any attributes need carrying.
        /// Registering attributes here means the result owns the union of both inputs' definitions.
        /// </summary>
        private sealed class EmitContext
        {
            public readonly GeometryData Result;
            public readonly Dictionary<SnapKey, GeometryData.Vertex> Weld = new();
            public readonly bool HasAttributes;

            public EmitContext(GeometryData result, GeometryData? a, GeometryData? b)
            {
                Result = result;
                HasAttributes = RegisterAttributes(result, a, b);
            }

            // meshA is registered first so it wins when both inputs define the same attribute name.
            private static bool RegisterAttributes(GeometryData result, GeometryData? a, GeometryData? b)
            {
                foreach (var mesh in new[] { a, b })
                {
                    if (mesh == null) continue;
                    foreach (var def in mesh.VertexAttributes) result.AddVertexAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                    foreach (var def in mesh.EdgeAttributes) result.AddEdgeAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                    foreach (var def in mesh.LoopAttributes) result.AddLoopAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                    foreach (var def in mesh.FaceAttributes) result.AddFaceAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                }

                return result.VertexAttributes.Count > 0 || result.EdgeAttributes.Count > 0 ||
                       result.LoopAttributes.Count > 0 || result.FaceAttributes.Count > 0;
            }
        }

        private static void Emit(EmitContext ctx, Brush brush, List<Polygon> polygons, bool flip)
        {
            var ring = new List<Float3>();
            foreach (var polygon in polygons)
            {
                if (polygon.FirstEdge == -1)
                    continue;

                ring.Clear();
                brush.CollectRing(polygon, ring);
                if (flip)
                    ring.Reverse();

                EmitFace(ctx, brush, polygon, ring);
            }
        }

        private static void EmitFace(EmitContext ctx, Brush brush, Polygon polygon, List<Float3> ring)
        {
            var verts = new List<GeometryData.Vertex>(ring.Count);
            var positions = new List<Float3>(ring.Count);
            foreach (var point in ring)
            {
                var vertex = GetOrAddVertex(ctx, point);
                // Skip vertices coincident with the previous one (collapsed edges).
                if (verts.Count > 0 && verts[verts.Count - 1] == vertex)
                    continue;
                verts.Add(vertex);
                positions.Add(point);
            }

            // Drop a wrap-around duplicate between last and first.
            if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
            {
                verts.RemoveAt(verts.Count - 1);
                positions.RemoveAt(positions.Count - 1);
            }

            if (verts.Count < 3)
                return;

            var face = ctx.Result.AddFace(verts.ToArray());
            if (face == null || !ctx.HasAttributes)
                return;

            CarryFaceAttributes(ctx, brush, polygon, positions, face);
            CarryCornerAttributes(ctx, brush, polygon, verts, positions, face);
            CarryEdgeAttributes(ctx, brush, polygon, verts, positions);
        }

        private static GeometryData.Vertex GetOrAddVertex(EmitContext ctx, Float3 point)
        {
            var key = new SnapKey(point, WeldTolerance);
            if (ctx.Weld.TryGetValue(key, out var existing))
                return existing;

            var vertex = ctx.Result.AddVertex(point);
            ctx.Weld[key] = vertex;
            return vertex;
        }

        private readonly struct SnapKey : IEquatable<SnapKey>
        {
            private readonly int _x, _y, _z;

            public SnapKey(Float3 point, float tolerance)
            {
                _x = (int)MathF.Round(point.X / tolerance);
                _y = (int)MathF.Round(point.Y / tolerance);
                _z = (int)MathF.Round(point.Z / tolerance);
            }

            public bool Equals(SnapKey other) => _x == other._x && _y == other._y && _z == other._z;
            public override bool Equals(object? obj) => obj is SnapKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
        }

        #endregion

        #region Attribute carrying

        // Face attributes are taken from the source face containing the polygon's centroid.
        private static void CarryFaceAttributes(EmitContext ctx, Brush brush, Polygon polygon,
            List<Float3> positions, GeometryData.Face face)
        {
            if (ctx.Result.FaceAttributes.Count == 0)
                return;

            Float3 centroid = Float3.Zero;
            foreach (var p in positions) centroid += p;
            centroid /= positions.Count;

            if (!brush.ResolveFace(polygon.PlaneIndex, centroid, out var source))
                return;

            foreach (var def in ctx.Result.FaceAttributes)
            {
                if (source.Mesh.HasFaceAttribute(def.Name) &&
                    source.Face.Attributes.TryGetValue(def.Name, out var value))
                {
                    face.Attributes[def.Name] = GeometryData.AttributeValue.Copy(value);
                }
            }
        }

        // Loop (per-corner) and vertex attributes are interpolated at each output corner.
        private static void CarryCornerAttributes(EmitContext ctx, Brush brush, Polygon polygon,
            List<GeometryData.Vertex> verts, List<Float3> positions, GeometryData.Face face)
        {
            bool anyLoop = ctx.Result.LoopAttributes.Count > 0;
            bool anyVertex = ctx.Result.VertexAttributes.Count > 0;
            if (!anyLoop && !anyVertex)
                return;

            for (int i = 0; i < verts.Count; i++)
            {
                if (!brush.ResolveCorner(polygon.PlaneIndex, positions[i], out var source, out var weights))
                    continue;

                if (anyLoop)
                {
                    var loop = face.GetLoop(verts[i]);
                    if (loop != null)
                    {
                        foreach (var def in ctx.Result.LoopAttributes)
                        {
                            if (!source.Mesh.HasLoopAttribute(def.Name)) continue;
                            loop.Attributes[def.Name] = InterpolateLoop(def, source, weights);
                        }
                    }
                }

                if (anyVertex)
                {
                    foreach (var def in ctx.Result.VertexAttributes)
                    {
                        if (!source.Mesh.HasVertexAttribute(def.Name)) continue;
                        verts[i].Attributes[def.Name] = InterpolateVertex(def, source, weights);
                    }
                }
            }
        }

        // Edge attributes are copied for output edges that lie along a source face boundary edge.
        private static void CarryEdgeAttributes(EmitContext ctx, Brush brush, Polygon polygon,
            List<GeometryData.Vertex> verts, List<Float3> positions)
        {
            if (ctx.Result.EdgeAttributes.Count == 0)
                return;

            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var sourceEdge = brush.ResolveEdge(polygon.PlaneIndex, positions[i], positions[j], out var source);
                if (sourceEdge == null)
                    continue;

                var edge = ctx.Result.FindEdge(verts[i], verts[j]);
                if (edge == null)
                    continue;

                foreach (var def in ctx.Result.EdgeAttributes)
                {
                    if (source!.Mesh.HasEdgeAttribute(def.Name) &&
                        sourceEdge.Attributes.TryGetValue(def.Name, out var value))
                    {
                        edge.Attributes[def.Name] = GeometryData.AttributeValue.Copy(value);
                    }
                }
            }
        }

        private static GeometryData.AttributeValue InterpolateLoop(GeometryData.AttributeDefinition def,
            SourceFace source, float[] weights)
        {
            int n = source.Corners.Length;
            var perCorner = new GeometryData.AttributeValue?[n];
            for (int k = 0; k < n; k++)
            {
                var loop = source.Loops[k];
                if (loop != null && loop.Attributes.TryGetValue(def.Name, out var value))
                    perCorner[k] = value;
            }
            return Interpolate(def, perCorner, weights);
        }

        private static GeometryData.AttributeValue InterpolateVertex(GeometryData.AttributeDefinition def,
            SourceFace source, float[] weights)
        {
            int n = source.Verts.Length;
            var perCorner = new GeometryData.AttributeValue?[n];
            for (int k = 0; k < n; k++)
            {
                if (source.Verts[k].Attributes.TryGetValue(def.Name, out var value))
                    perCorner[k] = value;
            }
            return Interpolate(def, perCorner, weights);
        }

        private static GeometryData.AttributeValue Interpolate(GeometryData.AttributeDefinition def,
            GeometryData.AttributeValue?[] perCorner, float[] weights)
        {
            int dims = def.Type.Dimensions;
            var acc = new float[dims];

            for (int k = 0; k < perCorner.Length; k++)
            {
                float w = weights[k];
                switch (perCorner[k])
                {
                    case GeometryData.FloatAttributeValue fv:
                        for (int d = 0; d < dims && d < fv.Data.Length; d++) acc[d] += w * fv.Data[d];
                        break;
                    case GeometryData.IntAttributeValue iv:
                        for (int d = 0; d < dims && d < iv.Data.Length; d++) acc[d] += w * iv.Data[d];
                        break;
                }
            }

            if (def.Type.BaseType == GeometryData.AttributeBaseType.Float)
                return new GeometryData.FloatAttributeValue { Data = acc };

            var rounded = new int[dims];
            for (int d = 0; d < dims; d++) rounded[d] = (int)MathF.Round(acc[d]);
            return new GeometryData.IntAttributeValue { Data = rounded };
        }

        #endregion

        #region Source face tracking

        /// <summary>
        /// A face of an input mesh, cached for attribute interpolation: its ordered corner positions,
        /// the source vertices, and the source loops (loop i owns the boundary edge from corner i to i+1).
        /// </summary>
        private sealed class SourceFace
        {
            public GeometryData Mesh = null!;
            public GeometryData.Face Face = null!;
            public Float3[] Corners = Array.Empty<Float3>();
            public GeometryData.Vertex[] Verts = Array.Empty<GeometryData.Vertex>();
            public GeometryData.Loop?[] Loops = Array.Empty<GeometryData.Loop?>();

            public static SourceFace Build(GeometryData mesh, GeometryData.Face face)
            {
                var verts = face.NeighborVertices();
                int n = verts.Count;
                var info = new SourceFace
                {
                    Mesh = mesh,
                    Face = face,
                    Corners = new Float3[n],
                    Verts = new GeometryData.Vertex[n],
                    Loops = new GeometryData.Loop?[n]
                };
                for (int i = 0; i < n; i++)
                {
                    info.Verts[i] = verts[i];
                    info.Corners[i] = verts[i].Point;
                    info.Loops[i] = face.GetLoop(verts[i]);
                }
                return info;
            }
        }

        #endregion

        #region Half-edge brush

        private enum Side { On, Inside, Outside }

        private enum SplitResult
        {
            CompletelyInside,
            CompletelyOutside,
            Split,
            PlaneAligned,
            PlaneOppositeAligned
        }

        private sealed class HalfEdge
        {
            public int Next;
            public int Twin;
            public int Vertex;
            public int Polygon;
        }

        private sealed class Polygon
        {
            public int FirstEdge = -1;
            public int PlaneIndex = -1;
            public Float3 BoundsMin;
            public Float3 BoundsMax;
        }

        /// <summary>
        /// A convex solid stored as a half-edge mesh built from its bounding planes. This mirrors the
        /// Realtime-CSG CSGMesh, ported onto Prowl.Vector types and adapted for binary operations in a
        /// shared world space (no per-brush translation).
        /// </summary>
        private sealed class Brush
        {
            public readonly Plane[] Planes;
            public readonly List<Float3> Vertices;
            public readonly List<HalfEdge> Edges;
            public readonly List<Polygon> Polygons;
            public Float3 BoundsMin;
            public Float3 BoundsMax;

            // Source faces grouped by plane index (more than one when input faces are coplanar).
            public List<SourceFace>[] PlaneFaces = Array.Empty<List<SourceFace>>();

            private Brush(Plane[] planes, List<Float3> vertices, List<HalfEdge> edges, List<Polygon> polygons)
            {
                Planes = planes;
                Vertices = vertices;
                Edges = edges;
                Polygons = polygons;
            }

            #region Construction

            /// <summary>
            /// Builds a brush from a convex <see cref="GeometryData"/> by collecting its unique outward
            /// face planes. Returns null if the geometry has too few planes to bound a solid.
            /// </summary>
            public static Brush? FromConvexGeometry(GeometryData geometry)
            {
                if (geometry == null)
                    return null;

                var planes = new List<Plane>();
                var planeFaces = new List<List<SourceFace>>();

                foreach (var face in geometry.Faces)
                {
                    if (face.VertCount < 3)
                        continue;

                    Float3 normal = face.Normal();
                    if (Float3.LengthSquared(normal) < DistanceEpsilon * DistanceEpsilon)
                        continue;

                    var plane = Plane.FromNormalAndPoint(normal, face.Center());

                    int existing = -1;
                    for (int i = 0; i < planes.Count; i++)
                    {
                        if (Float3.Dot(planes[i].Normal, plane.Normal) > 1.0f - 1e-4f &&
                            Maths.Abs(planes[i].D - plane.D) < DistanceEpsilon)
                        {
                            existing = i;
                            break;
                        }
                    }

                    var info = SourceFace.Build(geometry, face);
                    if (existing >= 0)
                    {
                        planeFaces[existing].Add(info);
                    }
                    else
                    {
                        planes.Add(plane);
                        planeFaces.Add(new List<SourceFace> { info });
                    }
                }

                if (planes.Count < 4)
                    return null;

                var brush = CreateFromPlanes(planes.ToArray());
                brush.PlaneFaces = planeFaces.ToArray();
                return brush;
            }

            private sealed class EdgeIntersection
            {
                public readonly int[] PlaneIndices = new int[2];
                public readonly HalfEdge Edge;

                public EdgeIntersection(HalfEdge edge, int planeIndexA, int planeIndexB)
                {
                    Edge = edge;
                    PlaneIndices[0] = planeIndexA;
                    PlaneIndices[1] = planeIndexB;
                }
            }

            private sealed class PointIntersection
            {
                public readonly List<EdgeIntersection> Edges = new List<EdgeIntersection>();
                public readonly HashSet<int> PlaneIndices = new HashSet<int>();
                public readonly int VertexIndex;

                public PointIntersection(int vertexIndex, List<int> planes)
                {
                    VertexIndex = vertexIndex;
                    foreach (var plane in planes)
                        PlaneIndices.Add(plane);
                }
            }

            // Reconstructs the convex half-edge mesh as the intersection of the planes' half-spaces.
            private static Brush CreateFromPlanes(Plane[] planes)
            {
                var pointIntersections = new List<PointIntersection>(planes.Length * planes.Length);
                var intersectingPlanes = new List<int>();
                var vertices = new List<Float3>();
                var edges = new List<HalfEdge>();

                // Find all points where 3 (or more) planes meet and which lie inside every other plane.
                for (int planeIndex1 = 0; planeIndex1 < planes.Length - 2; planeIndex1++)
                {
                    for (int planeIndex2 = planeIndex1 + 1; planeIndex2 < planes.Length - 1; planeIndex2++)
                    {
                        for (int planeIndex3 = planeIndex2 + 1; planeIndex3 < planes.Length; planeIndex3++)
                        {
                            Float3 vertex = Plane.Intersection(planes[planeIndex1], planes[planeIndex2], planes[planeIndex3]);

                            if (float.IsNaN(vertex.X) || float.IsNaN(vertex.Y) || float.IsNaN(vertex.Z) ||
                                float.IsInfinity(vertex.X) || float.IsInfinity(vertex.Y) || float.IsInfinity(vertex.Z))
                                continue;

                            intersectingPlanes.Clear();
                            intersectingPlanes.Add(planeIndex1);
                            intersectingPlanes.Add(planeIndex2);
                            intersectingPlanes.Add(planeIndex3);

                            bool valid = true;
                            for (int planeIndex4 = 0; planeIndex4 < planes.Length; planeIndex4++)
                            {
                                if (planeIndex4 == planeIndex1 || planeIndex4 == planeIndex2 || planeIndex4 == planeIndex3)
                                    continue;

                                Side side = OnSide(planes[planeIndex4], vertex);
                                if (side == Side.On)
                                {
                                    if (planeIndex4 < planeIndex3)
                                    {
                                        // This vertex was already generated by an earlier plane triple.
                                        valid = false;
                                        break;
                                    }
                                    intersectingPlanes.Add(planeIndex4);
                                }
                                else if (side == Side.Outside)
                                {
                                    valid = false; // Intersection is outside the brush.
                                    break;
                                }
                            }
                            if (!valid)
                                continue;

                            int vertexIndex = vertices.Count;
                            vertices.Add(vertex);
                            pointIntersections.Add(new PointIntersection(vertexIndex, intersectingPlanes));
                        }
                    }
                }

                // Build the intersection edges, each formed by a pair of planes shared by two points.
                var foundPlanes = new int[2];
                for (int i = 0; i < pointIntersections.Count; i++)
                {
                    var pointA = pointIntersections[i];
                    for (int j = i + 1; j < pointIntersections.Count; j++)
                    {
                        var pointB = pointIntersections[j];

                        int foundPlaneIndex = 0;
                        foreach (var currentPlaneIndex in pointA.PlaneIndices)
                        {
                            if (!pointB.PlaneIndices.Contains(currentPlaneIndex))
                                continue;

                            foundPlanes[foundPlaneIndex] = currentPlaneIndex;
                            foundPlaneIndex++;
                            if (foundPlaneIndex == 2)
                                break;
                        }

                        if (foundPlaneIndex < 2)
                            continue;

                        var halfEdgeA = new HalfEdge();
                        int halfEdgeAIndex = edges.Count;
                        edges.Add(halfEdgeA);

                        var halfEdgeB = new HalfEdge();
                        int halfEdgeBIndex = edges.Count;
                        edges.Add(halfEdgeB);

                        halfEdgeA.Twin = halfEdgeBIndex;
                        halfEdgeB.Twin = halfEdgeAIndex;

                        halfEdgeA.Vertex = pointA.VertexIndex;
                        halfEdgeB.Vertex = pointB.VertexIndex;

                        pointA.Edges.Add(new EdgeIntersection(halfEdgeA, foundPlanes[0], foundPlanes[1]));
                        pointB.Edges.Add(new EdgeIntersection(halfEdgeB, foundPlanes[0], foundPlanes[1]));
                    }
                }

                var polygons = new List<Polygon>(planes.Length);
                for (int i = 0; i < planes.Length; i++)
                    polygons.Add(new Polygon { PlaneIndex = i });

                // Link the half-edges around each polygon by ordering the edges meeting at every point.
                for (int i = pointIntersections.Count - 1; i >= 0; i--)
                {
                    var pointEdges = pointIntersections[i].Edges;

                    // A valid corner needs at least 3 edges meeting; otherwise the point is spurious.
                    if (pointEdges.Count <= 2)
                        continue;

                    for (int j = 0; j < pointEdges.Count - 1; j++)
                    {
                        var edge1 = pointEdges[j];
                        for (int k = j + 1; k < pointEdges.Count; k++)
                        {
                            var edge2 = pointEdges[k];

                            int planeIndex1, planeIndex2;
                            if (edge1.PlaneIndices[0] == edge2.PlaneIndices[0]) { planeIndex1 = 0; planeIndex2 = 0; }
                            else if (edge1.PlaneIndices[0] == edge2.PlaneIndices[1]) { planeIndex1 = 0; planeIndex2 = 1; }
                            else if (edge1.PlaneIndices[1] == edge2.PlaneIndices[0]) { planeIndex1 = 1; planeIndex2 = 0; }
                            else if (edge1.PlaneIndices[1] == edge2.PlaneIndices[1]) { planeIndex1 = 1; planeIndex2 = 1; }
                            else continue;

                            var sharedPlane = planes[edge1.PlaneIndices[planeIndex1]];
                            var edge1Plane = planes[edge1.PlaneIndices[1 - planeIndex1]];
                            var edge2Plane = planes[edge2.PlaneIndices[1 - planeIndex2]];

                            Float3 direction = Float3.Cross(sharedPlane.Normal, edge1Plane.Normal);

                            HalfEdge ingoing;
                            int outgoingIndex;
                            if (Float3.Dot(direction, edge2Plane.Normal) < 0)
                            {
                                ingoing = edge2.Edge;
                                outgoingIndex = edge1.Edge.Twin;
                            }
                            else
                            {
                                ingoing = edge1.Edge;
                                outgoingIndex = edge2.Edge.Twin;
                            }
                            var outgoing = edges[outgoingIndex];

                            // Link out-going to in-going; the half-edges form the circular polygon loop.
                            ingoing.Next = outgoingIndex;

                            int polygonIndex = edge1.PlaneIndices[planeIndex1];
                            ingoing.Polygon = polygonIndex;
                            outgoing.Polygon = polygonIndex;
                            polygons[polygonIndex].FirstEdge = outgoingIndex;
                        }
                    }
                }

                var brush = new Brush(planes, vertices, edges, polygons);
                foreach (var polygon in polygons)
                    brush.RecomputeBounds(polygon);
                brush.RecomputeBounds();
                return brush;
            }

            #endregion

            #region Source resolution

            // Picks the source face on a plane that best contains a point, returning its mean-value weights.
            public bool ResolveCorner(int planeIndex, Float3 point, out SourceFace source, out float[] weights)
            {
                var candidates = PlaneFaces[planeIndex];
                source = null!;
                weights = Array.Empty<float>();
                if (candidates.Count == 0)
                    return false;

                float bestScore = float.NegativeInfinity;
                foreach (var candidate in candidates)
                {
                    var w = new float[candidate.Corners.Length];
                    if (!Intersection.PolygonMeanValueWeights(point, candidate.Corners, w))
                        continue;

                    float minWeight = float.PositiveInfinity;
                    foreach (var value in w)
                        if (value < minWeight) minWeight = value;

                    if (minWeight > bestScore)
                    {
                        bestScore = minWeight;
                        source = candidate;
                        weights = w;
                    }
                }

                return source != null;
            }

            public bool ResolveFace(int planeIndex, Float3 point, out SourceFace source)
                => ResolveCorner(planeIndex, point, out source, out _);

            // Returns the source boundary edge an output edge lies on, or null if it's a cut edge.
            public GeometryData.Edge? ResolveEdge(int planeIndex, Float3 pa, Float3 pb, out SourceFace? source)
            {
                source = null;
                if (!ResolveFace(planeIndex, (pa + pb) * 0.5f, out var fi))
                    return null;
                source = fi;

                int n = fi.Corners.Length;
                for (int k = 0; k < n; k++)
                {
                    Float3 v0 = fi.Corners[k];
                    Float3 v1 = fi.Corners[(k + 1) % n];
                    if (Intersection.DistanceSqPointToLineSegment(v0, v1, pa) < WeldTolerance * WeldTolerance &&
                        Intersection.DistanceSqPointToLineSegment(v0, v1, pb) < WeldTolerance * WeldTolerance)
                    {
                        return fi.Loops[k]?.Edge;
                    }
                }
                return null;
            }

            #endregion

            #region Bounds

            private void RecomputeBounds()
            {
                bool first = true;
                foreach (var polygon in Polygons)
                {
                    if (polygon.FirstEdge == -1)
                        continue;
                    if (first)
                    {
                        BoundsMin = polygon.BoundsMin;
                        BoundsMax = polygon.BoundsMax;
                        first = false;
                    }
                    else
                    {
                        BoundsMin = Maths.Min(BoundsMin, polygon.BoundsMin);
                        BoundsMax = Maths.Max(BoundsMax, polygon.BoundsMax);
                    }
                }
                if (first)
                    BoundsMin = BoundsMax = Float3.Zero;
            }

            public void RecomputeBounds(Polygon polygon)
            {
                if (polygon.FirstEdge == -1)
                {
                    polygon.BoundsMin = polygon.BoundsMax = Float3.Zero;
                    return;
                }

                int start = polygon.FirstEdge;
                int current = start;
                var min = Vertices[Edges[current].Vertex];
                var max = min;
                int guard = 0;
                do
                {
                    Float3 v = Vertices[Edges[current].Vertex];
                    min = Maths.Min(min, v);
                    max = Maths.Max(max, v);
                    current = Edges[current].Next;
                } while (current != start && ++guard < Edges.Count);

                polygon.BoundsMin = min;
                polygon.BoundsMax = max;
            }

            #endregion

            #region Ring traversal

            public void CollectRing(Polygon polygon, List<Float3> output)
            {
                int start = polygon.FirstEdge;
                int current = start;
                int guard = 0;
                do
                {
                    output.Add(Vertices[Edges[current].Vertex]);
                    current = Edges[current].Next;
                } while (current != start && ++guard < Edges.Count);
            }

            #endregion

            #region Categorization

            /// <summary>
            /// Splits and categorizes all of this brush's polygons against another convex brush.
            /// Polygons that straddle the other solid are split; the outside pieces are emitted into
            /// <paramref name="outside"/> and the remaining inside piece keeps being clipped.
            /// </summary>
            public void CategorizeAgainst(Brush cutting,
                out List<Polygon> inside, out List<Polygon> aligned,
                out List<Polygon> revAligned, out List<Polygon> outside)
            {
                inside = new List<Polygon>();
                aligned = new List<Polygon>();
                revAligned = new List<Polygon>();
                outside = new List<Polygon>();

                // Snapshot count: splitting appends outside pieces past this point and they're already routed.
                int count = Polygons.Count;
                for (int i = count - 1; i >= 0; i--)
                {
                    var inputPolygon = Polygons[i];
                    if (inputPolygon.FirstEdge == -1)
                        continue;

                    var finalResult = SplitResult.CompletelyInside;

                    if (Overlaps(cutting.BoundsMin, cutting.BoundsMax, inputPolygon.BoundsMin, inputPolygon.BoundsMax))
                    {
                        for (int otherIndex = 0; otherIndex < cutting.Planes.Length; otherIndex++)
                        {
                            var cuttingPlane = cutting.Planes[otherIndex];

                            Side side = OnSide(cuttingPlane, inputPolygon.BoundsMin, inputPolygon.BoundsMax);
                            if (side == Side.Outside)
                            {
                                finalResult = SplitResult.CompletelyOutside;
                                break;
                            }
                            if (side == Side.Inside)
                                continue;

                            var intermediateResult = PolygonSplit(cuttingPlane, inputPolygon, out var outsidePolygon);

                            if (intermediateResult == SplitResult.CompletelyOutside)
                            {
                                finalResult = SplitResult.CompletelyOutside;
                                break;
                            }
                            if (intermediateResult == SplitResult.Split)
                            {
                                outside.Add(outsidePolygon!);
                            }
                            else if (intermediateResult != SplitResult.CompletelyInside)
                            {
                                finalResult = intermediateResult;
                            }
                        }
                    }
                    else
                    {
                        finalResult = SplitResult.CompletelyOutside;
                    }

                    switch (finalResult)
                    {
                        case SplitResult.CompletelyInside: inside.Add(inputPolygon); break;
                        case SplitResult.CompletelyOutside: outside.Add(inputPolygon); break;
                        case SplitResult.PlaneAligned: aligned.Add(inputPolygon); break;
                        case SplitResult.PlaneOppositeAligned: revAligned.Add(inputPolygon); break;
                    }
                }
            }

            #endregion

            #region Polygon splitting

            // Splits a half-edge in two, inserting a new vertex. Returns the index of the new edge.
            private int EdgeSplit(HalfEdge thisEdge, Float3 vertex)
            {
                int thisTwinIndex = thisEdge.Twin;
                var thisTwin = Edges[thisTwinIndex];
                int thisEdgeIndex = thisTwin.Twin;

                var newEdge = new HalfEdge();
                int newEdgeIndex = Edges.Count;
                var newTwin = new HalfEdge();
                int newTwinIndex = newEdgeIndex + 1;
                int vertexIndex = Vertices.Count;

                newEdge.Polygon = thisEdge.Polygon;
                newTwin.Polygon = thisTwin.Polygon;

                newEdge.Vertex = thisEdge.Vertex;
                thisEdge.Vertex = vertexIndex;

                newTwin.Vertex = thisTwin.Vertex;
                thisTwin.Vertex = vertexIndex;

                newEdge.Next = thisEdge.Next;
                thisEdge.Next = newEdgeIndex;

                newTwin.Next = thisTwin.Next;
                thisTwin.Next = newTwinIndex;

                newEdge.Twin = thisTwinIndex;
                thisTwin.Twin = newEdgeIndex;

                thisEdge.Twin = newTwinIndex;
                newTwin.Twin = thisEdgeIndex;

                Edges.Add(newEdge);
                Edges.Add(newTwin);
                Vertices.Add(vertex);
                return newEdgeIndex;
            }

            // Splits a polygon by a plane into an inside remainder (kept in inputPolygon) and an outside
            // piece, or classifies it as completely inside/outside/aligned with the plane.
            private SplitResult PolygonSplit(Plane cuttingPlane, Polygon inputPolygon, out Polygon? outsidePolygon)
            {
                outsidePolygon = null;

                HalfEdge prev = Edges[inputPolygon.FirstEdge];
                HalfEdge current = Edges[prev.Next];
                HalfEdge next = Edges[current.Next];
                HalfEdge last = next;
                HalfEdge? enterEdge = null;
                HalfEdge? exitEdge = null;

                Float3 prevVertex = Vertices[prev.Vertex];
                float prevDistance = Distance(cuttingPlane, prevVertex);
                Side prevSide = OnSide(prevDistance);

                Float3 currentVertex = Vertices[current.Vertex];
                float currentDistance = Distance(cuttingPlane, currentVertex);
                Side currentSide = OnSide(currentDistance);

                do
                {
                    Float3 nextVertex = Vertices[next.Vertex];
                    float nextDistance = Distance(cuttingPlane, nextVertex);
                    Side nextSide = OnSide(nextDistance);

                    if (prevSide != currentSide)
                    {
                        if (currentSide != Side.On)
                        {
                            if (prevSide != Side.On)
                            {
                                Float3 newVertex = EdgePlaneIntersection(prevVertex, currentVertex, prevDistance, currentDistance);
                                EdgeSplit(current, newVertex);

                                if (prevSide == Side.Inside)
                                    exitEdge = current;
                                else if (prevSide == Side.Outside)
                                    enterEdge = current;

                                prevDistance = 0;
                                prev = Edges[prev.Next];
                                prevSide = Side.On;

                                if (exitEdge != null && enterEdge != null)
                                    break;

                                current = Edges[prev.Next];
                                currentVertex = Vertices[current.Vertex];

                                next = Edges[current.Next];
                                nextVertex = Vertices[next.Vertex];
                            }
                        }
                        else
                        {
                            if (prevSide == Side.On || nextSide == Side.On || prevSide == nextSide)
                            {
                                if (prevSide == Side.Inside || nextSide == Side.Inside)
                                {
                                    prevSide = Side.Inside;
                                    enterEdge = exitEdge = null;
                                    break;
                                }
                                if (prevSide == Side.Outside || nextSide == Side.Outside)
                                {
                                    prevSide = Side.Outside;
                                    enterEdge = exitEdge = null;
                                    break;
                                }
                            }
                            else
                            {
                                if (prevSide == Side.Inside)
                                {
                                    exitEdge = current;
                                    if (enterEdge != null)
                                        break;
                                }
                                else
                                {
                                    enterEdge = current;
                                    if (exitEdge != null)
                                        break;
                                }
                            }
                        }
                    }

                    prev = current;
                    current = next;
                    next = Edges[next.Next];

                    prevDistance = currentDistance;
                    currentDistance = nextDistance;
                    prevSide = currentSide;
                    currentSide = nextSide;
                    prevVertex = currentVertex;
                    currentVertex = nextVertex;
                } while (next != last);

                if (enterEdge != null && exitEdge != null)
                {
                    outsidePolygon = new Polygon();
                    int outsidePolygonIndex = Polygons.Count;
                    Polygons.Add(outsidePolygon);

                    var outsideEdge = new HalfEdge();
                    int outsideEdgeIndex = Edges.Count;
                    var insideEdge = new HalfEdge();
                    int insideEdgeIndex = outsideEdgeIndex + 1;

                    outsideEdge.Twin = insideEdgeIndex;
                    insideEdge.Twin = outsideEdgeIndex;

                    outsideEdge.Polygon = outsidePolygonIndex;

                    outsideEdge.Vertex = exitEdge.Vertex;
                    insideEdge.Vertex = enterEdge.Vertex;

                    outsideEdge.Next = exitEdge.Next;
                    insideEdge.Next = enterEdge.Next;

                    exitEdge.Next = insideEdgeIndex;
                    enterEdge.Next = outsideEdgeIndex;

                    outsidePolygon.FirstEdge = outsideEdgeIndex;
                    inputPolygon.FirstEdge = insideEdgeIndex;

                    outsidePolygon.PlaneIndex = inputPolygon.PlaneIndex;

                    Edges.Add(outsideEdge);
                    Edges.Add(insideEdge);

                    // Reassign the outside loop's polygon references.
                    int start = outsidePolygon.FirstEdge;
                    int iterator = start;
                    int guard = 0;
                    do
                    {
                        Edges[iterator].Polygon = outsidePolygonIndex;
                        iterator = Edges[iterator].Next;
                    } while (iterator != start && ++guard < Edges.Count);

                    RecomputeBounds(outsidePolygon);
                    RecomputeBounds(inputPolygon);
                    return SplitResult.Split;
                }

                switch (prevSide)
                {
                    case Side.Inside: return SplitResult.CompletelyInside;
                    case Side.Outside: return SplitResult.CompletelyOutside;
                    default:
                        var polygonPlane = Planes[inputPolygon.PlaneIndex];
                        return Float3.Dot(polygonPlane.Normal, cuttingPlane.Normal) > 0
                            ? SplitResult.PlaneAligned
                            : SplitResult.PlaneOppositeAligned;
                }
            }

            #endregion
        }

        #endregion

        #region Plane helpers

        private static float Distance(Plane plane, Float3 point) => Float3.Dot(plane.Normal, point) - plane.D;

        private static Side OnSide(float distance)
        {
            if (distance > DistanceEpsilon) return Side.Outside;
            if (distance < -DistanceEpsilon) return Side.Inside;
            return Side.On;
        }

        private static Side OnSide(Plane plane, Float3 point) => OnSide(Distance(plane, point));

        // Classifies an AABB against a plane using its p-vertex / n-vertex; only two corners are needed.
        private static Side OnSide(Plane plane, Float3 min, Float3 max)
        {
            Float3 n = plane.Normal;

            float bx = n.X <= 0 ? min.X : max.X;
            float by = n.Y <= 0 ? min.Y : max.Y;
            float bz = n.Z <= 0 ? min.Z : max.Z;
            if (OnSide(n.X * bx + n.Y * by + n.Z * bz - plane.D) == Side.Inside)
                return Side.Inside;

            float fx = n.X >= 0 ? min.X : max.X;
            float fy = n.Y >= 0 ? min.Y : max.Y;
            float fz = n.Z >= 0 ? min.Z : max.Z;
            if (OnSide(n.X * fx + n.Y * fy + n.Z * fz - plane.D) == Side.Outside)
                return Side.Outside;

            return Side.On;
        }

        // Point where the segment start->end crosses the plane, given the signed distance at each end.
        private static Float3 EdgePlaneIntersection(Float3 start, Float3 end, float startDistance, float endDistance)
        {
            Float3 v = end - start;
            float length = endDistance - startDistance;
            float delta = endDistance / length;
            return end - delta * v;
        }

        private static bool Overlaps(Float3 minA, Float3 maxA, Float3 minB, Float3 maxB)
        {
            if (maxA.X < minB.X || minA.X > maxB.X) return false;
            if (maxA.Y < minB.Y || minA.Y > maxB.Y) return false;
            if (maxA.Z < minB.Z || minA.Z > maxB.Z) return false;
            return true;
        }

        #endregion
    }
}
