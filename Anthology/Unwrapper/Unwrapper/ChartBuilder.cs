using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Builds UV charts from a mesh region using Lloyd-style iterative clustering.
/// Each chart maintains a planar "proxy" (a centre + normal) and grows greedily into the
/// best-fitting facets; an outer loop re-fits proxies and re-seeds until things settle.
/// </summary>
internal sealed class ChartBuilder
{
    private readonly double _smallChartAreaFraction;
    private readonly double _smallChartFaceFraction;
    private readonly double _compactnessPower;
    private readonly double _straightnessPower;
    private readonly double _stableChangeFraction;
    private readonly double _stableChangeFraction2;

    /// <summary>Candidate facet sitting on a chart's frontier; lower priority = added sooner.</summary>
    private struct Candidate
    {
        public int FaceIndex;
        public double Priority0;  // baseline used while sorting
        public double Priority;   // recomputed after each insertion

        public Candidate(int faceIndex, double priority)
        {
            FaceIndex = faceIndex;
            Priority0 = priority;
            Priority = priority;
        }
    }

    private sealed class ChartState
    {
        public Double3 ProxyNormal;
        public Double3 ProxyCentre;
        public int SeedFace;
        public int FaceCount;
        public double Area;
        public List<Candidate> Frontier = new();
        public bool FrontierDirty;

        public ChartState(HalfEdgeMesh m, int seed)
        {
            SeedFace = seed;
            ProxyNormal = m.FaceAttributes[seed].Normal;
            ProxyCentre = m.FaceAttributes[seed].Centroid;
        }
    }

    /// <summary>
    /// An adjacent pair of charts. After the Lloyd loop converges, these are ranked by how
    /// well a single proxy would still fit the union — the lowest-error pairs get merged.
    /// </summary>
    public sealed class ChartBoundary
    {
        public int First;
        public int Second;
        public Double3 FirstCentre;   // unused name: see initialiser
        public Double3 SecondCentre;
        public double FitError;
        public bool HasCrease;
    }

    private HalfEdgeMesh _workMesh = new();
    private MeshRegion _source = null!;
    private readonly List<ChartState> _charts = new();
    private int[] _faceToChart = System.Array.Empty<int>();
    private int[] _faceToChartPrev = System.Array.Empty<int>();
    private int[] _faceToChartPrev2 = System.Array.Empty<int>();
    private double _regionArea;

    public ChartBuilder(MeshRegion source, UnwrapOptions options)
    {
        _source = source;
        _smallChartAreaFraction = options.ChartAreaThreshold;
        _smallChartFaceFraction = options.ChartFacetCountThreshold;
        _compactnessPower = options.CompactnessPower;
        _straightnessPower = options.StraightnessPower;
        _stableChangeFraction = options.LloydChangePrevThreshold;
        _stableChangeFraction2 = options.LloydChangePrev2Threshold;
    }

    public void Initialise(double discardThreshold)
    {
        InitialiseCommon(discardThreshold);
        PruneTinyCharts();
    }

    /// <summary>Force at least two charts even on uniform regions (used during force-resegmentation).</summary>
    public void InitialiseWithForcedSplit(double discardThreshold)
    {
        InitialiseCommon(discardThreshold);
        while (_charts.Count == 1)
        {
            UpdateProxies();

            int seed = -1;
            double maxError = -1.0;

            for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
            {
                double err = NormalDeviation(0, faceI);
                if (err > maxError && faceI != _charts[0].SeedFace)
                {
                    seed = faceI;
                    maxError = err;
                }
            }

            if (seed == -1) return;
            _faceToChart[seed] = -1;
            SpawnChart(seed, discardThreshold);
        }
    }

