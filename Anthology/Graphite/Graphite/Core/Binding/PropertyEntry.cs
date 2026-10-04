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


internal sealed class PropertyEntry
{
    public PropertyEntryKind Kind;
    public UniformScalarType UniformType;
    public bool ReadOnly;
    public bool BackedBlock;


    public unsafe struct UniformPayload
    {
        // inline 64 bytes (a float4x4) to dodge a heap alloc per entry. you aren't binding a million of these.
        public fixed byte _e0[64];

        public ref T As<T>() where T : unmanaged
            => ref Unsafe.As<byte, T>(ref _e0[0]);
    }

    public UniformPayload Uniform;

    /// <summary>Bumped on every write; lets binders skip repacking or re-resolving unchanged entries.</summary>
    public uint Version;

    public DeviceBufferRange? Buffer;
    public Texture? Texture;
    public TextureView? TextureView;
    public Sampler? Sampler;


    public void WriteUniform<T>(T value, UniformScalarType type) where T : unmanaged
    {
        Kind = PropertyEntryKind.Uniform;
        UniformType = type;
        Uniform.As<T>() = value;
        unchecked { Version++; }
    }


    public void SetBuffer(DeviceBufferRange buffer, bool readOnly, bool backedBlock = false)
    {
        Kind = PropertyEntryKind.Buffer;
        ReadOnly = readOnly;
        BackedBlock = backedBlock;
        Buffer = buffer;
        Texture = null;
        TextureView = null;
        Sampler = null;
        unchecked { Version++; }
    }


    public void SetTexture(Texture? texture, TextureView? view, Sampler? sampler)
    {
        Kind = PropertyEntryKind.Texture;
        Texture = texture;
        TextureView = view;
        Sampler = sampler;
        Buffer = null;
        unchecked { Version++; }
    }


    public void SetSampler(Sampler sampler)
    {
        Kind = PropertyEntryKind.Sampler;
        Sampler = sampler;
        Texture = null;
        TextureView = null;
        Buffer = null;
        unchecked { Version++; }
    }
}
