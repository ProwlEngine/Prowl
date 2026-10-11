using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A shader plus the values and keywords that configure it. Reads resolve the local value, then the parent chain, then
/// the schema default. Setters are host thread only and O(1).
/// </summary>
public sealed class Material : IDisposable
{
    [Flags]
    internal enum Dirt : byte
    {
        None = 0,
        Record = 1,
        Draw = 2,
    }

    private readonly MaterialSystem _system;
    private Shader _shader;
    private Material? _parent;
    private readonly List<Material> _children = new();
    private MaterialValue[] _locals;
    private Dictionary<PropertyID, MaterialValue>? _unknown;
    private readonly Dictionary<string, string> _keywords = new(StringComparer.Ordinal);
    private int _renderQueue = -1;
    private PassState? _stateOverride;
    private bool _castShadows = true;

    internal Material(MaterialSystem system, int id, Shader shader)
    {
        _system = system;
        Id = id;
        _shader = shader;
        _locals = new MaterialValue[shader.Properties.Count];
    }

    /// <summary>Dense id, stable for the material's life.</summary>
    public int Id { get; }

    public uint Version { get; private set; }

    public bool IsDisposed { get; private set; }

    internal Dirt Dirty { get; set; }

    /// <summary>The table and slot the record was last written to, owned by the material system.</summary>
    internal MaterialTable? Table { get; set; }

    internal uint Slot { get; set; }

    /// <summary>What decides the material's draws, as of the last extraction. The render thread reads only this.</summary>
    internal MaterialDraw Draw { get; set; } = null!;

    internal MaterialDraw SnapshotDraw()
    {
        IReadOnlyList<VariantSpace> axes = _shader.MaterialAxes;
        Keyword[] keywords = new Keyword[axes.Count];
        ulong variant = 0;
        ulong stride = 1;
        for (int i = 0; i < axes.Count; i++)
        {
            string value = GetKeyword(axes[i].Name);
            int index = 0;
            for (int v = 0; v < axes[i].Values.Count; v++)
            {
                if (axes[i].Values[v] == value)
                    index = v;
            }
            keywords[i] = new Keyword(axes[i].Name, axes[i].Values[index]);
            variant += (ulong)index * stride;
            stride *= (ulong)axes[i].Values.Count;
        }

        return new MaterialDraw(_shader, RenderQueue, StateOverride, CastShadows, variant, keywords);
    }

    /// <summary>Swapping keeps every value whose name the new shader still has, and keeps the others for later.</summary>
    public Shader Shader
    {
        get => _shader;
        set
        {
            if (value == _shader)
                return;

            List<(PropertyID, MaterialValue)> values = [.. LocalValues];
            _shader = value;
            _locals = new MaterialValue[value.Properties.Count];
            _unknown = null;
            foreach ((PropertyID name, MaterialValue local) in values)
                Store(name, local);
            Changed(Dirt.Record | Dirt.Draw);
        }
    }

    /// <summary>Values this material does not set come from the parent. Throws on a cycle.</summary>
    public Material? Parent
    {
        get => _parent;
        set
        {
            if (value == _parent)
                return;

            for (Material? ancestor = value; ancestor != null; ancestor = ancestor._parent)
            {
                if (ancestor == this)
                    throw new InvalidOperationException("A material cannot inherit from itself.");
            }

            _parent?._children.Remove(this);
            _parent = value;
            value?._children.Add(this);
            Changed(Dirt.Record | Dirt.Draw);
        }
    }

    /// <summary>Negative means the shader's. Queues from 2500 up draw as transparent.</summary>
    public int RenderQueue
    {
        get => _renderQueue >= 0 ? _renderQueue : _parent?.RenderQueue ?? _shader.RenderQueue;
        set
        {
            _renderQueue = value;
            Changed(Dirt.Draw);
        }
    }

    public PassState? StateOverride
    {
        get => _stateOverride ?? _parent?.StateOverride;
        set
        {
            _stateOverride = value;
            Changed(Dirt.Draw);
        }
    }

    public bool CastShadows
    {
        get => _castShadows;
        set
        {
            _castShadows = value;
            Changed(Dirt.Draw);
        }
    }

