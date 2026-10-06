using System;

namespace Prowl.Graphite;

/// <summary>How a pass uses a declared texture. A declaration names exactly one state.</summary>
public enum TextureState : byte
{
    /// <summary>Read through a sampled or read-only texture binding.</summary>
    Sampled = 1 << 0,

    /// <summary>Read and written through a storage binding. Color only.</summary>
    Storage = 1 << 1,

    /// <summary>Rendered into as a framebuffer attachment. Output only.</summary>
    Attachment = 1 << 2,

    /// <summary>Source of a copy, resolve or blit.</summary>
    TransferSrc = 1 << 3,

    /// <summary>Destination of a copy, resolve or blit. Output only.</summary>
    TransferDst = 1 << 4,

    /// <summary>Depth attachment that is tested but not written, and can be sampled at the same time. Depth only.</summary>
    DepthReadOnly = 1 << 5,
}

/// <summary>How a pass uses a declared buffer. Combine flags when a pass uses a buffer several ways.</summary>
[Flags]
public enum BufferAccess : ushort
{
    /// <summary>No access.</summary>
    None = 0,

    /// <summary>Read through a structured buffer or storage binding.</summary>
    ShaderRead = 1 << 0,

    /// <summary>Written through a storage binding. Output only.</summary>
    ShaderWrite = 1 << 1,

    /// <summary>Read as a uniform buffer.</summary>
    Uniform = 1 << 2,

    /// <summary>Read as a vertex buffer.</summary>
    Vertex = 1 << 3,

    /// <summary>Read as an index buffer.</summary>
    Index = 1 << 4,

    /// <summary>Read as indirect draw or dispatch arguments.</summary>
    Indirect = 1 << 5,

    /// <summary>Source of a copy.</summary>
    TransferRead = 1 << 6,

    /// <summary>Destination of a copy. Output only.</summary>
    TransferWrite = 1 << 7,

    /// <summary>Every write kind.</summary>
    AllWrites = ShaderWrite | TransferWrite,

    /// <summary>Every read kind. Default for buffer inputs.</summary>
    AllReads = ShaderRead | Uniform | Vertex | Index | Indirect | TransferRead,
}

internal readonly struct TextureBarrier(Texture texture, TextureState? before, TextureState? after, bool fromUndefined = false)
{
    public readonly Texture Texture = texture;
    public readonly TextureState? Before = before;
    public readonly TextureState? After = after;
    public readonly bool FromUndefined = fromUndefined;
}
