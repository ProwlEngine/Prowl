// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector.Spatial;

namespace Prowl.Vector.Geometry
{
    /// <summary>How a brush combines with the brushes before it in the scene.</summary>
    public enum CSGOperation
    {
        /// <summary>Adds the brush's volume to the solid.</summary>
        Additive,
        /// <summary>Removes the brush's volume from the solid.</summary>
        Subtractive
    }

    /// <summary>
    /// An opaque handle to a brush placed in a <see cref="CSGScene"/>. Keep it to move, re-order,
    /// change, or remove the brush later. Handles become invalid once their brush is removed.
    /// </summary>
    public sealed class CSGBrushHandle
    {
        internal CSGScene.SceneBrush Brush;
        internal CSGBrushHandle(CSGScene.SceneBrush brush) => Brush = brush;

        /// <summary>True while the brush is still part of its scene.</summary>
        public bool IsValid => Brush is { Attached: true };

        /// <summary>The brush's current operation.</summary>
        public CSGOperation Operation => Brush.Op;

        /// <summary>The brush's current world transform.</summary>
        public Float4x4 Transform => Brush.Transform;
    }

    /// <summary>
    /// An incremental N-ary CSG scene built from convex brushes (after Sander van Rossen's Realtime-CSG
    /// model). Brushes form an ordered list; later brushes override earlier ones where they overlap, so
    /// the final solid is "the last brush whose volume contains a point decides whether it is solid".
    ///
    /// Each brush only interacts with brushes it spatially overlaps, so adding, moving, re-ordering, or
    /// removing one brush re-evaluates only that brush and the ones it touches; everything else keeps its
    /// cached surface. Overlap queries go through a <see cref="Bvh{T}"/> broadphase so large scenes stay
    /// cheap to edit.
    ///
    /// Every brush MUST be a convex closed solid. Build a concave shape from several brushes. Vertex,
    /// edge, loop, and face attributes are carried through; where two brushes define an attribute of the
    /// same name, the earlier brush's schema wins.
    ///
    /// Each face is partitioned exactly (no gaps or overlaps). By default a T-junction welding pass makes
    /// the output watertight where independent cuts subdivide a shared face; disable it via
    /// <see cref="WeldTJunctions"/> for raw, faster output.
    /// </summary>
    public sealed class CSGScene
    {
        private const float DistanceEpsilon = 0.0001f;
        private const float NormalEpsilon = 1.0f / 65535.0f;
        private const float WeldTolerance = 0.0001f;

        private readonly List<SceneBrush> _brushes = new();
        private readonly List<AABB> _pendingDirtyRegions = new();
        private int _nextId;

        /// <summary>Whether <see cref="Build"/> runs a T-junction welding pass for watertight output. Default true.</summary>
        public bool WeldTJunctions { get; set; } = true;

        /// <summary>Number of brushes in the scene.</summary>
        public int Count => _brushes.Count;

        /// <summary>Number of brushes whose surface was recomputed during the most recent <see cref="Build"/>.</summary>
        public int LastRebuildCount { get; private set; }

        #region Public editing API

        /// <summary>Adds a convex brush to the end of the scene and returns its handle.</summary>
        public CSGBrushHandle Add(GeometryData convexShape, CSGOperation operation = CSGOperation.Additive)
            => Add(convexShape, operation, Float4x4.Identity);

        /// <summary>Adds a convex brush with a world transform to the end of the scene.</summary>
        public CSGBrushHandle Add(GeometryData convexShape, CSGOperation operation, Float4x4 transform)
            => Insert(_brushes.Count, convexShape, operation, transform);

        /// <summary>Inserts a convex brush at a specific order index.</summary>
        public CSGBrushHandle Insert(int index, GeometryData convexShape, CSGOperation operation, Float4x4 transform)
        {
            if (convexShape == null) throw new ArgumentNullException(nameof(convexShape));
            index = Maths.Clamp(index, 0, _brushes.Count);

            var brush = new SceneBrush
            {
                Id = _nextId++,
                Shape = convexShape,
                Op = operation,
                Transform = transform,
                Attached = true
            };
            RecomputeWorld(brush);

            _brushes.Insert(index, brush);
            Reindex();

            brush.Dirty = true;
            _pendingDirtyRegions.Add(BrushBox(brush));
            return new CSGBrushHandle(brush);
        }

