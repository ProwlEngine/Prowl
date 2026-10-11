using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A descriptor table per frame in flight slot, kept in step with one desired state. A table is only written when its
/// slot comes around again, since a table a frame in flight still reads must not change.
/// </summary>
internal sealed class TableRing : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ResourceLayoutDescription _layout;
    private readonly Dictionary<(PropertyID Name, uint Index), object> _desired = new();
    private readonly List<(PropertyID Name, uint Index)> _log = new();
    private readonly List<(DescriptorTable Table, int Applied)> _tables = new();

    public TableRing(GraphicsDevice device, ResourceLayoutDescription layout, string name)
    {
        _device = device;
        _layout = layout;
        Name = name;
    }

    public string Name { get; }

    public void Set(PropertyID name, uint index, object resource)
    {
        if (_desired.TryGetValue((name, index), out object? existing) && Equals(existing, resource))
            return;
        _desired[(name, index)] = resource;
        _log.Add((name, index));
    }

    /// <summary>The table for a frame in flight slot, with every change since its last use written.</summary>
    public DescriptorTable Get(uint ringSlot)
    {
        while (_tables.Count <= ringSlot)
        {
            DescriptorTable created = _device.ResourceFactory.CreateDescriptorTable(_layout);
            created.Name = $"{Name} {_tables.Count}";
            _tables.Add((created, 0));
        }

        (DescriptorTable table, int applied) = _tables[(int)ringSlot];
        for (int i = applied; i < _log.Count; i++)
        {
            (PropertyID name, uint index) = _log[i];
            switch (_desired[(name, index)])
            {
                case Texture texture:
                    table.SetTexture(name, index, texture);
                    break;
                case Sampler sampler:
                    table.SetSampler(name, index, sampler);
                    break;
                case DeviceBufferRange range:
                    table.SetBuffer(name, index, range);
                    break;
                case DeviceBuffer buffer:
                    table.SetBuffer(name, index, buffer);
                    break;
            }
        }
        _tables[(int)ringSlot] = (table, _log.Count);
        Compact();
        return table;
    }

    public void Dispose()
    {
        foreach ((DescriptorTable table, _) in _tables)
            table.Dispose();
        _tables.Clear();
    }

    // Drops the log entries every table has applied.
    private void Compact()
    {
        if (_log.Count < 1024)
            return;
        int lowest = int.MaxValue;
        foreach ((_, int applied) in _tables)
            lowest = Math.Min(lowest, applied);
        if (lowest == 0 || _tables.Count < _device.MaxExecutingTasks)
            return;

        _log.RemoveRange(0, lowest);
        for (int i = 0; i < _tables.Count; i++)
            _tables[i] = (_tables[i].Table, _tables[i].Applied - lowest);
    }
}

/// <summary>
/// The renderer's bindless textures and samplers. A texture gets a slot in the table of its type the first time a
/// material or global uses it, and every world's frame table and every view's sampler table follow. Default textures
/// (white, black, gray, normal, transparent, plus any the host registers) fill in for textures that are not set.
/// </summary>
public sealed class RendererTextures : IMaterialResources, IDisposable
{
    /// <summary>Slots in the 2D texture table.</summary>
    public const uint Textures2D = 16384;

    /// <summary>Slots in each of the other texture tables.</summary>
    public const uint OtherTextures = 2048;

    /// <summary>Slots in the sampler table.</summary>
    public const uint Samplers = 64;

    private static readonly string[] s_tableNames = ["Graphite_Textures2D", "Graphite_Textures2DArray", "Graphite_Textures3D", "Graphite_TexturesCube", "Graphite_TexturesCubeArray"];

    private readonly GraphicsDevice _device;
    private readonly object _lock = new();
    private readonly Dictionary<Texture, uint>[] _slots = [new(), new(), new(), new(), new()];
    private readonly uint[] _nextSlot = new uint[5];
    private readonly Dictionary<(string Name, MaterialPropertyType Type), Texture> _defaults = new();
    private readonly Dictionary<string, Texture> _registeredDefaults = new(StringComparer.Ordinal);
    private readonly List<Texture> _owned = new();
    private readonly List<(SamplerDescription? Description, Sampler Sampler)> _samplers = new();
    private readonly List<TableRing> _frameTables = new();
    private readonly List<TableRing> _viewTables = new();
    private readonly List<(MaterialPropertyType Type, uint Slot, Texture Texture)> _assigned = new();

