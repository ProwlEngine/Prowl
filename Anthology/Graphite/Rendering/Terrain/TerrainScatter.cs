using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Where and how one kind of prop scatters across the terrain.</summary>
public sealed class TerrainScatterRule
{
    /// <summary>Names the rule in instance ids, so it should stay the same as rules are added or reordered.</summary>
    public int Id { get; init; }

    /// <summary>Lower places first: it claims the scatter points and space before the rules after it.</summary>
    public int Priority { get; init; }

    /// <summary>Instances per 100 square metres where every mask passes, before footprints thin them.</summary>
    public float Density { get; init; } = 1f;

    /// <summary>Metres across the space an instance keeps clear: two instances stand at least the average of their footprints apart.</summary>
    public float Footprint { get; init; } = 1f;

    /// <summary>The ground's rise per metre it accepts, 0 for flat.</summary>
    public float MinSteepness { get; init; }
    public float MaxSteepness { get; init; } = float.PositiveInfinity;

    /// <summary>The world heights it accepts.</summary>
    public double MinHeight { get; init; } = double.NegativeInfinity;
    public double MaxHeight { get; init; } = double.PositiveInfinity;

    /// <summary>The material layer it grows on, -1 for any, and how much of the ground that layer must cover.</summary>
    public int Layer { get; init; } = -1;
    public float MinLayerWeight { get; init; } = 0.5f;

    /// <summary>Metres across the patches of a noise mask, 0 for none, and the noise from 0 to 1 it needs to grow.</summary>
    public float NoiseSize { get; init; }
    public float NoiseThreshold { get; init; } = 0.5f;

    /// <summary>Picks the noise field, so rules sharing one grow in the same patches, such as the trees of one forest.</summary>
    public int NoiseSeed { get; init; }

    /// <summary>How far an instance's up leans from world up toward the ground's normal, 0 to 1.</summary>
    public float Align { get; init; }

    /// <summary>The range its uniform scale picks from.</summary>
    public float MinScale { get; init; } = 1f;
    public float MaxScale { get; init; } = 1f;

    /// <summary>Metres an instance sinks into the ground, times its scale.</summary>
    public float Sink { get; init; }
}

/// <summary>One scattered prop: its id, the index of its rule, where it stands, its turn about up, its scale and its up.</summary>
public readonly record struct TerrainInstance(ulong Id, int Rule, Double3 Position, float Yaw, float Scale, Float3 Up);

/// <summary>
/// Deterministic prop placement over a terrain in cells of <see cref="CellSize"/> metres. Every cell shares one seamless
/// tile of ranked blue noise points. Each point goes to the rule whose slice of the accumulated local densities its rank
/// falls in, so rules never stack and a rule masked out leaves its points to the next. The terrain's detail cut scales
/// every rule's density where it was painted. Footprints then thin the points:
/// one is dropped when an earlier one, by priority and then by id, stands too close, which needs only the points nearby,
/// so a cell always comes out the same whichever thread builds it and whatever was built before. Instances are cached
/// by cell. Safe to use from several threads.
/// </summary>
public sealed class TerrainScatter
{
    public const double CellSize = 64.0;

    /// <summary>The most instances the tile holds per 100 square metres, across all rules.</summary>
    public const double MaxDensity = TilePoints / (CellSize * CellSize) * 100.0;

    private const int TilePoints = 4096;

    private static readonly Lazy<(double[] X, double[] Z)> s_tile = new(BuildTile);

    private readonly TerrainData _data;
    private readonly TerrainScatterRule[] _rules;
    private readonly int[] _order;
    private readonly ulong _seed;
    private readonly double _margin;
    private readonly ConcurrentDictionary<(int, int), TerrainInstance[]> _cells = new();
    private readonly HashSet<ulong> _removed = new();
    private long _version;

    public TerrainScatter(TerrainData data, IReadOnlyList<TerrainScatterRule> rules, ulong seed = 0)
    {
        _data = data;
        _rules = [.. rules];
        _seed = seed;
        _order = new int[_rules.Length];
        for (int i = 0; i < _order.Length; i++)
            _order[i] = i;
        Array.Sort(_order, (a, b) => _rules[a].Priority != _rules[b].Priority ? _rules[a].Priority.CompareTo(_rules[b].Priority) : a.CompareTo(b));
        foreach (TerrainScatterRule rule in _rules)
            _margin = Math.Max(_margin, rule.Footprint);
    }

