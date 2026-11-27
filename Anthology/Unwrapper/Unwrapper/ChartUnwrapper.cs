// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>Output of <see cref="ChartUnwrapper.Unwrap"/>.</summary>
public sealed class ChartUnwrapResult
{
    /// <summary>One UV per triangle corner, indexed as <c>3*triangleIndex + cornerIndex</c>, packed into the unit square.</summary>
    public Double2[] PerCornerUVs { get; }

    /// <summary>The chart each triangle ended up in, or -1 for a triangle with no area, which gets no UVs.</summary>
    public int[] TriangleCharts { get; }

    public int ChartCount { get; }

    internal ChartUnwrapResult(Double2[] perCornerUVs, int[] triangleCharts, int chartCount)
    {
        PerCornerUVs = perCornerUVs;
        TriangleCharts = triangleCharts;
        ChartCount = chartCount;
    }
}

/// <summary>
/// Unwraps a mesh whose seams the caller has already cut. Vertices are taken as they are given: two triangles are
/// joined in UV space only where they share vertex indices, so a seam is made by giving each side its own copies of
/// the vertices along it. Every connected piece that has a border is flattened whole as one chart, however much it
/// stretches. A piece with no border cannot be laid flat, so it is cut into charts as <see cref="UnwrapMesh"/> would.
/// </summary>
public static class ChartUnwrapper
{
    private const double FlattenTolerance = 1e-8;
    private const double FlattenIterationBudget = 5.0;
    private const int OverlapRasterExtent = 512;
    private const double MinTriangleArea = 1e-14;

    /// <param name="positions">One position per vertex.</param>
    /// <param name="triangles">Flat triangle list, three vertex indices per triangle.</param>
    /// <param name="options">Only the pack margin and the conformal settings are used.</param>
    /// <param name="pins">
    /// Vertices to hold at given UVs. A piece with two or more of them is laid out around them and left where they
    /// put it, outside the packing. A piece with fewer is unwrapped and packed as if it had none.
    /// </param>
    public static ChartUnwrapResult Unwrap(Double3[] positions, int[] triangles, UnwrapOptions? options = null, IReadOnlyDictionary<int, Double2>? pins = null)
    {
        if (positions is null) throw new System.ArgumentNullException(nameof(positions));
        if (triangles is null) throw new System.ArgumentNullException(nameof(triangles));
        if (triangles.Length % 3 != 0) throw new System.ArgumentException("triangles length must be a multiple of 3.", nameof(triangles));
        options ??= new UnwrapOptions();

        int triangleCount = triangles.Length / 3;
        var perCorner = new Double2[triangles.Length];
        var triangleCharts = new int[triangleCount];
        for (int i = 0; i < triangleCount; ++i) triangleCharts[i] = -1;

        // Triangles without area are left out, and so are the vertices only they use
        var keptTriangles = new List<int>(triangleCount);
        var vertexSlot = new int[positions.Length];
        for (int i = 0; i < vertexSlot.Length; ++i) vertexSlot[i] = -1;
        var flatPositions = new List<double>(positions.Length * 3);
        var flatTriangles = new List<int>(triangles.Length);
        var slotSource = new List<int>(positions.Length);
        for (int t = 0; t < triangleCount; ++t)
        {
            int a = triangles[3 * t], b = triangles[3 * t + 1], c = triangles[3 * t + 2];
            if (a == b || b == c || a == c) continue;
            double area = 0.5 * Double3.Length(Double3.Cross(positions[b] - positions[a], positions[c] - positions[a]));
            if (!(area > MinTriangleArea)) continue;

            keptTriangles.Add(t);
            for (int k = 0; k < 3; ++k)
            {
                int v = triangles[3 * t + k];
                if (vertexSlot[v] < 0)
                {
                    slotSource.Add(v);
                    vertexSlot[v] = flatPositions.Count / 3;
                    flatPositions.Add(positions[v].X);
                    flatPositions.Add(positions[v].Y);
                    flatPositions.Add(positions[v].Z);
                }
                flatTriangles.Add(vertexSlot[v]);
            }
        }
        if (keptTriangles.Count == 0)
            return new ChartUnwrapResult(perCorner, triangleCharts, 0);

        var mesh = new HalfEdgeMesh();
        mesh.Build(flatPositions.Count / 3, flatPositions.ToArray(), keptTriangles.Count, flatTriangles.ToArray(), creaseAngleDegrees: 180.0);

        var regions = new List<MeshRegion>();
        HalfEdgeMesh.FindConnectedRegions(mesh, regions);

        var charts = new List<UvChart>(regions.Count);
        var pinnedCharts = new List<UvChart>();
        OverlapChecker? overlapChecker = null;
        var regionVertices = new List<int>();
        var regionLookup = new Dictionary<int, int>();
        foreach (MeshRegion region in regions)
        {
            Dictionary<int, Double2>? regionPins = null;
            if (pins != null && pins.Count > 0)
            {
                region.CollectVertices(regionVertices, regionLookup);
                foreach (int meshVertex in regionVertices)
                {
                    int source = slotSource[mesh.Vertices[meshVertex].SourceIndex];
                    if (pins.TryGetValue(source, out Double2 uv))
                        (regionPins ??= new Dictionary<int, Double2>())[meshVertex] = uv;
                }
            }
            if (regionPins is { Count: >= 2 } && TryFlattenWhole(region, pinnedCharts, regionPins))
                continue;
            if (HasBorder(region) && TryFlattenWhole(region, charts))
                continue;
            overlapChecker ??= new OverlapChecker(OverlapRasterExtent);
            UnwrapPipeline.ProcessRegion(region, charts, options, overlapChecker);
        }

        AtlasPacker.Pack(charts, options.PackMargin);
        charts.AddRange(pinnedCharts);

        for (int chartI = 0; chartI < charts.Count; ++chartI)
        {
            UvChart chart = charts[chartI];
            for (int f = 0; f < chart.Region!.Triangles.Length; ++f)
            {
                int source = keptTriangles[chart.Region.Triangles[f]];
                triangleCharts[source] = chartI;
                perCorner[3 * source + 0] = chart.UVs[3 * f + 0];
                perCorner[3 * source + 1] = chart.UVs[3 * f + 1];
                perCorner[3 * source + 2] = chart.UVs[3 * f + 2];
            }
        }
        return new ChartUnwrapResult(perCorner, triangleCharts, charts.Count);
    }

