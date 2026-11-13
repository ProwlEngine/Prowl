using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>What the terrain's page streaming is doing, as of the last frame.</summary>
public readonly record struct TerrainStreamingStats(int Resident, int Capacity, int Wanted, int Loading, int Uploaded, int[] ResidentByLevel);

/// <summary>
/// The GPU page pools and which pages fill them. Each frame the main thread picks the pages near the viewers, coarse
/// before fine and near before far, starts loading the missing ones on worker threads, and assigns the decoded ones a
/// slice, evicting the least recently wanted. The top levels stay resident. A frame feature uploads the assigned pages
/// at the start of the frame, so the frame's draws already see them, see Graphite/Docs/TerrainResearch.md part 2.4.
/// </summary>
internal sealed class TerrainPool : IDisposable
{
    public const int PinnedLevels = 3;
    private const int MaxLoading = 32;

    private sealed class Slot
    {
        public int Slice;
        public long LastWanted;
        public bool Pinned;
        public bool Holes;
    }

    public sealed class Snapshot
    {
        public readonly Dictionary<TerrainPageKey, int> Pages = new();
        public readonly HashSet<TerrainPageKey> Holes = new();
        public readonly List<(int Slice, TerrainPage Page)> Uploads = new();
        public readonly List<TerrainPageKey> Arrived = new();
        public float[]? Table;
        public volatile bool Uploaded;
    }

    private readonly TerrainData _data;
    private readonly Dictionary<TerrainPageKey, Slot> _resident = new();
    private readonly Stack<int> _free = new();
    private readonly HashSet<TerrainPageKey> _loading = new();
    private readonly ConcurrentQueue<(TerrainPageKey Key, TerrainPage? Page)> _ready = new();
    private readonly List<(TerrainPageKey Key, double Priority)> _wanted = new();
    private readonly HashSet<TerrainPageKey> _wantedSet = new();
    private readonly HashSet<TerrainPageKey> _refresh = new();
    private readonly uint[] _noControl = new uint[TerrainPage.Samples];
    private long _frame;
    private bool _tableChanged = true;
    private Snapshot _current = new();

