using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>How <see cref="TerrainErosion"/> wears a heightfield down, stage by stage. A stage with no work is skipped.</summary>
public sealed class TerrainErosionSettings
{
    public ulong Seed { get; init; }

    /// <summary>Stream power erosion on a grid this many times coarser, carving the drainage network and valleys.</summary>
    public int FlowDownsample { get; init; } = 4;
    public int FlowIterations { get; init; } = 80;

    /// <summary>The stream power constant K in erosion = K A^m S, with A in square metres.</summary>
    public double FlowErosion { get; init; } = 0.04;
    public double FlowAreaExponent { get; init; } = 0.5;

    /// <summary>Square metres of catchment a cell needs before it erodes as a channel, leaving hillslopes to diffusion.</summary>
    public double FlowChannelArea { get; init; } = 2000.0;

    /// <summary>Hillslope diffusion per iteration, 0 to 0.2, rounding ridges and the walls of channels.</summary>
    public double FlowDiffusion { get; init; } = 0.04;

    /// <summary>How much of a depression fills with sediment each iteration, 1 to fill it at once into a flat floor.</summary>
    public double FlowLakeFill { get; init; } = 0.05;

    /// <summary>Fine detail kept in carved ground fades out over this many metres of carving.</summary>
    public double FlowDetailFade { get; init; } = 30.0;

    /// <summary>Droplets per sample for particle hydraulic erosion, cutting gullies and dropping fans.</summary>
    public double Droplets { get; init; } = 0.25;
    public int DropletLifetime { get; init; } = 64;
    public double DropletInertia { get; init; } = 0.1;
    public double DropletCapacity { get; init; } = 4.0;
    public double DropletMinCapacity { get; init; } = 0.01;
    public double DropletErosion { get; init; } = 0.3;
    public double DropletDeposition { get; init; } = 0.3;
    public double DropletEvaporation { get; init; } = 0.02;
    public double DropletGravity { get; init; } = 4.0;
    public int DropletRadius { get; init; } = 4;

    /// <summary>Thermal erosion slumps slopes steeper than the talus angle, in degrees.</summary>
    public int ThermalIterations { get; init; } = 10;
    public double TalusAngle { get; init; } = 40.0;

    /// <summary>Passes of light smoothing at the end, softening the speckle droplets leave.</summary>
    public int SmoothIterations { get; init; } = 2;
}

/// <summary>
/// Erodes a heightfield in metres, row by row from z 0: stream power erosion on a coarse grid for the drainage network,
/// then particle hydraulic erosion for gullies and fans, then thermal erosion for scree. Deterministic for a seed.
/// </summary>
public static class TerrainErosion
{
    private static readonly (int X, int Z, float Distance)[] s_neighbours =
    [
        (-1, -1, MathF.Sqrt(2)), (0, -1, 1), (1, -1, MathF.Sqrt(2)),
        (-1, 0, 1), (1, 0, 1),
        (-1, 1, MathF.Sqrt(2)), (0, 1, 1), (1, 1, MathF.Sqrt(2)),
    ];

    public static void Erode(float[] heights, int width, int depth, double spacing, TerrainErosionSettings settings)
    {
        if (settings.FlowIterations > 0)
            Flow(heights, width, depth, spacing, settings);
        if (settings.Droplets > 0)
            Droplets(heights, width, depth, settings);
        if (settings.ThermalIterations > 0)
            Thermal(heights, width, depth, settings.TalusAngle, settings.ThermalIterations);
        if (settings.SmoothIterations > 0)
        {
            float[] scratch = new float[heights.Length];
            for (int i = 0; i < settings.SmoothIterations; i++)
            {
                Diffuse(heights, scratch, width, depth, 0.15f);
                Array.Copy(scratch, heights, heights.Length);
            }
        }
    }

    /// <summary>A heightmap's samples in metres for a terrain from <paramref name="bottom"/> over <paramref name="range"/>.</summary>
    public static float[] ToMetres(TerrainHeightmap map, double bottom, double range)
    {
        float[] heights = new float[map.Samples.Length];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = (float)(bottom + map.Samples[i] / 65535.0 * range);
        return heights;
    }

