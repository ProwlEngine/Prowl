using System;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>Receives the values of a <see cref="PropertySet"/>, for hosts that mirror one elsewhere.</summary>
public interface IPropertyVisitor
{
    /// <summary>A uniform, its bytes laid out as its type's components in order.</summary>
    void Uniform(PropertyID name, UniformScalarType type, ReadOnlySpan<byte> value);

    void Texture(PropertyID name, Texture? texture, TextureView? view, Sampler? sampler);

    void Buffer(PropertyID name, DeviceBufferRange buffer);
}

public sealed partial class PropertySet
{
    /// <summary>Hands every uniform, texture and buffer the set holds to the visitor.</summary>
    public unsafe void Visit(IPropertyVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach ((PropertyID name, PropertyEntry entry) in _entries)
        {
            switch (entry.Kind)
            {
                case PropertyEntryKind.Uniform:
                    fixed (byte* bytes = entry.Uniform._e0)
                        visitor.Uniform(name, entry.UniformType, new ReadOnlySpan<byte>(bytes, 64));
                    break;
                case PropertyEntryKind.Texture:
                    visitor.Texture(name, entry.Texture, entry.TextureView, entry.Sampler);
                    break;
                case PropertyEntryKind.Buffer when entry.Buffer is { } buffer:
                    visitor.Buffer(name, buffer);
                    break;
            }
        }
    }
}
