using Prowl.Vector;

namespace Photonic.Demo;

/// <summary>
/// Per-group atlas packer with two optimisations on top of the naive per-triangle approach:
///   1. <b>Min-area orientation</b>: for each group, the layout is rotated so the smallest
///      enclosing bbox aligned with one of the group's edges is picked — sliver triangles end
///      up horizontal instead of diagonal, saving ~50% atlas area on average.
///   2. <b>Adjacent quad pairing</b>: two triangles that share an edge and have near-aligned
///      face normals are merged into one 4-vert group with no padding between them. Sponza is
///      mostly quad-triangulated, so this halves the padding overhead on flat surfaces.
/// </summary>
internal static class TrianglePacker
{
    private const float PairNormalDot = 0.95f;   // pair if face normals are within ~18 degrees
    private const float Padding = 1.0f;          // texels of padding between groups

    public static CombinedSponza Repack(CombinedSponza src, int atlasWidth, int atlasHeight,
                                        System.Action<string>? progress)
    {
        int triCount = src.Indices.Length / 3;
        var indices = src.Indices;
        var positions = src.Vertices;
        progress?.Invoke($"[pack] preparing layout for {triCount} triangles...");

        // 1) Face normals -----------------------------------------------------------------------
        var triNormals = new Float3[triCount];
        for (int t = 0; t < triCount; t++)
        {
            var v0 = positions[indices[t * 3]];
            var e1 = positions[indices[t * 3 + 1]] - v0;
            var e2 = positions[indices[t * 3 + 2]] - v0;
            var n = Float3.Cross(e1, e2);
            float ln = Float3.Length(n);
            triNormals[t] = ln > 1e-12f ? n / ln : new Float3(0, 1, 0);
        }

        // 2) Edge -> triangle adjacency ---------------------------------------------------------
        var edgeMap = new System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<int>>();
        for (int t = 0; t < triCount; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = indices[t * 3 + e];
                int b = indices[t * 3 + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(key, out var list)) { list = new System.Collections.Generic.List<int>(); edgeMap[key] = list; }
                list.Add(t);
            }
        }