    public IReadOnlyList<TerrainScatterRule> Rules => _rules;

    public TerrainData Data => _data;

    /// <summary>The world x and z where cell 0, 0 starts.</summary>
    public Double2 Origin => new(_data.Position.X, _data.Position.Z);

    /// <summary>The cell holding a world x and z.</summary>
    public (int X, int Z) CellAt(Double2 xz) =>
        ((int)Math.Floor((xz.X - _data.Position.X) / CellSize), (int)Math.Floor((xz.Y - _data.Position.Z) / CellSize));

    /// <summary>The instances in a cell, in tile order, without the removed ones.</summary>
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

    /// <summary>The cells whose instances the ground in a world x and z box can move or thin, footprints included.</summary>
    public IEnumerable<(int X, int Z)> CellsTouching(Double2 min, Double2 max)
    {
        (int x0, int z0) = CellAt(new Double2(min.X - _margin, min.Y - _margin));
        (int x1, int z1) = CellAt(new Double2(max.X + _margin, max.Y + _margin));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                yield return (x, z);
    }

    /// <summary>Forgets the cached cells over a world x and z box, for when the ground under it changed.</summary>
    public void Invalidate(Double2 min, Double2 max)
    {
        Interlocked.Increment(ref _version);
        foreach ((int x, int z) in CellsTouching(min, max))
            _cells.TryRemove((x, z), out _);
    }

    /// <summary>Leaves an instance out from now on, every other instance staying where it was.</summary>
    public void Remove(ulong id)
    {
        lock (_removed)
            _removed.Add(id);
        _cells.TryRemove(CellOf(id), out _);
    }

    /// <summary>Brings back every removed instance.</summary>
    public void ClearRemoved()
    {
        lock (_removed)
            _removed.Clear();
        _cells.Clear();
    }

