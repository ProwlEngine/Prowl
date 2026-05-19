using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Rasterises a chart at low resolution to detect UV overlaps, then decides which sub-regions
/// can be re-flattened in place and which need full re-segmentation.
/// </summary>
internal sealed class OverlapChecker
{
    private readonly int _tagBufferExtent;
    private readonly int _countBufferExtent;
    private readonly int[] _tagBuffer;
    private readonly byte[] _countBuffer;
    private readonly int[] _scanLeft;
    private readonly int[] _scanRight;
    private byte[] _faceMarkedOverlapping = System.Array.Empty<byte>();

    public OverlapChecker(int rasterExtent)
    {
        _tagBufferExtent = rasterExtent;
        _countBufferExtent = rasterExtent;
        _tagBuffer = new int[rasterExtent * rasterExtent];
        _countBuffer = new byte[rasterExtent * rasterExtent];
        _scanLeft = new int[rasterExtent];
        _scanRight = new int[rasterExtent];
    }

    /// <summary>
    /// Returns <c>true</c> if the chart has no overlaps. Otherwise <paramref name="reflattenList"/>
    /// receives sub-regions to re-flatten and <paramref name="resegmentList"/> receives sub-regions
    /// that need to go back through chart building.
    /// </summary>
    public bool Validate(UvChart chart, List<MeshRegion> reflattenList, List<MeshRegion> resegmentList)
    {
        var overlapping = new List<int>();
        DetectOverlaps(chart, overlapping);
        if (overlapping.Count == 0) return true;

        var faceLookup = new Dictionary<int, int>();
        chart.Region!.BuildTriangleLookup(faceLookup);

        var subRegions = new List<MeshRegion>();
        var subRegionFaceMap = new List<List<int>>();
        {
            var mapFaces = new int[overlapping.Count];
            for (int i = 0; i < overlapping.Count; ++i)
                mapFaces[i] = chart.Region.Triangles[overlapping[i]];

            HalfEdgeMesh.FindConnectedRegionsIn(chart.Region.Mesh, mapFaces, subRegions);
            for (int compI = 0; compI < subRegions.Count; ++compI)
            {
                var list = new List<int>();
                subRegions[compI].RemapTriangles(faceLookup, list);
                subRegionFaceMap.Add(list);
            }
        }

        FindUvBounds(chart, overlapping, out double uvExt, out Double2 uvMin);

        // Self-overlapping sub-regions can't be salvaged by cutting alone — they go back to segmentation.
        var selfOverlapped = new List<MeshRegion>();
        for (int compI = subRegions.Count - 1; compI >= 0; --compI)
        {
            System.Array.Clear(_countBuffer, 0, _countBufferExtent * _countBufferExtent);
            for (int facetI = 0; facetI < subRegions[compI].Triangles.Length; ++facetI)
            {
                int triI = subRegionFaceMap[compI][facetI];
                ProjectTriangle(chart, triI, _countBufferExtent, uvMin, uvExt, out Double2 p0, out Double2 p1, out Double2 p2);
                if (facetI > 0 && RasterCountTouchedPixels(p0, p1, p2) > 0)
                {
                    selfOverlapped.Add(subRegions[compI]);
                    subRegions.RemoveAt(compI);
                    subRegionFaceMap.RemoveAt(compI);
                    break;
                }
                RasterMarkArea(p0, p1, p2);
            }
        }

        int totalFaceCount = chart.Region.Triangles.Length;

        // For every remaining sub-region, count how many others overlap with it.
        var queue = new List<RankedOverlap>(subRegions.Count);
        var overlapsBetween = new Dictionary<int, HashSet<int>>();

        for (int compI = 0; compI < subRegions.Count; ++compI)
            queue.Add(new RankedOverlap(compI, totalFaceCount - subRegions[compI].Triangles.Length));

        for (int outer = 0; outer < queue.Count; ++outer)
        {
            System.Array.Clear(_countBuffer, 0, _countBufferExtent * _countBufferExtent);
            for (int facetI = 0; facetI < subRegions[outer].Triangles.Length; ++facetI)
            {
                int triI = subRegionFaceMap[outer][facetI];
                ProjectTriangle(chart, triI, _countBufferExtent, uvMin, uvExt, out Double2 p0, out Double2 p1, out Double2 p2);
                RasterMarkArea(p0, p1, p2);
            }

            for (int inner = 0; inner < queue.Count; ++inner)
            {
                if (inner == outer) continue;
                for (int facetI = 0; facetI < subRegions[inner].Triangles.Length; ++facetI)
                {
                    int triI = subRegionFaceMap[inner][facetI];
                    ProjectTriangle(chart, triI, _countBufferExtent, uvMin, uvExt, out Double2 p0, out Double2 p1, out Double2 p2);
                    if (RasterCountTouchedPixels(p0, p1, p2) > 0)
                    {
                        var info = queue[outer];
                        info.OverlapCount++;
                        queue[outer] = info;

                        if (!overlapsBetween.TryGetValue(outer, out var set))
                            overlapsBetween[outer] = set = new HashSet<int>();
                        set.Add(inner);
                        break;
                    }
                }
            }
        }

        // Greedy: cut the sub-regions that overlap the most others first, refreshing overlap sets each time.
        queue.Sort((a, b) =>
        {
            int r = a.OverlapCount.CompareTo(b.OverlapCount);
            return r != 0 ? r : a.SizeInverse.CompareTo(b.SizeInverse);
        });

        var cutRegions = new List<MeshRegion>();
        var cutRegionFaceMap = new List<List<int>>();
        for (int slot = queue.Count - 1; slot >= 0; --slot)
        {
            int compIndex = queue[slot].SubRegionIndex;
            if (overlapsBetween.TryGetValue(compIndex, out var targetSet) && targetSet.Count > 0)
            {
                cutRegions.Add(subRegions[compIndex]);
                subRegions[compIndex] = null!;
                cutRegionFaceMap.Add(subRegionFaceMap[compIndex]);
                subRegionFaceMap[compIndex] = null!;

                foreach (int other in targetSet)
                    if (overlapsBetween.TryGetValue(other, out var otherSet)) otherSet.Remove(compIndex);
            }
        }

        var unionForCut = new List<MeshRegion>();
        unionForCut.AddRange(selfOverlapped);
        unionForCut.AddRange(cutRegions);

        if (unionForCut.Count == 0) return true;

        var remaining = new List<MeshRegion>();
        HalfEdgeMesh.FindRegionsExcluding(chart.Region.Mesh, chart.Region.Triangles, unionForCut, remaining);

        reflattenList.AddRange(remaining);
        reflattenList.AddRange(cutRegions);
        resegmentList.AddRange(selfOverlapped);

        return false;
    }

