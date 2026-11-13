// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>Buckets the networked objects distance decides into cells, so a viewpoint only looks at the ones near it.</summary>
internal sealed class InterestGrid
{
    private const float MaxCell = 1e9f;

    private readonly Dictionary<(int X, int Y, int Z), List<NetworkObject>> _cells = new();
    private readonly Stack<List<NetworkObject>> _spare = new();

    // Objects with their own cull distance, or too far out to bucket, which every viewpoint looks at.
    private readonly List<NetworkObject> _wide = new();
    private float _cellSize;

    internal uint BuiltTick;
    internal int BuiltCallbacks;
    internal bool Built;

    internal void Build(IEnumerable<NetworkObject> entries, float cellSize, uint tick, int callbacks = 0)
    {
        foreach (List<NetworkObject> cell in _cells.Values)
        {
            cell.Clear();
            _spare.Push(cell);
        }

        _cells.Clear();
        _wide.Clear();
        _cellSize = cellSize;
        BuiltTick = tick;
        BuiltCallbacks = callbacks;
        Built = true;
        foreach (NetworkObject entry in entries)
        {
            if (entry.TearingDown || !entry.GameObject.IsValid() ||
                entry.GameObject.Network.Visibility != NetworkVisibility.Default)
            {
                continue;
            }

            if (entry.GameObject.Network.CullDistance > 0 || CellOf(entry.GameObject.Transform.Position) is not { } key)
            {
                _wide.Add(entry);
                continue;
            }

            if (!_cells.TryGetValue(key, out List<NetworkObject>? cell))
            {
                _cells[key] = cell = _spare.Count > 0 ? _spare.Pop() : new List<NetworkObject>();
            }

            cell.Add(entry);
        }
    }

    internal void Clear()
    {
        Built = false;
        _cells.Clear();
        _wide.Clear();
    }

    /// <summary>Adds every object that may be within <paramref name="range" /> of <paramref name="center" />.</summary>
    internal void Gather(Float3 center, float range, HashSet<NetworkObject> into)
    {
        AddAll(_wide, into);
        if (float.IsNaN(range) || !float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z))
        {
            return;
        }

        (int X, int Y, int Z) min = Clamped(center - new Float3(range)), max = Clamped(center + new Float3(range));
        double count = (double)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
        if (count > _cells.Count)
        {
            foreach (((int X, int Y, int Z) key, List<NetworkObject> cell) in _cells)
            {
                if (key.X >= min.X && key.X <= max.X && key.Y >= min.Y && key.Y <= max.Y && key.Z >= min.Z && key.Z <= max.Z)
                {
                    AddAll(cell, into);
                }
            }

            return;
        }

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    if (_cells.TryGetValue((x, y, z), out List<NetworkObject>? cell))
                    {
                        AddAll(cell, into);
                    }
                }
            }
        }
    }

    private static void AddAll(List<NetworkObject> objects, HashSet<NetworkObject> into)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            into.Add(objects[i]);
        }
    }

    private (int, int, int)? CellOf(Float3 position)
    {
        float x = MathF.Floor(position.X / _cellSize), y = MathF.Floor(position.Y / _cellSize), z = MathF.Floor(position.Z / _cellSize);
        return Math.Abs(x) <= MaxCell && Math.Abs(y) <= MaxCell && Math.Abs(z) <= MaxCell ? ((int)x, (int)y, (int)z) : null;
    }

    private (int, int, int) Clamped(Float3 position) =>
        (Clamp(position.X), Clamp(position.Y), Clamp(position.Z));

    private int Clamp(float value) => (int)Math.Clamp(MathF.Floor(value / _cellSize), -MaxCell, MaxCell);
}