    public static TerrainHeightmap ToHeightmap(float[] heights, int width, int depth, double bottom, double range)
    {
        ushort[] samples = new ushort[width * depth];
        Parallel.For(0, depth, z =>
        {
            for (int i = z * width; i < (z + 1) * width; i++)
                samples[i] = (ushort)Math.Round(Math.Clamp((heights[i] - bottom) / range, 0, 1) * 65535.0);
        });
        return new TerrainHeightmap(width, depth, samples);
    }

    /// <summary>Stream power erosion on a coarse copy, its change added back with fine detail fading out where it carved.</summary>
    public static void Flow(float[] heights, int width, int depth, double spacing, TerrainErosionSettings settings)
    {
        int step = Math.Max(1, settings.FlowDownsample);
        int coarseWidth = (width - 1 + step - 1) / step + 1, coarseDepth = (depth - 1 + step - 1) / step + 1;
        double scaleX = (width - 1) / (double)(coarseWidth - 1), scaleZ = (depth - 1) / (double)(coarseDepth - 1);

        float[] before = new float[coarseWidth * coarseDepth];
        Parallel.For(0, coarseDepth, z =>
        {
            for (int x = 0; x < coarseWidth; x++)
                before[z * coarseWidth + x] = Bilinear(heights, width, depth, x * scaleX, z * scaleZ);
        });
        float[] after = (float[])before.Clone();
        StreamPower(after, coarseWidth, coarseDepth, spacing * (scaleX + scaleZ) * 0.5, settings);

        double fade = Math.Max(settings.FlowDetailFade, 1e-3);
        Parallel.For(0, depth, z =>
        {
            for (int x = 0; x < width; x++)
            {
                double cx = x / scaleX, cz = z / scaleZ;
                double carvedFrom = Smooth(before, coarseWidth, coarseDepth, cx, cz);
                double carved = Smooth(after, coarseWidth, coarseDepth, cx, cz);
                double detail = heights[z * width + x] - carvedFrom;
                double keep = Math.Clamp(1 + (carved - carvedFrom) / fade, 0, 1);
                heights[z * width + x] = (float)(carved + detail * keep);
            }
        });
    }

    /// <summary>Implicit stream power erosion with sediment filling depressions and hillslope diffusion, edges as outlets.</summary>
    public static void StreamPower(float[] heights, int width, int depth, double spacing, TerrainErosionSettings settings)
    {
        int count = width * depth;
        Drainage drainage = new(width, depth);
        float[] area = new float[count];
        float[] scratch = new float[count];
        float cellArea = (float)(spacing * spacing);
        float lakeFill = (float)Math.Clamp(settings.FlowLakeFill, 0, 1);
        double diffusion = Math.Clamp(settings.FlowDiffusion, 0, 0.2);

        for (int iteration = 0; iteration < settings.FlowIterations; iteration++)
        {
            drainage.Route(heights, settings.Seed + (ulong)iteration * 0x632BE59BD9B4E019UL);
            int[] order = drainage.Order, receiver = drainage.Receiver;
            float[] level = drainage.Level;

            Array.Fill(area, cellArea);
            for (int k = count - 1; k >= 0; k--)
            {
                int i = order[k];
                if (receiver[i] >= 0)
                    area[receiver[i]] += area[i];
            }

            for (int k = 0; k < count; k++)
            {
                int i = order[k], r = receiver[i];
                if (r < 0)
                    continue;
                if (heights[i] < level[i])
                {
                    heights[i] += (level[i] - heights[i]) * lakeFill;
                    continue;
                }
                if (heights[i] <= heights[r])
                    continue;
                bool diagonal = i % width != r % width && i / width != r / width;
                double distance = spacing * (diagonal ? Math.Sqrt(2) : 1);
                double channel = area[i] - settings.FlowChannelArea;
                if (channel <= 0)
                    continue;
                double f = settings.FlowErosion * Math.Pow(channel, settings.FlowAreaExponent) / distance;
                heights[i] = (float)((heights[i] + f * heights[r]) / (1 + f));
            }

            if (diffusion > 0)
            {
                Diffuse(heights, scratch, width, depth, (float)diffusion);
                Array.Copy(scratch, heights, count);
            }
        }
    }