    private void InitialiseCommon(double discardThreshold)
    {
        var vertexList = new List<int>(3 * _source.Triangles.Length);
        var vertexLookup = new Dictionary<int, int>(3 * _source.Triangles.Length);
        _source.CollectVertices(vertexList, vertexLookup);
        _workMesh.BuildFromRegion(_source, vertexList, vertexLookup);

        _regionArea = 0.0;
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
            _regionArea += _workMesh.FaceAttributes[faceI].Area;

        _faceToChart = new int[_workMesh.Triangles.Count];
        _faceToChartPrev = new int[_workMesh.Triangles.Count];
        _faceToChartPrev2 = new int[_workMesh.Triangles.Count];
        for (int i = 0; i < _faceToChart.Length; ++i)
        {
            _faceToChart[i] = -1;
            _faceToChartPrev[i] = -1;
            _faceToChartPrev2[i] = -1;
        }

        // Seed placement loop: spawn a chart at the worst-fitting facet, regrow, repeat until covered.
        int seedI = 0;
        while (seedI != -1)
        {
            SpawnChart(seedI, discardThreshold);
            UpdateProxies();
            RegrowChartFromSeed(_charts.Count - 1, discardThreshold);

            seedI = -1;
            double maxError = -1.0;
            for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
            {
                double err = NormalDeviation(_charts.Count - 1, faceI);
                if (_faceToChart[faceI] == -1 && NumericHelpers.ApproxLess(maxError, err, 1e-6))
                {
                    seedI = faceI;
                    maxError = err;
                }
            }
        }
    }

