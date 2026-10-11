using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A global property with its stable word offset inside every globals region.</summary>
public readonly record struct GlobalProperty(PropertyID Name, MaterialPropertyType Type, uint Offset);

/// <summary>
/// Values and keywords every shader can read by naming them. The renderer has one set, each world another that
/// overrides it. Setters are host thread only.
/// </summary>
public sealed class GlobalProperties
{
    private readonly GlobalRegistry _registry;
    private readonly Dictionary<PropertyID, MaterialValue> _values = new();
    private readonly Dictionary<string, string> _keywords = new(StringComparer.Ordinal);

    internal GlobalProperties(GlobalRegistry registry, GlobalProperties? fallback)
    {
        _registry = registry;
        Fallback = fallback;
    }

    /// <summary>The renderer's globals for a world's set, null for the renderer's own.</summary>
    public GlobalProperties? Fallback { get; }

    public uint Version { get; private set; }

    /// <summary>Bumps when a keyword changes, so cached variant choices know to look again.</summary>
    public uint KeywordVersion { get; private set; }

    /// <summary>The resolved keywords as of the last extraction: this set's over the fallback's. The render thread reads only this.</summary>
    internal IReadOnlyDictionary<string, string> KeywordSnapshot { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    private (uint Own, uint Fallback) _snapshotVersions = (uint.MaxValue, uint.MaxValue);

    internal void SnapshotKeywords()
    {
        (uint, uint) versions = (KeywordVersion, Fallback?.KeywordVersion ?? 0);
        if (versions == _snapshotVersions)
            return;

        Dictionary<string, string> snapshot = Fallback != null ? new(Fallback._keywords, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in _keywords)
            snapshot[pair.Key] = pair.Value;
        KeywordSnapshot = snapshot;
        _snapshotVersions = versions;
    }

    public void SetFloat(PropertyID name, float value) => Set(name, MaterialValue.FromFloat(value));

    public void SetFloat2(PropertyID name, Float2 value) => Set(name, MaterialValue.FromFloat2(value));

    public void SetFloat3(PropertyID name, Float3 value) => Set(name, MaterialValue.FromFloat3(value));

    public void SetFloat4(PropertyID name, Float4 value) => Set(name, MaterialValue.FromFloat4(value));

    public void SetColor(PropertyID name, Float4 value) => Set(name, MaterialValue.FromColor(value));

    public void SetInt(PropertyID name, int value) => Set(name, MaterialValue.FromInt(value));

    public void SetMatrix(PropertyID name, Float4x4 value) => Set(name, MaterialValue.FromMatrix(value));

    /// <summary>
    /// A storage buffer every shader declaring a buffer of this name reads, a world's over the renderer's. Null removes it.
    /// The host keeps it alive while it is set. Host thread.
    /// </summary>
    public void SetBuffer(PropertyID name, DeviceBuffer? buffer)
    {
        if (buffer == null)
            _buffers.Remove(name);
        else
            _buffers[name] = buffer;
        BufferVersion++;
    }

    public uint BufferVersion { get; private set; }

    /// <summary>The resolved buffers as of the last extraction: this set's over the fallback's. The render thread reads only this.</summary>
    internal IReadOnlyDictionary<PropertyID, DeviceBuffer> BufferSnapshot { get; private set; } = new Dictionary<PropertyID, DeviceBuffer>();

    private readonly Dictionary<PropertyID, DeviceBuffer> _buffers = new();
    private (uint Own, uint Fallback) _bufferVersions = (uint.MaxValue, uint.MaxValue);

    internal void SnapshotBuffers()
    {
        (uint, uint) versions = (BufferVersion, Fallback?.BufferVersion ?? 0);
        if (versions == _bufferVersions)
            return;

        Dictionary<PropertyID, DeviceBuffer> snapshot = Fallback != null ? new(Fallback._buffers) : new();
        foreach (KeyValuePair<PropertyID, DeviceBuffer> pair in _buffers)
            snapshot[pair.Key] = pair.Value;
        BufferSnapshot = snapshot;
        _bufferVersions = versions;
    }

    public void SetTexture(PropertyID name, Texture? texture, SamplerDescription? sampler = null)
    {
        MaterialPropertyType type = _registry.TryGet(name, out GlobalProperty property) && property.Type >= MaterialPropertyType.Texture2D
            ? property.Type
            : MaterialPropertyType.Texture2D;
        Set(name, MaterialValue.FromTexture(type, texture, sampler, MaterialValue.IdentityScaleOffset));
    }

    /// <summary>Removes a value, falling back to the renderer's for a world, then zero.</summary>
    public void Clear(PropertyID name)
    {
        if (_values.Remove(name))
            Changed();
    }

    /// <summary>Picks a value of a global axis. Material axes are set by materials.</summary>
    public void SetKeyword(string axis, string value)
    {
        if (_keywords.TryGetValue(axis, out string? existing) && existing == value)
            return;
        _keywords[axis] = value;
        KeywordVersion++;
        Version++;
    }

    public void EnableKeyword(string axis) => SetKeyword(axis, VariantSpace.ToggleValues[1]);

    public void DisableKeyword(string axis) => SetKeyword(axis, VariantSpace.ToggleValues[0]);

    public void ClearKeyword(string axis)
    {
        if (!_keywords.Remove(axis))
            return;
        KeywordVersion++;
        Version++;
    }

    /// <summary>This set's value, then the fallback's. False when neither sets it.</summary>
    public bool TryGet(PropertyID name, out MaterialValue value)
    {
        if (_values.TryGetValue(name, out value))
            return true;
        if (Fallback != null)
            return Fallback.TryGet(name, out value);
        return false;
    }

    /// <summary>This set's keyword, then the fallback's, or null.</summary>
    public string? GetKeyword(string axis)
    {
        if (_keywords.TryGetValue(axis, out string? value))
            return value;
        return Fallback?.GetKeyword(axis);
    }

    public IEnumerable<(PropertyID Name, MaterialValue Value)> LocalValues
    {
        get
        {
            foreach (KeyValuePair<PropertyID, MaterialValue> pair in _values)
                yield return (pair.Key, pair.Value);
        }
    }

    public IEnumerable<Keyword> LocalKeywords
    {
        get
        {
            foreach (KeyValuePair<string, string> pair in _keywords)
                yield return new Keyword(pair.Key, pair.Value);
        }
    }

    private void Set(PropertyID name, in MaterialValue value)
    {
        if (!_registry.Declare(name, value.Type, out GlobalProperty property, out string? error))
        {
            _registry.Report($"global.{name.Text()}", error!);
            return;
        }

        if (!MaterialSchema.Compatible(property.Type, value.Type))
        {
            _registry.Report($"global.{name.Text()}.type", $"The global {name.Text()} is a {property.Type}, not a {value.Type}.");
            return;
        }

        _values[name] = value;
        Changed();
    }

    private void Changed()
    {
        Version++;
        _registry.MarkDirty(this);
    }
}

/// <summary>
/// Gives every global name a stable std430 offset the first time a shader declares it or the host sets it. Offsets
/// never move, a new global appends. Holds one region per world in a storage buffer: the renderer's values with the
/// world's overrides, rewritten only when a value changes. Thread safe, so shaders can declare globals while they load.
/// </summary>
internal sealed class GlobalRegistry : IDisposable
{
    private const uint InitialRegionWords = 64;