        /// <summary>Removes a brush from the scene. Its handle becomes invalid.</summary>
        public void Remove(CSGBrushHandle handle)
        {
            var brush = Validate(handle);
            _pendingDirtyRegions.Add(BrushBox(brush)); // dirty whatever it used to touch
            brush.Attached = false;
            _brushes.Remove(brush);
            Reindex();
        }

        /// <summary>Moves/transforms a brush. Only the brush and the brushes it touches are re-evaluated.</summary>
        public void SetTransform(CSGBrushHandle handle, Float4x4 transform)
        {
            var brush = Validate(handle);
            _pendingDirtyRegions.Add(BrushBox(brush)); // brushes it currently touches
            brush.Transform = transform;
            RecomputeWorld(brush);
            _pendingDirtyRegions.Add(BrushBox(brush)); // brushes it now touches
            brush.Dirty = true;
        }

        /// <summary>Changes a brush's additive/subtractive operation.</summary>
        public void SetOperation(CSGBrushHandle handle, CSGOperation operation)
        {
            var brush = Validate(handle);
            if (brush.Op == operation) return;
            brush.Op = operation;
            brush.Dirty = true;
            _pendingDirtyRegions.Add(BrushBox(brush));
        }

        /// <summary>Re-orders a brush to a new index, preserving everyone else's relative order.</summary>
        public void SetOrder(CSGBrushHandle handle, int index)
        {
            var brush = Validate(handle);
            index = Maths.Clamp(index, 0, _brushes.Count - 1);
            if (_brushes.IndexOf(brush) == index) return;

            _brushes.Remove(brush);
            _brushes.Insert(index, brush);
            Reindex();
            brush.Dirty = true;
            _pendingDirtyRegions.Add(BrushBox(brush));
        }

        /// <summary>
        /// Evaluates the scene into a single mesh. Only brushes flagged dirty since the last call are
        /// re-evaluated; the rest reuse their cached surface. The merged mesh is rebuilt each call.
        /// </summary>
        public GeometryData Build()
        {
            var bvh = new Bvh<SceneBrush>(_brushes, BrushBox, new BvhBuildSettings { Quality = BvhBuildQuality.Fast, MaxLeafSize = 4 });

            // Propagate pending dirty regions to whatever brushes now occupy them.
            if (_pendingDirtyRegions.Count > 0)
            {
                var hits = new List<SceneBrush>();
                foreach (var region in _pendingDirtyRegions)
                {
                    hits.Clear();
                    bvh.QueryAABB(region, hits);
                    foreach (var b in hits) b.Dirty = true;
                }
                _pendingDirtyRegions.Clear();
            }

            int recomputed = 0;
            var neighborBuffer = new List<SceneBrush>();
            foreach (var brush in _brushes)
            {
                if (!brush.Dirty) continue;
                Recompute(brush, bvh, neighborBuffer);
                brush.Dirty = false;
                recomputed++;
            }
            LastRebuildCount = recomputed;

            return Assemble();
        }

        #endregion

        #region Bookkeeping

        private SceneBrush Validate(CSGBrushHandle handle)
        {
            if (handle == null || handle.Brush == null || !handle.Brush.Attached)
                throw new InvalidOperationException("The brush handle is not valid for this scene.");
            return handle.Brush;
        }

        private void Reindex()
        {
            for (int i = 0; i < _brushes.Count; i++)
                _brushes[i].Order = i;
        }

        private static AABB BrushBox(SceneBrush brush) => brush.Bounds;

        #endregion

        #region World-space derivation

