using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// A persistent descriptor set written slot by slot and bound whole with <see cref="CommandBuffer.SetDescriptorTable"/>,
/// for bindless texture tables and other per frame data. Its layout must have <see cref="ResourceLayoutDescription.Table"/> set.
/// <para>
/// Every binding is partially bound and update after bind: slots never written may stay empty if shaders never read
/// them, and slots may be rewritten while the table is bound. A slot that work still in flight on the GPU reads must
/// not be rewritten until that work has retired. Textures are written in their resting layout, so graph transient
/// textures do not belong in a table.
/// </para>
/// </summary>
public abstract class DescriptorTable : GraphicsResource
{
    private readonly ResourceLayoutDescription _layout;
    private readonly Dictionary<PropertyID, int> _elementsByName = new();
    private readonly DescriptorTableEntry[][] _entries;

    /// <summary>The layout the table was created from.</summary>
    public ResourceLayoutDescription Layout => _layout;

    /// <summary>Bumps on every write.</summary>
    public uint Version { get; private set; }

    private protected DescriptorTable(in ResourceLayoutDescription layout)
    {
        _layout = layout with { Elements = (ResourceLayoutElementDescription[])layout.Elements.Clone() };
        _entries = new DescriptorTableEntry[_layout.Elements.Length][];
        for (int i = 0; i < _layout.Elements.Length; i++)
        {
            _elementsByName[_layout.Elements[i].Name] = i;
            _entries[i] = new DescriptorTableEntry[_layout.Elements[i].Count];
        }
    }

    /// <summary>Writes a sampled or storage texture slot, with its sampler for a combined image sampler element.</summary>
    public void SetTexture(PropertyID name, uint index, Texture texture, Sampler? sampler = null)
        => Write(name, index, new DescriptorTableEntry { Texture = texture, Sampler = sampler });

    /// <summary>Writes a sampled or storage texture slot from a view, with its sampler for a combined image sampler element.</summary>
    public void SetTexture(PropertyID name, uint index, TextureView view, Sampler? sampler = null)
        => Write(name, index, new DescriptorTableEntry { View = view, Sampler = sampler });

    /// <summary>Writes a sampler slot.</summary>
    public void SetSampler(PropertyID name, uint index, Sampler sampler)
        => Write(name, index, new DescriptorTableEntry { Sampler = sampler });

    /// <summary>Writes a storage buffer slot with a range of a buffer.</summary>
    public void SetBuffer(PropertyID name, uint index, DeviceBufferRange range)
        => Write(name, index, new DescriptorTableEntry { Buffer = range });

    /// <summary>Writes a storage buffer slot with a whole buffer.</summary>
    public void SetBuffer(PropertyID name, uint index, DeviceBuffer buffer)
        => SetBuffer(name, index, new DeviceBufferRange(buffer, 0, buffer.SizeInBytes));

    internal int ElementCount => _entries.Length;

    internal ReadOnlySpan<DescriptorTableEntry> EntriesOf(int element) => _entries[element];

    private void Write(PropertyID name, uint index, in DescriptorTableEntry entry)
    {
        if (!_elementsByName.TryGetValue(name, out int element))
            throw new RenderException($"Descriptor table has no element '{PropertyID.ToString(name)}'.");

        ResourceLayoutElementDescription description = _layout.Elements[element];
        if (index >= description.Count)
            throw new RenderException($"Slot {index} is past the {description.Count} slots of '{PropertyID.ToString(name)}'.");

        bool fits = description.Kind switch
        {
            ResourceKind.TextureReadOnly => (entry.Texture ?? entry.View?.Target) != null
                && ((description.Options & ResourceLayoutElementOptions.CombinedImageSampler) == 0 || entry.Sampler != null),
            ResourceKind.TextureReadWrite => (entry.Texture ?? entry.View?.Target) != null,
            ResourceKind.Sampler => entry.Sampler != null,
            ResourceKind.StructuredBufferReadOnly or ResourceKind.StructuredBufferReadWrite => entry.Buffer != null,
            _ => false,
        };

        if (!fits)
            throw new RenderException($"'{PropertyID.ToString(name)}' is a {description.Kind} element and cannot take this write.");

        WriteCore(element, description, index, entry);
        _entries[element][index] = entry;
        unchecked { Version++; }
    }

    private protected abstract void WriteCore(int element, in ResourceLayoutElementDescription description, uint index, in DescriptorTableEntry entry);
}

internal struct DescriptorTableEntry
{
    public Texture? Texture;
    public TextureView? View;
    public Sampler? Sampler;
    public DeviceBufferRange? Buffer;

    public readonly bool IsEmpty => Texture == null && View == null && Sampler == null && Buffer == null;
}
