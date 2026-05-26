using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Veldrid;

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
        public fixed byte _e0[128];
    }

    public UniformPayload Uniform;

    public DeviceBuffer? Buffer;
    public uint BufferOffset;
    public uint BufferSize;

    public Texture? Texture;
    public TextureView? TextureView;
    public Sampler? Sampler;


    public void WriteUniform<T>(T value, UniformScalarType type) where T : unmanaged
    {
        Kind = PropertyEntryKind.Uniform;
        UniformType = type;
        MemoryMarshal.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<UniformPayload, byte>(ref Uniform), 128), value);
    }


    public void SetBuffer(DeviceBuffer buffer, uint offset, uint size, bool readOnly)
    {
        Kind = PropertyEntryKind.Buffer;
        ReadOnly = readOnly;
        Buffer = buffer;
        BufferOffset = offset;
        BufferSize = size;
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
