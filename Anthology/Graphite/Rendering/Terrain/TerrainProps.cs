using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>What a scatter rule's instances draw as.</summary>
public sealed class TerrainPropKind
{
    public required GpuMesh Mesh { get; init; }
    public LodSet? Lods { get; init; }
    public required Material[] Materials { get; init; }
    public RenderableFlags Flags { get; init; } = RenderableFlags.Static | RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
    public float LodBias { get; init; } = 1f;
}

/// <summary>
/// Keeps a <see cref="TerrainScatter"/>'s instances placed as renderables in the cells around a viewer. Cells are
/// scattered on worker threads and added on <see cref="Update"/>, nearest first, and leave once a cell beyond the range.
/// When the terrain is edited the cells under it are scattered again at once, instances that stay keeping their
/// renderables and moving to the new ground. Host thread, the thread that edits the terrain.
/// </summary>
public sealed class TerrainProps : IDisposable
{
    private readonly RenderWorld _world;
    private readonly TerrainScatter _scatter;
    private readonly TerrainPropKind?[] _kinds;
    private readonly Dictionary<(int, int), List<Placed>> _placed = new();
    private readonly Dictionary<(int, int), Task<IReadOnlyList<TerrainInstance>>> _building = new();

    /// <param name="kinds">One per scatter rule by index, null for a rule placed by something else.</param>
    public TerrainProps(RenderWorld world, TerrainScatter scatter, IReadOnlyList<TerrainPropKind?> kinds)
    {
        _world = world;
        _scatter = scatter;
        _kinds = [.. kinds];
        scatter.Data.Edited += OnEdited;
    }

    private readonly record struct Placed(ulong Id, int Rule, RenderableHandle Handle);

    /// <summary>Metres around the viewer whose cells are placed.</summary>
    public double Range { get; set; } = 400;

    /// <summary>The most cells added in one update.</summary>
    public int CellsPerUpdate { get; set; } = 4;

    /// <summary>The renderables placed.</summary>
    public int Count { get; private set; }

    /// <summary>The cells placed.</summary>
    public int Cells => _placed.Count;

    /// <summary>The cells still scattering.</summary>
    public int Building => _building.Count;

    public void Update(Double3 viewer)
    {
        Double2 at = new(viewer.X, viewer.Z);
        (int cx, int cz) = _scatter.CellAt(at);
        int reach = (int)Math.Ceiling(Range / TerrainScatter.CellSize) + 1;
        List<((int, int) Cell, double Away)> wanted = new();
        for (int z = cz - reach; z <= cz + reach; z++)
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                double away = Away(x, z, at);
                if (away <= Range)
                    wanted.Add(((x, z), away));
            }
        wanted.Sort((a, b) => a.Away.CompareTo(b.Away));

        foreach ((int, int) cell in _placed.Keys.ToArray())
            if (Away(cell.Item1, cell.Item2, at) > Range + TerrainScatter.CellSize)
                Clear(cell);

        int added = 0;
        foreach (((int x, int z) cell, double _) in wanted)
        {
            if (_placed.ContainsKey(cell))
                continue;
            if (!_building.TryGetValue(cell, out Task<IReadOnlyList<TerrainInstance>>? task))
            {
                _building[cell] = Task.Run(() => _scatter.Cell(cell.x, cell.z));
                continue;
            }
            if (!task.IsCompleted || added >= CellsPerUpdate)
                continue;
            _building.Remove(cell);
            Place(cell, task.Result);
            added++;
        }