        // 3) Pair triangles whose normals nearly match ------------------------------------------
        var paired = new int[triCount];
        var sharedEdge = new (int a, int b)[triCount];
        for (int i = 0; i < triCount; i++) paired[i] = -1;
        int pairCount = 0;
        for (int t = 0; t < triCount; t++)
        {
            if (paired[t] != -1) continue;
            for (int e = 0; e < 3 && paired[t] == -1; e++)
            {
                int a = indices[t * 3 + e];
                int b = indices[t * 3 + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                var list = edgeMap[key];
                for (int j = 0; j < list.Count; j++)
                {
                    int u = list[j];
                    if (u == t || paired[u] != -1) continue;
                    if (Float3.Dot(triNormals[t], triNormals[u]) < PairNormalDot) continue;
                    paired[t] = u; paired[u] = t;
                    sharedEdge[t] = (a, b); sharedEdge[u] = (a, b);
                    pairCount++;
                    break;
                }
            }
        }
        progress?.Invoke($"[pack] paired {pairCount} quads ({200.0 * pairCount / triCount:0.0}% of triangles)");

        // 4) Per-group local 2D layout (with min-area orientation). For triangles we use 3 points;
        //    for quad pairs we use 4 (shared edge verts + each triangle's unique vert).
        var localCornerUV = new Float2[triCount * 3];
        var groupTriA = new System.Collections.Generic.List<int>();
        var groupTriB = new System.Collections.Generic.List<int>();
        var groupW = new System.Collections.Generic.List<float>();
        var groupH = new System.Collections.Generic.List<float>();
        var triGroup = new int[triCount];
        for (int i = 0; i < triCount; i++) triGroup[i] = -1;

        for (int t = 0; t < triCount; t++)
        {
            if (triGroup[t] != -1) continue;
            int u = paired[t];

            if (u == -1)
            {
                ComputeLoneLayout(t, indices, positions, localCornerUV, out float w, out float h);
                int gi = groupTriA.Count;
                groupTriA.Add(t); groupTriB.Add(-1); groupW.Add(w); groupH.Add(h);
                triGroup[t] = gi;
            }
            else
            {
                ComputePairLayout(t, u, sharedEdge[t], indices, positions, localCornerUV, out float w, out float h);
                int gi = groupTriA.Count;
                groupTriA.Add(t); groupTriB.Add(u); groupW.Add(w); groupH.Add(h);
                triGroup[t] = gi; triGroup[u] = gi;
            }
        }
        int groupCount = groupTriA.Count;

        // 5) Sort groups by max(W, H) descending — classic shelf-packing heuristic.
        var order = new int[groupCount];
        for (int i = 0; i < groupCount; i++) order[i] = i;
        System.Array.Sort(order, (a, b) =>
        {
            float ma = System.Math.Max(groupW[a], groupH[a]);
            float mb = System.Math.Max(groupW[b], groupH[b]);
            return mb.CompareTo(ma);
        });

        // 6) Trial-pack at unit scale to find natural footprint, then choose final scale.
        float totalAreaWithPad = 0;
        for (int g = 0; g < groupCount; g++) totalAreaWithPad += (groupW[g] + Padding) * (groupH[g] + Padding);
        float targetW = (float)System.Math.Sqrt(totalAreaWithPad);

        float trialMaxX = 0, trialMaxY = 0;
        {
            float cx = Padding, cy = Padding, rowH = 0;
            for (int oi = 0; oi < groupCount; oi++)
            {
                int g = order[oi];
                float w = groupW[g], h = groupH[g];
                if (cx + w + Padding > targetW) { cy += rowH + Padding; cx = Padding; rowH = 0; }
                trialMaxX = System.Math.Max(trialMaxX, cx + w);
                trialMaxY = System.Math.Max(trialMaxY, cy + h);
                cx += w + Padding;
                rowH = System.Math.Max(rowH, h);
            }
        }

        float sX = (atlasWidth  - 2 * Padding) / (trialMaxX + Padding);
        float sY = (atlasHeight - 2 * Padding) / (trialMaxY + Padding);
        float scale = System.Math.Min(sX, sY) * 0.99f;
        progress?.Invoke($"[pack] natural footprint {trialMaxX:0}x{trialMaxY:0}, scale = {scale:0.000} tex/unit");

        // 7) Final pack at chosen scale: record each group's atlas-pixel offset.
        var groupOffset = new Float2[groupCount];
        {
            float maxPixW = atlasWidth - Padding;
            float cx = Padding, cy = Padding, rowH = 0;
            for (int oi = 0; oi < groupCount; oi++)
            {
                int g = order[oi];
                float w = groupW[g] * scale, h = groupH[g] * scale;
                if (cx + w + Padding > maxPixW) { cy += rowH + Padding; cx = Padding; rowH = 0; }
                groupOffset[g] = new Float2(cx, cy);
                cx += w + Padding;
                rowH = System.Math.Max(rowH, h);
            }
        }

        // 8) Emit geometry. Each lone group: 3 verts. Each paired group: 4 verts (shared edge).
        int totalVerts = 0;
        for (int g = 0; g < groupCount; g++) totalVerts += groupTriB[g] == -1 ? 3 : 4;

        var newPositions = new Float3[totalVerts];
        var newNormals   = new Float3[totalVerts];
        var newUV0       = new Float2[totalVerts];
        var newUV1       = new Float2[totalVerts];
        var newIndices   = new int[triCount * 3];

        int slot = 0;
        for (int g = 0; g < groupCount; g++)
        {
            int t = groupTriA[g];
            int u = groupTriB[g];
            var off = groupOffset[g];

            if (u == -1)
            {
                for (int c = 0; c < 3; c++)
                {
                    int oldI = indices[t * 3 + c];
                    newPositions[slot] = positions[oldI];
                    newNormals[slot]   = src.Normals[oldI];
                    newUV0[slot]       = src.UV0[oldI];
                    var luv = localCornerUV[t * 3 + c];
                    newUV1[slot] = new Float2((off.X + luv.X * scale) / atlasWidth,
                                              (off.Y + luv.Y * scale) / atlasHeight);
                    newIndices[t * 3 + c] = slot;
                    slot++;
                }
            }
            else
            {
                int sA = sharedEdge[t].a, sB = sharedEdge[t].b;
                int cornerA_t = -1, cornerB_t = -1, cornerC_t = -1;
                for (int c = 0; c < 3; c++)
                {
                    int oldI = indices[t * 3 + c];
                    if (oldI == sA) cornerA_t = c;
                    else if (oldI == sB) cornerB_t = c;
                    else cornerC_t = c;
                }
                int cornerA_u = -1, cornerB_u = -1, cornerD_u = -1;
                for (int c = 0; c < 3; c++)
                {
                    int oldI = indices[u * 3 + c];
                    if (oldI == sA) cornerA_u = c;
                    else if (oldI == sB) cornerB_u = c;
                    else cornerD_u = c;
                }
                int uniqueT_old = indices[t * 3 + cornerC_t];
                int uniqueU_old = indices[u * 3 + cornerD_u];

                int slotA = slot++, slotB = slot++, slotC = slot++, slotD = slot++;

                // Shared edge verts
                FillVert(slotA, sA, localCornerUV[t * 3 + cornerA_t], off, scale, atlasWidth, atlasHeight,
                         src, newPositions, newNormals, newUV0, newUV1);
                FillVert(slotB, sB, localCornerUV[t * 3 + cornerB_t], off, scale, atlasWidth, atlasHeight,
                         src, newPositions, newNormals, newUV0, newUV1);
                // Triangle-unique verts
                FillVert(slotC, uniqueT_old, localCornerUV[t * 3 + cornerC_t], off, scale, atlasWidth, atlasHeight,
                         src, newPositions, newNormals, newUV0, newUV1);
                FillVert(slotD, uniqueU_old, localCornerUV[u * 3 + cornerD_u], off, scale, atlasWidth, atlasHeight,
                         src, newPositions, newNormals, newUV0, newUV1);

                newIndices[t * 3 + cornerA_t] = slotA;
                newIndices[t * 3 + cornerB_t] = slotB;
                newIndices[t * 3 + cornerC_t] = slotC;
                newIndices[u * 3 + cornerA_u] = slotA;
                newIndices[u * 3 + cornerB_u] = slotB;
                newIndices[u * 3 + cornerD_u] = slotD;
            }
        }

        progress?.Invoke($"[pack] emitted {newPositions.Length} verts (orig {src.Vertices.Length}, {triCount * 3} without pairing)");

        return new CombinedSponza
        {
            Vertices = newPositions,
            Normals = newNormals,
            UV0 = newUV0,
            UV1 = newUV1,
            Indices = newIndices,
            SubMeshes = src.SubMeshes,
            Materials = src.Materials,
            Textures = src.Textures,
        };
    }