    private void DetectOverlaps(UvChart chart, List<int> overlapping)
    {
        int faceCount = chart.Region!.Triangles.Length;
        _faceMarkedOverlapping = new byte[faceCount];

        Double2 uvRect = chart.UvMax - chart.UvMin;
        double uvExt = uvRect.X > uvRect.Y ? uvRect.X : uvRect.Y;

        System.Array.Clear(_tagBuffer, 0, _tagBuffer.Length);
        for (int faceI = 0; faceI < faceCount; ++faceI)
        {
            ProjectTriangle(chart, faceI, _tagBufferExtent, chart.UvMin, uvExt, out Double2 p0, out Double2 p1, out Double2 p2);
            RasterTagAndDetect(faceI, p0, p1, p2);
        }

        for (int faceI = 0; faceI < faceCount; ++faceI)
            if (_faceMarkedOverlapping[faceI] != 0) overlapping.Add(faceI);
    }

    private static void ProjectTriangle(UvChart chart, int facetI, int extent, Double2 min, double ext, out Double2 p0, out Double2 p1, out Double2 p2)
    {
        double s = (double)(extent - 1) / ext;
        p0 = s * (chart.UVs[3 * facetI + 0] - min);
        p1 = s * (chart.UVs[3 * facetI + 1] - min);
        p2 = s * (chart.UVs[3 * facetI + 2] - min);
    }