    /// <summary>
    /// Run <paramref name="innerIters"/> Lloyd iterations. Returns <c>false</c> when the
    /// segmentation has stabilised (most facets keep the same chart across iterations).
    /// </summary>
    public bool RunLloydPass(int innerIters, double discardThreshold)
    {
        for (int it = 0; it < innerIters; ++it)
        {
            UpdateProxies();
            ResnapSeeds();

            (_faceToChartPrev2, _faceToChartPrev) = (_faceToChartPrev, _faceToChartPrev2);
            (_faceToChart, _faceToChartPrev) = (_faceToChartPrev, _faceToChart);
            for (int i = 0; i < _faceToChart.Length; ++i) _faceToChart[i] = -1;

            for (int ci = 0; ci < _charts.Count; ++ci)
            {
                var chart = _charts[ci];
                chart.Area = 0.0;
                chart.FaceCount = 0;
                EnqueueFace(chart.SeedFace, ci);
            }
            GrowChartsFromFrontier(discardThreshold);
        }

        PruneTinyCharts();

        int changeThreshold1 = (int)(_workMesh.Triangles.Count * _stableChangeFraction) + 1;
        int changeThreshold2 = (int)(_workMesh.Triangles.Count * _stableChangeFraction2) + 1;
        int change1 = 0, change2 = 0;
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            if (_faceToChartPrev[faceI] != _faceToChart[faceI]) ++change1;
            if (_faceToChartPrev2[faceI] != _faceToChart[faceI]) ++change2;
        }
        return !(change1 < changeThreshold1 || change2 < changeThreshold2);
    }

    /// <summary>Claim any facets left without a chart by spawning fresh charts for them.</summary>
    public void ClaimRemainingFaces(double seedThreshold, double discardThreshold)
    {
        int prev = -1;
        while (prev != _charts.Count)
        {
            prev = _charts.Count;
            for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
            {
                if (_faceToChart[faceI] == -1)
                {
                    SpawnChart(faceI, seedThreshold);
                    UpdateProxies();
                    ResnapSeeds();
                    RegrowChartFromSeed(_charts.Count - 1, discardThreshold);
                }
            }
        }
    }

    /// <summary>Emit one <see cref="MeshRegion"/> per chart, sharing the source mesh.</summary>
    public void EmitRegions(List<MeshRegion> output)
    {
        int[] perChartCount = new int[_charts.Count];
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            int chartI = _faceToChart[faceI];
            if (chartI < 0) continue;
            ++perChartCount[chartI];
        }

        int chartStart = output.Count;
        for (int i = 0; i < _charts.Count; ++i)
            output.Add(new MeshRegion(_source.Mesh, perChartCount[i]));

        System.Array.Clear(perChartCount, 0, perChartCount.Length);
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            int chartI = _faceToChart[faceI];
            if (chartI < 0) continue;
            int slot = perChartCount[chartI]++;
            output[chartStart + chartI].Triangles[slot] = _source.Triangles[faceI];
        }
    }

    private void DropChart(int srcChartI, int dstChartI)
    {
        for (int faceI = 0; faceI < _faceToChart.Length; ++faceI)
        {
            if (_faceToChart[faceI] == srcChartI) _faceToChart[faceI] = dstChartI;
            else if (_faceToChart[faceI] > srcChartI) --_faceToChart[faceI];
        }

        if (dstChartI != -1)
            _charts[dstChartI].FaceCount += _charts[srcChartI].FaceCount;

        _charts.RemoveAt(srcChartI);
    }

    private void SpawnChart(int seedI, double discardThreshold)
    {
        _charts.Add(new ChartState(_workMesh, seedI));
        FloodFromSeed(seedI, _charts.Count - 1, discardThreshold);
    }

    private void FloodFromSeed(int seedI, int chartI, double discardThreshold)
    {
        EnqueueFace(seedI, chartI);
        GrowChartsFromFrontier(discardThreshold);
    }

    private void RegrowChartFromSeed(int chartI, double discardThreshold)
    {
        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            if (_faceToChart[faceI] == chartI) _faceToChart[faceI] = -1;
        }
        FloodFromSeed(_charts[chartI].SeedFace, chartI, discardThreshold);
    }

    private void EnqueueFace(int faceI, int chartI, double weight = 1.0)
    {
        _charts[chartI].Frontier.Add(new Candidate(faceI, weight * BasePriority(faceI, chartI)));
        _charts[chartI].FrontierDirty = true;
    }

    /// <summary>
    /// Multi-source flood: at each step pick the cheapest pending insertion across all charts,
    /// commit it, then refresh any newly-dirty queues. Best-first growth.
    /// </summary>
    private void GrowChartsFromFrontier(double discardThreshold)
    {
        var sharedTopChart = new List<int>();
        while (true)
        {
            sharedTopChart.Clear();
            const double errorEpsilon = 1e-6;

            double minError = 1e30;
            for (int i = 0; i < _charts.Count; ++i)
            {
                var chart = _charts[i];
                double chartError = chart.Frontier.Count > 0 ? chart.Frontier[^1].Priority : 1e32;
                if (NumericHelpers.ApproxLess(chartError, minError, errorEpsilon))
                    minError = chartError;
            }

            for (int i = 0; i < _charts.Count; ++i)
            {
                var chart = _charts[i];
                if (chart.Frontier.Count > 0 && System.Math.Abs(chart.Frontier[^1].Priority - minError) < errorEpsilon)
                    sharedTopChart.Add(i);
            }

            if (sharedTopChart.Count == 0) break;

            for (int gi = 0; gi < sharedTopChart.Count; ++gi)
            {
                int chartI = sharedTopChart[gi];
                var chart = _charts[chartI];

                Candidate target = chart.Frontier[^1];
                chart.Frontier.RemoveAt(chart.Frontier.Count - 1);

                if (NormalDeviation(chartI, target.FaceIndex) < discardThreshold
                    && _faceToChart[target.FaceIndex] == -1)
                {
                    chart.Area += _workMesh.FaceAttributes[target.FaceIndex].Area;
                    chart.FaceCount++;

                    _faceToChart[target.FaceIndex] = chartI;
                    ExtendFrontier(_workMesh.Triangles[target.FaceIndex]);

                    for (int ci = 0; ci < _charts.Count; ++ci)
                    {
                        var dirty = _charts[ci];
                        if (!dirty.FrontierDirty) continue;

                        for (int e = 0; e < dirty.Frontier.Count; ++e)
                        {
                            var v = dirty.Frontier[e];
                            v.Priority = StraightnessAdjustedPriority(v.FaceIndex, ci, v.Priority0);
                            dirty.Frontier[e] = v;
                        }
                        dirty.Frontier.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                        dirty.FrontierDirty = false;
                    }
                }
            }
        }
    }

    /// <summary>For each neighbour of <paramref name="face"/>, propose adding it to any adjacent chart.</summary>
    private void ExtendFrontier(MeshFace face)
    {
        HalfEdge edge = face.FirstEdge!;
        for (int n = 0; n < 3; ++n)
        {
            MeshFace? neighbour = edge.Twin!.Face;
            if (neighbour is not null && _faceToChart[_workMesh.IndexOf(neighbour)] == -1)
            {
                HalfEdge neighEdge = neighbour.FirstEdge!;
                for (int side = 0; side < 3; ++side)
                {
                    var info = _workMesh.EdgeAttributes[_workMesh.IndexOf(neighEdge)];
                    if (!info.IsCrease)
                    {
                        // Seam edges get a penalty so chart growth prefers to follow them.
                        double weight = info.IsUvSeam ? 2.0 : 1.0;
                        MeshFace? check = neighEdge.Twin!.Face;
                        if (check is not null && _faceToChart[_workMesh.IndexOf(check)] != -1)
                            EnqueueFace(_workMesh.IndexOf(neighbour), _faceToChart[_workMesh.IndexOf(check)], weight);
                    }
                    neighEdge = neighEdge.Next!;
                }
            }
            edge = edge.Next!;
        }
    }

    /// <summary>Drop charts that ended up too small to justify their own UV island.</summary>
    private void PruneTinyCharts()
    {
        double areaCutoff = _regionArea * _smallChartAreaFraction;
        int faceCutoff = (int)(_workMesh.Triangles.Count * _smallChartFaceFraction) + 1;

        for (int chartI = _charts.Count - 1; chartI >= 0; --chartI)
        {
            var chart = _charts[chartI];
            if (chart.Area < areaCutoff && chart.FaceCount < faceCutoff)
                DropChart(chartI, -1);
        }
    }

    private void UpdateProxies()
    {
        var fitters = new ProxyAccumulator[_charts.Count];
        for (int i = 0; i < fitters.Length; ++i) fitters[i].Begin();

        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            int chartI = _faceToChart[faceI];
            if (chartI < 0) continue;
            ref var info = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_workMesh.FaceAttributes)[faceI];
            fitters[chartI].Accept(info.Normal, info.Centroid, info.Area);
        }

        for (int chartI = 0; chartI < _charts.Count; ++chartI)
        {
            fitters[chartI].Finish();
            _charts[chartI].ProxyNormal = fitters[chartI].Normal;
            _charts[chartI].ProxyCentre = fitters[chartI].Centre;
        }
    }

    private void ResnapSeeds()
    {
        double[] error = new double[_charts.Count];
        for (int i = 0; i < error.Length; ++i)
        {
            error[i] = 1e32;
            _charts[i].SeedFace = -1;
        }

        for (int faceI = 0; faceI < _faceToChart.Length; ++faceI)
        {
            int chartI = _faceToChart[faceI];
            if (chartI < 0) continue;
            double cur = NormalDeviation(chartI, faceI);
            if (NumericHelpers.ApproxLess(cur, error[chartI], 1e-6))
            {
                error[chartI] = cur;
                _charts[chartI].SeedFace = faceI;
            }
        }

        // Re-fitting may have stranded a chart; drop those before continuing.
        for (int chartI = _charts.Count - 1; chartI >= 0; --chartI)
        {
            if (_charts[chartI].FaceCount == 0 || _charts[chartI].SeedFace == -1)
                _charts.RemoveAt(chartI);
        }
    }

    /// <summary>Squared deviation between two unit normals — used as a "fit" score.</summary>
    private static double NormalDeviation(Double3 n1, Double3 n2)
    {
        double dot = Double3.Dot(n1, n2);
        return (1.0 - dot) * (1.0 - dot);
    }

    private double NormalDeviation(int chartI, int faceI)
        => NormalDeviation(_charts[chartI].ProxyNormal, _workMesh.FaceAttributes[faceI].Normal);

    /// <summary>Quadrature of squared in-plane distance from triangle vertices to the chart's proxy plane.</summary>
    private double InPlaneDistanceSq(int chartI, int faceI)
    {
        MeshFace f = _workMesh.Triangles[faceI];
        Double3 v0 = f.FirstEdge!.Apex!.Position;
        Double3 v1 = f.FirstEdge!.Next!.Apex!.Position;
        Double3 v2 = f.FirstEdge!.Next!.Next!.Apex!.Position;

        double[] d = new double[3];
        double dist = 0.0;
        var chart = _charts[chartI];
        for (int side = 0; side < 3; ++side)
        {
            Double3 p = side == 0 ? v0 : (side == 1 ? v1 : v2);
            Double3 offset = p - chart.ProxyCentre;
            offset -= Double3.Dot(offset, chart.ProxyNormal) * chart.ProxyNormal;
            double len2 = Double3.Dot(offset, offset);
            dist += len2;
            d[side] = System.Math.Sqrt(len2);
        }
        dist += d[0] * d[1] + d[1] * d[2] + d[2] * d[0];
        dist *= _workMesh.FaceAttributes[faceI].Area / 6.0;
        return dist;
    }

    /// <summary>Ratio of shared-vs-cut edges — pushes the segmentation toward contiguous charts.</summary>
    private double StraightnessRatio(int chartI, int faceI)
    {
        MeshFace f = _workMesh.Triangles[faceI];
        HalfEdge he0 = f.FirstEdge!;
        HalfEdge he1 = he0.Next!;
        HalfEdge he2 = he1.Next!;
        MeshFace?[] neighbours =
        {
            he0.Twin!.Face,
            he1.Twin!.Face,
            he2.Twin!.Face,
        };
        HalfEdge[] edges = { he0, he1, he2 };

        double outer = 0.0, inner = 0.0;
        for (int side = 0; side < 3; ++side)
        {
            double len = _workMesh.EdgeAttributes[_workMesh.IndexOf(edges[side])].Length;
            if (neighbours[side] is not null && _faceToChart[_workMesh.IndexOf(neighbours[side]!)] == chartI)
                inner += len;
            else
                outer += len;
        }
        return inner > 0.0 ? outer / inner : 0.0;
    }

    private double BasePriority(int faceI, int chartI)
        => NormalDeviation(chartI, faceI) * System.Math.Pow(InPlaneDistanceSq(chartI, faceI), _compactnessPower);

    private double StraightnessAdjustedPriority(int faceI, int chartI, double basePriority)
        => basePriority * System.Math.Pow(StraightnessRatio(chartI, faceI), _straightnessPower);

    // ---- Boundary collection + chart merging ------------------------------------------------

    /// <summary>Find every adjacent chart pair and rank by merged-proxy fit error.</summary>
    public void CollectChartBoundaries(List<ChartBoundary> output)
    {
        output.Clear();
        var byKey = new Dictionary<long, int>();

        for (int faceI = 0; faceI < _workMesh.Triangles.Count; ++faceI)
        {
            HalfEdge edge = _workMesh.Triangles[faceI].FirstEdge!;
            for (int side = 0; side < 3; ++side)
            {
                if (!MeshOps.IsBorderEdge(edge) && !MeshOps.IsBorderEdge(edge.Twin!))
                {
                    int chart1 = _faceToChart[_workMesh.IndexOf(edge.Face!)];
                    int chart2 = _faceToChart[_workMesh.IndexOf(edge.Twin!.Face!)];

                    int a = System.Math.Min(chart1, chart2);
                    int b = System.Math.Max(chart1, chart2);

                    if (a != b)
                    {
                        long key = ((long)b << 32) | (uint)a;
                        if (!byKey.TryGetValue(key, out int slot))
                        {
                            slot = output.Count;
                            byKey[key] = slot;
                            output.Add(NewBoundary(chart1, chart2));
                        }
                        if (_workMesh.EdgeAttributes[_workMesh.IndexOf(edge)].IsCrease)
                            output[slot].HasCrease = true;
                    }
                }
                edge = edge.Next!;
            }
        }

        foreach (var b in output) b.FitError = MeasureMergeError(b);

        // Geometric pre-sort + stable sort by fit error.
        output.Sort((a, b) =>
        {
            const double eps = 1e-6;
            int Cmp(double x, double y) => System.Math.Abs(x - y) > eps ? (x > y ? 1 : -1) : 0;
            int r;
            if ((r = Cmp(a.FirstCentre.X, b.FirstCentre.X)) != 0) return r;
            if ((r = Cmp(a.FirstCentre.Y, b.FirstCentre.Y)) != 0) return r;
            if ((r = Cmp(a.FirstCentre.Z, b.FirstCentre.Z)) != 0) return r;
            if ((r = Cmp(a.SecondCentre.X, b.SecondCentre.X)) != 0) return r;
            if ((r = Cmp(a.SecondCentre.Y, b.SecondCentre.Y)) != 0) return r;
            if ((r = Cmp(a.SecondCentre.Z, b.SecondCentre.Z)) != 0) return r;
            return 0;
        });
        var indexed = new List<(ChartBoundary Info, int Order)>(output.Count);
        for (int i = 0; i < output.Count; ++i) indexed.Add((output[i], i));
        indexed.Sort((a, b) =>
        {
            int r = a.Info.FitError.CompareTo(b.Info.FitError);
            return r != 0 ? r : a.Order.CompareTo(b.Order);
        });
        for (int i = 0; i < indexed.Count; ++i) output[i] = indexed[i].Info;
    }

    private ChartBoundary NewBoundary(int c1, int c2)
    {
        ChartState ch1 = _charts[c1];
        ChartState ch2 = _charts[c2];
        var b = new ChartBoundary { First = c1, Second = c2, FirstCentre = ch1.ProxyCentre, SecondCentre = ch2.ProxyCentre };
        if (b.First > b.Second)
        {
            (b.First, b.Second) = (b.Second, b.First);
            (b.FirstCentre, b.SecondCentre) = (b.SecondCentre, b.FirstCentre);
        }
        return b;
    }

    public MeshRegion BuildMergedRegion(ChartBoundary boundary)
    {
        var faces = new List<int>();
        for (int faceI = 0; faceI < _faceToChart.Length; ++faceI)
        {
            if (_faceToChart[faceI] == boundary.First || _faceToChart[faceI] == boundary.Second)
                faces.Add(faceI);
        }
        var region = new MeshRegion(_source.Mesh, faces.Count);
        for (int i = 0; i < faces.Count; ++i)
            region.Triangles[i] = _source.Triangles[faces[i]];
        return region;
    }

    public void MergeAcross(List<ChartBoundary> boundaries, int targetIndex)
    {
        int kept = boundaries[targetIndex].First;
        int removed = boundaries[targetIndex].Second;
        DropChart(removed, kept);

        // Chart indices shifted after removal; patch every boundary in place.
        foreach (var b in boundaries)
        {
            if (b.First > removed) b.First -= 1;
            else if (b.First == removed) b.First = kept;

            if (b.Second > removed) b.Second -= 1;
            else if (b.Second == removed) b.Second = kept;

            if (b.First > b.Second) (b.First, b.Second) = (b.Second, b.First);
        }

        UpdateProxies();
        ResnapSeeds();

        foreach (var b in boundaries) b.FitError = MeasureMergeError(b);

        var tail = boundaries.GetRange(targetIndex + 1, boundaries.Count - (targetIndex + 1));
        var indexed = new List<(ChartBoundary Info, int Order)>(tail.Count);
        for (int i = 0; i < tail.Count; ++i) indexed.Add((tail[i], i));
        indexed.Sort((a, b) =>
        {
            int r = a.Info.FitError.CompareTo(b.Info.FitError);
            return r != 0 ? r : a.Order.CompareTo(b.Order);
        });
        for (int i = 0; i < tail.Count; ++i) boundaries[targetIndex + 1 + i] = indexed[i].Info;
    }

    private double MeasureMergeError(ChartBoundary b)
    {
        if (b.HasCrease) return 1e32;

        var fitter = new ProxyAccumulator();
        fitter.Begin();
        for (int faceI = 0; faceI < _faceToChart.Length; ++faceI)
        {
            if (_faceToChart[faceI] == b.First || _faceToChart[faceI] == b.Second)
            {
                var fi = _workMesh.FaceAttributes[faceI];
                fitter.Accept(fi.Normal, fi.Centroid, fi.Area);
            }
        }
        // A double-sided fan would average to a zero proxy normal — reject the merge.
        if (Double3.Length(fitter.Normal) < NumericHelpers.Tiny) return 1e32;
        fitter.Finish();

        Double3 mergedNormal = fitter.Normal;
        double mergedError = 0.0, mergedArea = 0.0;
        for (int faceI = 0; faceI < _faceToChart.Length; ++faceI)
        {
            if (_faceToChart[faceI] == b.First || _faceToChart[faceI] == b.Second)
            {
                var fi = _workMesh.FaceAttributes[faceI];
                double dev = NormalDeviation(mergedNormal, fi.Normal);
                mergedError += fi.Area * dev;
                mergedArea += fi.Area;
            }
        }
        return mergedError / mergedArea;
    }
}

/// <summary>Area-weighted average of triangle normals and centroids — yields a chart's proxy plane.</summary>
internal struct ProxyAccumulator
{
    public Double3 Normal;
    public Double3 Centre;
    public double TotalArea;

    public void Begin()
    {
        Normal = Centre = default;
        TotalArea = 0.0;
    }

    public void Finish()
    {
        Normal = Double3.Normalize(Normal);
        Centre /= TotalArea;
    }

    public void Accept(Double3 normal, Double3 centre, double area)
    {
        Normal += area * normal;
        Centre += area * centre;
        TotalArea += area;
    }
}
