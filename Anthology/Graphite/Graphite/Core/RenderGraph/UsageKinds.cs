using System;

namespace Prowl.Graphite.RenderGraph;

/// <summary>How a pass uses a declared texture. Combine flags when a pass switches kinds with RenderContext.Transition.</summary>
[Flags]
public enum TextureUsageKind
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
public enum BufferUsageKind
{
    /// <summary>Read through a read-only structured buffer binding.</summary>
    ShaderRead = 1 << 0,

    /// <summary>Read as a uniform buffer.</summary>
    Uniform = 1 << 1,

    /// <summary>Read as a vertex buffer.</summary>
    Vertex = 1 << 2,

    /// <summary>Read as an index buffer.</summary>
    Index = 1 << 3,

    /// <summary>Read as indirect draw or dispatch arguments.</summary>
    Indirect = 1 << 4,

    /// <summary>Source of a copy.</summary>
    TransferSrc = 1 << 5,

    /// <summary>Read and written through a storage binding. Output only.</summary>
    Storage = 1 << 6,

    /// <summary>Destination of a copy. Output only.</summary>
    TransferDst = 1 << 7,

    /// <summary>Every read kind. Default for buffer inputs.</summary>
    AnyRead = ShaderRead | Uniform | Vertex | Index | Indirect | TransferSrc,
}

internal readonly struct ResourceAccess
{
    private const TextureUsageKind ColorKinds = TextureUsageKind.Sampled | TextureUsageKind.Storage
        | TextureUsageKind.Attachment | TextureUsageKind.TransferSrc | TextureUsageKind.TransferDst;
    private const TextureUsageKind TextureWrites = TextureUsageKind.Storage | TextureUsageKind.Attachment | TextureUsageKind.TransferDst;
    private const TextureUsageKind TextureReads = TextureUsageKind.Sampled | TextureUsageKind.Storage
        | TextureUsageKind.TransferSrc | TextureUsageKind.DepthReadOnly;

    public readonly RenderResourceID Id;
    public readonly bool IsTexture;
    public readonly bool IsOutput;
    public readonly TextureUsageKind TextureUsage;
    public readonly TextureUsageKind TextureInitial;
    public readonly TextureUsageKind? DepthUsage;
    public readonly BufferUsageKind BufferUsage;

    private ResourceAccess(
        RenderResourceID id,
        bool isTexture,
        bool isOutput,
        TextureUsageKind textureUsage,
        TextureUsageKind textureInitial,
        TextureUsageKind? depthUsage,
        BufferUsageKind bufferUsage)
    {
        Id = id;
        IsTexture = isTexture;
        IsOutput = isOutput;
        TextureUsage = textureUsage;
        TextureInitial = textureInitial;
        DepthUsage = depthUsage;
        BufferUsage = bufferUsage;
    }

    public static ResourceAccess Texture(
        RenderResourceID id,
        TextureUsageKind usage,
        TextureUsageKind? initial,
        TextureUsageKind? depthUsage,
        bool isOutput)
    {
        string role = isOutput ? "output" : "input";
        if (usage == 0 || (usage & ~ColorKinds) != 0)
            throw new ArgumentException($"Texture usage {usage} is not valid for color; DepthReadOnly belongs in depthUsage.", nameof(usage));
        if (isOutput && (usage & TextureWrites) == 0)
            throw new ArgumentException($"Texture output usage {usage} must include Attachment, Storage or TransferDst.", nameof(usage));
        if (!isOutput && (usage & ~TextureReads) != 0)
            throw new ArgumentException($"Texture usage {usage} writes, declare it with DeclareOutputTexture.", nameof(usage));

        TextureUsageKind start;
        if (initial is TextureUsageKind explicitInitial)
        {
            if (!IsSingleKind(explicitInitial) || (usage & explicitInitial) == 0)
                throw new ArgumentException($"Initial kind {explicitInitial} must be one of the declared kinds {usage}.", nameof(initial));
            start = explicitInitial;
        }
        else if (IsSingleKind(usage))
        {
            start = usage;
        }
        else
        {
            throw new ArgumentException($"Texture {role} declares several kinds ({usage}); name the start state with initial.", nameof(initial));
        }

        if (depthUsage is TextureUsageKind depth)
        {
            bool validDepth = IsSingleKind(depth)
                && depth is not TextureUsageKind.Storage
                && (isOutput || (depth & ~TextureReads) == 0);
            if (!validDepth)
                throw new ArgumentException($"Depth usage {depth} is not valid for a pass {role}.", nameof(depthUsage));
        }

        return new ResourceAccess(id, true, isOutput, usage, start, depthUsage, 0);
    }

    public static ResourceAccess Buffer(RenderResourceID id, BufferUsageKind usage, bool isOutput)
    {
        const BufferUsageKind writes = BufferUsageKind.Storage | BufferUsageKind.TransferDst;
        if (usage == 0)
            throw new ArgumentException("Buffer usage must name at least one kind.", nameof(usage));
        if (!isOutput && (usage & writes) != 0)
            throw new ArgumentException($"Buffer usage {usage} writes, declare it with DeclareOutputBuffer.", nameof(usage));
        if (isOutput && (usage & writes) == 0)
            throw new ArgumentException($"Buffer output usage {usage} must include Storage or TransferDst.", nameof(usage));

        return new ResourceAccess(id, false, isOutput, 0, 0, null, usage);
    }

    private static bool IsSingleKind(TextureUsageKind kind) => kind != 0 && (kind & (kind - 1)) == 0;

    public static TextureState ToState(TextureUsageKind kind) => kind switch
    {
        TextureUsageKind.Sampled => Graphite.TextureState.Sampled,
        TextureUsageKind.Storage => Graphite.TextureState.Storage,
        TextureUsageKind.Attachment => Graphite.TextureState.Attachment,
        TextureUsageKind.TransferSrc => Graphite.TextureState.TransferSrc,
        TextureUsageKind.TransferDst => Graphite.TextureState.TransferDst,
        _ => Graphite.TextureState.DepthReadOnly,
    };

    public TextureState? DepthState(TextureUsageKind colorKind)
    {
        if (DepthUsage is TextureUsageKind depth)
            return ToState(depth);
        return colorKind == TextureUsageKind.Storage ? null : ToState(colorKind);
    }

    public BufferAccess BufferAccess
    {
        get
        {
            BufferAccess access = Graphite.BufferAccess.None;
            if ((BufferUsage & BufferUsageKind.ShaderRead) != 0) access |= Graphite.BufferAccess.ShaderRead;
            if ((BufferUsage & BufferUsageKind.Uniform) != 0) access |= Graphite.BufferAccess.Uniform;
            if ((BufferUsage & BufferUsageKind.Vertex) != 0) access |= Graphite.BufferAccess.Vertex;
            if ((BufferUsage & BufferUsageKind.Index) != 0) access |= Graphite.BufferAccess.Index;
            if ((BufferUsage & BufferUsageKind.Indirect) != 0) access |= Graphite.BufferAccess.Indirect;
            if ((BufferUsage & BufferUsageKind.TransferSrc) != 0) access |= Graphite.BufferAccess.TransferRead;
            if ((BufferUsage & BufferUsageKind.Storage) != 0) access |= Graphite.BufferAccess.ShaderRead | Graphite.BufferAccess.ShaderWrite;
            if ((BufferUsage & BufferUsageKind.TransferDst) != 0) access |= Graphite.BufferAccess.TransferWrite;
            return access;
        }
    }
}