    public TerrainPool(GraphicsDevice device, TerrainData data, int capacity)
    {
        _data = data;
        int pinned = 0;
        foreach (TerrainPageKey key in data.PageKeys)
            pinned += key.Level >= data.Levels - PinnedLevels ? 1 : 0;
        Capacity = Math.Max(capacity, pinned + 16);

        ResourceFactory factory = device.ResourceFactory;
        uint slices = (uint)Capacity;
        Heights = factory.CreateTexture(TextureDescription.Texture2D(TerrainPage.Size, TerrainPage.Size, 1, slices, PixelFormat.R16_UNorm, TextureUsage.Sampled));
        Normals = factory.CreateTexture(TextureDescription.Texture2D(TerrainPage.Size, TerrainPage.Size, 1, slices, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Control = factory.CreateTexture(TextureDescription.Texture2D(TerrainPage.Size, TerrainPage.Size, 1, slices, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Heights.Name = "Graphite Terrain Heights";
        Normals.Name = "Graphite Terrain Normals";
        Control.Name = "Graphite Terrain Control";
        (TableX, TableZ) = data.PagesAt(0);
        PageTable = factory.CreateTexture(TextureDescription.Texture2D((uint)TableX, (uint)TableZ, 1, (uint)data.Levels, PixelFormat.R32_Float, TextureUsage.Sampled));
        PageTable.Name = "Graphite Terrain Page Table";
        for (int slice = Capacity - 1; slice >= 0; slice--)
            _free.Push(slice);

        // The pinned levels load before the first frame, so the world is never missing, only coarse
        foreach (TerrainPageKey key in data.PageKeys)
        {
            if (key.Level < data.Levels - PinnedLevels || data.ReadPage(key) is not { } page)
                continue;
            int slice = _free.Pop();
            device.UpdateTexture<ushort>(Heights, page.Heights, TextureRegion.Whole(Heights, 0, (uint)slice));
            device.UpdateTexture<byte>(Normals, page.NormalAo, TextureRegion.Whole(Normals, 0, (uint)slice));
            device.UpdateTexture<uint>(Control, page.Control ?? _noControl, TextureRegion.Whole(Control, 0, (uint)slice));
            _resident[key] = new Slot { Slice = slice, Pinned = true, Holes = HasHoles(page) };
        }
        Publish();
        Stats = new TerrainStreamingStats(_resident.Count, Capacity, 0, 0, 0, Count());
    }

    public int Capacity { get; }
    public Texture Heights { get; }
    public Texture Normals { get; }
    public Texture Control { get; }

    /// <summary>Per level a slice of the finest level's page grid, holding each resident page's pool slice, -1 for none.</summary>
    public Texture PageTable { get; }
    public int TableX { get; }
    public int TableZ { get; }

    /// <summary>The pages each frame may assign and upload.</summary>
    public int UploadsPerFrame { get; set; } = 8;

    public TerrainStreamingStats Stats { get; private set; }

    /// <summary>The frame's pages, read by the walk on the render thread.</summary>
    public Snapshot Current => _current;

    /// <summary>Uploads the resident ones of pages an edit replaced again on the next update, as arrivals so caches refill over them. Main thread.</summary>
    public void Refresh(IEnumerable<TerrainPageKey> keys)
    {
        foreach (TerrainPageKey key in keys)
            if (_resident.ContainsKey(key))
                _refresh.Add(key);
    }

    /// <summary>Picks, loads and assigns pages for the viewers. Main thread.</summary>
    public void Update(IReadOnlyList<Double3> viewers, Func<int, double> range)
    {
        _frame++;
        Want(viewers, range);

        foreach ((TerrainPageKey key, double _) in _wanted)
        {
            if (_resident.TryGetValue(key, out Slot? slot))
                slot.LastWanted = _frame;
        }

        // Start loads for the most wanted missing pages
        foreach ((TerrainPageKey key, double _) in _wanted)
        {
            if (_loading.Count >= MaxLoading)
                break;
            if (_resident.ContainsKey(key) || !_loading.Add(key))
                continue;
            TerrainData data = _data;
            Task.Run(() => _ready.Enqueue((key, data.ReadPage(key))));
        }

        // Pages a skipped frame never uploaded go again
        Snapshot previous = _current;
        List<(int Slice, TerrainPage Page)> carried = previous.Uploaded ? [] : previous.Uploads;

        int assigned = 0;
        List<(int, TerrainPage)> uploads = new(carried);
        List<TerrainPageKey> arrived = new();
        while (assigned < UploadsPerFrame && _ready.TryDequeue(out (TerrainPageKey Key, TerrainPage? Page) item))
        {
            _loading.Remove(item.Key);
            if (item.Page != null && _data.IsEdited(item.Key))
                item.Page = _data.GetPage(item.Key);
            if (item.Page == null || _resident.ContainsKey(item.Key) || !_wantedSet.Contains(item.Key) || !TryTakeSlice(out int slice))
                continue;
            _resident[item.Key] = new Slot { Slice = slice, LastWanted = _frame, Holes = HasHoles(item.Page) };
            uploads.Add((slice, item.Page));
            arrived.Add(item.Key);
            _tableChanged = true;
            assigned++;
        }

        foreach (TerrainPageKey key in _refresh)
        {
            if (!_resident.TryGetValue(key, out Slot? slot) || arrived.Contains(key) || _data.GetPage(key) is not TerrainPage page)
                continue;
            slot.Holes = HasHoles(page);
            uploads.Add((slot.Slice, page));
            arrived.Add(key);
        }
        _refresh.Clear();

        Publish();
        _current.Uploads.AddRange(uploads);
        _current.Arrived.AddRange(arrived);

        Stats = new TerrainStreamingStats(_resident.Count, Capacity, _wanted.Count, _loading.Count, assigned, Count());
    }

    private static bool HasHoles(TerrainPage page) => page.Control != null && Array.Exists(page.Control, TerrainControl.IsHole);

    private int[] Count()
    {
        int[] byLevel = new int[_data.Levels];
        foreach (TerrainPageKey key in _resident.Keys)
            byLevel[key.Level]++;
        return byLevel;
    }

    // The pages whose box lies within its level's range of a viewer, a quarter beyond for prefetch, coarse first then near
    private void Want(IReadOnlyList<Double3> viewers, Func<int, double> range)
    {
        _wanted.Clear();
        _wantedSet.Clear();
        Double3 origin = _data.Position;
        for (int level = _data.Levels - 1; level >= 0; level--)
        {
            double size = TerrainPage.Quads * _data.SpacingAt(level);
            double reach = range(level) * 1.25;
            (int pagesX, int pagesZ) = _data.PagesAt(level);
            foreach (Double3 viewer in viewers)
            {
                int x0 = Math.Max((int)Math.Floor((viewer.X - reach - origin.X) / size), 0), x1 = Math.Min((int)Math.Floor((viewer.X + reach - origin.X) / size), pagesX - 1);
                int z0 = Math.Max((int)Math.Floor((viewer.Z - reach - origin.Z) / size), 0), z1 = Math.Min((int)Math.Floor((viewer.Z + reach - origin.Z) / size), pagesZ - 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        TerrainPageKey key = new(level, x, z);
                        if (!_data.TryGetPageBounds(key, out double low, out double high))
                            continue;
                        double dx = Math.Max(Math.Max(origin.X + x * size - viewer.X, viewer.X - origin.X - (x + 1) * size), 0);
                        double dz = Math.Max(Math.Max(origin.Z + z * size - viewer.Z, viewer.Z - origin.Z - (z + 1) * size), 0);
                        double dy = Math.Max(Math.Max(low - viewer.Y, viewer.Y - high), 0);
                        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (distance < reach && _wantedSet.Add(key))
                            _wanted.Add((key, (_data.Levels - level) * 1e9 + distance));
                    }
            }
        }
        _wanted.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    // A free slice, or the one of the resident page wanted longest ago, never a pinned one or one wanted this frame
    private bool TryTakeSlice(out int slice)
    {
        if (_free.TryPop(out slice))
            return true;
        TerrainPageKey oldest = default;
        long when = long.MaxValue;
        foreach ((TerrainPageKey key, Slot candidate) in _resident)
        {
            if (!candidate.Pinned && candidate.LastWanted < _frame && candidate.LastWanted < when)
            {
                oldest = key;
                when = candidate.LastWanted;
            }
        }
        if (when == long.MaxValue)
            return false;
        slice = _resident[oldest].Slice;
        _resident.Remove(oldest);
        _tableChanged = true;
        return true;
    }

    private void Publish()
    {
        Snapshot snapshot = new();
        foreach ((TerrainPageKey key, Slot slot) in _resident)
        {
            snapshot.Pages[key] = slot.Slice;
            if (slot.Holes)
                snapshot.Holes.Add(key);
        }
        if (_tableChanged || !_current.Uploaded)
        {
            float[] table = new float[TableX * TableZ * _data.Levels];
            Array.Fill(table, -1f);
            foreach ((TerrainPageKey key, Slot slot) in _resident)
                table[(key.Level * TableZ + key.Z) * TableX + key.X] = slot.Slice;
            snapshot.Table = table;
            _tableChanged = false;
        }
        _current = snapshot;
    }

    /// <summary>Uploads the frame's assigned pages into their slices. Render thread, at the start of the frame.</summary>
    public void Upload(CommandBuffer cmd, Snapshot snapshot)
    {
        foreach ((int slice, TerrainPage page) in snapshot.Uploads)
        {
            cmd.UpdateTexture<ushort>(Heights, page.Heights, TextureRegion.Whole(Heights, 0, (uint)slice));
            cmd.UpdateTexture<byte>(Normals, page.NormalAo, TextureRegion.Whole(Normals, 0, (uint)slice));
            cmd.UpdateTexture<uint>(Control, page.Control ?? _noControl, TextureRegion.Whole(Control, 0, (uint)slice));
        }
        if (snapshot.Table != null)
        {
            int layer = TableX * TableZ;
            for (int level = 0; level < _data.Levels; level++)
                cmd.UpdateTexture<float>(PageTable, snapshot.Table.AsSpan(level * layer, layer), TextureRegion.Whole(PageTable, 0, (uint)level));
        }
        snapshot.Uploaded = true;
    }

    public void Dispose()
    {
        Heights.Dispose();
        Normals.Dispose();
        Control.Dispose();
        PageTable.Dispose();
    }
}
