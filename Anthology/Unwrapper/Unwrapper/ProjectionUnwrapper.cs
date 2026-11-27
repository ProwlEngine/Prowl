// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Box-projection unwrapper. Every triangle picks the projection direction its normal points
/// closest to, connected same-direction triangles become a chart, and each chart is flattened by
/// dropping it onto that direction's plane. No solve, no iteration.
/// </summary>
/// <remarks>
/// A chart is fold-free because every triangle in it faces the projection plane, but a surface
/// that spirals back over itself while staying within one direction bucket can still self-overlap.
/// That needs a genuinely curved, connected, single-direction region to happen, so it is rare on
/// the architectural and prop geometry this is aimed at — and unlike the conformal path there is
/// no overlap validation pass to catch it.
/// </remarks>
internal static class ProjectionUnwrapper
{
    private const double DiagonalComponent = 0.5773502691896258; // 1/sqrt(3)

    private static readonly Double3[] FaceDirections =
    {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

    private static readonly Double3[] CornerDirections =
    {
        new( DiagonalComponent,  DiagonalComponent,  DiagonalComponent),
        new( DiagonalComponent,  DiagonalComponent, -DiagonalComponent),
        new( DiagonalComponent, -DiagonalComponent,  DiagonalComponent),
        new( DiagonalComponent, -DiagonalComponent, -DiagonalComponent),
        new(-DiagonalComponent,  DiagonalComponent,  DiagonalComponent),
        new(-DiagonalComponent,  DiagonalComponent, -DiagonalComponent),
        new(-DiagonalComponent, -DiagonalComponent,  DiagonalComponent),
        new(-DiagonalComponent, -DiagonalComponent, -DiagonalComponent),
    };

    /// <summary>
    /// Entry point mirroring <see cref="UnwrapPipeline.Run"/>: take a cleaned mesh through
    /// segmentation, projection and packing, then write one Double2 per triangle corner.
    /// </summary>
    public static void Run(CleanedGeometry geometry, UnwrapOptions options, Double2[] outputUV, System.Action<string>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var mesh = new HalfEdgeMesh { ProgressSink = progress is null ? null : s => progress($"  {s}") };
        mesh.Build(geometry.VertexCount, geometry.Positions, geometry.TriangleCount, geometry.Triangles,
                   creaseAngleDegrees: options.HardAngle,
                   cornerUVs: geometry.TriangleUVs);
        progress?.Invoke($"[mesh] build done in {sw.ElapsedMilliseconds} ms; {mesh.Vertices.Count} verts, {mesh.Edges.Count} half-edges");

        sw.Restart();
        Double3[] directions = options.Projection.UseDiagonalAxes ? Concat(FaceDirections, CornerDirections) : FaceDirections;
        int[] faceDirection = AssignDirections(mesh, directions);
        progress?.Invoke($"[project] {directions.Length} directions assigned in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        AbsorbSmallClusters(mesh, directions, faceDirection, options.Projection.MinChartTriangles);
        var regions = new List<MeshRegion>();
        var regionDirection = new List<int>();
        BuildCharts(mesh, faceDirection, regions, regionDirection);
        progress?.Invoke($"[project] {regions.Count} charts in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        var charts = new UvChart[regions.Count];
        System.Threading.Tasks.Parallel.For(
            0, regions.Count,
            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism },
            i => charts[i] = ProjectChart(regions[i], directions[regionDirection[i]]));
        progress?.Invoke($"[project] charts flattened in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        AtlasPacker.Pack(charts, options.PackMargin);
        progress?.Invoke($"[pack] {charts.Length} charts packed in {sw.ElapsedMilliseconds} ms");

        for (int i = 0; i < outputUV.Length; ++i) outputUV[i] = default;
        foreach (var chart in charts)
        {
            for (int f = 0; f < chart.Region!.Triangles.Length; ++f)
            {
                int triIndex = geometry.TriangleRemap[chart.Region.Triangles[f]];
                outputUV[3 * triIndex + 0] = chart.UVs[3 * f + 0];
                outputUV[3 * triIndex + 1] = chart.UVs[3 * f + 1];
                outputUV[3 * triIndex + 2] = chart.UVs[3 * f + 2];
            }
        }
    }

    private static int[] AssignDirections(HalfEdgeMesh mesh, Double3[] directions)
    {
        var faceDirection = new int[mesh.Triangles.Count];
        for (int faceI = 0; faceI < faceDirection.Length; ++faceI)
        {
            Double3 normal = mesh.FaceAttributes[faceI].Normal;
            int best = 0;
            double bestDot = Double3.Dot(normal, directions[0]);
            for (int d = 1; d < directions.Length; ++d)
            {
                double dot = Double3.Dot(normal, directions[d]);
                if (dot > bestDot) { bestDot = dot; best = d; }
            }
            faceDirection[faceI] = best;
        }
        return faceDirection;
    }

    /// <summary>
    /// Re-label clusters below the size cut-off to the direction of the neighbouring cluster they
    /// share the most edge length with. A handful of triangles straddling a direction boundary
    /// would otherwise each become a chart, and every chart costs a packing margin.
    /// </summary>
    private static void AbsorbSmallClusters(HalfEdgeMesh mesh, Double3[] directions, int[] faceDirection, int minTriangles)
    {
        if (minTriangles <= 1) return;

        int faceCount = mesh.Triangles.Count;
        var clusterId = new int[faceCount];
        int clusterCount = LabelClusters(mesh, faceDirection, clusterId);

        var clusterSize = new int[clusterCount];
        for (int faceI = 0; faceI < faceCount; ++faceI) ++clusterSize[clusterId[faceI]];

        // Shared border length per (small cluster, candidate direction), keyed on the cluster.
        var borderLength = new Dictionary<long, double>();
        for (int faceI = 0; faceI < faceCount; ++faceI)
        {
            int cluster = clusterId[faceI];
            if (clusterSize[cluster] >= minTriangles) continue;

            HalfEdge edge = mesh.Triangles[faceI].FirstEdge!;
            for (int side = 0; side < 3; ++side)
            {
                MeshFace? neighbour = edge.Twin!.Face;
                if (neighbour is not null && clusterId[neighbour.Index] != cluster)
                {
                    long key = ((long)cluster << 32) | (uint)faceDirection[neighbour.Index];
                    borderLength.TryGetValue(key, out double len);
                    borderLength[key] = len + mesh.EdgeAttributes[edge.Index].Length;
                }
                edge = edge.Next!;
            }
        }

        var bestDirection = new Dictionary<int, (int Direction, double Length)>();
        foreach (var kv in borderLength)
        {
            int cluster = (int)(kv.Key >> 32);
            int direction = (int)(uint)kv.Key;
            if (!bestDirection.TryGetValue(cluster, out var cur) || kv.Value > cur.Length)
                bestDirection[cluster] = (direction, kv.Value);
        }

        // Absorbing a cluster into a direction it barely faces would squash it to a sliver, so
        // require every face to keep at least the squareness the six-direction fit already promises.
        var rejected = new HashSet<int>();
        for (int faceI = 0; faceI < faceCount; ++faceI)
        {
            int cluster = clusterId[faceI];
            if (!bestDirection.TryGetValue(cluster, out var pick)) continue;
            if (Double3.Dot(mesh.FaceAttributes[faceI].Normal, directions[pick.Direction]) < DiagonalComponent)
                rejected.Add(cluster);
        }

        for (int faceI = 0; faceI < faceCount; ++faceI)
        {
            int cluster = clusterId[faceI];
            if (bestDirection.TryGetValue(cluster, out var pick) && !rejected.Contains(cluster))
                faceDirection[faceI] = pick.Direction;
        }
    }

    /// <summary>Flood-fill faces into connected same-direction clusters; returns the cluster count.</summary>
    private static int LabelClusters(HalfEdgeMesh mesh, int[] faceDirection, int[] clusterId)
    {
        int faceCount = mesh.Triangles.Count;
        for (int i = 0; i < faceCount; ++i) clusterId[i] = -1;

        var stack = new Stack<int>();
        int clusterCount = 0;
        for (int seed = 0; seed < faceCount; ++seed)
        {
            if (clusterId[seed] != -1) continue;

            int direction = faceDirection[seed];
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int faceI = stack.Pop();
                if (clusterId[faceI] != -1) continue;
                clusterId[faceI] = clusterCount;

                HalfEdge edge = mesh.Triangles[faceI].FirstEdge!;
                for (int side = 0; side < 3; ++side)
                {
                    MeshFace? neighbour = edge.Twin!.Face;
                    if (neighbour is not null && clusterId[neighbour.Index] == -1 && faceDirection[neighbour.Index] == direction)
                        stack.Push(neighbour.Index);
                    edge = edge.Next!;
                }
            }
            ++clusterCount;
        }
        return clusterCount;
    }

    private static void BuildCharts(HalfEdgeMesh mesh, int[] faceDirection, List<MeshRegion> regions, List<int> regionDirection)
    {
        int faceCount = mesh.Triangles.Count;
        var clusterId = new int[faceCount];
        int clusterCount = LabelClusters(mesh, faceDirection, clusterId);

        var clusterSize = new int[clusterCount];
        for (int faceI = 0; faceI < faceCount; ++faceI) ++clusterSize[clusterId[faceI]];

        for (int c = 0; c < clusterCount; ++c)
        {
            regions.Add(new MeshRegion(mesh, clusterSize[c]));
            regionDirection.Add(0);
        }

        System.Array.Clear(clusterSize, 0, clusterCount);
        for (int faceI = 0; faceI < faceCount; ++faceI)
        {
            int cluster = clusterId[faceI];
            regions[cluster].Triangles[clusterSize[cluster]++] = faceI;
            regionDirection[cluster] = faceDirection[faceI];
        }
    }

    /// <summary>Drop a chart's corners onto its projection plane, then square it up for packing.</summary>
    private static UvChart ProjectChart(MeshRegion region, Double3 direction)
    {
        BuildBasis(direction, out Double3 axisU, out Double3 axisV);

        var chart = new UvChart(region);
        for (int f = 0; f < region.Triangles.Length; ++f)
        {
            HalfEdge edge = region.Mesh.Triangles[region.Triangles[f]].FirstEdge!;
            for (int corner = 0; corner < 3; ++corner)
            {
                Double3 p = edge.Apex!.Position;
                chart.UVs[3 * f + corner] = new Double2(Double3.Dot(p, axisU), Double3.Dot(p, axisV));
                edge = edge.Next!;
            }
        }

        chart.TightenAndOrient();
        chart.NormaliseToSurfaceArea();
        return chart;
    }

    /// <summary>Right-handed basis for a projection plane: <c>cross(u, v)</c> is the direction itself.</summary>
    private static void BuildBasis(Double3 direction, out Double3 axisU, out Double3 axisV)
    {
        Double3 helper = System.Math.Abs(direction.X) < 0.9 ? new Double3(1, 0, 0) : new Double3(0, 1, 0);
        axisU = Double3.Normalize(Double3.Cross(helper, direction));
        axisV = Double3.Cross(direction, axisU);
    }

    private static Double3[] Concat(Double3[] a, Double3[] b)
    {
        var result = new Double3[a.Length + b.Length];
        System.Array.Copy(a, result, a.Length);
        System.Array.Copy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
