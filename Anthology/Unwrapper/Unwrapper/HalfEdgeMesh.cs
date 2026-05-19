using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// A connected half-edge graph. Owns vertices, half-edges (with cached length/seam/crease flags
/// plus per-corner UVs) and triangles. Supports connected-component splitting and topological cuts.
/// </summary>
internal sealed class HalfEdgeMesh
{
    public readonly List<Vertex> Vertices = new();
    public readonly List<HalfEdge> Edges = new();
    public readonly List<EdgeAttribute> EdgeAttributes = new();
    public readonly List<Double2> EdgeUVs = new();
    public readonly List<MeshFace> Triangles = new();
    public readonly List<FaceAttribute> FaceAttributes = new();

    /// <summary>Crease threshold cached so derived sub-meshes preserve it.</summary>
    public double CreaseThresholdDegrees;

    // O(1) accessors via the stamped Index field. Never reorder the underlying lists
    // without re-stamping or these will lie.
    public int IndexOf(Vertex v) => v.Index;
    public int IndexOf(MeshFace t) => t.Index;
    public int IndexOf(HalfEdge e) => e.Index;

    private Vertex Append(Vertex v) { v.Index = Vertices.Count; Vertices.Add(v); return v; }
    private MeshFace Append(MeshFace t) { t.Index = Triangles.Count; Triangles.Add(t); return t; }
    private HalfEdge Append(HalfEdge e) { e.Index = Edges.Count; Edges.Add(e); return e; }

    /// <summary>Optional progress sink — receives per-phase timing while <see cref="Build"/> runs.</summary>
    public System.Action<string>? ProgressSink;