    /// <summary>Steepest descent drainage over a surface with its depressions filled to their spill points.</summary>
    private sealed class Drainage
    {
        private readonly int _width, _depth;
        private readonly bool[] _closed;
        private readonly int[] _donorStart, _donorFill, _donors;
        private readonly PriorityQueue<int, float> _open = new();
        private readonly Queue<int> _pit = new();

        public Drainage(int width, int depth)
        {
            _width = width;
            _depth = depth;
            int count = width * depth;
            _closed = new bool[count];
            _donorStart = new int[count + 1];
            _donorFill = new int[count + 1];
            _donors = new int[count];
            Level = new float[count];
            Receiver = new int[count];
            Order = new int[count];
        }

        /// <summary>The filled surface, rising by the smallest step across flats so every cell drains.</summary>
        public float[] Level { get; }

        /// <summary>The neighbour each cell drains to, or -1 for an edge cell draining off the map.</summary>
        public int[] Receiver { get; }

        /// <summary>Cells ordered so every cell comes after its receiver.</summary>
        public int[] Order { get; }

        public void Route(float[] heights, ulong seed)
        {
            Fill(heights);
            FindReceivers(seed);
            SortUpstream();
        }

        private bool IsEdge(int x, int z) => x == 0 || z == 0 || x == _width - 1 || z == _depth - 1;

        // Priority flood with epsilon after Barnes, Lehman and Mulla
        private void Fill(float[] heights)
        {
            Array.Clear(_closed);
            for (int z = 0; z < _depth; z++)
                for (int x = 0; x < _width; x++)
                {
                    if (!IsEdge(x, z))
                        continue;
                    int i = z * _width + x;
                    _closed[i] = true;
                    Level[i] = heights[i];
                    _open.Enqueue(i, heights[i]);
                }

            while (_pit.Count > 0 || _open.Count > 0)
            {
                int c = _pit.Count > 0 ? _pit.Dequeue() : _open.Dequeue();
                int cx = c % _width, cz = c / _width;
                foreach ((int dx, int dz, float _) in s_neighbours)
                {
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= _width || nz >= _depth)
                        continue;
                    int n = nz * _width + nx;
                    if (_closed[n])
                        continue;
                    _closed[n] = true;
                    if (heights[n] <= Level[c])
                    {
                        Level[n] = MathF.BitIncrement(Level[c]);
                        _pit.Enqueue(n);
                    }
                    else
                    {
                        Level[n] = heights[n];
                        _open.Enqueue(n, heights[n]);
                    }
                }
            }
        }