    private readonly object _lock = new();
    private readonly GraphicsDevice _device;
    private readonly IMaterialResources _resources;
    private readonly Dictionary<PropertyID, GlobalProperty> _properties = new();
    private readonly List<GlobalProperty> _ordered = new();
    private readonly List<GlobalProperties?> _regions = new();
    private readonly Stack<int> _freeRegions = new();
    private readonly HashSet<GlobalProperties> _dirty = new();
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<int> _pendingFree = new();
    private readonly List<(ulong Execution, int Region)> _retiringRegions = new();
    private readonly List<(ulong Execution, DeviceBuffer Buffer)> _retiringBuffers = new();
    private GpuTable _table;
    private GpuTable? _replaced;
    private uint _usedWords;
    private bool _allDirty = true;

    public GlobalRegistry(GraphicsDevice device, IMaterialResources resources)
    {
        _device = device;
        _resources = resources;
        _table = new GpuTable(device, "Globals", InitialRegionWords, 4);
        Renderer = new GlobalProperties(this, null);
        _regions.Add(null);
    }

    /// <summary>The renderer wide values, region 0.</summary>
    public GlobalProperties Renderer { get; }

    public GlobalProperty[] Properties
    {
        get
        {
            lock (_lock)
                return [.. _ordered];
        }
    }

