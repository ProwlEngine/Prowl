using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>One placed instance: a stable id, its prototype, where it stands in the world, its turn about up, its scale across, up and across, the up it leans to, and its tint.</summary>
public readonly record struct TerrainInstance(ulong Id, int Prototype, Double3 Position, float Yaw, Float3 Scale, Float3 Up, Float4 Color);

/// <summary>Instances over a terrain in square cells, which <see cref="TerrainProps"/> keeps placed around a viewer.</summary>
public interface ITerrainInstances
{
    /// <summary>Metres a cell spans.</summary>
    double CellSize { get; }

    /// <summary>The world x and z where cell 0, 0 starts.</summary>
    Double2 Origin { get; }

    /// <summary>The instances whose base lies in a cell. Any thread.</summary>
    IReadOnlyList<TerrainInstance> Cell(int x, int z);

    /// <summary>Raised with a world x and z box whose cells changed, to be placed again.</summary>
    event Action<Double2, Double2>? Changed;
}

/// <summary>
/// The terrain's trees, see <see cref="TerrainData.Trees"/>, sorted into cells, each standing on the ground under it, and
/// following the trees and the ground as they change.
/// </summary>
public sealed class TerrainTreeInstances : ITerrainInstances, IDisposable
{
    private readonly TerrainData _data;
    private volatile Dictionary<(int, int), TerrainInstance[]> _cells = new();

    public TerrainTreeInstances(TerrainData data, double cellSize = 64)
    {
        _data = data;
        CellSize = cellSize;
        Sort();
        data.TreesChanged += OnTreesChanged;
        data.Edited += OnEdited;
    }

    public double CellSize { get; }

    public Double2 Origin => new(_data.Position.X, _data.Position.Z);

    public event Action<Double2, Double2>? Changed;

    public IReadOnlyList<TerrainInstance> Cell(int x, int z) => _cells.TryGetValue((x, z), out TerrainInstance[]? cell) ? cell : [];

    public void Dispose()
    {
        _data.TreesChanged -= OnTreesChanged;
        _data.Edited -= OnEdited;
    }

    private void OnEdited(TerrainEdited edited)
    {
        Sort();
        Changed?.Invoke(edited.Min, edited.Max);
    }

    private void OnTreesChanged()
    {
        Sort();
        Changed?.Invoke(Origin, Origin + new Double2(_data.SizeX, _data.SizeZ));
    }

    private void Sort()
    {
        Dictionary<(int, int), List<TerrainInstance>> cells = new();
        IReadOnlyList<TerrainTree> trees = _data.Trees;
        for (int i = 0; i < trees.Count; i++)
        {
            TerrainTree tree = trees[i];
            (int, int) cell = ((int)Math.Floor(tree.Position.X / CellSize), (int)Math.Floor(tree.Position.Z / CellSize));
            if (!cells.TryGetValue(cell, out List<TerrainInstance>? list))
                cells[cell] = list = new List<TerrainInstance>();
            Double3 at = _data.Position + tree.Position;
            if (_data.TryGetHeight(new Double2(at.X, at.Z), out double ground))
                at = new Double3(at.X, ground, at.Z);
            list.Add(new TerrainInstance((ulong)i, tree.Prototype, at, tree.Yaw,
                new Float3(tree.WidthScale, tree.HeightScale, tree.WidthScale), new Float3(0f, 1f, 0f), tree.Tint));
        }
        Dictionary<(int, int), TerrainInstance[]> sorted = new(cells.Count);
        foreach ((var key, List<TerrainInstance> list) in cells)
            sorted[key] = list.ToArray();
        _cells = sorted;
    }
}

/// <summary>
/// The terrain's mesh details grown from its density maps, see <see cref="TerrainDetailPrototype"/>: every finest quad
/// holds each prototype's density times its <see cref="TerrainDetailPrototype.Density"/> per square metre on average,
/// placed by an integer hash of the quad, so the same ground always grows the same details. Smooth noise picks each
/// one's size between the prototype's smallest and largest, and its tint between healthy and dry. Cells are cached until
/// an edit changes them. Safe to use from several threads.
/// </summary>
public sealed class TerrainDetailInstances : ITerrainInstances, IDisposable
{
    private readonly TerrainData _data;
    private readonly TerrainDetailPrototype[] _prototypes;
    private readonly ConcurrentDictionary<(int, int), TerrainInstance[]> _cells = new();
    private long _version;

    public TerrainDetailInstances(TerrainData data, IReadOnlyList<TerrainDetailPrototype> prototypes, double cellSize = 32)
    {
        _data = data;
        _prototypes = [.. prototypes];
        CellSize = cellSize;
        data.Edited += OnEdited;
    }

    public double CellSize { get; }

    public Double2 Origin => new(_data.Position.X, _data.Position.Z);

    public event Action<Double2, Double2>? Changed;