    private static void FillVert(int slot, int oldI, Float2 localUV, Float2 off, float scale,
                                 int atlasW, int atlasH, CombinedSponza src,
                                 Float3[] newPos, Float3[] newNor, Float2[] newUV0, Float2[] newUV1)
    {
        newPos[slot] = src.Vertices[oldI];
        newNor[slot] = src.Normals[oldI];
        newUV0[slot] = src.UV0[oldI];
        newUV1[slot] = new Float2((off.X + localUV.X * scale) / atlasW,
                                  (off.Y + localUV.Y * scale) / atlasH);
    }

    /// <summary>
    /// Compute 2D local coords for a lone triangle, oriented to minimise bbox area.
    /// </summary>
    private static void ComputeLoneLayout(int t, int[] indices, Float3[] positions,
                                          Float2[] localCornerUV, out float w, out float h)
    {
        var v0 = positions[indices[t * 3]];
        var e1 = positions[indices[t * 3 + 1]] - v0;
        var e2 = positions[indices[t * 3 + 2]] - v0;
        var n = Float3.Cross(e1, e2);
        float ln = Float3.Length(n);
        if (ln < 1e-12f)
        {
            localCornerUV[t * 3] = Float2.Zero;
            localCornerUV[t * 3 + 1] = Float2.Zero;
            localCornerUV[t * 3 + 2] = Float2.Zero;
            w = 0; h = 0;
            return;
        }

        // Project onto an arbitrary in-plane basis (we'll rotate after).
        ProjectToPlane(n / ln, e1, out var tang, out var bita);
        var q0 = new Float2(0, 0);
        var q1 = new Float2(Float3.Dot(e1, tang), Float3.Dot(e1, bita));
        var q2 = new Float2(Float3.Dot(e2, tang), Float3.Dot(e2, bita));

        var pts = new[] { q0, q1, q2 };
        OptimizeOrientation(pts, out var optimized, out w, out h);
        localCornerUV[t * 3]     = optimized[0];
        localCornerUV[t * 3 + 1] = optimized[1];
        localCornerUV[t * 3 + 2] = optimized[2];
    }