    /// <summary>Forgets cached cells, for when the terrain or its position changes.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _cells.Clear();
    }

    /// <summary>The cell an instance id came from.</summary>
    public static (int X, int Z) CellOf(ulong id) => ((int)((long)id >> 40), (int)((long)(id << 24) >> 40));

    private static ulong IdOf(int cx, int cz, int rank) =>
        (ulong)(uint)cx << 40 | ((ulong)(uint)cz & 0xFFFFFF) << 16 | (uint)rank;

    private struct Candidate
    {
        public int Rule, Rank, Cx, Cz;
        public double X, Z, Height;
        public Float3 Normal;
        public ulong Id, Order;
    }

    private TerrainInstance[] Build(int cx, int cz)
    {
        List<Candidate> own = new(), near = new();
        for (int nz = -1; nz <= 1; nz++)
            for (int nx = -1; nx <= 1; nx++)
                Gather(cx + nx, cz + nz, nx == 0 && nz == 0 ? own : near, nx == 0 && nz == 0 ? null : (cx, cz));
        if (own.Count == 0)
            return [];

        double bucket = Math.Max(_margin, 1.0);
        Dictionary<(int, int), List<int>> grid = new();
        List<Candidate> all = new(own.Count + near.Count);
        all.AddRange(own);
        all.AddRange(near);
        for (int i = 0; i < all.Count; i++)
        {
            (int, int) key = ((int)Math.Floor(all[i].X / bucket), (int)Math.Floor(all[i].Z / bucket));
            if (!grid.TryGetValue(key, out List<int>? list))
                grid[key] = list = new List<int>();
            list.Add(i);
        }

        List<TerrainInstance> kept = new(own.Count);
        for (int i = 0; i < own.Count; i++)
        {
            Candidate c = own[i];
            if (Crowded(c, all, grid, bucket))
                continue;
            lock (_removed)
            {
                if (_removed.Contains(c.Id))
                    continue;
            }
            kept.Add(Instance(c));
        }
        return [.. kept];
    }

    // Whether an earlier candidate stands within the average of the two footprints
    private bool Crowded(in Candidate c, List<Candidate> all, Dictionary<(int, int), List<int>> grid, double bucket)
    {
        int bx = (int)Math.Floor(c.X / bucket), bz = (int)Math.Floor(c.Z / bucket);
        float footprint = _rules[c.Rule].Footprint;
        for (int z = bz - 1; z <= bz + 1; z++)
            for (int x = bx - 1; x <= bx + 1; x++)
            {
                if (!grid.TryGetValue((x, z), out List<int>? list))
                    continue;
                foreach (int j in list)
                {
                    Candidate o = all[j];
                    if (o.Order > c.Order || (o.Order == c.Order && o.Id >= c.Id))
                        continue;
                    double apart = (footprint + _rules[o.Rule].Footprint) * 0.5;
                    double dx = o.X - c.X, dz = o.Z - c.Z;
                    if (dx * dx + dz * dz < apart * apart)
                        return true;
                }
            }
        return false;
    }

    private TerrainInstance Instance(in Candidate c)
    {
        TerrainScatterRule rule = _rules[c.Rule];
        ulong hash = Mix(c.Id ^ _seed * 0x9E3779B97F4A7C15ul ^ 0xA5A5A5A5ul);
        float yaw = (float)(Unit(hash) * Math.PI * 2.0);
        float scale = rule.MinScale + (rule.MaxScale - rule.MinScale) * (float)Unit(Mix(hash));
        Float3 up = rule.Align <= 0f ? new Float3(0f, 1f, 0f) : Float3.Normalize(new Float3(0f, 1f, 0f) * (1f - rule.Align) + c.Normal * rule.Align);
        Double3 at = new(c.X + _data.Position.X, c.Height - rule.Sink * scale, c.Z + _data.Position.Z);
        return new TerrainInstance(c.Id, c.Rule, at, yaw, scale, up);
    }

    // Adds a cell's candidates, or with a target cell only those within the largest footprint of it
    private void Gather(int cx, int cz, List<Candidate> into, (int X, int Z)? near)
    {
        (double[] tx, double[] tz) = s_tile.Value;
        double ox = cx * CellSize, oz = cz * CellSize;
        double minX = 0, minZ = 0, maxX = 0, maxZ = 0;
        if (near is (int nx, int nz))
        {
            minX = nx * CellSize - _margin;
            minZ = nz * CellSize - _margin;
            maxX = (nx + 1) * CellSize + _margin;
            maxZ = (nz + 1) * CellSize + _margin;
        }
        for (int rank = 0; rank < TilePoints; rank++)
        {
            double x = ox + tx[rank], z = oz + tz[rank];
            if (near != null && (x < minX || x >= maxX || z < minZ || z >= maxZ))
                continue;
            if (TryClaim(cx, cz, rank, x, z, out Candidate c))
                into.Add(c);
        }
    }

    // Walks the rules in priority order adding up their local densities until the point's rank falls in one's slice
    private bool TryClaim(int cx, int cz, int rank, double x, double z, out Candidate candidate)
    {
        candidate = default;
        double u = (rank + 0.5) / TilePoints * MaxDensity;
        double sum = 0;
        bool sampled = false;
        double height = 0;
        Float3 normal = default;
        double keep = -1;
        foreach (int r in _order)
        {
            TerrainScatterRule rule = _rules[r];
            if (rule.Density <= 0f || !Passes(rule, x, z, ref sampled, ref height, ref normal))
                continue;
            if (keep < 0)
                keep = _data.TryGetDetailCut(new Double2(x + _data.Position.X, z + _data.Position.Z), out float cut) ? 1 - cut : 1;
            sum += rule.Density * keep;
            if (u >= sum)
                continue;
            ulong id = IdOf(cx, cz, rank);
            candidate = new Candidate
            {
                Rule = r, Rank = rank, Cx = cx, Cz = cz, X = x, Z = z, Height = height, Normal = normal, Id = id,
                Order = (ulong)(uint)(rule.Priority - int.MinValue) << 32 | (uint)(Mix(id ^ _seed) >> 32),
            };
            return true;
        }
        return false;
    }

    private bool Passes(TerrainScatterRule rule, double x, double z, ref bool sampled, ref double height, ref Float3 normal)
    {
        Double2 world = new(x + _data.Position.X, z + _data.Position.Z);
        if (!sampled)
        {
            if (!_data.TryGetHeight(world, out height) || !_data.TryGetNormal(world, out normal))
                return false;
            sampled = true;
        }
        if (height < rule.MinHeight || height > rule.MaxHeight)
            return false;
        double steepness = Math.Sqrt(normal.X * normal.X + normal.Z * normal.Z) / Math.Max(normal.Y, 1e-6);
        if (steepness < rule.MinSteepness || steepness > rule.MaxSteepness)
            return false;
        if (rule.Layer >= 0 && (!_data.TryGetLayerWeight(world, rule.Layer, out float weight) || weight < rule.MinLayerWeight))
            return false;
        if (rule.NoiseSize > 0f && Noise(x / rule.NoiseSize, z / rule.NoiseSize, (ulong)rule.NoiseSeed) < rule.NoiseThreshold)
            return false;
        return true;
    }

    // Three octaves of gradient noise from about 0 to 1, each turned against the last so patches follow no grid
    private double Noise(double x, double z, ulong salt)
    {
        ulong s = Mix(_seed ^ salt * 0xD1B54A32D192ED03ul);
        double sum = 0, amplitude = 0.6;
        for (int octave = 0; octave < 3; octave++)
        {
            sum += Gradient(x, z, s + (ulong)octave) * amplitude;
            (x, z) = (x * 1.6 - z * 1.2 + 5.3, x * 1.2 + z * 1.6 - 2.9);
            amplitude *= 0.45;
        }
        return Math.Clamp(0.5 + sum * 0.9, 0, 1);
    }

    private static readonly double[] s_gradients = [1, 0, 0.70710678, 0.70710678, 0, 1, -0.70710678, 0.70710678, -1, 0, -0.70710678, -0.70710678, 0, -1, 0.70710678, -0.70710678];

    private static double Gradient(double x, double z, ulong seed)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        long ix = (long)fx, iz = (long)fz;
        double tx = x - fx, tz = z - fz;
        double u = tx * tx * tx * (tx * (tx * 6 - 15) + 10), v = tz * tz * tz * (tz * (tz * 6 - 15) + 10);
        double Corner(long a, long b, double dx, double dz)
        {
            int g = (int)(Mix(seed ^ (ulong)a * 0x9E3779B97F4A7C15ul ^ (ulong)b * 0xC2B2AE3D27D4EB4Ful) >> 61) * 2;
            return s_gradients[g] * dx + s_gradients[g + 1] * dz;
        }
        double a0 = Corner(ix, iz, tx, tz), a1 = Corner(ix + 1, iz, tx - 1, tz);
        double b0 = Corner(ix, iz + 1, tx, tz - 1), b1 = Corner(ix + 1, iz + 1, tx - 1, tz - 1);
        return (a0 + (a1 - a0) * u) * (1 - v) + (b0 + (b1 - b0) * u) * v;
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

    // Mitchell's best candidate on a torus: each point is the farthest of a few random tries from those before it, so
    // every prefix of the ranks is itself spread evenly and the tile repeats without a seam
    private static (double[] X, double[] Z) BuildTile()
    {
        const int Tries = 12, Grid = 64;
        double size = CellSize, bucket = size / Grid;
        double[] xs = new double[TilePoints], zs = new double[TilePoints];
        List<int>[] buckets = new List<int>[Grid * Grid];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = new List<int>();
        ulong state = 0x5EED5EED5EEDul;
        double Next() => Unit(state = Mix(state + 0x9E3779B97F4A7C15ul));

        double Nearest(double x, double z, int count)
        {
            if (count == 0)
                return double.MaxValue;
            int bx = (int)(x / bucket), bz = (int)(z / bucket);
            double best = double.MaxValue;
            for (int ring = 0; ring <= Grid / 2; ring++)
            {
                if (best < double.MaxValue && (ring - 1) * bucket > Math.Sqrt(best))
                    break;
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring)
                            continue;
                        foreach (int j in buckets[(bz + dz + Grid) % Grid * Grid + (bx + dx + Grid) % Grid])
                        {
                            double ex = Math.Abs(xs[j] - x), ez = Math.Abs(zs[j] - z);
                            ex = Math.Min(ex, size - ex);
                            ez = Math.Min(ez, size - ez);
                            best = Math.Min(best, ex * ex + ez * ez);
                        }
                    }
            }
            return best;
        }

        for (int i = 0; i < TilePoints; i++)
        {
            double bestX = 0, bestZ = 0, bestD = -1;
            for (int t = 0; t < Tries; t++)
            {
                double x = Next() * size, z = Next() * size;
                double d = Nearest(x, z, i);
                if (d > bestD)
                    (bestX, bestZ, bestD) = (x, z, d);
            }
            xs[i] = bestX;
            zs[i] = bestZ;
            buckets[Math.Min((int)(bestZ / bucket), Grid - 1) * Grid + Math.Min((int)(bestX / bucket), Grid - 1)].Add(i);
        }
        return (xs, zs);
    }
}
