using System;

namespace Prowl.Graphite;

internal readonly struct ResourceAccess
{
    public readonly RenderResourceID Id;
    public readonly int Resource;
    public readonly int Version;
    public readonly TargetLoadStoreOps? FirstWrite;
    public readonly bool IsTexture;
    public readonly bool IsOutput;
    public readonly TextureState TextureUsage;
    public readonly TextureState? DepthUsage;
    public readonly BufferAccess BufferUsage;

    private ResourceAccess(
        RenderResourceID id,
        int resource,
        int version,
        TargetLoadStoreOps? firstWrite,
        bool isTexture,
        bool isOutput,
        TextureState textureUsage,
        TextureState? depthUsage,
        BufferAccess bufferUsage)
    {
        Id = id;
        Resource = resource;
        Version = version;
        FirstWrite = firstWrite;
        IsTexture = isTexture;
        IsOutput = isOutput;
        TextureUsage = textureUsage;
        DepthUsage = depthUsage;
        BufferUsage = bufferUsage;
    }

    public static ResourceAccess Texture(
        RenderResourceID id,
        int resource,
        int version,
        TextureState usage,
        TextureState? depthUsage,
        bool isOutput,
        TargetLoadStoreOps? firstWrite = null)
    {
        string role = isOutput ? "output" : "input";
        if (usage == 0 || (usage & ~ColorStates) != 0)
            throw new ArgumentException($"Texture usage {usage} is not valid for color; DepthReadOnly belongs in depthUsage.", nameof(usage));
        if (isOutput && !IsSingle(usage & WriteStates))
            throw new ArgumentException($"Texture output usage {usage} must include exactly one of Attachment, Storage or TransferDst.", nameof(usage));
        if (!isOutput && (usage & ~ReadStates) != 0)
            throw new ArgumentException($"Texture usage {usage} writes, declare it with Write.", nameof(usage));

        if (depthUsage is TextureState depth)
        {
            bool validDepth = Enum.IsDefined(depth)
                && depth != TextureState.Storage
                && (isOutput || IsRead(depth));
            if (!validDepth)
                throw new ArgumentException($"Depth usage {depth} is not valid for a pass {role}.", nameof(depthUsage));
        }

        return new ResourceAccess(id, resource, version, firstWrite, true, isOutput, usage, depthUsage, BufferAccess.None);
    }

    public static ResourceAccess Buffer(RenderResourceID id, int resource, int version, BufferAccess usage, bool isOutput)
    {
        if (usage == BufferAccess.None)
            throw new ArgumentException("Buffer usage must name at least one access.", nameof(usage));
        if ((usage & ~(BufferAccess.AllReads | BufferAccess.AllWrites)) != 0)
            throw new ArgumentException($"Buffer usage {usage} contains undefined flags.", nameof(usage));
        if (!isOutput && (usage & BufferAccess.AllWrites) != 0)
            throw new ArgumentException($"Buffer usage {usage} writes, declare it with Write.", nameof(usage));
        if (isOutput && (usage & BufferAccess.AllWrites) == 0)
            throw new ArgumentException($"Buffer output usage {usage} must include ShaderWrite or TransferWrite.", nameof(usage));

        return new ResourceAccess(id, resource, version, null, false, isOutput, default, null, usage);
    }

    private const TextureState WriteStates = TextureState.Storage | TextureState.Attachment | TextureState.TransferDst;
    private const TextureState ReadStates = TextureState.Sampled | TextureState.Storage | TextureState.TransferSrc;
    private const TextureState ColorStates = WriteStates | ReadStates;

    private static bool IsSingle(TextureState state) => state != 0 && (state & (state - 1)) == 0;

    private static bool IsRead(TextureState state)
        => state is TextureState.Sampled or TextureState.Storage or TextureState.TransferSrc or TextureState.DepthReadOnly;

    public TextureState Entry => IsOutput ? TextureUsage & WriteStates : (TextureState)((int)TextureUsage & -(int)TextureUsage);

    public TextureState? DepthState(TextureState colorState)
    {
        if (DepthUsage is TextureState depth)
            return depth;
        return colorState == TextureState.Storage ? null : colorState;
    }

    public TextureState DepthAllowed => DepthUsage ?? (TextureUsage & ~TextureState.Storage);
}
