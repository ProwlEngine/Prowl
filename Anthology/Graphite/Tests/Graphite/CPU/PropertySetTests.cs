using System;
using System.Linq;
using Xunit;

namespace Prowl.Graphite.Tests;

// Covers the CPU-only surface of PropertySet: scalar uniform writes, entry de-duplication
// by name. Resource setters (buffer/texture/sampler)
// require a live GraphicsDevice and are exercised by the GPU resource tests instead.
public class PropertySetTests
{
    [Fact]
    public void SetScalar_SameName_DifferentType_StaysOneEntry()
    {
        PropertySet set = new();

        set.SetFloat("v", 1.0f);
        set.SetInt("v", 2);

        // The entry is rewritten in place with the new scalar type rather than duplicated.
        Assert.Equal(1, set.EntryCount);
    }

    [Fact]
    public void SetUniform_KeepsTheTypeAndBytesOfTypesWithoutASetter()
    {
        PropertySet set = new();
        byte[] value = BitConverter.GetBytes(uint.MaxValue).Concat(BitConverter.GetBytes(7u)).ToArray();
        UniformCapture capture = new();

        set.SetUniform("v", UniformScalarType.UInt2, value);
        set.Visit(capture);

        Assert.Equal(UniformScalarType.UInt2, capture.Type);
        Assert.Equal(value, capture.Bytes);
    }

    private sealed class UniformCapture : IPropertyVisitor
    {
        public UniformScalarType Type;
        public byte[] Bytes = [];

        public void Uniform(PropertyID name, UniformScalarType type, ReadOnlySpan<byte> value)
        {
            Type = type;
            Bytes = value[..8].ToArray();
        }

        public void Texture(PropertyID name, Texture? texture, TextureView? view, Sampler? sampler) { }

        public void Buffer(PropertyID name, DeviceBufferRange buffer) { }
    }
}