    internal RendererTextures(GraphicsDevice device)
    {
        _device = device;
        _samplers.Add((null, device.ResourceFactory.CreateSampler(SamplerDescription.Linear)));
    }

    /// <summary>Makes a named default available to texture properties, like <c>"surface"</c>.</summary>
    public void RegisterDefault(string name, Texture texture)
    {
        lock (_lock)
            _registeredDefaults[name] = texture;
    }

    uint IMaterialResources.TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName)
    {
        lock (_lock)
            return Slot(texture ?? Default(defaultName, type), type);
    }

    uint IMaterialResources.SamplerSlot(SamplerDescription? sampler)
    {
        lock (_lock)
        {
            for (int i = 0; i < _samplers.Count; i++)
            {
                if (Nullable.Equals(_samplers[i].Description, sampler))
                    return (uint)i;
            }

            if (_samplers.Count == Samplers)
                return 0;
            _samplers.Add((sampler, _device.ResourceFactory.CreateSampler(sampler!.Value)));
            uint slot = (uint)_samplers.Count - 1;
            foreach (TableRing ring in _viewTables)
                ring.Set("Graphite_Samplers", slot, _samplers[(int)slot].Sampler);
            return slot;
        }
    }

    internal void Track(TableRing frameTable)
    {
        lock (_lock)
        {
            _frameTables.Add(frameTable);
            foreach ((MaterialPropertyType type, uint slot, Texture texture) in _assigned)
                frameTable.Set(s_tableNames[GraphiteBindings.TextureTable(type)], slot, texture);
        }
    }

    internal void TrackView(TableRing viewTable)
    {
        lock (_lock)
        {
            _viewTables.Add(viewTable);
            for (int i = 0; i < _samplers.Count; i++)
                viewTable.Set("Graphite_Samplers", (uint)i, _samplers[i].Sampler);
        }
    }

    internal void Untrack(TableRing table)
    {
        lock (_lock)
        {
            _frameTables.Remove(table);
            _viewTables.Remove(table);
        }
    }

    public void Dispose()
    {
        foreach (Texture texture in _owned)
            texture.Dispose();
        foreach ((_, Sampler sampler) in _samplers)
            sampler.Dispose();
    }

    private uint Slot(Texture texture, MaterialPropertyType type)
    {
        int table = GraphiteBindings.TextureTable(type);
        if (_slots[table].TryGetValue(texture, out uint slot))
            return slot;

        uint capacity = table == 0 ? Textures2D : OtherTextures;
        if (_nextSlot[table] == capacity)
            return 0;
        slot = _nextSlot[table]++;
        _slots[table][texture] = slot;
        _assigned.Add((type, slot, texture));
        foreach (TableRing ring in _frameTables)
            ring.Set(s_tableNames[table], slot, texture);
        return slot;
    }

    private Texture Default(string name, MaterialPropertyType type)
    {
        if (type == MaterialPropertyType.Texture2D && _registeredDefaults.TryGetValue(name, out Texture? registered))
            return registered;
        if (_defaults.TryGetValue((name, type), out Texture? existing))
            return existing;

        (byte r, byte g, byte b, byte a) = name switch
        {
            "black" => ((byte)0, (byte)0, (byte)0, (byte)255),
            "gray" or "grey" => ((byte)128, (byte)128, (byte)128, (byte)255),
            "normal" or "bump" => ((byte)128, (byte)128, (byte)255, (byte)255),
            "transparent" or "clear" => ((byte)0, (byte)0, (byte)0, (byte)0),
            _ => ((byte)255, (byte)255, (byte)255, (byte)255),
        };

        TextureDescription description = type switch
        {
            MaterialPropertyType.Texture2DArray => TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled) with { ArrayLayers = 1 },
            MaterialPropertyType.Texture3D => TextureDescription.Texture3D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled),
            MaterialPropertyType.TextureCube or MaterialPropertyType.TextureCubeArray => TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled) with { Type = TextureType.TextureCube },
            _ => TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled),
        };

        Texture texture = _device.ResourceFactory.CreateTexture(description);
        texture.Name = $"Default {name} {type}";
        uint faces = type is MaterialPropertyType.TextureCube or MaterialPropertyType.TextureCubeArray ? 6u : 1u;
        for (uint face = 0; face < faces; face++)
            _device.UpdateTexture<byte>(texture, [r, g, b, a], new TextureRegion(0, 0, 0, 1, 1, 1, 0, face));
        _owned.Add(texture);
        _defaults[(name, type)] = texture;
        return texture;
    }
}
