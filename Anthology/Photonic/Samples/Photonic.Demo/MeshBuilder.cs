using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Photonic.Demo;

/// <summary>
/// Accumulates procedural geometry into a <see cref="LoadedModel"/>. Lightmap UVs are emitted in
/// world units per chart; <see cref="Build"/> shelf-packs every chart into [0,1]^2 at one shared
/// scale, so texel density stays uniform across the whole model and Photonic's auto packer sees a
/// single world-to-UV ratio.
/// </summary>
internal sealed class MeshBuilder
{
    // Gap between charts, in world units. Charts are packed in world space and then scaled into
    // [0,1], so a world-space gap lands as the same number of atlas texels on every model whatever
    // its size. The relative floor keeps tiny models from packing edge to edge.
    private const float ChartGapWorldUnits = 0.15f;
    private const float MinChartPadding = 0.002f;

    private readonly List<Float3> _positions = new();
    private readonly List<Float3> _normals = new();
    private readonly List<Float2> _uv0 = new();
    private readonly List<Float2> _lightmapUV = new();
    private readonly List<int> _chartStarts = new();
    private readonly List<LoadedModel.MaterialInfo> _materials = new();
    private readonly List<List<int>> _materialIndices = new();
    private readonly List<LoadedModel.TextureBlob?> _textures = new();
    private int _material;

    public int VertexCount => _positions.Count;

    public int AddMaterial(string name, Float3 baseColor, int diffuseTextureIndex = -1)
    {
        _materials.Add(new LoadedModel.MaterialInfo
        {
            Name = name,
            BaseColor = baseColor,
            DiffuseTextureIndex = diffuseTextureIndex,
        });
        _materialIndices.Add(new List<int>());
        return _materials.Count - 1;
    }

    public int AddTexture(LoadedModel.TextureBlob blob)
    {
        _textures.Add(blob);
        return _textures.Count - 1;
    }

    public void UseMaterial(int index) => _material = index;

    /// <summary>Every vertex added from here on belongs to a new lightmap chart.</summary>
    public void BeginChart() => _chartStarts.Add(_positions.Count);

    public int Vertex(Float3 position, Float3 normal, Float2 uv0, Float2 lightmapUV)
    {
        _positions.Add(position);
        _normals.Add(normal);
        _uv0.Add(uv0);
        _lightmapUV.Add(lightmapUV);
        return _positions.Count - 1;
    }

    public void Triangle(int a, int b, int c)
    {
        if (_materials.Count == 0) AddMaterial("Default", new Float3(0.7f, 0.7f, 0.7f));
        var list = _materialIndices[_material];
        list.Add(a);
        list.Add(b);
        list.Add(c);
    }

    public void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    /// <summary>
    /// Recompute normals by area-weighted face averaging, welded on quantised position so charts
    /// that duplicate their seam vertices still end up smooth across the seam.
    /// </summary>
    public void SmoothNormals()
    {
        var accum = new Dictionary<(int, int, int), Float3>(_positions.Count);
        foreach (var list in _materialIndices)
        {
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                var p0 = _positions[list[i]];
                var p1 = _positions[list[i + 1]];
                var p2 = _positions[list[i + 2]];
                var faceNormal = Float3.Cross(p1 - p0, p2 - p0);
                for (int c = 0; c < 3; c++)
                {
                    var key = PositionKey(_positions[list[i + c]]);
                    accum.TryGetValue(key, out var sum);
                    accum[key] = sum + faceNormal;
                }
            }
        }