    public DeviceBuffer Buffer => _table.Buffer;

    /// <summary>Words per region. A view finds its world's globals at region times this.</summary>
    public uint RegionWords => _table.StrideWords;

    /// <summary>Called once per distinct problem, like a global set with two types.</summary>
    public Action<string>? Reporter { get; set; }

    public bool TryGet(PropertyID name, out GlobalProperty property)
    {
        lock (_lock)
            return _properties.TryGetValue(name, out property);
    }

    /// <summary>
    /// Finds or adds a global. Fails when the name already exists with an incompatible type, which is a load error for a
    /// shader declaring it.
    /// </summary>
    public bool Declare(PropertyID name, MaterialPropertyType type, out GlobalProperty property, out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (_properties.TryGetValue(name, out property))
            {
                if (Same(property.Type, type))
                    return true;
                error = $"The global {name.Text()} is already a {property.Type}, it cannot also be a {type}.";
                return false;
            }

            (uint size, uint alignment) = MaterialSchema.Std430(type);
            uint offset = MaterialSchema.AlignUp(_usedWords * 4, alignment) / 4;
            _usedWords = offset + size / 4;
            property = new GlobalProperty(name, type, offset);
            _properties[name] = property;
            _ordered.Add(property);
            _allDirty = true;
            return true;
        }
    }

    /// <summary>Adds a region for a world's globals, which override the renderer's.</summary>
    public (GlobalProperties Globals, int Region) AddWorld()
    {
        lock (_lock)
        {
            GlobalProperties globals = new(this, Renderer);
            int region;
            if (_freeRegions.Count > 0)
            {
                region = _freeRegions.Pop();
                _regions[region] = globals;
            }
            else
            {
                region = _regions.Count;
                _regions.Add(globals);
            }
            _dirty.Add(globals);
            return (globals, region);
        }
    }

    /// <summary>Frees a world's region once the GPU no longer reads it.</summary>
    public void RemoveWorld(int region)
    {
        lock (_lock)
        {
            _dirty.Remove(_regions[region]!);
            _regions[region] = null;
            _pendingFree.Add(region);
        }
    }

    internal ReadOnlySpan<uint> ReadRegion(int region) => _table.Read((uint)region);

    public int RegionOf(GlobalProperties globals) => globals == Renderer ? 0 : _regions.IndexOf(globals);

    /// <summary>Repacks every region at the next extraction, after a bindless slot a texture global may hold was freed.</summary>
    public void MarkAllDirty()
    {
        lock (_lock)
            _allDirty = true;
    }

    internal void MarkDirty(GlobalProperties globals)
    {
        lock (_lock)
        {
            if (globals == Renderer)
                _allDirty = true;
            else
                _dirty.Add(globals);
        }
    }

    internal void Report(string key, string message)
    {
        lock (_lock)
        {
            if (!_reported.Add(key))
                return;
        }
        Reporter?.Invoke(message);
    }

    /// <summary>True when globals were declared past the current region size, until the next extraction grows the regions.</summary>
    public bool NeedsGrowth
    {
        get
        {
            lock (_lock)
                return _usedWords > _table.StrideWords;
        }
    }