    /// <summary>Build a new mesh from positions + indices, with optional per-corner UV hints.</summary>
    /// <param name="assumeManifold">
    /// Skip the per-vertex non-manifold detection walk. Pass <c>true</c> when the caller can
    /// guarantee the input topology is already manifold (e.g. when re-building from a region
    /// of an already-cleaned source mesh). Saves an O(V) star walk per build.
    /// </param>
    public void Build(
        int positionCount, double[] positions,
        int triangleCount, int[] triangles,
        double creaseAngleDegrees = 89.0,
        double[]? cornerUVs = null,
        bool assumeManifold = false)
    {
        var phaseSw = System.Diagnostics.Stopwatch.StartNew();
        CreaseThresholdDegrees = creaseAngleDegrees;

        Vertices.Clear();
        Edges.Clear();
        EdgeAttributes.Clear();
        EdgeUVs.Clear();
        Triangles.Clear();
        FaceAttributes.Clear();

        for (int i = 0; i < positionCount; ++i)
        {
            Append(new Vertex(
                new Double3(positions[3 * i + 0], positions[3 * i + 1], positions[3 * i + 2]),
                i));
        }

        // For each vertex, list the half-edges that originate there. Used to discover twins
        // (do we already have an edge B→A? then it's the twin of the A→B we're about to make).
        var outgoingPerVertex = new List<List<HalfEdge>>(positionCount);
        for (int i = 0; i < positionCount; ++i)
            outgoingPerVertex.Add(new List<HalfEdge>());

        for (int triI = 0; triI < triangleCount; ++triI)
        {
            var face = Append(new MeshFace());

            Vertex a = Vertices[triangles[3 * triI + 0]];
            Vertex b = Vertices[triangles[3 * triI + 1]];
            Vertex c = Vertices[triangles[3 * triI + 2]];
            Vertex[] apex = { a, b, c };

            HalfEdge[] faceEdges = new HalfEdge[3];
            Vertex from = apex[2];
            for (int side = 0; side < 3; ++side)
            {
                Vertex to = apex[side];

                var edge = Append(new HalfEdge { Face = face, Apex = to });
                faceEdges[side] = edge;

                to.IncidentEdge = edge;
                outgoingPerVertex[from.Index].Add(edge);

                // If the opposite half-edge already exists, twin them up.
                HalfEdge? twin = FindOutgoingTo(outgoingPerVertex[to.Index], from);
                if (twin is not null) MeshOps.StitchTwins(edge, twin);

                EdgeUVs.Add(cornerUVs is not null
                    ? new Double2(cornerUVs[6 * triI + 2 * side + 0], cornerUVs[6 * triI + 2 * side + 1])
                    : default);

                from = to;
            }

            // Close the ring of three sequential edges around the face.
            MeshOps.StitchSequential(faceEdges[2], faceEdges[0]);
            MeshOps.StitchSequential(faceEdges[0], faceEdges[1]);
            MeshOps.StitchSequential(faceEdges[1], faceEdges[2]);

            face.FirstEdge = faceEdges[0];
        }

        ProgressSink?.Invoke($"[hem] faces+interior edges in {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // For every interior edge missing a twin, mint a border edge to fill in.
        int interiorCount = Edges.Count;
        for (int heI = 0; heI < interiorCount; ++heI)
        {
            HalfEdge interior = Edges[heI];
            if (interior.Twin is null)
            {
                var border = Append(new HalfEdge());
                MeshOps.StitchTwins(border, interior);
                border.Apex = interior.Previous!.Apex;
                outgoingPerVertex[interior.Apex!.Index].Add(border);
                EdgeUVs.Add(default);
            }
        }

        ProgressSink?.Invoke($"[hem] mint border edges in {phaseSw.ElapsedMilliseconds} ms ({Edges.Count - interiorCount} added)");
        phaseSw.Restart();

        // Stitch the border edges into their own (possibly multi-loop) rings.
        // The walks below are bounded; without a cap, non-manifold input could spin forever.
        int safetyCap = Edges.Count * 2 + 16;
        for (int heI = 0; heI < Edges.Count; ++heI)
        {
            HalfEdge edge = Edges[heI];
            if (edge.Face is null)
            {
                HalfEdge next = edge.Twin!;
                int steps = 0;
                while (next.Face is not null && steps++ < safetyCap) next = next.Previous!.Twin!;

                HalfEdge prev = edge.Twin!;
                steps = 0;
                while (prev.Face is not null && steps++ < safetyCap) prev = prev.Next!.Twin!;

                edge.Next = next;
                edge.Previous = prev;
            }
        }

        ProgressSink?.Invoke($"[hem] stitch border rings in {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // Split non-manifold vertices into per-fan copies so the rest of the pipeline can assume
        // each vertex star is a simple loop. Skipped when the caller already guaranteed clean
        // topology (the inner per-region rebuilds during chart merging hit that path).
        if (assumeManifold) goto skipNonManifold;
        ProgressSink?.Invoke($"[hem] pre non-manifold split");
        int seenVertexCount = outgoingPerVertex.Count;
        for (int vi = 0; vi < seenVertexCount; ++vi)
        {
            Vertex original = Vertices[vi];
            int recordedDegree = outgoingPerVertex[vi].Count;
            int observedDegree = MeshOps.FanSize(original, cap: recordedDegree + 1);
            if (observedDegree == recordedDegree) continue;

            var remaining = new HashSet<HalfEdge>(outgoingPerVertex[vi]);
            int peelCap = remaining.Count + 8;
            int beforePeel = remaining.Count;
            PeelFan(original, original.IncidentEdge!.Twin!, remaining, outgoingPerVertex[vi], peelCap);
            if (remaining.Count == beforePeel)
            {
                ProgressSink?.Invoke($"[hem] WARN: vertex {vi} fan can't be peeled, leaving {remaining.Count} edges joined");
                continue;
            }

            int fanGuard = remaining.Count + 16;
            while (remaining.Count > 0 && fanGuard-- > 0)
            {
                var twin = Append(new Vertex(original.Position, original.SourceIndex));
                var twinStar = new List<HalfEdge>();
                outgoingPerVertex.Add(twinStar);

                HalfEdge startEdge = default!;
                foreach (var first in remaining) { startEdge = first; break; }
                int before = remaining.Count;
                PeelFan(twin, startEdge, remaining, twinStar, peelCap);
                if (remaining.Count == before)
                {
                    ProgressSink?.Invoke($"[hem] WARN: extra fan at vertex {vi} couldn't be peeled, {remaining.Count} edges left");
                    break;
                }
            }
        }

        ProgressSink?.Invoke($"[hem] non-manifold split in {phaseSw.ElapsedMilliseconds} ms ({Vertices.Count - positionCount} extra verts)");
        skipNonManifold:
        phaseSw.Restart();

        // Now that topology is final, fill in face attributes.
        for (int triI = 0; triI < triangleCount; ++triI)
        {
            MeshFace f = Triangles[triI];
            FaceAttributes.Add(new FaceAttribute
            {
                Centroid = MeshOps.ComputeFaceCentroid(f),
                Normal = MeshOps.ComputeFaceNormal(f),
                Area = MeshOps.ComputeFaceArea(f),
                SourceIndex = triI,
                VertexA = triangles[3 * triI + 0],
                VertexB = triangles[3 * triI + 1],
                VertexC = triangles[3 * triI + 2],
            });
        }

        EdgeAttributes.Clear();
        for (int i = 0; i < Edges.Count; ++i) EdgeAttributes.Add(default);

        // UV-seam detection: an edge is a seam if either of its endpoint UVs disagrees with the
        // matching UV on the twin side.
        for (int heI = 0; heI < Edges.Count; ++heI)
        {
            HalfEdge edge = Edges[heI];
            if (EdgeAttributes[heI].IsUvSeam) continue;

            HalfEdge twin = edge.Twin!;
            Double2 fromUv = EdgeUVs[IndexOf(edge.Previous!)];
            Double2 toUv = EdgeUVs[heI];
            Double2 twinFromUv = EdgeUVs[IndexOf(twin)];
            Double2 twinToUv = EdgeUVs[IndexOf(twin.Previous!)];

            if (fromUv != twinFromUv || toUv != twinToUv)
            {
                var info = EdgeAttributes[heI];
                info.IsUvSeam = true;
                EdgeAttributes[heI] = info;

                int twinSlot = IndexOf(twin);
                var twinInfo = EdgeAttributes[twinSlot];
                twinInfo.IsUvSeam = true;
                EdgeAttributes[twinSlot] = twinInfo;
            }
        }

        // Crease flag: any internal edge whose dihedral angle exceeds the threshold.
        double creaseCos = System.Math.Cos(creaseAngleDegrees * (System.Math.PI / 180.0));
        for (int heI = 0; heI < Edges.Count; ++heI)
        {
            HalfEdge edge = Edges[heI];
            var info = EdgeAttributes[heI];

            info.IsCrease = true;
            if (MeshOps.IsBorderEdge(edge) || MeshOps.IsBorderEdge(edge.Twin!))
            {
                EdgeAttributes[heI] = info;
                continue;
            }

            Double3 n1 = FaceAttributes[IndexOf(edge.Face!)].Normal;
            Double3 n2 = FaceAttributes[IndexOf(edge.Twin!.Face!)].Normal;

            if (NumericHelpers.ApproxGreater(Double3.Dot(n1, n2), creaseCos, 1e-6))
                info.IsCrease = false;

            EdgeAttributes[heI] = info;
        }

        // Cache per-edge length.
        for (int heI = 0; heI < Edges.Count; ++heI)
        {
            var info = EdgeAttributes[heI];
            info.Length = Double3.Length(MeshOps.EdgeVector(Edges[heI]));
            EdgeAttributes[heI] = info;
        }

        ProgressSink?.Invoke($"[hem] attributes in {phaseSw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Re-build this mesh from a subset of another mesh's faces.
    /// Preserves vertex order so the parametriser stays aligned with the source.
    /// </summary>
    public void BuildFromRegion(MeshRegion region, IList<int> vertexIndices, IDictionary<int, int> vertexLookup)
    {
        int vCount = vertexIndices.Count;
        int fCount = region.Triangles.Length;
        double[] position = new double[3 * vCount];
        double[] uvs = new double[2 * 3 * fCount];
        int[] tris = new int[3 * fCount];

        for (int posI = 0; posI < vCount; ++posI)
        {
            Vertex v = region.Mesh.Vertices[vertexIndices[posI]];
            position[3 * posI + 0] = v.Position.X;
            position[3 * posI + 1] = v.Position.Y;
            position[3 * posI + 2] = v.Position.Z;
        }

        for (int triI = 0; triI < fCount; ++triI)
        {
            int srcFacet = region.Triangles[triI];
            MeshFace f = region.Mesh.Triangles[srcFacet];
            HalfEdge edge = f.FirstEdge!;
            FaceAttribute info = region.Mesh.FaceAttributes[srcFacet];
            int v0 = info.VertexA, v1 = info.VertexB, v2 = info.VertexC;

            // The non-manifold split may have cloned vertices; route each corner to the slot
            // matching its source index so winding stays correct.
            for (int side = 0; side < 3; ++side)
            {
                int sourceIndex = edge.Apex!.SourceIndex;
                int slot;
                if (sourceIndex == v0) slot = 3 * triI + 0;
                else if (sourceIndex == v1) slot = 3 * triI + 1;
                else slot = 3 * triI + 2;

                Double2 srcUv = region.Mesh.EdgeUVs[region.Mesh.IndexOf(edge)];
                tris[slot] = vertexLookup[region.Mesh.IndexOf(edge.Apex!)];
                uvs[2 * slot + 0] = srcUv.X;
                uvs[2 * slot + 1] = srcUv.Y;

                edge = edge.Next!;
            }
        }

        // Source mesh has already been through the geometry-prep pipeline (weld + non-manifold
        // fix + degenerate removal); subset rebuilds inherit that cleanliness.
        Build(vCount, position, fCount, tris, region.Mesh.CreaseThresholdDegrees, uvs, assumeManifold: true);
    }

    private static HalfEdge? FindOutgoingTo(List<HalfEdge> outgoing, Vertex target)
    {
        for (int i = 0; i < outgoing.Count; ++i)
            if (outgoing[i].Apex == target) return outgoing[i];
        return null;
    }

    /// <summary>
    /// Walk one manifold fan starting at <paramref name="fanStart"/>, reassigning each
    /// half-edge's apex to <paramref name="owner"/> and removing it from <paramref name="remaining"/>.
    /// The walks are bounded by <paramref name="cap"/> in case the input is non-manifold in
    /// a way that produces an unbounded cycle.
    /// </summary>
    private static void PeelFan(Vertex owner, HalfEdge fanStart, HashSet<HalfEdge> remaining, List<HalfEdge> outStar, int cap)
    {
        outStar.Clear();

        // Step backwards to ground the fan on a border if there is one.
        HalfEdge start = fanStart;
        int steps = 0;
        while (!MeshOps.IsBorderEdge(start) && steps++ < cap)
        {
            start = start.Previous!.Twin!;
            if (start == fanStart) break;
        }

        owner.IncidentEdge = start.Twin;

        HalfEdge cur = start;
        cur.Twin!.Apex = owner;
        outStar.Add(cur);
        remaining.Remove(cur);

        steps = 0;
        while (!MeshOps.IsBorderEdge(cur.Twin!) && steps++ < cap)
        {
            cur = cur.Twin!.Next!;
            if (cur == start) break;

            cur.Twin!.Apex = owner;
            outStar.Add(cur);
            remaining.Remove(cur);
        }

        if (MeshOps.IsBorderEdge(start))
            MeshOps.StitchSequential(cur.Twin!, start);
    }

    /// <summary>Split the mesh into its connected components by face adjacency.</summary>
    public static void FindConnectedRegions(HalfEdgeMesh mesh, List<MeshRegion> output)
    {
        int[] regionId = new int[mesh.Triangles.Count];
        for (int i = 0; i < regionId.Length; ++i) regionId[i] = -1;

        int regionCount = 0;
        FloodFill(mesh, ref regionCount, regionId, sourceMarker: -1);
        Gather(mesh, regionId, regionCount, output);
    }

    /// <summary>Treat <paramref name="cutoutFacets"/> as the foreground and find the components within it.</summary>
    public static void FindConnectedRegionsIn(HalfEdgeMesh mesh, IList<int> cutoutFacets, List<MeshRegion> output)
    {
        int[] regionId = new int[mesh.Triangles.Count];
        for (int i = 0; i < regionId.Length; ++i) regionId[i] = -1;
        for (int i = 0; i < cutoutFacets.Count; ++i) regionId[cutoutFacets[i]] = -2;

        int regionCount = 0;
        FloodFill(mesh, ref regionCount, regionId, sourceMarker: -2);
        Gather(mesh, regionId, regionCount, output);
    }

    /// <summary>Find components of <paramref name="sourceFacets"/> that aren't inside <paramref name="cutoutRegions"/>.</summary>
    public static void FindRegionsExcluding(HalfEdgeMesh mesh, IList<int> sourceFacets, IList<MeshRegion> cutoutRegions, List<MeshRegion> output)
    {
        int[] regionId = new int[mesh.Triangles.Count];
        for (int i = 0; i < regionId.Length; ++i) regionId[i] = -1;
        for (int i = 0; i < sourceFacets.Count; ++i) regionId[sourceFacets[i]] = -2;
        foreach (var region in cutoutRegions)
            foreach (int facet in region.Triangles)
                regionId[facet] = -3;

        int regionCount = 0;
        FloodFill(mesh, ref regionCount, regionId, sourceMarker: -2);
        Gather(mesh, regionId, regionCount, output);
    }

    private static void FloodFill(HalfEdgeMesh mesh, ref int regionCount, int[] regionId, int sourceMarker)
    {
        var stack = new Stack<int>();
        for (int facetI = 0; facetI < mesh.Triangles.Count; ++facetI)
        {
            if (regionId[facetI] != sourceMarker) continue;

            stack.Push(facetI);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                if (regionId[cur] != sourceMarker) continue;
                regionId[cur] = regionCount;

                HalfEdge edge = mesh.Triangles[cur].FirstEdge!;
                for (int side = 0; side < 3; ++side)
                {
                    MeshFace? neighbour = edge.Twin!.Face;
                    if (neighbour is not null && regionId[mesh.IndexOf(neighbour)] == sourceMarker)
                        stack.Push(mesh.IndexOf(neighbour));
                    edge = edge.Next!;
                }
            }
            ++regionCount;
        }
    }

    private static void Gather(HalfEdgeMesh mesh, int[] regionId, int regionCount, List<MeshRegion> output)
    {
        if (regionCount == 0) return;

        int[] perRegionCount = new int[regionCount];
        for (int i = 0; i < mesh.Triangles.Count; ++i)
            if (regionId[i] >= 0) ++perRegionCount[regionId[i]];

        int startSlot = output.Count;
        for (int r = 0; r < regionCount; ++r)
            output.Add(new MeshRegion(mesh, perRegionCount[r]));

        System.Array.Clear(perRegionCount, 0, perRegionCount.Length);
        for (int i = 0; i < mesh.Triangles.Count; ++i)
        {
            int r = regionId[i];
            if (r >= 0)
                output[startSlot + r].Triangles[perRegionCount[r]++] = i;
        }
    }
}
