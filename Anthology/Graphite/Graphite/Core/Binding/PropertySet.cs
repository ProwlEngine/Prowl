using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>
/// User-owned named shader resources/uniforms to bind and upload for a draw. Applied to the command
/// buffer's active set; last applied wins.
/// <para>
/// Not thread-safe. Upload at your own risk.
/// </para>
/// </summary>
public sealed partial class PropertySet
{
    private readonly Dictionary<PropertyID, PropertyEntry> _entries;

    private uint _resourceVersion;
    private uint _version;


    /// <summary>
    /// Empty PropertySet, 0 capacity.
    /// </summary>
    public PropertySet() : this(0)
    {
    }


    /// <summary>Empty PropertySet with given entry capacity.</summary>
    /// <param name="initialEntryCapacity">Initial dictionary capacity.</param>
    public PropertySet(int initialEntryCapacity)
    {
        _entries = new(initialEntryCapacity);
    }


    /// <summary>
    /// Bumps on any resource setter call (buffer/texture/sampler). Uniform scalar writes don't bump it.
    /// </summary>
    public uint ResourceVersion => _resourceVersion;


    /// <summary>
    /// Bumps on any mutation, including uniform writes. Superset of ResourceVersion; unchanged means
    /// nothing changed. Used to skip re-merging an unchanged set.
    /// </summary>
    internal uint Version => _version;


    /// <summary>Entry count.</summary>
    public int EntryCount => _entries.Count;


    internal Dictionary<PropertyID, PropertyEntry> Entries => _entries;


    /// <summary>Sets a float uniform.</summary>
    public void SetFloat(PropertyID name, float v) => WriteUniform(name, v, UniformScalarType.Float1);
    /// <summary>Sets a float2 uniform.</summary>
    public void SetFloat2(PropertyID name, Float2 v) => WriteUniform(name, v, UniformScalarType.Float2);
    /// <summary>Sets a float3 uniform.</summary>
    public void SetFloat3(PropertyID name, Float3 v) => WriteUniform(name, v, UniformScalarType.Float3);
    /// <summary>Sets a float4 uniform.</summary>
    public void SetFloat4(PropertyID name, Float4 v) => WriteUniform(name, v, UniformScalarType.Float4);

    /// <summary>Sets an int uniform.</summary>
    public void SetInt(PropertyID name, int v) => WriteUniform(name, v, UniformScalarType.Int1);
    /// <summary>Sets an int2 uniform.</summary>
    public void SetInt2(PropertyID name, Int2 v) => WriteUniform(name, v, UniformScalarType.Int2);
    /// <summary>Sets an int3 uniform.</summary>
    public void SetInt3(PropertyID name, Int3 v) => WriteUniform(name, v, UniformScalarType.Int3);
    /// <summary>Sets an int4 uniform.</summary>
    public void SetInt4(PropertyID name, Int4 v) => WriteUniform(name, v, UniformScalarType.Int4);

    /// <summary>Sets a double uniform.</summary>
    public void SetDouble(PropertyID name, double v) => WriteUniform(name, v, UniformScalarType.Double1);
    /// <summary>Sets a double2 uniform.</summary>
    public void SetDouble2(PropertyID name, Double2 v) => WriteUniform(name, v, UniformScalarType.Double2);
    /// <summary>Sets a double3 uniform.</summary>
    public void SetDouble3(PropertyID name, Double3 v) => WriteUniform(name, v, UniformScalarType.Double3);
    /// <summary>Sets a double4 uniform.</summary>
    public void SetDouble4(PropertyID name, Double4 v) => WriteUniform(name, v, UniformScalarType.Double4);

    /// <summary>Sets a float4x4 matrix uniform.</summary>
    public void SetMatrix(PropertyID name, Float4x4 v) => WriteUniform(name, v, UniformScalarType.Float4x4);
    /// <summary>Sets a double4x4 matrix uniform.</summary>
    public void SetDoubleMatrix(PropertyID name, Double4x4 v) => WriteUniform(name, v, UniformScalarType.Double4x4);


    /// <inheritdoc cref="SetBuffer(PropertyID, DeviceBufferRange, bool)"/>
    public void SetBuffer(PropertyID name, DeviceBuffer buffer, bool readOnly = true)
    {
        ValidationHelpers.RequireNotNull(buffer, nameof(buffer), nameof(SetBuffer));
        SetBuffer(name, new DeviceBufferRange(buffer, 0, buffer.SizeInBytes), readOnly);
    }

    /// <summary>
    /// Binds a buffer to the named slot. Covers whole-uniform-buffer and structured-buffer paths. If
    /// readOnly is false and the buffer's a uniform buffer, the binder sets its uniforms; if true (default),
    /// just binds it.
    /// </summary>
    public void SetBuffer(PropertyID name, DeviceBufferRange range, bool readOnly = true)
    {
        ValidationHelpers.RequireNotNull(range.Buffer, nameof(range), nameof(SetBuffer));
        GetOrCreate(name).SetBuffer(range, readOnly);
        unchecked { _resourceVersion++; _version++; }
    }


    /// <inheritdoc cref="SetTexture(PropertyID, TextureView, Sampler)"/>
    public void SetTexture(PropertyID name, Texture texture, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(texture, nameof(texture), nameof(SetTexture));
        GetOrCreate(name).SetTexture(texture, null, sampler);
        unchecked { _resourceVersion++; _version++; }
    }

    /// <summary>
    /// Binds a texture to the named slot with an optional sampler. Sampler goes to the matched sampler
    /// slot. Null sampler uses the default linear sampler.
    /// </summary>
    public void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(view, nameof(view), nameof(SetTexture));
        GetOrCreate(name).SetTexture(null, view, sampler);
        unchecked { _resourceVersion++; _version++; }
    }

    /// <summary>
    /// Binds a render texture's first color texture to the named slot with an optional sampler.
    /// </summary>
    public void SetTexture(PropertyID name, RenderTexture renderTexture, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(renderTexture, nameof(renderTexture), nameof(SetTexture));
        SetTexture(name, renderTexture.ColorTextures[0], sampler);
    }


    /// <summary>
    /// Binds a sampler to the named slot, independent of any texture.
    /// </summary>
    public void SetSampler(PropertyID name, Sampler sampler)
    {
        ValidationHelpers.RequireNotNull(sampler, nameof(sampler), nameof(SetSampler));
        GetOrCreate(name).SetSampler(sampler);
        unchecked { _resourceVersion++; _version++; }
    }


    /// <summary>
    /// Clears all entries and bumps the resource version.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        unchecked { _resourceVersion++; _version++; }
    }


    /// <summary>
    /// Merges another set into this one, overwriting matching entries.
    /// </summary>
    public void ApplyOther(PropertySet other)
    {
        bool dirtyResources = false;

        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in other.Entries)
        {
            PropertyEntry entry = kv.Value;
            bool isUniform = entry.Kind == PropertyEntryKind.Uniform;

            _entries[kv.Key] = entry;

            if (!isUniform) dirtyResources = true;
        }

        if (dirtyResources) unchecked { _resourceVersion++; }
        unchecked { _version++; }
    }


    private void WriteUniform<T>(PropertyID key, T value, UniformScalarType type) where T : unmanaged
    {
        GetOrCreate(key).WriteUniform(value, type);
        unchecked { _version++; }
    }


    private PropertyEntry GetOrCreate(PropertyID key)
    {
        if (!_entries.TryGetValue(key, out PropertyEntry? entry))
        {
            entry = new PropertyEntry();
            _entries[key] = entry;
        }
        return entry;
    }
}