        private static void RecomputeWorld(SceneBrush brush)
        {
            brush.Faces.Clear();

            AABB brushBounds = default;
            bool firstBrush = true;

            foreach (var face in brush.Shape.Faces)
            {
                if (face.VertCount < 3) continue;

                var verts = face.NeighborVertices();
                int n = verts.Count;
                var world = new Float3[n];
                var srcVerts = new GeometryData.Vertex[n];
                var srcLoops = new GeometryData.Loop?[n];

                for (int i = 0; i < n; i++)
                {
                    srcVerts[i] = verts[i];
                    srcLoops[i] = face.GetLoop(verts[i]);
                    world[i] = Float4x4.TransformPoint(verts[i].Point, brush.Transform);
                }

                // Plane and normal from the transformed winding (Newell's method).
                Float3 normal = Float3.Zero;
                Float3 centroid = Float3.Zero;
                for (int i = 0; i < n; i++)
                {
                    Float3 cur = world[i];
                    Float3 nxt = world[(i + 1) % n];
                    normal.X += (cur.Y - nxt.Y) * (cur.Z + nxt.Z);
                    normal.Y += (cur.Z - nxt.Z) * (cur.X + nxt.X);
                    normal.Z += (cur.X - nxt.X) * (cur.Y + nxt.Y);
                    centroid += cur;
                }
                if (Float3.LengthSquared(normal) < DistanceEpsilon * DistanceEpsilon) continue;

                var plane = Plane.FromNormalAndPoint(Float3.Normalize(normal), centroid / n);

                Float3 faceMin = world[0], faceMax = world[0];
                for (int i = 1; i < n; i++)
                {
                    faceMin = Maths.Min(faceMin, world[i]);
                    faceMax = Maths.Max(faceMax, world[i]);
                }
                AABB faceBounds = new AABB(faceMin, faceMax);

                brush.Faces.Add(new FaceData
                {
                    World = world,
                    Plane = plane,
                    Source = face,
                    SrcVerts = srcVerts,
                    SrcLoops = srcLoops,
                    Bounds = faceBounds
                });

                if (firstBrush) { brushBounds = faceBounds; firstBrush = false; }
                else brushBounds = brushBounds.Encapsulating(faceBounds);
            }

            brush.Bounds = brushBounds;
        }

        #endregion

        #region Per-brush surface computation

        private enum Relation { Interior, Aligned, Opposite }

        private readonly struct Rel
        {
            public readonly int Order;
            public readonly CSGOperation Type;
            public readonly Relation Relation;
            public Rel(int order, CSGOperation type, Relation relation) { Order = order; Type = type; Relation = relation; }
        }

        private sealed class Fragment
        {
            public List<Float3> Poly;
            public List<Rel> Rels;
            public Fragment(List<Float3> poly, List<Rel> rels) { Poly = poly; Rels = rels; }
        }

        private void Recompute(SceneBrush self, Bvh<SceneBrush> bvh, List<SceneBrush> neighborBuffer)
        {
            self.Output.Clear();

            neighborBuffer.Clear();
            bvh.QueryAABB(BrushBox(self), neighborBuffer);

            var work = new List<Fragment>();
            var next = new List<Fragment>();

            foreach (var face in self.Faces)
            {
                work.Clear();
                work.Add(new Fragment(new List<Float3>(face.World), new List<Rel>()));

                foreach (var b in neighborBuffer)
                {
                    if (b == self) continue;
                    if (!face.Bounds.Intersects(b.Bounds))
                        continue;

                    next.Clear();
                    foreach (var frag in work)
                        SplitByBrush(frag, b, face.Plane, next);

                    (work, next) = (next, work);
                }

                foreach (var frag in work)
                {
                    if (frag.Poly.Count < 3) continue;
                    if (Visible(frag.Rels, self, out bool flip))
                        self.Output.Add(new OutFrag { Poly = frag.Poly.ToArray(), Source = face, Flip = flip });
                }
            }
        }

        // Splits a face fragment by a neighbour brush into the piece inside/on the brush (tagged with the
        // relation) and the pieces outside it (untagged). All pieces are appended to <paramref name="results"/>.
        private static void SplitByBrush(Fragment frag, SceneBrush b, Plane facePlane, List<Fragment> results)
        {
            // Detect a brush face coplanar with this face (a shared surface).
            int coincident = -1;
            Relation coincRelation = Relation.Interior;
            for (int i = 0; i < b.Faces.Count; i++)
            {
                Plane bp = b.Faces[i].Plane;
                float dn = Float3.Dot(bp.Normal, facePlane.Normal);
                if (dn > 1.0f - NormalEpsilon && Maths.Abs(bp.D - facePlane.D) < DistanceEpsilon)
                {
                    coincident = i; coincRelation = Relation.Aligned; break;
                }
                if (dn < -(1.0f - NormalEpsilon) && Maths.Abs(bp.D + facePlane.D) < DistanceEpsilon)
                {
                    coincident = i; coincRelation = Relation.Opposite; break;
                }
            }

            var inside = new List<Float3>(frag.Poly);
            var back = new List<Float3>();
            var front = new List<Float3>();

            for (int i = 0; i < b.Faces.Count; i++)
            {
                if (i == coincident) continue;

                ClipByPlane(inside, b.Faces[i].Plane, back, front);

                if (front.Count >= 3)
                    results.Add(new Fragment(new List<Float3>(front), frag.Rels));

                if (back.Count < 3) { inside = null!; break; }
                inside = new List<Float3>(back);
            }

            if (inside != null && inside.Count >= 3)
            {
                var rels = new List<Rel>(frag.Rels) { new Rel(b.Order, b.Op, coincident >= 0 ? coincRelation : Relation.Interior) };
                results.Add(new Fragment(inside, rels));
            }
        }