    public void SetFloat(PropertyID name, float value) => Set(name, MaterialValue.FromFloat(value));

    public void SetFloat2(PropertyID name, Float2 value) => Set(name, MaterialValue.FromFloat2(value));

    public void SetFloat3(PropertyID name, Float3 value) => Set(name, MaterialValue.FromFloat3(value));

    public void SetFloat4(PropertyID name, Float4 value) => Set(name, MaterialValue.FromFloat4(value));

    public void SetColor(PropertyID name, Float4 value) => Set(name, MaterialValue.FromColor(value));

    public void SetInt(PropertyID name, int value) => Set(name, MaterialValue.FromInt(value));

    public void SetMatrix(PropertyID name, Float4x4 value) => Set(name, MaterialValue.FromMatrix(value));

    /// <summary>Sets a texture, keeping its scale offset. A null texture means the schema default.</summary>
    public void SetTexture(PropertyID name, Texture? texture, SamplerDescription? sampler = null)
    {
        int index = _shader.FindProperty(name);
        MaterialPropertyType type = index >= 0 && _shader.Properties[index].Type >= MaterialPropertyType.Texture2D
            ? _shader.Properties[index].Type
            : TextureTypeOf(texture);
        Float4 scaleOffset = TryGet(name, out MaterialValue current) && current.IsTexture ? current.ScaleOffset : MaterialValue.IdentityScaleOffset;
        Set(name, MaterialValue.FromTexture(type, texture, sampler, scaleOffset));
    }

    public void SetTextureScaleOffset(PropertyID name, Float4 scaleOffset)
    {
        if (!TryGet(name, out MaterialValue current) || !current.IsTexture)
        {
            _system.Report(new RendererError("material.texture.missing", $"The shader {_shader.Name} has no texture {name.Text()} to set a scale offset on.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{name.Text()}" });
            return;
        }
        Set(name, current.WithScaleOffset(scaleOffset));
    }

    /// <summary>Picks a value of a material axis. Global axes are set by the renderer, a world or a view and are ignored here.</summary>
    public void SetKeyword(string axis, string value)
    {
        if (_shader.FindAxis(axis, out VariantScope scope) >= 0 && scope == VariantScope.Global)
        {
            _system.Report(new RendererError("material.keyword.global", $"The axis {axis} is global in {_shader.Name}, materials cannot set it.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{axis}" });
            return;
        }

        if (_keywords.TryGetValue(axis, out string? existing) && existing == value)
            return;
        _keywords[axis] = value;
        Changed(Dirt.Draw);
    }

    public void EnableKeyword(string axis) => SetKeyword(axis, VariantSpace.ToggleValues[1]);

    public void DisableKeyword(string axis) => SetKeyword(axis, VariantSpace.ToggleValues[0]);

    /// <summary>Removes a local value or keyword, falling back to the parent, then the default.</summary>
    public void ClearOverride(PropertyID name)
    {
        int index = _shader.FindProperty(name);
        bool removed = index >= 0 ? !_locals[index].IsEmpty : _unknown?.Remove(name) == true;
        if (index >= 0)
            _locals[index] = default;
        if (removed)
            Changed(Dirt.Record);

        string? axis = PropertyID.ToString(name);
        if (axis != null && _keywords.Remove(axis))
            Changed(Dirt.Draw);
    }

    /// <summary>The value this material draws with: local, then the parent chain, then the shader default.</summary>
    public bool TryGet(PropertyID name, out MaterialValue value)
    {
        int index = _shader.FindProperty(name);
        for (Material? material = this; material != null; material = material._parent)
        {
            value = material.GetLocal(name);
            if (!value.IsEmpty && (index < 0 || MaterialSchema.Compatible(_shader.Properties[index].Type, value.Type)))
                return true;
        }

        value = index >= 0 ? _shader.Properties[index].Default : default;
        return index >= 0;
    }

