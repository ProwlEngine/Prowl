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

    /// <summary>The LOD bias its samplers carry. Set only by the render thread, which reads it without the texture lock.</summary>
    internal float SamplerBias;

    public TableRing(GraphicsDevice device, ResourceLayoutDescription layout, string name)
    {
        _device = device;
        _layout = layout;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Changes some table has not applied yet.</summary>
    internal int LogCount => _log.Count;

    public void Set(PropertyID name, uint index, object resource)
    {
        if (_desired.TryGetValue((name, index), out object? existing) && Equals(existing, resource))
            return;
        _desired[(name, index)] = resource;
        _log.Add((name, index));
        if (_log.Count >= 1024)
            Compact();
    }

    /// <summary>The table for a frame in flight slot, with every change since its last use written.</summary>
    public DescriptorTable Get(uint ringSlot)
    {
        while (_tables.Count <= ringSlot)
        {
            DescriptorTable created = _device.ResourceFactory.CreateDescriptorTable(_layout);
            created.Name = $"{Name} {_tables.Count}";
            foreach (KeyValuePair<(PropertyID Name, uint Index), object> pair in _desired)
                Write(created, pair.Key.Name, pair.Key.Index, pair.Value);
            _tables.Add((created, _log.Count));
        }

        (DescriptorTable table, int applied) = _tables[(int)ringSlot];
        for (int i = applied; i < _log.Count; i++)
        {
            (PropertyID name, uint index) = _log[i];
            Write(table, name, index, _desired[(name, index)]);
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

    private static void Write(DescriptorTable table, PropertyID name, uint index, object resource)
    {
        switch (resource)
        {
            case Texture texture:
                table.SetTexture(name, index, texture);
                break;
            case TextureView view:
                table.SetTexture(name, index, view);
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

    // Drops the log entries every existing table has applied. Tables made later start from the whole desired state.
    private void Compact()
    {
        int lowest = _log.Count;
        foreach ((_, int applied) in _tables)
            lowest = Math.Min(lowest, applied);
        if (lowest == 0)
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
    internal const uint Textures2D = 16384;
    internal const uint OtherTextures = 2048;
    internal const uint Samplers = 64;

    private static readonly string[] s_tableNames = ["Graphite_Textures2D", "Graphite_Textures2DArray", "Graphite_Textures3D", "Graphite_TexturesCube", "Graphite_TexturesCubeArray"];

    private readonly GraphicsDevice _device;
    private readonly object _lock = new();
    private readonly Dictionary<Texture, uint>[] _slots = [new(), new(), new(), new(), new()];
    private readonly uint[] _nextSlot = new uint[5];
    private readonly Dictionary<(string Name, MaterialPropertyType Type), Texture> _defaults = new();
    private readonly Dictionary<string, Texture> _registeredDefaults = new(StringComparer.Ordinal);
    private readonly List<Texture> _owned = new();
    private readonly List<(SamplerDescription? Description, Sampler Sampler)> _samplers = new();
    private readonly List<TableRing> _viewTables = new();
    private readonly Dictionary<(int Slot, float Bias), Sampler> _biased = new();
    private readonly Stack<uint>[] _freeSlots = [new(), new(), new(), new(), new()];
    private readonly List<(ulong Execution, int Table, uint Slot)> _retiringSlots = new();
    private static readonly MaterialPropertyType[] s_tableTypes = [MaterialPropertyType.Texture2D, MaterialPropertyType.Texture2DArray, MaterialPropertyType.Texture3D, MaterialPropertyType.TextureCube, MaterialPropertyType.TextureCubeArray];

    internal RendererTextures(GraphicsDevice device)
    {
        _device = device;
        _samplers.Add((null, device.ResourceFactory.CreateSampler(SamplerDescription.Linear)));
        Table = device.ResourceFactory.CreateDescriptorTable(ProgramCache.FrameLayout);
        Table.Name = "Bindless textures";
    }

    /// <summary>The bindless textures every program reads as set 0, one table for every world and view.</summary>
    internal DescriptorTable Table { get; }

    /// <summary>Raised once when a table is full, so textures fall back to slot 0.</summary>
    internal Action<RendererError>? Reporter { get; set; }

    /// <summary>Whether a table was full since the last sweep, so textures that fell back to slot 0 need resolving again.</summary>
    internal bool Overflowed { get; set; }

    /// <summary>Slots in the 2D texture table.</summary>
    public uint Texture2DCapacity => Textures2D;

    /// <summary>Slots in each of the 2D array, 3D, cube and cube array tables.</summary>
    public uint OtherTextureCapacity => OtherTextures;

    /// <summary>Slots in the sampler table.</summary>
    public uint SamplerCapacity => Samplers;

    /// <summary>Makes a named default available to texture properties, like <c>"surface"</c>.</summary>
    public void RegisterDefault(string name, Texture texture)
    {
        lock (_lock)
            _registeredDefaults[name] = texture;
    }

    uint IMaterialResources.TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName)
    {
        lock (_lock)
            return Slot(texture is { IsDisposed: false } ? texture : Default(defaultName, type), type);
    }

    // Shadow maps keep their ordinary 2D slot for the existing light records, but comparison
    // sampling uses a separate descriptor array so Metal does not treat color textures as depth.
    internal uint ShadowSlot(Texture texture)
    {
        lock (_lock)
        {
            uint slot = Slot(texture, MaterialPropertyType.Texture2D);
            Table.SetTexture("Graphite_ShadowTextures2D", slot, texture);
            return slot;
        }
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
            {
                Reporter?.Invoke(new RendererError("textures.samplers.full", $"Every one of the {Samplers} sampler slots is in use, new samplers draw with the default sampler.", RendererErrorSeverity.Warning));
                return 0;
            }
            _samplers.Add((sampler, _device.ResourceFactory.CreateSampler(sampler!.Value)));
            uint slot = (uint)_samplers.Count - 1;
            foreach (TableRing ring in _viewTables)
                ring.Set("Graphite_Samplers", slot, Biased((int)slot, ring.SamplerBias));
            return slot;
        }
    }

    /// <summary>
    /// Frees the slots of disposed textures: the slot shows a default texture at once and is reused once
    /// <paramref name="lastDispatched"/> has completed. Returns true when a slot was freed, so records holding it repack.
    /// Host thread, at extraction.
    /// </summary>
    internal bool Sweep(ulong lastDispatched, ulong completed, HashSet<Texture> freed)
    {
        freed.Clear();
        lock (_lock)
        {
            for (int i = _retiringSlots.Count - 1; i >= 0; i--)
            {
                if (_retiringSlots[i].Execution > completed)
                    continue;
                (_, int retired, uint slot) = _retiringSlots[i];
                Table.SetTexture(s_tableNames[retired], slot, Default("black", s_tableTypes[retired]));
                _freeSlots[retired].Push(slot);
                _retiringSlots.RemoveAt(i);
            }

            for (int table = 0; table < _slots.Length; table++)
            {
                List<Texture>? disposed = null;
                foreach (Texture texture in _slots[table].Keys)
                {
                    if (texture.IsDisposed)
                        (disposed ??= new()).Add(texture);
                }
                if (disposed == null)
                    continue;

                foreach (Texture texture in disposed)
                {
                    freed.Add(texture);
                    uint slot = _slots[table][texture];
                    _slots[table].Remove(texture);
                    _retiringSlots.Add((lastDispatched, table, slot));
                }
            }
            return freed.Count > 0;
        }
    }

    internal void TrackView(TableRing viewTable)
    {
        lock (_lock)
        {
            _viewTables.Add(viewTable);
            viewTable.SamplerBias = 0;
            for (int i = 0; i < _samplers.Count; i++)
                viewTable.Set("Graphite_Samplers", (uint)i, _samplers[i].Sampler);
        }
    }

    /// <summary>Gives a view's sampler table every sampler with a texture LOD bias added, 0 for the samplers as made. Render thread.</summary>
    internal void SetViewBias(TableRing viewTable, float bias)
    {
        if (viewTable.SamplerBias == bias)
            return;
        lock (_lock)
        {
            viewTable.SamplerBias = bias;
            for (int i = 0; i < _samplers.Count; i++)
                viewTable.Set("Graphite_Samplers", (uint)i, Biased(i, bias));
        }
    }

    // A sampler slot's sampler with a LOD bias added, made once per slot and bias
    private Sampler Biased(int slot, float bias)
    {
        if (bias == 0)
            return _samplers[slot].Sampler;
        if (_biased.TryGetValue((slot, bias), out Sampler? made))
            return made;
        SamplerDescription description = _samplers[slot].Description ?? SamplerDescription.Linear;
        made = _device.ResourceFactory.CreateSampler(description with { LodBias = description.LodBias + bias });
        _biased[(slot, bias)] = made;
        return made;
    }

    internal void Untrack(TableRing table)
    {
        lock (_lock)
        {
            _viewTables.Remove(table);
        }
    }

    public void Dispose()
    {
        Table.Dispose();
        foreach (Texture texture in _owned)
            texture.Dispose();
        foreach ((_, Sampler sampler) in _samplers)
            sampler.Dispose();
        foreach (Sampler sampler in _biased.Values)
            sampler.Dispose();
    }

    private uint Slot(Texture texture, MaterialPropertyType type)
    {
        int table = GraphiteBindings.TextureTable(type);
        if (_slots[table].TryGetValue(texture, out uint slot))
            return slot;

        uint capacity = table == 0 ? Textures2D : OtherTextures;
        if (_freeSlots[table].Count > 0)
            slot = _freeSlots[table].Pop();
        else if (_nextSlot[table] < capacity)
            slot = _nextSlot[table]++;
        else
        {
            Overflowed = true;
            Reporter?.Invoke(new RendererError($"textures.{s_tableNames[table]}.full", $"Every one of the {capacity} slots of {s_tableNames[table]} is in use, new textures there show slot 0.", RendererErrorSeverity.Warning));
            return 0;
        }
        _slots[table][texture] = slot;
        Table.SetTexture(s_tableNames[table], slot, texture);
        return slot;
    }

    private Texture Default(string name, MaterialPropertyType type)
    {
        if (type == MaterialPropertyType.Texture2D && _registeredDefaults.TryGetValue(name, out Texture? registered) && !registered.IsDisposed)
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