    /// <summary>
    /// Packs islands that are already laid out into the unit square, each given as three UVs per triangle and
    /// changed in place. Islands keep their turn and their size next to each other.
    /// </summary>
    public static void Pack(IReadOnlyList<Double2[]> islands, double margin = 1.0 / 256.0)
    {
        if (islands is null) throw new System.ArgumentNullException(nameof(islands));
        var charts = new List<UvChart>(islands.Count);
        foreach (Double2[] island in islands)
        {
            if (island.Length % 3 != 0) throw new System.ArgumentException("every island needs three UVs per triangle.", nameof(islands));
            if (island.Length > 0) charts.Add(new UvChart(island));
        }
        AtlasPacker.Pack(charts, margin);
    }

    private static bool HasBorder(MeshRegion region)
    {
        foreach (int triangle in region.Triangles)
        {
            HalfEdge edge = region.Mesh.Triangles[triangle].FirstEdge!;
            for (int k = 0; k < 3; ++k, edge = edge.Next!)
            {
                if (edge.Twin is null || MeshOps.IsBorderEdge(edge.Twin))
                    return true;
            }
        }
        return false;
    }

    // Flattens the region as one chart. False when the solve fails or gives no usable layout.
    private static bool TryFlattenWhole(MeshRegion region, List<UvChart> charts, IReadOnlyDictionary<int, Double2>? pins = null)
    {
        var chart = new UvChart(region);
        try
        {
            var flattener = new UvFlattener(region);
            flattener.Setup();
            flattener.BuildAngleSystem();
            flattener.SolveAngleSystem(FlattenTolerance, FlattenIterationBudget);
            flattener.SolveConformalLayout(FlattenTolerance, FlattenIterationBudget, pins);
            if (pins != null) flattener.ExtractUvs(chart);
            else flattener.ExtractAndFinaliseUvs(chart);
        }
        catch (System.Exception e) when (e is not System.OutOfMemoryException)
        {
            return false;
        }

        foreach (Double2 uv in chart.UVs)
        {
            if (double.IsNaN(uv.X) || double.IsNaN(uv.Y) || double.IsInfinity(uv.X) || double.IsInfinity(uv.Y))
                return false;
        }
        if (!(chart.UvArea > 0.0))
            return false;
        charts.Add(chart);
        return true;
    }
}
