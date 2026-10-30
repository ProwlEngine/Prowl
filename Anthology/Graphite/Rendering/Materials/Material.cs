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
    private SurfaceSettings? _surface;

    internal Material(MaterialSystem system, int id, Shader shader)
    {
        _system = system;
        Id = id;
        _shader = shader;
        _locals = new MaterialValue[shader.Properties.Count];
    }

    /// <summary>Dense id, stable for the material's life.</summary>
    public int Id { get; }

    /// <summary>Name debug tools show, or null.</summary>
    public string? Name { get; set; }

    public uint Version { get; private set; }

    public bool IsDisposed { get; private set; }

    internal IReadOnlyList<Material> Children => _children;

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
        for (int i = 0; i < axes.Count; i++)
        {
            string value = GetKeyword(axes[i].Name);
            keywords[i] = new Keyword(axes[i].Name, HasValue(axes[i], value) ? value : axes[i].Values[0]);
        }

        bool twoSided = AxisOn(_shader.TwoSidedAxis);
        MaterialDraw draw = !AxisOn(_shader.TransmissiveAxis)
            ? new MaterialDraw(_shader, RenderQueue, DrawState(twoSided, false), CastShadows, keywords, Surface?.Type)
            : new MaterialDraw(_shader, Math.Max(RenderQueue, SurfaceSettings.TransparentQueue), DrawState(twoSided, true), CastShadows, keywords, SurfaceType.Transparent)
            {
                Transmissive = true,
                Volume = !twoSided,
            };
        if (!draw.Transparent || draw.StateOverride is not { } state || !CullsNone(state))
            return draw;
        return draw with { Sides = (draw with { StateOverride = Culling(FaceCullMode.Front).Apply(state) }, draw with { StateOverride = Culling(FaceCullMode.Back).Apply(state) }) };
    }

    // Whether a state draws both sides without writing depth, so a closed mesh's faces would blend in index order
    private static bool CullsNone(PassState state)
        => (state.Set & PassStateFields.CullMode) != 0 && state.Raster.CullMode == FaceCullMode.None
            && !((state.Set & PassStateFields.DepthWrite) != 0 && state.DepthStencil.DepthWriteEnabled);

    private static PassState Culling(FaceCullMode mode)
    {
        PassState state = new() { Set = PassStateFields.CullMode };
        state.Raster.CullMode = mode;
        return state;
    }

    private bool AxisOn(string? axis) => axis != null && GetKeyword(axis) == VariantSpace.ToggleValues[1];

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
            if (!IsDisposed)
                value?._children.Add(this);
            Changed(Dirt.Record | Dirt.Draw);
        }
    }

    /// <summary>
    /// How the surface draws, which sets the render queue, render state and surface axes this material does not set
    /// itself. Null inherits the parent's, and with none anywhere the shader's own passes decide.
    /// </summary>
    public SurfaceSettings? Surface
    {
        get => _surface ?? _parent?.Surface;
        set
        {
            _surface = value;
            Changed(Dirt.Draw);
        }
    }

    /// <summary>Negative means the surface's, else the shader's. Without a surface, queues from 2500 up draw as transparent.</summary>
    public int RenderQueue
    {
        get => _renderQueue >= 0 ? _renderQueue : _surface is { } surface ? surface.RenderQueue : _parent?.RenderQueue ?? _shader.RenderQueue;
        set
        {
            _renderQueue = value;
            Changed(Dirt.Draw);
        }
    }

    public PassState? StateOverride
    {
        get
        {
            PassState? inherited = _surface is { } surface ? surface.StateFor(_shader.PremultipliedOutput) : _parent?.StateOverride;
            return _stateOverride is { } local ? inherited is { } below ? local.Apply(below) : local : inherited;
        }
        set
        {
            _stateOverride = value;
            Changed(Dirt.Draw);
        }
    }

    /// <summary>
    /// The state the material draws with: <see cref="StateOverride"/>, culling off while the shader's
    /// <see cref="Shader.TwoSidedAxis"/> is on, and a transparent surface's blending while it transmits.
    /// </summary>
    private PassState? DrawState(bool twoSided, bool transmissive)
    {
        PassState? state = StateOverride;
        if (transmissive && Surface?.Type != SurfaceType.Transparent)
        {
            SurfaceSettings surface = Surface ?? new SurfaceSettings();
            PassState blended = (surface with { Type = SurfaceType.Transparent }).StateFor(_shader.PremultipliedOutput);
            state = state is { } below ? blended.Apply(below) : blended;
        }
        if (!twoSided)
            return state;
        PassState cullOff = new() { Set = PassStateFields.CullMode };
        cullOff.Raster.CullMode = FaceCullMode.None;
        return state is { } under ? cullOff.Apply(under) : cullOff;
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

    /// <summary>Sets a texture, keeping its scale offset, rotation, channel and UV set. A null texture means the schema default.</summary>
    public void SetTexture(PropertyID name, Texture? texture, SamplerDescription? sampler = null)
    {
        int index = _shader.FindProperty(name);
        MaterialPropertyType type = index >= 0 && _shader.Properties[index].Type >= MaterialPropertyType.Texture2D
            ? _shader.Properties[index].Type
            : TextureTypeOf(texture);
        bool has = TryGet(name, out MaterialValue current) && current.IsTexture;
        Float4 scaleOffset = has ? current.ScaleOffset : MaterialValue.IdentityScaleOffset;
        Set(name, MaterialValue.FromTexture(type, texture, sampler, scaleOffset, has ? current.MapChannel : 0, has ? current.MapUVSet : 0, has ? current.Rotation : 0));
    }

    public void SetTextureScaleOffset(PropertyID name, Float4 scaleOffset)
    {
        if (CurrentTexture(name, "a scale offset") is { } current)
            Set(name, current.WithScaleOffset(scaleOffset));
    }

    /// <summary>The radians a [Map] texture turns its UVs counter clockwise by, after its scale and before its offset, as KHR_texture_transform.</summary>
    public void SetTextureRotation(PropertyID name, float radians)
    {
        if (CurrentTexture(name, "a rotation") is { } current)
            Set(name, current.WithRotation(radians));
    }

    /// <summary>The channel a map reads for a scalar input, 0 to 3 for R to A. Colour inputs read RGB.</summary>
    public void SetTextureChannel(PropertyID name, int channel)
    {
        if (CurrentTexture(name, "a channel") is { } current)
            Set(name, current.WithMapping(Math.Clamp(channel, 0, 3), current.MapUVSet));
    }

    /// <summary>The UV set a map samples, 0 or 1.</summary>
    public void SetTextureUVSet(PropertyID name, int uvSet)
    {
        if (CurrentTexture(name, "a UV set") is { } current)
            Set(name, current.WithMapping(current.MapChannel, Math.Clamp(uvSet, 0, 1)));
    }

    private MaterialValue? CurrentTexture(PropertyID name, string what)
    {
        if (TryGet(name, out MaterialValue current) && current.IsTexture)
            return current;
        _system.Report(new RendererError("material.texture.missing", $"The shader {_shader.Name} has no texture {name.Text()} to set {what} on.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{name.Text()}" });
        return null;
    }

    /// <summary>Picks a value of a material axis. Global axes are set by the renderer, a world or a view and are ignored here.</summary>
    public void SetKeyword(string axis, string value)
    {
        int index = _shader.FindAxis(axis, out VariantScope scope);
        if (index >= 0 && scope == VariantScope.Global)
        {
            _system.Report(new RendererError("material.keyword.global", $"The axis {axis} is global in {_shader.Name}, materials cannot set it.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{axis}" });
            return;
        }
        if (index >= 0 && !HasValue(_shader.MaterialAxes[index], value))
        {
            _system.Report(new RendererError("material.keyword.value", $"The axis {axis} of {_shader.Name} has no value {value}, only {string.Join(" ", _shader.MaterialAxes[index].Values)}.", RendererErrorSeverity.Warning, this) { Key = $"{_shader.Name}.{axis}.{value}" });
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
            Changed(index >= 0 && DrivesKeyword(index) ? Dirt.Record | Dirt.Draw : Dirt.Record);

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

    /// <summary>
    /// The keyword value this material draws with for an axis: set on it or up the parent chain, else from the surface
    /// settings, else from the property that drives it, else the axis default.
    /// </summary>
    public string GetKeyword(string axis)
    {
        for (Material? material = this; material != null; material = material._parent)
        {
            if (material._keywords.TryGetValue(axis, out string? value))
                return value;
        }

        int index = _shader.FindAxis(axis, out VariantScope scope);
        if (index < 0 || scope != VariantScope.Material)
            return "";
        VariantSpace space = _shader.MaterialAxes[index];

        if (Surface is { } surface)
        {
            if (axis == SurfaceSettings.SurfaceAxis && HasValue(space, surface.SurfaceKeyword))
                return surface.SurfaceKeyword;
        }

        bool bound = false, on = false;
        foreach (KeywordBinding binding in _shader.KeywordBindings)
        {
            if (binding.Axis != axis || !TryGet(_shader.Properties[binding.Property].Name, out MaterialValue value))
                continue;
            if (binding.Kind == KeywordBindingKind.Enum)
                return space.Values[Math.Clamp(value.Type == MaterialPropertyType.Int ? value.Int : (int)value.Float, 0, space.Values.Count - 1)];
            bound = true;
            on |= binding.Kind == KeywordBindingKind.Texture ? value.Texture != null : (value.Type == MaterialPropertyType.Int ? value.Int : value.Float) != 0;
        }
        if (bound)
            return VariantSpace.ToggleValues[on ? 1 : 0];

        return space.Values[0];
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
        _parent?._children.Remove(this);
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
        Changed(DrivesKeyword(index) ? Dirt.Record | Dirt.Draw : Dirt.Record);
    }

    private static bool HasValue(VariantSpace space, string value)
    {
        foreach (string candidate in space.Values)
        {
            if (candidate == value)
                return true;
        }
        return false;
    }

    private bool DrivesKeyword(int property)
    {
        foreach (KeywordBinding binding in _shader.KeywordBindings)
        {
            if (binding.Property == property)
                return true;
        }
        return false;
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

    internal static MaterialPropertyType TextureTypeOf(Texture? texture)
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
internal sealed class MaterialTable(GraphicsDevice device, RenderStore store, Shader shader, int index)
{
    private readonly Stack<uint> _free = new();
    private uint _next;

    public Shader Shader { get; } = shader;

    public GpuTable Records { get; } = new(device, store, $"Graphite.Materials{index}", shader.Schema.StrideWords, 64);

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
}

/// <summary>
/// Owns materials, their ids and the per shader tables. Dirty materials are packed into their table at extraction on
/// the host thread, and the tables reach the GPU when the render thread flushes them.
/// </summary>
internal sealed class MaterialSystem(GraphicsDevice device, RenderStore store, IMaterialResources resources)
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
    private int _tableCount;
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
            changed |= table.Records.Flush(uploader);
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
                _tables.Remove(shader);
            }
            _tablesChanged = true;
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

    private MaterialTable Table(Shader shader)
    {
        if (!_tables.TryGetValue(shader, out MaterialTable? table))
        {
            table = new MaterialTable(device, store, shader, _tableCount++);
            _tables[shader] = table;
            _tablesChanged = true;
        }
        return table;
    }
}

/// <summary>A material's draw deciding state, snapshotted at extraction: shader, queue, state, shadows and its material keywords.</summary>
internal sealed record MaterialDraw(Shader Shader, int RenderQueue, PassState? StateOverride, bool CastShadows, Keyword[] Keywords, SurfaceType? Surface = null)
{
    /// <summary>Whether the shader's <see cref="Shader.TransmissiveAxis"/> is on, so it draws its Transmission pass for Forward.</summary>
    public bool Transmissive { get; init; }

    /// <summary>Whether it transmits through a volume, one sided, so it draws its BackDepth pass.</summary>
    public bool Volume { get; init; }

    /// <summary>
    /// For a transparent draw of both sides without depth writes: the same draw culling front faces, then culling back
    /// faces, which sorted draws take in that order so a closed mesh's far side stays behind its near side.
    /// </summary>
    public (MaterialDraw Back, MaterialDraw Front)? Sides { get; init; }

    /// <summary>The surface decides when there is one, else queues from 2500 up are transparent.</summary>
    public bool Transparent => Surface is { } surface ? surface == SurfaceType.Transparent : RenderQueue >= Shader.TransparentQueue;
}