    /// <summary>Only what this material sets, empty when it sets nothing for the name.</summary>
    public MaterialValue GetLocal(PropertyID name)
    {
        int index = _shader.FindProperty(name);
        if (index >= 0)
            return _locals[index];
        return _unknown != null && _unknown.TryGetValue(name, out MaterialValue value) ? value : default;
    }

    /// <summary>The keyword value this material draws with for an axis: local, then the parent chain, then the axis default.</summary>
    public string GetKeyword(string axis)
    {
        for (Material? material = this; material != null; material = material._parent)
        {
            if (material._keywords.TryGetValue(axis, out string? value))
                return value;
        }

        int index = _shader.FindAxis(axis, out VariantScope scope);
        return index >= 0 && scope == VariantScope.Material ? _shader.MaterialAxes[index].Values[0] : "";
    }

    public IEnumerable<(PropertyID Name, MaterialValue Value)> LocalValues
    {
        get
        {
            for (int i = 0; i < _locals.Length; i++)
            {
                if (!_locals[i].IsEmpty)
                    yield return (_shader.Properties[i].Name, _locals[i]);
            }

            if (_unknown != null)
            {
                foreach (KeyValuePair<PropertyID, MaterialValue> pair in _unknown)
                    yield return (pair.Key, pair.Value);
            }
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

    /// <summary>A material that inherits everything from this one until it sets its own values.</summary>
    public Material CreateChild()
    {
        Material child = _system.Create(_shader);
        child.Parent = this;
        return child;
    }

    /// <summary>Frees the material's slot once the GPU no longer uses it. Children keep reading its values.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        _system.Free(this);
    }

    /// <summary>Fills one resolved value per schema property.</summary>
    internal void Resolve(Span<MaterialValue> values)
    {
        IReadOnlyList<MaterialProperty> properties = _shader.Properties;
        for (int i = 0; i < properties.Count; i++)
        {
            MaterialValue value = properties[i].Default;
            for (Material? material = this; material != null; material = material._parent)
            {
                MaterialValue local = material.GetLocal(properties[i].Name);
                if (!local.IsEmpty && MaterialSchema.Compatible(properties[i].Type, local.Type))
                {
                    value = local;
                    break;
                }
            }
            values[i] = value;
        }
    }

    private void Set(PropertyID name, in MaterialValue value)
    {
        int index = _shader.FindProperty(name);
        if (index < 0)
            _system.Report(new RendererError("material.property.missing", $"The shader {_shader.Name} has no property {name.Text()}. The value is kept in case the shader gains it.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{name.Text()}" });
        else if (!MaterialSchema.Compatible(_shader.Properties[index].Type, value.Type))
            _system.Report(new RendererError("material.property.type", $"The property {name.Text()} of {_shader.Name} is a {_shader.Properties[index].Type}, not a {value.Type}.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{name.Text()}.type" });

        Store(name, value);
        Changed(Dirt.Record);
    }

    private void Store(PropertyID name, in MaterialValue value)
    {
        int index = _shader.FindProperty(name);
        if (index >= 0)
            _locals[index] = value;
        else
            (_unknown ??= new())[name] = value;
    }

    private void Changed(Dirt dirt)
    {
        Version++;
        _system.MarkDirty(this, dirt);
        foreach (Material child in _children)
            child.Changed(dirt);
    }

    private static MaterialPropertyType TextureTypeOf(Texture? texture)
    {
        if (texture == null)
            return MaterialPropertyType.Texture2D;
        return texture.Type switch
        {
            TextureType.TextureCube => texture.ArrayLayers > 1 ? MaterialPropertyType.TextureCubeArray : MaterialPropertyType.TextureCube,
            TextureType.Texture3D => MaterialPropertyType.Texture3D,
            _ => texture.ArrayLayers > 1 ? MaterialPropertyType.Texture2DArray : MaterialPropertyType.Texture2D,
        };
    }
}

/// <summary>The GPU table holding the record of every material of one shader. Slots are reused only after GPU retire.</summary>
internal sealed class MaterialTable(GraphicsDevice device, Shader shader) : IDisposable
{
    private readonly Stack<uint> _free = new();
    private uint _next;

    public Shader Shader { get; } = shader;

