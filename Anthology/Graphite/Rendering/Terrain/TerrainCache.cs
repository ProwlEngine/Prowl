using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The terrain's material cache: per view slot, clipmap levels around the view's camera, each twice the texel size of the
/// one inside it, holding the blended layers so the terrain's pixels sample them instead of blending every layer themselves.
/// Texels wrap around each level, so as the viewer moves a level only fills the strips it gained. Pages arriving refill
/// the area they cover. The fill is spread over frames, coarse levels first across every slot, and a level is only
/// sampled once it has no work left. See Graphite/Docs/TerrainResearch.md part 2.5.
/// </summary>
internal sealed class TerrainCache : IDisposable
{
    private const int Snap = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public Float4 Rect, Level, Terrain;
    }

    private sealed class Level
    {
        public long X, Z;
        public bool Placed;
        public readonly Queue<(long X, long Z, int W, int H)> Work = new();
    }

    public sealed class Frame
    {
        public readonly List<(int Slot, int Level, long X, long Z, int W, int H)> Fills = new();
        public readonly float[] Levels;

        public Frame(int levels, int slots) => Levels = new float[levels * slots * 4];
    }

    private readonly Level[][] _levels;
    private readonly InternalCompute _program;
    private readonly PropertySet _properties = new();
    private readonly Sampler _point, _linear;

    public TerrainCache(Renderer renderer, int levels, int size, double texel, int slots)
    {
        LevelCount = levels;
        Size = size;
        Texel = texel;
        Slots = slots;
        _levels = new Level[slots][];
        for (int slot = 0; slot < slots; slot++)
        {
            _levels[slot] = new Level[levels];
            for (int i = 0; i < levels; i++)
                _levels[slot][i] = new Level();
        }
        ResourceFactory factory = renderer.Device.ResourceFactory;
        Albedo = factory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, 1, (uint)(levels * slots), PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage | TextureUsage.Sampled));
        Surface = factory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, 1, (uint)(levels * slots), PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage | TextureUsage.Sampled));
        Data = factory.CreateTexture(TextureDescription.Texture2D((uint)levels, (uint)slots, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        Albedo.Name = "Graphite Terrain Cache Albedo";
        Surface.Name = "Graphite Terrain Cache Surface";
        _program = renderer.Effects.Compute("Internal/TerrainCache.glsl");
        _point = factory.CreateSampler(SamplerDescription.Point);
        _linear = factory.CreateSampler(SamplerDescription.Linear);
        Current = new Frame(levels, slots);
    }

    public int LevelCount { get; }
    public int Size { get; }
    public double Texel { get; }
    public int Slots { get; }

    public Texture Albedo { get; }
    public Texture Surface { get; }

    /// <summary>Per level along x and slot along y a texel: its first texel's corner in metres from the terrain's corner along x and z, its texel size, 1 when it can be sampled.</summary>
    public Texture Data { get; }

    /// <summary>Texels filled a frame at most.</summary>
    public int Budget { get; set; } = 1 << 19;

    public Frame Current { get; private set; }

    /// <summary>Starts a slot over for a new view.</summary>
    public void Reset(int slot)
    {
        foreach (Level level in _levels[slot])
        {
            level.Placed = false;
            level.Work.Clear();
        }
    }

    /// <summary>
    /// Moves each slot's levels to its viewer, null for an unused slot, queues what they gained and what arriving pages
    /// cover, and picks the frame's fills. Main thread.
    /// </summary>
    public void Update(IReadOnlyList<Double2?> viewers, TerrainData data, IReadOnlyList<TerrainPageKey> arrived)
    {
        Frame frame = new(LevelCount, Slots);
        for (int slot = 0; slot < Slots; slot++)
        {
            if (viewers[slot] is { } viewer)
                Place(_levels[slot], viewer, data, arrived);
        }

        // Coarse levels first across the slots, rectangles split into bands of rows to fit the budget
        int budget = Budget;
        for (int i = LevelCount - 1; i >= 0 && budget > 0; i--)
        {
            for (int slot = 0; slot < Slots && budget > 0; slot++)
            {
                if (viewers[slot] != null)
                    budget = Take(frame, slot, i, _levels[slot][i], budget);
            }
        }

        for (int slot = 0; slot < Slots; slot++)
        {
            for (int i = 0; i < LevelCount; i++)
            {
                Level level = _levels[slot][i];
                double texel = Texel * (1 << i);
                int at = (slot * LevelCount + i) * 4;
                frame.Levels[at] = (float)(level.X * texel);
                frame.Levels[at + 1] = (float)(level.Z * texel);
                frame.Levels[at + 2] = (float)texel;
                frame.Levels[at + 3] = level.Placed && level.Work.Count == 0 ? 1f : 0f;
            }
        }
        Current = frame;
    }

    private void Place(Level[] levels, Double2 viewer, TerrainData data, IReadOnlyList<TerrainPageKey> arrived)
    {
        for (int i = 0; i < LevelCount; i++)
        {
            Level level = levels[i];
            double texel = Texel * (1 << i);
            long x = (long)Math.Floor(viewer.X / texel / Snap) * Snap - Size / 2;
            long z = (long)Math.Floor(viewer.Y / texel / Snap) * Snap - Size / 2;
            if (!level.Placed || Math.Abs(x - level.X) >= Size || Math.Abs(z - level.Z) >= Size)
            {
                level.Work.Clear();
                level.Work.Enqueue((x, z, Size, Size));
            }
            else
            {
                long dx = x - level.X, dz = z - level.Z;
                if (dx != 0)
                    level.Work.Enqueue((dx > 0 ? level.X + Size : x, z, (int)Math.Abs(dx), Size));
                if (dz != 0)
                    level.Work.Enqueue((x, dz > 0 ? level.Z + Size : z, Size, (int)Math.Abs(dz)));
            }
            level.X = x;
            level.Z = z;
            level.Placed = true;

            foreach (TerrainPageKey key in arrived)
            {
                double span = TerrainPage.Quads * data.SpacingAt(key.Level) / texel;
                long px0 = Math.Max((long)Math.Floor(key.X * span), x), pz0 = Math.Max((long)Math.Floor(key.Z * span), z);
                long px1 = Math.Min((long)Math.Ceiling((key.X + 1) * span), x + Size), pz1 = Math.Min((long)Math.Ceiling((key.Z + 1) * span), z + Size);
                if (px1 > px0 && pz1 > pz0)
                    level.Work.Enqueue((px0, pz0, (int)(px1 - px0), (int)(pz1 - pz0)));
            }
        }
    }

    private int Take(Frame frame, int slot, int index, Level level, int budget)
    {
        while (budget > 0 && level.Work.TryPeek(out (long X, long Z, int W, int H) queued))
        {
            (long X, long Z, int W, int H) work = Clip(queued, level);
            if (work.W <= 0 || work.H <= 0)
            {
                level.Work.Dequeue();
                continue;
            }
            int rows = Math.Min(work.H, Math.Max(budget / Math.Max(work.W, 1), 1));
            frame.Fills.Add((slot, index, work.X, work.Z, work.W, rows));
            budget -= work.W * rows;
            level.Work.Dequeue();
            if (rows < work.H)
            {
                Queue<(long, long, int, int)> rest = new();
                rest.Enqueue((work.X, work.Z + rows, work.W, work.H - rows));
                while (level.Work.Count > 0)
                    rest.Enqueue(level.Work.Dequeue());
                foreach ((long, long, int, int) item in rest)
                    level.Work.Enqueue(item);
            }
        }
        return budget;
    }

    // The part of a rectangle inside the level's current texels, as work queued before it moved would otherwise wrap onto texels of other places
    private (long X, long Z, int W, int H) Clip((long X, long Z, int W, int H) work, Level level)
    {
        long x0 = Math.Max(work.X, level.X), z0 = Math.Max(work.Z, level.Z);
        long x1 = Math.Min(work.X + work.W, level.X + Size), z1 = Math.Min(work.Z + work.H, level.Z + Size);
        return (x0, z0, (int)Math.Max(x1 - x0, 0), (int)Math.Max(z1 - z0, 0));
    }

    /// <summary>Runs the frame's fills and writes the level data. Render thread.</summary>
    public void Render(CommandBuffer cmd, Frame frame, TerrainPool pool, TerrainData data, TerrainLayers? layers, Texture control)
    {
        cmd.UpdateTexture<float>(Data, frame.Levels, TextureRegion.Whole(Data, 0, 0));
        if (frame.Fills.Count == 0)
            return;
        TerrainHeader header = data.Header;
        _properties.SetTexture("Terrain_CacheAlbedo", Albedo);
        _properties.SetTexture("Terrain_CacheSurface", Surface);
        _properties.SetTexture("Terrain_PageTable", pool.PageTable, _point);
        _properties.SetTexture("_TerrainHeights", pool.Heights, _linear);
        _properties.SetTexture("_TerrainNormals", pool.Normals, _linear);
        _properties.SetTexture("_TerrainControl", control, _point);
        _properties.SetFloat4("_TerrainSpacing", new Float4((float)header.Spacing, layers?.Count ?? 0, 0, 0));
        if (layers != null)
        {
            _properties.SetTexture("_TerrainAlbedo", layers.Albedo, _linear);
            _properties.SetTexture("_TerrainSurface", layers.Surface, _linear);
            _properties.SetTexture("_TerrainLayerData", layers.Data, _point);
        }
        cmd.SetComputeShader(_program.Program);
        cmd.SetProperties(_properties);
        Float4 terrain = new((float)header.Bottom, (float)header.Range, (float)header.Spacing, header.Levels);
        foreach ((int slot, int level, long x, long z, int w, int h) in frame.Fills)
        {
            cmd.SetPushConstants(new Push
            {
                Rect = new Float4(x, z, w, h),
                Level = new Float4((float)(Texel * (1 << level)), slot * LevelCount + level, Size, 0),
                Terrain = terrain,
            });
            cmd.Dispatch((uint)(w + 7) / 8, (uint)(h + 7) / 8, 1);
        }
    }

    public void Dispose()
    {
        _program.Dispose();
        _point.Dispose();
        _linear.Dispose();
        Albedo.Dispose();
        Surface.Dispose();
        Data.Dispose();
    }
}