    /// <summary>
    /// Compute 2D local coords for a pair of triangles sharing an edge. All 4 unique vertices are
    /// projected onto the (t-triangle) plane and the layout is rotated to min-area bbox.
    /// </summary>
    private static void ComputePairLayout(int t, int u, (int a, int b) shared, int[] indices,
                                          Float3[] positions, Float2[] localCornerUV,
                                          out float w, out float h)
    {
        int sA = shared.a, sB = shared.b;
        int cornerC_t = -1, cornerD_u = -1;
        for (int c = 0; c < 3; c++)
        {
            int oldI = indices[t * 3 + c];
            if (oldI != sA && oldI != sB) cornerC_t = c;
        }
        for (int c = 0; c < 3; c++)
        {
            int oldI = indices[u * 3 + c];
            if (oldI != sA && oldI != sB) cornerD_u = c;
        }
        int uniqueT_old = indices[t * 3 + cornerC_t];
        int uniqueU_old = indices[u * 3 + cornerD_u];

        var vA = positions[sA];
        var vB = positions[sB];
        var vC = positions[uniqueT_old];
        var vD = positions[uniqueU_old];

        var eAB = vB - vA;
        var eAC = vC - vA;
        var n = Float3.Cross(eAB, eAC);
        float ln = Float3.Length(n);
        if (ln < 1e-12f)
        {
            for (int c = 0; c < 3; c++)
            {
                localCornerUV[t * 3 + c] = Float2.Zero;
                localCornerUV[u * 3 + c] = Float2.Zero;
            }
            w = 0; h = 0; return;
        }

        ProjectToPlane(n / ln, eAB, out var tang, out var bita);
        var qA = new Float2(0, 0);
        var qB = new Float2(Float3.Dot(eAB, tang), Float3.Dot(eAB, bita));
        var qC = new Float2(Float3.Dot(eAC, tang), Float3.Dot(eAC, bita));
        var eAD = vD - vA;
        var qD = new Float2(Float3.Dot(eAD, tang), Float3.Dot(eAD, bita));

        var pts = new[] { qA, qB, qC, qD };
        OptimizeOrientation(pts, out var optimized, out w, out h);

        // Map back to the per-triangle corner slots.
        for (int c = 0; c < 3; c++)
        {
            int oldI = indices[t * 3 + c];
            if (oldI == sA) localCornerUV[t * 3 + c] = optimized[0];
            else if (oldI == sB) localCornerUV[t * 3 + c] = optimized[1];
            else localCornerUV[t * 3 + c] = optimized[2];
        }
        for (int c = 0; c < 3; c++)
        {
            int oldI = indices[u * 3 + c];
            if (oldI == sA) localCornerUV[u * 3 + c] = optimized[0];
            else if (oldI == sB) localCornerUV[u * 3 + c] = optimized[1];
            else localCornerUV[u * 3 + c] = optimized[3];
        }
    }

    /// <summary>
    /// Try rotating the input verts so each successive edge is aligned with +X; pick the
    /// orientation that gives the smallest enclosing axis-aligned bbox. Output verts are
    /// translated so the bbox starts at (0, 0).
    /// </summary>
    private static void OptimizeOrientation(Float2[] verts, out Float2[] optimized, out float w, out float h)
    {
        int n = verts.Length;
        optimized = new Float2[n];
        for (int j = 0; j < n; j++) optimized[j] = verts[j];
        w = 0; h = 0;
        float bestArea = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            var edge = new Float2(verts[(i + 1) % n].X - verts[i].X,
                                  verts[(i + 1) % n].Y - verts[i].Y);
            float len = (float)System.Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);
            if (len < 1e-12f) continue;
            float dx = edge.X / len;
            float dy = edge.Y / len;

            // Rotate every vertex into the basis where the edge becomes +X.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var rotated = new Float2[n];
            for (int j = 0; j < n; j++)
            {
                float rx = verts[j].X - verts[i].X;
                float ry = verts[j].Y - verts[i].Y;
                float nx =  rx * dx + ry * dy;   // along edge
                float ny = -rx * dy + ry * dx;   // perpendicular
                rotated[j] = new Float2(nx, ny);
                if (nx < minX) minX = nx;
                if (nx > maxX) maxX = nx;
                if (ny < minY) minY = ny;
                if (ny > maxY) maxY = ny;
            }
            float bw = maxX - minX, bh = maxY - minY;
            float area = bw * bh;
            if (area < bestArea)
            {
                bestArea = area;
                w = bw; h = bh;
                for (int j = 0; j < n; j++)
                    optimized[j] = new Float2(rotated[j].X - minX, rotated[j].Y - minY);
            }
        }
    }

    /// <summary>Build an orthonormal in-plane basis where tangent is along <paramref name="edge"/>.</summary>
    private static void ProjectToPlane(Float3 normal, Float3 edge, out Float3 tangent, out Float3 bitangent)
    {
        float len = Float3.Length(edge);
        if (len < 1e-12f)
        {
            // Fallback: pick any axis not parallel to normal.
            var axis = System.Math.Abs(normal.X) < 0.9f ? new Float3(1, 0, 0) : new Float3(0, 1, 0);
            tangent = Float3.Normalize(Float3.Cross(axis, normal));
        }
        else
        {
            tangent = edge / len;
        }
        bitangent = Float3.Cross(normal, tangent);
    }
}