    /// <summary>
    /// Grows the regions when new globals no longer fit, writes every region whose values changed, and snapshots the
    /// keywords the render thread reads. A renderer change or a new global rewrites them all. Host thread, while the
    /// render thread is idle, so the buffer and region size stay fixed for the frame.
    /// </summary>
    public void Extract()
    {
        lock (_lock)
        {
            if (_usedWords > _table.StrideWords)
            {
                uint words = _table.StrideWords;
                while (words < _usedWords)
                    words *= 2;
                if (_replaced == null)
                    _replaced = _table;
                else
                    _table.Dispose();
                _table = new GpuTable(_device, "Globals", words, (uint)_regions.Count);
            }

            Renderer.SnapshotKeywords();
            Renderer.SnapshotBuffers();
            foreach (GlobalProperties? world in _regions)
            {
                world?.SnapshotKeywords();
                world?.SnapshotBuffers();
            }

            if (_allDirty || _replaced != null)
            {
                for (int region = 0; region < _regions.Count; region++)
                    Pack(region, region == 0 ? Renderer : _regions[region]);
            }
            else
            {
                foreach (GlobalProperties globals in _dirty)
                    Pack(_regions.IndexOf(globals), globals);
            }

            _allDirty = false;
            _dirty.Clear();
        }
    }

    /// <summary>Queues the written regions. Returns true when the buffer changed, so graphs importing it need a rebuild. Render thread.</summary>
    public bool Flush(Uploader uploader, ulong lastDispatched)
    {
        lock (_lock)
        {
            foreach (int region in _pendingFree)
                _retiringRegions.Add((lastDispatched, region));
            _pendingFree.Clear();

            bool changed = false;
            if (_replaced != null)
            {
                _retiringBuffers.Add((lastDispatched, _replaced.Buffer));
                _replaced = null;
                changed = true;
            }

            if (_table.Flush(uploader) is { } grown)
            {
                _retiringBuffers.Add((lastDispatched, grown));
                changed = true;
            }
            return changed;
        }
    }

    public void Reclaim(ulong completedExecution)
    {
        lock (_lock)
        {
            for (int i = _retiringRegions.Count - 1; i >= 0; i--)
            {
                if (_retiringRegions[i].Execution > completedExecution)
                    continue;
                _freeRegions.Push(_retiringRegions[i].Region);
                _retiringRegions.RemoveAt(i);
            }

            for (int i = _retiringBuffers.Count - 1; i >= 0; i--)
            {
                if (_retiringBuffers[i].Execution > completedExecution)
                    continue;
                _retiringBuffers[i].Buffer.Dispose();
                _retiringBuffers.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        _table.Dispose();
        foreach ((_, DeviceBuffer buffer) in _retiringBuffers)
            buffer.Dispose();
        _retiringBuffers.Clear();
    }

    private void Pack(int region, GlobalProperties? globals)
    {
        Span<uint> words = _table.Edit((uint)region);
        words.Clear();
        if (globals == null)
            return;

        foreach (GlobalProperty property in _ordered)
        {
            int word = (int)property.Offset;
            bool set = globals.TryGet(property.Name, out MaterialValue value) && MaterialSchema.Compatible(property.Type, value.Type);
            if (property.Type >= MaterialPropertyType.Texture2D)
            {
                words[word] = _resources.TextureSlot(set ? value.Texture : null, property.Type, "black");
                words[word + 1] = _resources.SamplerSlot(set ? value.Sampler : null);
            }
            else if (set)
            {
                MaterialSchema.WriteNumber(property.Type, value, words, word, false);
            }
        }
    }

    private static bool Same(MaterialPropertyType declared, MaterialPropertyType type)
        => declared == type || (Numeric4(declared) && Numeric4(type));

    private static bool Numeric4(MaterialPropertyType type) => type is MaterialPropertyType.Float4 or MaterialPropertyType.Color;
}