    public GpuTable Records { get; } = new(device, $"Materials {shader.Name}", shader.Schema.StrideWords, 64);

    /// <summary>Slots held by live materials or still retiring. The table goes when this reaches zero.</summary>
    public int Used { get; private set; }

    public uint Allocate()
    {
        Used++;
        return _free.Count > 0 ? _free.Pop() : _next++;
    }

    public void Release(uint slot)
    {
        Used--;
        _free.Push(slot);
    }

    public void Dispose() => Records.Dispose();
}

/// <summary>
/// Owns materials, their ids and the per shader tables. Dirty materials are packed into their table at extraction on
/// the host thread, and the tables reach the GPU when the render thread flushes them.
/// </summary>
internal sealed class MaterialSystem(GraphicsDevice device, IMaterialResources resources) : IDisposable
{
    private readonly object _lock = new();
    private readonly Stack<int> _freeIds = new();
    private int _nextId;
    private readonly List<Material> _dirty = new();
    private readonly List<Material> _freed = new();
    private readonly Dictionary<Shader, MaterialTable> _tables = new();
    private readonly List<(MaterialTable Table, uint Slot)> _pendingSlots = new();
    private readonly List<(ulong Execution, MaterialTable Table, uint Slot)> _retiringSlots = new();
    private readonly List<(ulong Execution, int Id)> _retiringIds = new();
    private readonly List<int> _pendingIds = new();
    private readonly List<(ulong Execution, DeviceBuffer Buffer)> _retiringBuffers = new();
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<Material> _live = new();
    private MaterialValue[] _resolved = new MaterialValue[64];
    private bool _tablesChanged;

    /// <summary>Called once per distinct problem, like a property the shader does not have.</summary>
    public Action<RendererError>? Reporter { get; set; }

    public IReadOnlyDictionary<Shader, MaterialTable> Tables => _tables;

    /// <summary>Makes a material. Thread safe.</summary>
    public Material Create(Shader shader)
    {
        lock (_lock)
        {
            Material material = new(this, _freeIds.Count > 0 ? _freeIds.Pop() : _nextId++, shader);
            material.Dirty = Material.Dirt.Record | Material.Dirt.Draw;
            _dirty.Add(material);
            _live.Add(material);
            return material;
        }
    }

    internal void MarkDirty(Material material, Material.Dirt dirt)
    {
        lock (_lock)
        {
            if (material.Dirty == Material.Dirt.None && !material.IsDisposed)
                _dirty.Add(material);
            material.Dirty |= dirt;
        }
    }

    internal void Free(Material material)
    {
        lock (_lock)
        {
            _freed.Add(material);
            _live.Remove(material);
        }
    }

    /// <summary>Repacks every material's record at the next extraction, after a bindless slot they may hold was freed.</summary>
    public void MarkAllRecords()
    {
        lock (_lock)
        {
            foreach (Material material in _live)
            {
                if (material.Dirty == Material.Dirt.None)
                    _dirty.Add(material);
                material.Dirty |= Material.Dirt.Record;
            }
        }
    }

    internal void Report(RendererError error)
    {
        lock (_lock)
        {
            if (!_reported.Add(error.Key ?? error.Code))
                return;
        }
        Reporter?.Invoke(error);
    }