        for (int i = 0; i < _positions.Count; i++)
        {
            if (!accum.TryGetValue(PositionKey(_positions[i]), out var n)) continue;
            if (Float3.LengthSquared(n) < 1e-12f) continue;
            _normals[i] = Float3.Normalize(n);
        }
    }

    public LoadedModel Build(string name)
    {
        if (_materials.Count == 0) AddMaterial("Default", new Float3(0.7f, 0.7f, 0.7f));

        var indices = new List<int>(_positions.Count * 3);
        var slices = new List<LoadedModel.SubMeshSlice>(_materials.Count);
        for (int m = 0; m < _materialIndices.Count; m++)
        {
            var list = _materialIndices[m];
            if (list.Count == 0) continue;
            slices.Add(new LoadedModel.SubMeshSlice
            {
                IndexStart = indices.Count,
                IndexCount = list.Count,
                MaterialIndex = m,
            });
            indices.AddRange(list);
        }

        var packedUV = PackCharts();
        return new LoadedModel
        {
            SourcePath = $"(procedural)/{name}",
            DisplayName = name,
            Positions = _positions.ToArray(),
            Normals = _normals.ToArray(),
            UV0 = _uv0.ToArray(),
            Indices = indices.ToArray(),
            SubMeshes = slices.ToArray(),
            Materials = _materials.ToArray(),
            Textures = _textures.ToArray(),
            BestExistingUV = packedUV,
            HasDedicatedUV = true,
        };
    }

    private static (int, int, int) PositionKey(Float3 p) => (
        (int)MathF.Round(p.X * 2048f),
        (int)MathF.Round(p.Y * 2048f),
        (int)MathF.Round(p.Z * 2048f));

    /// <summary>
    /// Shelf-pack the charts into the unit square. Scale is shared by every chart: halve from 1
    /// until the layout fits, then bisect upward for a tighter fit.
    /// </summary>
    private Float2[] PackCharts()
    {
        int n = _positions.Count;
        var packed = new Float2[n];
        if (n == 0) return packed;

        var starts = new List<int>(_chartStarts);
        if (starts.Count == 0 || starts[0] != 0) starts.Insert(0, 0);

        int chartCount = starts.Count;
        var min = new Float2[chartCount];
        var size = new Float2[chartCount];
        for (int c = 0; c < chartCount; c++)
        {
            int from = starts[c];
            int to = c + 1 < chartCount ? starts[c + 1] : n;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = from; i < to; i++)
            {
                var uv = _lightmapUV[i];
                if (uv.X < minX) minX = uv.X;
                if (uv.Y < minY) minY = uv.Y;
                if (uv.X > maxX) maxX = uv.X;
                if (uv.Y > maxY) maxY = uv.Y;
            }
            if (to <= from) { minX = minY = maxX = maxY = 0f; }
            min[c] = new Float2(minX, minY);
            size[c] = new Float2(MathF.Max(maxX - minX, 1e-5f), MathF.Max(maxY - minY, 1e-5f));
        }

        var order = new int[chartCount];
        for (int i = 0; i < chartCount; i++) order[i] = i;
        Array.Sort(order, (a, b) => size[b].Y.CompareTo(size[a].Y));

        var origin = new Float2[chartCount];
        float scale = 1f;
        for (int guard = 0; guard < 80 && !TryShelfPack(order, size, scale, origin); guard++)
            scale *= 0.5f;

        float low = scale, high = scale * 2f;
        for (int i = 0; i < 20; i++)
        {
            float mid = (low + high) * 0.5f;
            if (TryShelfPack(order, size, mid, origin)) low = mid; else high = mid;
        }
        TryShelfPack(order, size, low, origin);

        for (int c = 0; c < chartCount; c++)
        {
            int from = starts[c];
            int to = c + 1 < chartCount ? starts[c + 1] : n;
            for (int i = from; i < to; i++)
                packed[i] = (_lightmapUV[i] - min[c]) * low + origin[c];
        }
        return packed;
    }

    private static bool TryShelfPack(int[] order, Float2[] size, float scale, Float2[] origin)
    {
        float pad = MathF.Max(ChartGapWorldUnits * scale, MinChartPadding);
        float x = pad, y = pad, rowHeight = 0f;
        bool fits = true;
        for (int oi = 0; oi < order.Length; oi++)
        {
            int c = order[oi];
            float w = size[c].X * scale;
            float h = size[c].Y * scale;
            if (x + w + pad > 1f)
            {
                y += rowHeight + pad;
                x = pad;
                rowHeight = 0f;
            }
            if (x + w + pad > 1f || y + h + pad > 1f) fits = false;
            origin[c] = new Float2(x, y);
            x += w + pad;
            if (h > rowHeight) rowHeight = h;
        }
        return fits;
    }
}