    public IReadOnlyList<TerrainInstance> Cell(int x, int z)
    {
        if (_cells.TryGetValue((x, z), out TerrainInstance[]? cached))
            return cached;
        long version = Interlocked.Read(ref _version);
        TerrainInstance[] built = Build(x, z);
        if (Interlocked.Read(ref _version) == version)
            _cells.TryAdd((x, z), built);
        return built;
    }

    public void Dispose() => _data.Edited -= OnEdited;

    private void OnEdited(TerrainEdited edited)
    {
        Interlocked.Increment(ref _version);
        _cells.Clear();
        Changed?.Invoke(edited.Min, edited.Max);
    }

    private TerrainInstance[] Build(int cx, int cz)
    {
        TerrainHeader header = _data.Header;
        double spacing = header.Spacing;
        int i0 = Math.Max((int)Math.Ceiling(cx * CellSize / spacing), 0), i1 = Math.Min((int)Math.Ceiling((cx + 1) * CellSize / spacing), header.QuadsX);
        int j0 = Math.Max((int)Math.Ceiling(cz * CellSize / spacing), 0), j1 = Math.Min((int)Math.Ceiling((cz + 1) * CellSize / spacing), header.QuadsZ);
        List<TerrainInstance> instances = new();
        (int pagesX, int pagesZ) = _data.PagesAt(0);
        for (int j = j0; j < j1; j++)
            for (int i = i0; i < i1; i++)
            {
                int px = Math.Min(i / TerrainPage.Quads, pagesX - 1), pz = Math.Min(j / TerrainPage.Quads, pagesZ - 1);
                if (_data.GetPage(new TerrainPageKey(0, px, pz)) is not { Details: not null } page)
                    continue;
                int index = TerrainPage.Index(i - px * TerrainPage.Quads, j - pz * TerrainPage.Quads);
                if (page.IsHole(index))
                    continue;
                for (int p = 0; p < _prototypes.Length; p++)
                {
                    TerrainDetailPrototype prototype = _prototypes[p];
                    if (prototype.Mesh == null || prototype.Density <= 0)
                        continue;
                    double expected = page.Density(p, index) * prototype.Density * spacing * spacing;
                    if (expected <= 0)
                        continue;
                    ulong quad = Mix((ulong)(uint)i * 0x9E3779B97F4A7C15ul ^ (ulong)(uint)j * 0xC2B2AE3D27D4EB4Ful ^ (ulong)p * 0xD1B54A32D192ED03ul ^ header.Seed);
                    int count = (int)Math.Floor(expected) + (Unit(quad) < expected - Math.Floor(expected) ? 1 : 0);
                    for (int k = 0; k < count; k++)
                    {
                        ulong h = Mix(quad + (ulong)k * 0x9E3779B97F4A7C15ul);
                        Double2 xz = new(_data.Position.X + (i + Unit(h)) * spacing, _data.Position.Z + (j + Unit(Mix(h ^ 1))) * spacing);
                        if (!_data.TryGetHeight(xz, out double height) || !_data.TryGetNormal(xz, out Float3 normal))
                            continue;
                        float t = (float)Math.Clamp(Noise(xz.X / prototype.NoiseSize, xz.Y / prototype.NoiseSize, (ulong)p) * 0.5 + 0.5, 0, 1);
                        float width = prototype.MinWidth + (prototype.MaxWidth - prototype.MinWidth) * t;
                        float tall = prototype.MinHeight + (prototype.MaxHeight - prototype.MinHeight) * t;
                        Float3 up = Float3.Normalize(Maths.Lerp(new Float3(0f, 1f, 0f), normal, Math.Clamp(prototype.AlignToGround, 0f, 1f)));
                        Float4 color = Maths.Lerp(prototype.HealthyColor, prototype.DryColor, t);
                        ulong id = (ulong)(uint)i << 40 ^ (ulong)(uint)j << 16 ^ (ulong)p << 8 ^ (ulong)k;
                        instances.Add(new TerrainInstance(id, p, new Double3(xz.X, height, xz.Y), (float)(Unit(Mix(h ^ 2)) * Math.Tau),
                            new Float3(width, tall, width), up, color));
                    }
                }
            }
        return instances.ToArray();
    }

    private static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9ul;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBul;
        return x ^ x >> 31;
    }

    private static double Unit(ulong x) => (x >> 11) * (1.0 / (1ul << 53));

    // Smooth value noise from -1 to 1 with a feature every unit
    private static double Noise(double x, double z, ulong seed)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        double tx = x - fx, tz = z - fz;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        long ix = (long)fx, iz = (long)fz;
        double Corner(long a, long b) => Unit(Mix((ulong)a * 0x9E3779B97F4A7C15ul ^ (ulong)b * 0xC2B2AE3D27D4EB4Ful ^ seed * 0xD1B54A32D192ED03ul)) * 2 - 1;
        double c00 = Corner(ix, iz), c10 = Corner(ix + 1, iz), c01 = Corner(ix, iz + 1), c11 = Corner(ix + 1, iz + 1);
        return (c00 + (c10 - c00) * tx) * (1 - tz) + (c01 + (c11 - c01) * tx) * tz;
    }
}