        // Decides whether a fully-classified fragment of brush 'self' lies on the final solid's surface.
        private static bool Visible(List<Rel> rels, SceneBrush self, out bool flip)
        {
            flip = false;
            int selfOrder = self.Order;

            // A later brush whose face is coincident-and-aligned owns this shared surface.
            foreach (var r in rels)
                if (r.Relation == Relation.Aligned && r.Order > selfOrder)
                    return false;

            // "inner" = volume just behind the face (self's interior side); "outer" = just in front.
            int innerOrder = selfOrder;
            CSGOperation innerType = self.Op;
            int outerOrder = int.MinValue;
            CSGOperation outerType = default;
            bool hasOuter = false;

            foreach (var r in rels)
            {
                switch (r.Relation)
                {
                    case Relation.Interior: // strictly inside: contains both sides
                        if (r.Order > innerOrder) { innerOrder = r.Order; innerType = r.Type; }
                        if (r.Order > outerOrder) { outerOrder = r.Order; outerType = r.Type; hasOuter = true; }
                        break;
                    case Relation.Aligned: // coincident, same facing: contains the inner side
                        if (r.Order > innerOrder) { innerOrder = r.Order; innerType = r.Type; }
                        break;
                    case Relation.Opposite: // coincident, opposite facing: contains the outer side
                        if (r.Order > outerOrder) { outerOrder = r.Order; outerType = r.Type; hasOuter = true; }
                        break;
                }
            }

            bool innerSolid = innerType == CSGOperation.Additive;
            bool outerSolid = hasOuter && outerType == CSGOperation.Additive;

            if (innerSolid == outerSolid)
                return false; // both sides solid or both empty: not a boundary

            flip = !innerSolid && outerSolid; // solid is in front: face must point the other way
            return true;
        }

        // Splits a convex polygon by a plane into the part behind it (back, inside half-space) and in
        // front of it (front, outside half-space). Points on the plane go to both.
        private static void ClipByPlane(List<Float3> poly, Plane plane, List<Float3> back, List<Float3> front)
        {
            back.Clear();
            front.Clear();

            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Float3 a = poly[i];
                Float3 b = poly[(i + 1) % n];
                float da = Float3.Dot(plane.Normal, a) - plane.D;
                float db = Float3.Dot(plane.Normal, b) - plane.D;
                int sa = da > DistanceEpsilon ? 1 : da < -DistanceEpsilon ? -1 : 0;
                int sb = db > DistanceEpsilon ? 1 : db < -DistanceEpsilon ? -1 : 0;

                if (sa <= 0) back.Add(a);
                if (sa >= 0) front.Add(a);

                if ((sa > 0 && sb < 0) || (sa < 0 && sb > 0))
                {
                    float t = da / (da - db);
                    Float3 p = a + (b - a) * t;
                    back.Add(p);
                    front.Add(p);
                }
            }
        }

        #endregion

        #region Assembly + T-junction welding

        private sealed class EmitItem
        {
            public SceneBrush Brush = null!;
            public List<Float3> Ring = null!;
            public FaceData Source = null!;
            public bool Flip;
        }

        private GeometryData Assemble()
        {
            var result = new GeometryData();
            bool hasAttributes = RegisterAttributes(result);

            var items = new List<EmitItem>();
            foreach (var brush in _brushes)
                foreach (var frag in brush.Output)
                    items.Add(new EmitItem { Brush = brush, Ring = new List<Float3>(frag.Poly), Source = frag.Source, Flip = frag.Flip });

            if (WeldTJunctions && items.Count > 0)
                WeldTJunctionPass(items);

            var weld = new Dictionary<SnapKey, GeometryData.Vertex>();
            foreach (var item in items)
                EmitPolygon(result, weld, item, hasAttributes);

            return result;
        }