    /// <summary>
    /// Compute per-row pixel ranges for a triangle. Y-sorted, slope-based, with deliberate
    /// half-pixel offsets to avoid double-counting shared edges between triangles.
    /// </summary>
    private void PrepareScanRanges(Double2 p0, Double2 p1, Double2 p2, out int startY, out int endY)
    {
        Double2[] apex = { p0, p1, p2 };
        if (apex[0].Y > apex[1].Y) (apex[0], apex[1]) = (apex[1], apex[0]);
        if (apex[0].Y > apex[2].Y) (apex[0], apex[2]) = (apex[2], apex[0]);
        if (apex[1].Y > apex[2].Y) (apex[1], apex[2]) = (apex[2], apex[1]);

        double[] dx = new double[3];
        if (apex[1].Y > apex[0].Y) dx[0] = (apex[1].X - apex[0].X) / (apex[1].Y - apex[0].Y);
        if (apex[2].Y > apex[0].Y) dx[1] = (apex[2].X - apex[0].X) / (apex[2].Y - apex[0].Y);
        if (apex[2].Y > apex[1].Y) dx[2] = (apex[2].X - apex[1].X) / (apex[2].Y - apex[1].Y);

        // Flat-top edge case: derive a tiny slope so the comparison below picks a side.
        if (dx[0] == 0.0 && apex[0].X != apex[1].X)
            dx[0] = apex[0].X < apex[1].X ? dx[1] + 1.0 : dx[1] - 0.00001;

        const double offset = 0.5 + NumericHelpers.FloatTiny;

        Double2 start = apex[0];
        Double2 end = apex[0];

        startY = (int)(apex[0].Y + offset);
        int midY = (int)apex[1].Y;
        endY = (int)(apex[2].Y - offset);

        if (dx[0] > dx[1])
        {
            for (int y = startY; y < midY; ++y)
            {
                _scanLeft[y] = (int)(start.X + offset);
                _scanRight[y] = (int)(end.X - offset) - 1;
                start.X += dx[1];
                end.X += dx[0];
            }
            end = apex[1];
            for (int y = midY; y < endY; ++y)
            {
                _scanLeft[y] = (int)(start.X + offset);
                _scanRight[y] = (int)(end.X - offset) - 1;
                start.X += dx[1];
                end.X += dx[2];
            }
        }
        else
        {
            for (int y = startY; y < midY; ++y)
            {
                _scanLeft[y] = (int)(start.X + offset);
                _scanRight[y] = (int)(end.X - offset) - 1;
                start.X += dx[0];
                end.X += dx[1];
            }
            start = apex[1];
            for (int y = midY; y < endY; ++y)
            {
                _scanLeft[y] = (int)(start.X + offset);
                _scanRight[y] = (int)(end.X - offset) - 1;
                start.X += dx[2];
                end.X += dx[1];
            }
        }

        endY = endY - 1;
    }

    private void RasterTagAndDetect(int tag, Double2 p0, Double2 p1, Double2 p2)
    {
        PrepareScanRanges(p0, p1, p2, out int startY, out int endY);
        for (int y = startY; y < endY; ++y)
        {
            for (int x = _scanLeft[y]; x < _scanRight[y]; ++x)
            {
                int curTag = _tagBuffer[y * _tagBufferExtent + x];
                if (curTag != 0)
                {
                    _faceMarkedOverlapping[curTag - 1] = 1;
                    _faceMarkedOverlapping[tag] = 1;
                }
                _tagBuffer[y * _tagBufferExtent + x] = tag + 1;
            }
        }
    }

    private int RasterCountTouchedPixels(Double2 p0, Double2 p1, Double2 p2)
    {
        PrepareScanRanges(p0, p1, p2, out int startY, out int endY);
        int count = 0;
        for (int y = startY; y < endY; ++y)
        {
            for (int x = _scanLeft[y]; x < _scanRight[y]; ++x)
                if (_countBuffer[y * _countBufferExtent + x] != 0) ++count;
        }
        return count;
    }

    private void RasterMarkArea(Double2 p0, Double2 p1, Double2 p2)
    {
        PrepareScanRanges(p0, p1, p2, out int startY, out int endY);
        for (int y = startY; y < endY; ++y)
        {
            if (_scanRight[y] > _scanLeft[y])
            {
                int from = y * _countBufferExtent + _scanLeft[y];
                int len = _scanRight[y] - _scanLeft[y];
                for (int i = 0; i < len; ++i) _countBuffer[from + i] = 0xFF;
            }
        }
    }

    private static void FindUvBounds(UvChart chart, IList<int> chartFaces, out double uvExt, out Double2 uvMin)
    {
        Double2 lo = new(1e32, 1e32), hi = new(-1e32, -1e32);
        for (int facetI = 0; facetI < chartFaces.Count; ++facetI)
        {
            int chartFaceI = chartFaces[facetI];
            for (int side = 0; side < 3; ++side)
            {
                Double2 uv = chart.UVs[3 * chartFaceI + side];
                if (uv.X < lo.X) lo.X = uv.X;
                if (uv.Y < lo.Y) lo.Y = uv.Y;
                if (uv.X > hi.X) hi.X = uv.X;
                if (uv.Y > hi.Y) hi.Y = uv.Y;
            }
        }
        Double2 rect = hi - lo;
        uvExt = rect.X > rect.Y ? rect.X : rect.Y;
        uvMin = lo;
    }

    private struct RankedOverlap
    {
        public int OverlapCount;
        public int SizeInverse;
        public int SubRegionIndex;

        public RankedOverlap(int subRegionIndex, int sizeInverse)
        {
            SubRegionIndex = subRegionIndex;
            SizeInverse = sizeInverse;
        }
    }
}