        // A lower neighbour picked at random weighted by slope, so flow spreads like multiple flow direction on average
        private void FindReceivers(ulong seed)
        {
            Parallel.For(0, _depth, z =>
            {
                Span<float> slopes = stackalloc float[8];
                for (int x = 0; x < _width; x++)
                {
                    int i = z * _width + x;
                    if (IsEdge(x, z))
                    {
                        Receiver[i] = -1;
                        continue;
                    }
                    float total = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        (int dx, int dz, float distance) = s_neighbours[k];
                        float slope = MathF.Max(0, (Level[i] - Level[(z + dz) * _width + x + dx]) / distance);
                        slopes[k] = slope * slope;
                        total += slopes[k];
                    }
                    ulong state = seed ^ (ulong)i * 0xD6E8FEB86659FD93UL;
                    float pick = (float)(Random(ref state) * total);
                    int best = -1;
                    for (int k = 0; k < 8; k++)
                    {
                        if (slopes[k] <= 0)
                            continue;
                        best = k;
                        pick -= slopes[k];
                        if (pick <= 0)
                            break;
                    }
                    Receiver[i] = (z + s_neighbours[best].Z) * _width + x + s_neighbours[best].X;
                }
            });
        }

        // Breadth first from the outlets through each cell's donors
        private void SortUpstream()
        {
            int count = _width * _depth;
            Array.Clear(_donorStart);
            for (int i = 0; i < count; i++)
                if (Receiver[i] >= 0)
                    _donorStart[Receiver[i] + 1]++;
            for (int i = 0; i < count; i++)
                _donorStart[i + 1] += _donorStart[i];
            Array.Copy(_donorStart, _donorFill, count + 1);
            for (int i = 0; i < count; i++)
                if (Receiver[i] >= 0)
                    _donors[_donorFill[Receiver[i]]++] = i;

            int head = 0, tail = 0;
            for (int i = 0; i < count; i++)
                if (Receiver[i] < 0)
                    Order[tail++] = i;
            while (head < tail)
            {
                int c = Order[head++];
                for (int d = _donorStart[c]; d < _donorStart[c + 1]; d++)
                    Order[tail++] = _donors[d];
            }
        }
    }

    private static void Diffuse(float[] source, float[] target, int width, int depth, float rate)
    {
        Parallel.For(0, depth, z =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                if (x == 0 || z == 0 || x == width - 1 || z == depth - 1)
                {
                    target[i] = source[i];
                    continue;
                }
                float laplacian = source[i - 1] + source[i + 1] + source[i - width] + source[i + width] - 4 * source[i];
                target[i] = source[i] + rate * laplacian;
            }
        });
    }

    /// <summary>
    /// Particle hydraulic erosion after Hans Theobald Beyer. Droplets run in tiles, each tile on its own seeded stream,
    /// and tiles far enough apart that their droplets never meet run in parallel.
    /// </summary>
    public static void Droplets(float[] heights, int width, int depth, TerrainErosionSettings settings)
    {
        int radius = Math.Max(1, settings.DropletRadius);
        List<(int X, int Z, float Weight)> brush = [];
        float total = 0;
        for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                float weight = radius - MathF.Sqrt(x * x + z * z);
                if (weight <= 0)
                    continue;
                brush.Add((x, z, weight));
                total += weight;
            }
        (int X, int Z, float Weight)[] weights = brush.ConvertAll(b => (b.X, b.Z, b.Weight / total)).ToArray();

        int tile = Math.Max(64, 2 * (settings.DropletLifetime + radius + 2) + 2);
        int tilesX = (width + tile - 1) / tile, tilesZ = (depth + tile - 1) / tile;
        for (int phase = 0; phase < 4; phase++)
        {
            List<(int X, int Z)> tiles = [];
            for (int tz = phase / 2; tz < tilesZ; tz += 2)
                for (int tx = phase % 2; tx < tilesX; tx += 2)
                    tiles.Add((tx, tz));
            Parallel.ForEach(tiles, t =>
            {
                int x0 = t.X * tile, z0 = t.Z * tile;
                int x1 = Math.Min(x0 + tile, width - 1), z1 = Math.Min(z0 + tile, depth - 1);
                if (x1 <= x0 || z1 <= z0)
                    return;
                ulong state = settings.Seed ^ (ulong)(t.Z * tilesX + t.X + 1) * 0x9E3779B97F4A7C15UL;
                int droplets = (int)Math.Round((x1 - x0) * (z1 - z0) * settings.Droplets);
                for (int d = 0; d < droplets; d++)
                {
                    double px = x0 + Random(ref state) * (x1 - x0), pz = z0 + Random(ref state) * (z1 - z0);
                    Droplet(heights, width, depth, px, pz, settings, weights);
                }
            });
        }
    }

    private static void Droplet(float[] heights, int width, int depth, double px, double pz, TerrainErosionSettings s, (int X, int Z, float Weight)[] brush)
    {
        double dirX = 0, dirZ = 0, speed = 1, water = 1, sediment = 0;
        for (int life = 0; life < s.DropletLifetime; life++)
        {
            int nodeX = (int)px, nodeZ = (int)pz;
            double fx = px - nodeX, fz = pz - nodeZ;
            (double height, double gradX, double gradZ) = Sample(heights, width, nodeX, nodeZ, fx, fz);

            dirX = dirX * s.DropletInertia - gradX * (1 - s.DropletInertia);
            dirZ = dirZ * s.DropletInertia - gradZ * (1 - s.DropletInertia);
            double length = Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 1e-9)
                break;
            dirX /= length;
            dirZ /= length;
            px += dirX;
            pz += dirZ;
            if (px < 0 || pz < 0 || px >= width - 1 || pz >= depth - 1)
                break;

            double drop = height - Sample(heights, width, (int)px, (int)pz, px - (int)px, pz - (int)pz).Height;
            double capacity = Math.Max(drop * speed * water * s.DropletCapacity, s.DropletMinCapacity);
            if (sediment > capacity || drop < 0)
            {
                double deposit = drop < 0 ? Math.Min(-drop, sediment) : (sediment - capacity) * s.DropletDeposition;
                sediment -= deposit;
                int i = nodeZ * width + nodeX;
                heights[i] += (float)(deposit * (1 - fx) * (1 - fz));
                heights[i + 1] += (float)(deposit * fx * (1 - fz));
                heights[i + width] += (float)(deposit * (1 - fx) * fz);
                heights[i + width + 1] += (float)(deposit * fx * fz);
            }
            else
            {
                double erode = Math.Min((capacity - sediment) * s.DropletErosion, drop);
                foreach ((int bx, int bz, float weight) in brush)
                {
                    int x = nodeX + bx, z = nodeZ + bz;
                    if (x < 0 || z < 0 || x >= width || z >= depth)
                        continue;
                    heights[z * width + x] -= (float)(erode * weight);
                }
                sediment += erode;
            }

            speed = Math.Sqrt(Math.Max(0, speed * speed + drop * s.DropletGravity));
            water *= 1 - s.DropletEvaporation;
        }
    }

    private static (double Height, double GradX, double GradZ) Sample(float[] heights, int width, int x, int z, double fx, double fz)
    {
        int i = z * width + x;
        double a = heights[i], b = heights[i + 1], c = heights[i + width], d = heights[i + width + 1];
        double gradX = (b - a) * (1 - fz) + (d - c) * fz;
        double gradZ = (c - a) * (1 - fx) + (d - b) * fx;
        double height = a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz;
        return (height, gradX, gradZ);
    }

    /// <summary>Moves material down every slope steeper than the talus angle until it is no steeper, conserving volume.</summary>
    public static void Thermal(float[] heights, int width, int depth, double talusAngle, int iterations)
    {
        float tangent = (float)Math.Tan(talusAngle * Math.PI / 180.0);
        float[] source = heights, target = new float[heights.Length];
        const float rate = 0.06f;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            float[] from = source, to = target;
            Parallel.For(0, depth, z =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = z * width + x;
                    float h = from[i], change = 0;
                    foreach ((int dx, int dz, float distance) in s_neighbours)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
                            continue;
                        float difference = h - from[nz * width + nx], limit = tangent * distance;
                        if (difference > limit)
                            change -= rate * (difference - limit);
                        else if (difference < -limit)
                            change += rate * (-difference - limit);
                    }
                    to[i] = h + change;
                }
            });
            (source, target) = (target, source);
        }
        if (source != heights)
            Array.Copy(source, heights, heights.Length);
    }

    private static float Bilinear(float[] values, int width, int depth, double x, double z)
    {
        int ix = Math.Clamp((int)x, 0, width - 2), iz = Math.Clamp((int)z, 0, depth - 2);
        double fx = Math.Clamp(x - ix, 0, 1), fz = Math.Clamp(z - iz, 0, 1);
        return (float)Sample(values, width, ix, iz, fx, fz).Height;
    }

    private static double Smooth(float[] values, int width, int depth, double x, double z)
    {
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
        double fx = x - ix, fz = z - iz;
        double result = 0;
        for (int row = -1; row <= 2; row++)
        {
            int sz = Math.Clamp(iz + row, 0, depth - 1);
            double line = 0;
            for (int column = -1; column <= 2; column++)
                line += values[sz * width + Math.Clamp(ix + column, 0, width - 1)] * BSpline(column - fx);
            result += line * BSpline(row - fz);
        }
        return result;
    }

    private static double BSpline(double t)
    {
        t = Math.Abs(t);
        if (t < 1)
            return (4 - 6 * t * t + 3 * t * t * t) / 6;
        if (t < 2)
            return (2 - t) * (2 - t) * (2 - t) / 6;
        return 0;
    }

    private static double Random(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }
}
