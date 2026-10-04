using System;

namespace Prowl.Graphite;

internal enum TextureState : byte
{
    Resting,
    Undefined,
    Sampled,
    Storage,
    Attachment,
    TransferSrc,
    TransferDst,
    DepthReadOnly,
}

[Flags]
internal enum BufferAccess : ushort
{
    None = 0,
    ShaderRead = 1 << 0,
    ShaderWrite = 1 << 1,
    Uniform = 1 << 2,
    Vertex = 1 << 3,
    Index = 1 << 4,
    Indirect = 1 << 5,
    TransferRead = 1 << 6,
    TransferWrite = 1 << 7,

    AllWrites = ShaderWrite | TransferWrite,
    AllReads = ShaderRead | Uniform | Vertex | Index | Indirect | TransferRead,
}

internal readonly struct TextureBarrier(Texture texture, TextureState before, TextureState after)
{
    public readonly Texture Texture = texture;
    public readonly TextureState Before = before;
    public readonly TextureState After = after;
}
