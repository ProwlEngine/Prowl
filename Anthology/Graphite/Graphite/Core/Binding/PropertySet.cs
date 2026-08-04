using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>
/// Named shader resources/uniforms for a draw. Last applied wins.
/// <para>
/// Not thread-safe.
/// </para>
/// </summary>
public sealed partial class PropertySet
{
    private readonly Dictionary<PropertyID, PropertyEntry> _entries;

    private uint _resourceVersion;
    private uint _version;


    /// <summary>
    /// Empty, 0 capacity.
    /// </summary>
    public PropertySet() : this(0)
    {
    }


    /// <summary>Empty, with given entry capacity.</summary>
    /// <param name="initialEntryCapacity">Initial dict capacity.</param>
    public PropertySet(int initialEntryCapacity)
    {
        _entries = new(initialEntryCapacity);
    }


    /// <summary>
    /// Bumps on resource setter calls. Uniform writes don't bump it.
    /// </summary>
    public uint ResourceVersion => _resourceVersion;


    internal uint Version => _version;


    /// <summary>Entry count.</summary>
    public int EntryCount => _entries.Count;


    internal Dictionary<PropertyID, PropertyEntry> Entries => _entries;


    /// <summary>Sets float uniform.</summary>
    public void SetFloat(PropertyID name, float v) => WriteUniform(name, v, UniformScalarType.Float1);
    /// <summary>Sets float2 uniform.</summary>
    public void SetFloat2(PropertyID name, Float2 v) => WriteUniform(name, v, UniformScalarType.Float2);
    /// <summary>Sets float3 uniform.</summary>
    public void SetFloat3(PropertyID name, Float3 v) => WriteUniform(name, v, UniformScalarType.Float3);
    /// <summary>Sets float4 uniform.</summary>
    public void SetFloat4(PropertyID name, Float4 v) => WriteUniform(name, v, UniformScalarType.Float4);

    /// <summary>Sets int uniform.</summary>
    public void SetInt(PropertyID name, int v) => WriteUniform(name, v, UniformScalarType.Int1);
    /// <summary>Sets int2 uniform.</summary>
    public void SetInt2(PropertyID name, Int2 v) => WriteUniform(name, v, UniformScalarType.Int2);
    /// <summary>Sets int3 uniform.</summary>
    public void SetInt3(PropertyID name, Int3 v) => WriteUniform(name, v, UniformScalarType.Int3);
    /// <summary>Sets int4 uniform.</summary>
    public void SetInt4(PropertyID name, Int4 v) => WriteUniform(name, v, UniformScalarType.Int4);

    /// <summary>Sets double uniform.</summary>
    public void SetDouble(PropertyID name, double v) => WriteUniform(name, v, UniformScalarType.Double1);
    /// <summary>Sets double2 uniform.</summary>
    public void SetDouble2(PropertyID name, Double2 v) => WriteUniform(name, v, UniformScalarType.Double2);
    /// <summary>Sets double3 uniform.</summary>
    public void SetDouble3(PropertyID name, Double3 v) => WriteUniform(name, v, UniformScalarType.Double3);
    /// <summary>Sets double4 uniform.</summary>
    public void SetDouble4(PropertyID name, Double4 v) => WriteUniform(name, v, UniformScalarType.Double4);

    /// <summary>Sets float4x4 matrix uniform.</summary>
    public void SetMatrix(PropertyID name, Float4x4 v) => WriteUniform(name, v, UniformScalarType.Float4x4);
    /// <summary>Sets double4x4 matrix uniform.</summary>
    public void SetDoubleMatrix(PropertyID name, Double4x4 v) => WriteUniform(name, v, UniformScalarType.Double4x4);


    /// <inheritdoc cref="SetBuffer(PropertyID, DeviceBufferRange, bool)"/>
    public void SetBuffer(PropertyID name, DeviceBuffer buffer, bool readOnly = true)
    {
        ValidationHelpers.RequireNotNull(buffer, nameof(buffer), nameof(SetBuffer));
        SetBuffer(name, new DeviceBufferRange(buffer, 0, buffer.SizeInBytes), readOnly);
    }

    /// <summary>
    /// Binds buffer to slot. readOnly=false on a uniform buffer sets its uniforms; true just binds it.
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
    /// Binds texture to slot with optional sampler. Null sampler = default linear.
    /// </summary>
    public void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(view, nameof(view), nameof(SetTexture));
        GetOrCreate(name).SetTexture(null, view, sampler);
        unchecked { _resourceVersion++; _version++; }
    }

    /// <summary>
    /// Binds render texture's first color texture to slot, optional sampler.
    /// </summary>
    public void SetTexture(PropertyID name, RenderTexture renderTexture, Sampler? sampler = null)
    {
        ValidationHelpers.RequireNotNull(renderTexture, nameof(renderTexture), nameof(SetTexture));
        SetTexture(name, renderTexture.ColorTextures[0], sampler);
    }


    /// <summary>
    /// Binds sampler to slot, independent of texture.
    /// </summary>
    public void SetSampler(PropertyID name, Sampler sampler)
    {
        ValidationHelpers.RequireNotNull(sampler, nameof(sampler), nameof(SetSampler));
        GetOrCreate(name).SetSampler(sampler);
        unchecked { _resourceVersion++; _version++; }
    }


    /// <summary>
    /// Clears everything, bumps resource version.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        unchecked { _resourceVersion++; _version++; }
    }


    /// <summary>
    /// Merges other set in, overwrites matches.
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