    /// <summary>
    /// Packs every dirty material into its shader's table, moving it to a new table after a shader swap. Materials whose
    /// draws change (shader, keywords, queue, state, shadows) are added to <paramref name="drawChanged"/>. Host thread.
    /// </summary>
    public void Extract(List<Material> drawChanged, List<Material>? recordChanged = null)
    {
        lock (_lock)
        {
            foreach (Material material in _freed)
            {
                if (material.Table != null)
                    _pendingSlots.Add((material.Table, material.Slot));
                material.Table = null;
                _pendingIds.Add(material.Id);
                drawChanged.Add(material);
            }
            _freed.Clear();

            foreach (Material material in _dirty)
            {
                Material.Dirt dirt = material.Dirty;
                material.Dirty = Material.Dirt.None;
                if (material.IsDisposed)
                    continue;

                if (material.Table?.Shader != material.Shader)
                {
                    if (material.Table != null)
                        _pendingSlots.Add((material.Table, material.Slot));
                    material.Table = Table(material.Shader);
                    material.Slot = material.Table.Allocate();
                    dirt |= Material.Dirt.Record | Material.Dirt.Draw;
                }

                if ((dirt & Material.Dirt.Record) != 0)
                {
                    int count = material.Shader.Properties.Count;
                    if (_resolved.Length < count)
                        Array.Resize(ref _resolved, count * 2);
                    Span<MaterialValue> values = _resolved.AsSpan(0, count);
                    material.Resolve(values);
                    material.Shader.Schema.Pack(values, material.Table.Records.Edit(material.Slot), resources);
                    recordChanged?.Add(material);
                }

                if ((dirt & Material.Dirt.Draw) != 0)
                {
                    material.Draw = material.SnapshotDraw();
                    drawChanged.Add(material);
                }
            }
            _dirty.Clear();
        }
    }

    /// <summary>
    /// Queues the tables' dirty records. Freed slots and ids wait for <paramref name="lastDispatched"/>. Returns true when
    /// the set of table buffers changed, so graphs importing them need a rebuild. Render thread.
    /// </summary>
    public bool Flush(Uploader uploader, ulong lastDispatched)
    {
        foreach ((MaterialTable table, uint slot) in _pendingSlots)
            _retiringSlots.Add((lastDispatched, table, slot));
        _pendingSlots.Clear();
        foreach (int id in _pendingIds)
            _retiringIds.Add((lastDispatched, id));
        _pendingIds.Clear();

        bool changed = _tablesChanged;
        _tablesChanged = false;
        foreach (MaterialTable table in _tables.Values)
        {
            if (table.Records.Flush(uploader) is { } replaced)
            {
                _retiringBuffers.Add((lastDispatched, replaced));
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Returns slots, ids and buffers freed by frames the GPU has finished. Render thread.</summary>
    public void Reclaim(ulong completedExecution)
    {
        for (int i = _retiringSlots.Count - 1; i >= 0; i--)
        {
            if (_retiringSlots[i].Execution > completedExecution)
                continue;
            _retiringSlots[i].Table.Release(_retiringSlots[i].Slot);
            _retiringSlots.RemoveAt(i);
        }

        List<Shader>? empty = null;
        foreach ((Shader shader, MaterialTable table) in _tables)
        {
            if (table.Used == 0)
                (empty ??= new()).Add(shader);
        }
        if (empty != null)
        {
            foreach (Shader shader in empty)
            {
                _tables[shader].Dispose();
                _tables.Remove(shader);
            }
            _tablesChanged = true;
        }

        for (int i = _retiringBuffers.Count - 1; i >= 0; i--)
        {
            if (_retiringBuffers[i].Execution > completedExecution)
                continue;
            _retiringBuffers[i].Buffer.Dispose();
            _retiringBuffers.RemoveAt(i);
        }

        for (int i = _retiringIds.Count - 1; i >= 0; i--)
        {
            if (_retiringIds[i].Execution > completedExecution)
                continue;
            lock (_lock)
                _freeIds.Push(_retiringIds[i].Id);
            _retiringIds.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        foreach (MaterialTable table in _tables.Values)
            table.Dispose();
        _tables.Clear();
        foreach ((_, DeviceBuffer buffer) in _retiringBuffers)
            buffer.Dispose();
        _retiringBuffers.Clear();
    }

    private MaterialTable Table(Shader shader)
    {
        if (!_tables.TryGetValue(shader, out MaterialTable? table))
        {
            table = new MaterialTable(device, shader);
            _tables[shader] = table;
            _tablesChanged = true;
        }
        return table;
    }
}

/// <summary>A material's draw deciding state, snapshotted at extraction: shader, queue, state, shadows and its material variant.</summary>
internal sealed record MaterialDraw(Shader Shader, int RenderQueue, PassState? StateOverride, bool CastShadows, ulong Variant, Keyword[] Keywords)
{
    public bool Transparent => RenderQueue >= Shader.TransparentQueue;
}