        // Inserts existing vertices that lie on the interior of another polygon's edge, removing the
        // T-junctions that arise when independent cuts subdivide a shared face.
        private void WeldTJunctionPass(List<EmitItem> items)
        {
            var seen = new HashSet<SnapKey>();
            var points = new List<Float3>();
            foreach (var item in items)
                foreach (var p in item.Ring)
                    if (seen.Add(new SnapKey(p, WeldTolerance)))
                        points.Add(p);

            var bvh = new Bvh<Float3>(points, p => new AABB(p, p).Expanded(WeldTolerance), BvhBuildSettings.Default);
            var candidates = new List<Float3>();

            foreach (var item in items)
                item.Ring = InsertCollinearPoints(item.Ring, bvh, candidates);
        }

        private static List<Float3> InsertCollinearPoints(List<Float3> ring, Bvh<Float3> points, List<Float3> candidates)
        {
            const float tol2 = WeldTolerance * WeldTolerance;
            int n = ring.Count;
            var output = new List<Float3>(n);
            var inserts = new List<(float t, Float3 point)>();

            for (int i = 0; i < n; i++)
            {
                Float3 a = ring[i];
                Float3 b = ring[(i + 1) % n];
                output.Add(a);

                Float3 ab = b - a;
                float abLen2 = Float3.Dot(ab, ab);
                if (abLen2 < tol2) continue;

                candidates.Clear();
                points.QueryAABB(new AABB(Maths.Min(a, b), Maths.Max(a, b)).Expanded(WeldTolerance), candidates);

                inserts.Clear();
                foreach (var c in candidates)
                {
                    if (Float3.LengthSquared(c - a) < tol2 || Float3.LengthSquared(c - b) < tol2) continue;
                    float t = Float3.Dot(c - a, ab) / abLen2;
                    if (t <= 0.0f || t >= 1.0f) continue;
                    Float3 closest = a + ab * t;
                    if (Float3.LengthSquared(c - closest) > tol2) continue;
                    inserts.Add((t, c));
                }

                if (inserts.Count == 0) continue;
                inserts.Sort((x, y) => x.t.CompareTo(y.t));

                Float3 last = a;
                foreach (var (_, point) in inserts)
                {
                    if (Float3.LengthSquared(point - last) < tol2) continue;
                    output.Add(point);
                    last = point;
                }
            }

            return output;
        }

        #endregion

        #region Emission + attribute carrying

        private void EmitPolygon(GeometryData result, Dictionary<SnapKey, GeometryData.Vertex> weld, EmitItem item, bool hasAttributes)
        {
            var ring = item.Ring;
            int count = ring.Count;
            var verts = new List<GeometryData.Vertex>(count);
            var positions = new List<Float3>(count);

            for (int idx = 0; idx < count; idx++)
            {
                Float3 point = item.Flip ? ring[count - 1 - idx] : ring[idx];
                var vertex = GetOrAddVertex(result, weld, point);
                if (verts.Count > 0 && verts[verts.Count - 1] == vertex) continue;
                verts.Add(vertex);
                positions.Add(point);
            }

            if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
            {
                verts.RemoveAt(verts.Count - 1);
                positions.RemoveAt(positions.Count - 1);
            }

            if (verts.Count < 3)
                return;

            var face = result.AddFace(verts.ToArray());
            if (face == null || !hasAttributes)
                return;

            var source = item.Source;
            var shape = item.Brush.Shape;

            foreach (var def in result.FaceAttributes)
            {
                if (shape.HasFaceAttribute(def.Name) && source.Source.Attributes.TryGetValue(def.Name, out var value))
                    face.Attributes[def.Name] = GeometryData.AttributeValue.Copy(value);
            }

            bool anyLoop = result.LoopAttributes.Count > 0;
            bool anyVertex = result.VertexAttributes.Count > 0;
            if (anyLoop || anyVertex)
            {
                var weights = new float[source.World.Length];
                for (int i = 0; i < verts.Count; i++)
                {
                    if (!Intersection.PolygonMeanValueWeights(positions[i], source.World, weights))
                        continue;

                    if (anyLoop)
                    {
                        var loop = face.GetLoop(verts[i]);
                        if (loop != null)
                        {
                            foreach (var def in result.LoopAttributes)
                            {
                                if (!shape.HasLoopAttribute(def.Name)) continue;
                                loop.Attributes[def.Name] = InterpolateLoop(def, source, weights);
                            }
                        }
                    }

                    if (anyVertex)
                    {
                        foreach (var def in result.VertexAttributes)
                        {
                            if (!shape.HasVertexAttribute(def.Name)) continue;
                            verts[i].Attributes[def.Name] = InterpolateVertex(def, source, weights);
                        }
                    }
                }
            }

            if (result.EdgeAttributes.Count > 0)
                CarryEdges(result, shape, source, verts, positions);
        }

