using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>What a prototype's instances draw as.</summary>
public sealed class TerrainPropKind
{
    public required GpuMesh Mesh { get; init; }
    public LodSet? Lods { get; init; }
    public required Material[] Materials { get; init; }
    public RenderableFlags Flags { get; init; } = RenderableFlags.Static | RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
    public float LodBias { get; init; } = 1f;
}

/// <summary>
/// Keeps instances placed as renderables in the cells around a viewer, from trees (<see cref="TerrainTreeInstances"/>)
/// or mesh details (<see cref="TerrainDetailInstances"/>). Cells are built on worker threads and added on
/// <see cref="Update"/>, nearest first, and leave once a cell beyond the range. When the instances change the cells
/// under the change are built again at once, instances that stay keeping their renderables and moving to their new
/// place. Host thread, the thread that edits the terrain.
/// </summary>
public sealed class TerrainProps : IDisposable
{
    private readonly RenderWorld _world;
    private readonly ITerrainInstances _source;
    private readonly TerrainPropKind?[] _kinds;
    private readonly Dictionary<(int, int), List<Placed>> _placed = new();
    private readonly Dictionary<(int, int), Task<IReadOnlyList<TerrainInstance>>> _building = new();

    /// <param name="world">The world the props draw in.</param>
    /// <param name="source">Where the placed instances come from.</param>
    /// <param name="kinds">One per prototype by index, null for one drawn some other way.</param>
    public TerrainProps(RenderWorld world, ITerrainInstances source, IReadOnlyList<TerrainPropKind?> kinds)
    {
        _world = world;
        _source = source;
        _kinds = [.. kinds];
        source.Changed += OnChanged;
    }

    private readonly record struct Placed(ulong Id, int Prototype, RenderableHandle Handle);

    /// <summary>Metres around the viewer whose cells are placed.</summary>
    public double Range { get; set; } = 400;

    /// <summary>The most cells added in one update.</summary>
    public int CellsPerUpdate { get; set; } = 4;

    /// <summary>The renderables placed.</summary>
    public int Count { get; private set; }

    /// <summary>The cells placed.</summary>
    public int Cells => _placed.Count;

    /// <summary>The cells still building.</summary>
    public int Building => _building.Count;

    public void Update(Double3 viewer)
    {
        Double2 at = new(viewer.X, viewer.Z);
        double size = _source.CellSize;
        int cx = (int)Math.Floor((at.X - _source.Origin.X) / size), cz = (int)Math.Floor((at.Y - _source.Origin.Y) / size);
        int reach = (int)Math.Ceiling(Range / size) + 1;
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
            if (Away(cell.Item1, cell.Item2, at) > Range + size)
                Clear(cell);

        int added = 0;
        foreach (((int x, int z) cell, double _) in wanted)
        {
            if (_placed.ContainsKey(cell))
                continue;
            if (!_building.TryGetValue(cell, out Task<IReadOnlyList<TerrainInstance>>? task))
            {
                _building[cell] = Task.Run(() => _source.Cell(cell.x, cell.z));
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

    /// <summary>Waits for every cell still building.</summary>
    public Task Settle() => Task.WhenAll(_building.Values);

    internal bool TryGetHandle(ulong id, out RenderableHandle handle)
    {
        foreach (List<Placed> list in _placed.Values)
        {
            int index = list.FindIndex(p => p.Id == id);
            if (index >= 0)
            {
                handle = list[index].Handle;
                return true;
            }
        }
        handle = default;
        return false;
    }

    /// <summary>Takes every cell away, to be placed again on the next updates.</summary>
    public void Reset()
    {
        foreach ((int, int) cell in _placed.Keys.ToArray())
            Clear(cell);
    }

    public void Dispose()
    {
        _source.Changed -= OnChanged;
        Reset();
    }

    private void OnChanged(Double2 min, Double2 max)
    {
        double size = _source.CellSize;
        int x0 = (int)Math.Floor((min.X - _source.Origin.X) / size), x1 = (int)Math.Floor((max.X - _source.Origin.X) / size);
        int z0 = (int)Math.Floor((min.Y - _source.Origin.Y) / size), z1 = (int)Math.Floor((max.Y - _source.Origin.Y) / size);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                _building.Remove((x, z));
                if (_placed.TryGetValue((x, z), out List<Placed>? old))
                    Replace((x, z), old, _source.Cell(x, z));
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
                if (kept.Prototype == instance.Prototype)
                {
                    _world.SetTransform(kept.Handle, Transform(instance));
                    _world.SetColor(kept.Handle, instance.Color);
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
        TerrainPropKind? kind = (uint)instance.Prototype < (uint)_kinds.Length ? _kinds[instance.Prototype] : null;
        if (kind == null)
            return null;
        RenderableHandle handle = _world.Add(new RenderableDesc
        {
            Mesh = kind.Mesh,
            Lods = kind.Lods,
            LodBias = kind.LodBias,
            Flags = kind.Flags,
            Transform = Transform(instance),
            Color = instance.Color,
        }, kind.Materials);
        return new Placed(instance.Id, instance.Prototype, handle);
    }

    private static Double4x4 Transform(in TerrainInstance instance)
    {
        Quaternion turn = Quaternion.FromToRotation(new Float3(0f, 1f, 0f), instance.Up) * Quaternion.RotateY(instance.Yaw);
        return Double4x4.CreateTRS(instance.Position, turn, new Double3(instance.Scale.X, instance.Scale.Y, instance.Scale.Z));
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
        double size = _source.CellSize;
        double ox = _source.Origin.X + x * size, oz = _source.Origin.Y + z * size;
        double dx = Math.Max(Math.Max(ox - at.X, at.X - ox - size), 0);
        double dz = Math.Max(Math.Max(oz - at.Y, at.Y - oz - size), 0);
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
