using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace Prowl.Graphite;


internal enum PropertyEntryKind : byte
{
    Uniform,
    Buffer,
    Texture,
    Sampler,
}


/// <summary>
/// A single entry stored inside a PropertySet. Carries either a uniform scalar/vector/matrix
/// payload or a managed reference to a GPU resource.
/// </summary>
internal sealed class PropertyEntry
{
    public PropertyEntryKind Kind;
    public UniformScalarType UniformType;
    public bool ReadOnly;


    public unsafe struct UniformPayload
    {
        // 'oh but mah memory usa-' you're not updating a million uniform double4x4's, a few fixed bytes won't hurt you
        public fixed byte _e0[128];

        public ref T As<T>() where T : unmanaged
            => ref Unsafe.As<byte, T>(ref _e0[0]);
    }

    public UniformPayload Uniform;

    public DeviceBufferRange? Buffer;
    public Texture? Texture;
    public TextureView? TextureView;
    public Sampler? Sampler;


    public void WriteUniform<T>(T value, UniformScalarType type) where T : unmanaged
    {
        Kind = PropertyEntryKind.Uniform;
        UniformType = type;
        Uniform.As<T>() = value;
    }


    public void SetBuffer(DeviceBufferRange buffer, bool readOnly)
    {
        Kind = PropertyEntryKind.Buffer;
        ReadOnly = readOnly;
        Buffer = buffer;
        Texture = null;
        TextureView = null;
        Sampler = null;
    }


    public void SetTexture(Texture? texture, TextureView? view, Sampler? sampler)
    {
        Kind = PropertyEntryKind.Texture;
        Texture = texture;
        TextureView = view;
        Sampler = sampler;
        Buffer = null;
    }


    public void SetSampler(Sampler sampler)
    {
        Kind = PropertyEntryKind.Sampler;
        Sampler = sampler;
        Texture = null;
        TextureView = null;
        Buffer = null;
    }
}