        private static void CarryEdges(GeometryData result, GeometryData shape, FaceData source,
            List<GeometryData.Vertex> verts, List<Float3> positions)
        {
            int n = verts.Count;
            int sn = source.World.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                for (int k = 0; k < sn; k++)
                {
                    Float3 v0 = source.World[k];
                    Float3 v1 = source.World[(k + 1) % sn];
                    if (Intersection.DistanceSqPointToLineSegment(v0, v1, positions[i]) >= WeldTolerance * WeldTolerance) continue;
                    if (Intersection.DistanceSqPointToLineSegment(v0, v1, positions[j]) >= WeldTolerance * WeldTolerance) continue;

                    var sourceEdge = source.SrcLoops[k]?.Edge;
                    if (sourceEdge == null) break;

                    var edge = result.FindEdge(verts[i], verts[j]);
                    if (edge == null) break;

                    foreach (var def in result.EdgeAttributes)
                    {
                        if (shape.HasEdgeAttribute(def.Name) && sourceEdge.Attributes.TryGetValue(def.Name, out var value))
                            edge.Attributes[def.Name] = GeometryData.AttributeValue.Copy(value);
                    }
                    break;
                }
            }
        }

        private bool RegisterAttributes(GeometryData result)
        {
            foreach (var brush in _brushes)
            {
                var mesh = brush.Shape;
                foreach (var def in mesh.VertexAttributes) result.AddVertexAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                foreach (var def in mesh.EdgeAttributes) result.AddEdgeAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                foreach (var def in mesh.LoopAttributes) result.AddLoopAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
                foreach (var def in mesh.FaceAttributes) result.AddFaceAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
            }

            return result.VertexAttributes.Count > 0 || result.EdgeAttributes.Count > 0 ||
                   result.LoopAttributes.Count > 0 || result.FaceAttributes.Count > 0;
        }

        private static GeometryData.AttributeValue InterpolateLoop(GeometryData.AttributeDefinition def, FaceData source, float[] weights)
        {
            int n = source.World.Length;
            var perCorner = new GeometryData.AttributeValue?[n];
            for (int k = 0; k < n; k++)
                if (source.SrcLoops[k] is { } loop && loop.Attributes.TryGetValue(def.Name, out var value))
                    perCorner[k] = value;
            return Interpolate(def, perCorner, weights);
        }

        private static GeometryData.AttributeValue InterpolateVertex(GeometryData.AttributeDefinition def, FaceData source, float[] weights)
        {
            int n = source.World.Length;
            var perCorner = new GeometryData.AttributeValue?[n];
            for (int k = 0; k < n; k++)
                if (source.SrcVerts[k].Attributes.TryGetValue(def.Name, out var value))
                    perCorner[k] = value;
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

        private static GeometryData.Vertex GetOrAddVertex(GeometryData result,
            Dictionary<SnapKey, GeometryData.Vertex> weld, Float3 point)
        {
            var key = new SnapKey(point, WeldTolerance);
            if (weld.TryGetValue(key, out var existing))
                return existing;
            var vertex = result.AddVertex(point);
            weld[key] = vertex;
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

        #region State

        internal sealed class FaceData
        {
            public Float3[] World = Array.Empty<Float3>();
            public Plane Plane;
            public GeometryData.Face Source = null!;
            public GeometryData.Vertex[] SrcVerts = Array.Empty<GeometryData.Vertex>();
            public GeometryData.Loop?[] SrcLoops = Array.Empty<GeometryData.Loop?>();
            public AABB Bounds;
        }

        internal sealed class OutFrag
        {
            public Float3[] Poly = Array.Empty<Float3>();
            public FaceData Source = null!;
            public bool Flip;
        }

        internal sealed class SceneBrush
        {
            public int Id;
            public int Order;
            public GeometryData Shape = null!;
            public CSGOperation Op;
            public Float4x4 Transform;
            public bool Attached;
            public bool Dirty;

            public readonly List<FaceData> Faces = new();
            public AABB Bounds;
            public readonly List<OutFrag> Output = new();
        }

        #endregion
    }
}