        foreach ((int, int) cell in _building.Keys.ToArray())
            if (_building[cell].IsCompleted && Away(cell.Item1, cell.Item2, at) > Range)
                _building.Remove(cell);
    }

    /// <summary>Waits for every cell still scattering.</summary>
    public Task Settle() => Task.WhenAll(_building.Values);

    /// <summary>Takes an instance away for good, as <see cref="TerrainScatter.Remove"/> does.</summary>
    public void Remove(ulong id)
    {
        _scatter.Remove(id);
        if (!_placed.TryGetValue(TerrainScatter.CellOf(id), out List<Placed>? list))
            return;
        int index = list.FindIndex(p => p.Id == id);
        if (index < 0)
            return;
        _world.Remove(list[index].Handle);
        list.RemoveAt(index);
        Count--;
    }

    internal bool TryGetHandle(ulong id, out RenderableHandle handle)
    {
        handle = default;
        if (!_placed.TryGetValue(TerrainScatter.CellOf(id), out List<Placed>? list))
            return false;
        int index = list.FindIndex(p => p.Id == id);
        if (index >= 0)
            handle = list[index].Handle;
        return index >= 0;
    }

    /// <summary>Takes every cell away, to be placed again on the next updates.</summary>
    public void Reset()
    {
        foreach ((int, int) cell in _placed.Keys.ToArray())
            Clear(cell);
    }

    public void Dispose()
    {
        _scatter.Data.Edited -= OnEdited;
        Reset();
    }

    private void OnEdited(TerrainEdited edited)
    {
        _scatter.Invalidate(edited.Min, edited.Max);
        foreach ((int x, int z) cell in _scatter.CellsTouching(edited.Min, edited.Max))
        {
            _building.Remove(cell);
            if (_placed.TryGetValue(cell, out List<Placed>? old))
                Replace(cell, old, _scatter.Cell(cell.x, cell.z));
        }
    }

    // Moves the instances that stay, adds the new ones and removes the gone ones
    private void Replace((int, int) cell, List<Placed> old, IReadOnlyList<TerrainInstance> instances)
    {
        Dictionary<ulong, Placed> before = new(old.Count);
        foreach (Placed placed in old)
            before[placed.Id] = placed;
        List<Placed> list = new(instances.Count);
        foreach (TerrainInstance instance in instances)
        {
            if (before.Remove(instance.Id, out Placed kept))
            {
                if (kept.Rule == instance.Rule)
                {
                    _world.SetTransform(kept.Handle, Transform(instance));
                    list.Add(kept);
                    continue;
                }
                _world.Remove(kept.Handle);
            }
            if (Add(instance) is Placed added)
                list.Add(added);
        }
        foreach (Placed gone in before.Values)
            _world.Remove(gone.Handle);
        Count += list.Count - old.Count;
        _placed[cell] = list;
    }

    private Placed? Add(in TerrainInstance instance)
    {
        TerrainPropKind? kind = (uint)instance.Rule < (uint)_kinds.Length ? _kinds[instance.Rule] : null;
        if (kind == null)
            return null;
        RenderableHandle handle = _world.Add(new RenderableDesc
        {
            Mesh = kind.Mesh,
            Lods = kind.Lods,
            LodBias = kind.LodBias,
            Flags = kind.Flags,
            Transform = Transform(instance),
        }, kind.Materials);
        return new Placed(instance.Id, instance.Rule, handle);
    }

    private static Double4x4 Transform(in TerrainInstance instance)
    {
        Quaternion turn = Quaternion.FromToRotation(new Float3(0f, 1f, 0f), instance.Up) * Quaternion.RotateY(instance.Yaw);
        return Double4x4.CreateTRS(instance.Position, turn, new Double3(instance.Scale, instance.Scale, instance.Scale));
    }

    private void Place((int, int) cell, IReadOnlyList<TerrainInstance> instances)
    {
        List<Placed> list = new(instances.Count);
        foreach (TerrainInstance instance in instances)
            if (Add(instance) is Placed added)
                list.Add(added);
        _placed[cell] = list;
        Count += list.Count;
    }

    private void Clear((int, int) cell)
    {
        foreach (Placed placed in _placed[cell])
            _world.Remove(placed.Handle);
        Count -= _placed[cell].Count;
        _placed.Remove(cell);
    }

    // The distance in x and z from a point to the nearest edge of a cell, 0 inside it
    private double Away(int x, int z, Double2 at)
    {
        double ox = _scatter.Origin.X + x * TerrainScatter.CellSize, oz = _scatter.Origin.Y + z * TerrainScatter.CellSize;
        double dx = Math.Max(Math.Max(ox - at.X, at.X - ox - TerrainScatter.CellSize), 0);
        double dz = Math.Max(Math.Max(oz - at.Y, at.Y - oz - TerrainScatter.CellSize), 0);
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
