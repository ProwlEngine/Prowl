using System;

namespace Prowl.Graphite.RenderGraph;

internal readonly struct ResourceAccess
{
    public readonly RenderResourceID Id;
    public readonly bool IsTexture;
    public readonly bool IsOutput;
    public readonly TextureState TextureUsage;
    public readonly TextureState? DepthUsage;
    public readonly BufferAccess BufferUsage;
    public readonly GraphResource? Description;

    private ResourceAccess(
        RenderResourceID id,
        bool isTexture,
        bool isOutput,
        TextureState textureUsage,
        TextureState? depthUsage,
        BufferAccess bufferUsage,
        GraphResource? description)
    {
        Id = id;
        IsTexture = isTexture;
        IsOutput = isOutput;
        TextureUsage = textureUsage;
        DepthUsage = depthUsage;
        BufferUsage = bufferUsage;
        Description = description;
    }

    public static ResourceAccess Texture(
        RenderResourceID id,
        TextureState usage,
        TextureState? depthUsage,
        bool isOutput,
        GraphResource? description = null)
    {
        string role = isOutput ? "output" : "input";
        if (!Enum.IsDefined(usage) || usage == TextureState.DepthReadOnly)
            throw new ArgumentException($"Texture usage {usage} is not valid for color, name exactly one state; DepthReadOnly belongs in depthUsage.", nameof(usage));
        if (isOutput && !IsWrite(usage))
            throw new ArgumentException($"Texture output usage {usage} must be Attachment, Storage or TransferDst.", nameof(usage));
        if (!isOutput && !IsRead(usage))
            throw new ArgumentException($"Texture usage {usage} writes, declare it with DeclareOutputTexture.", nameof(usage));

        if (depthUsage is TextureState depth)
        {
            bool validDepth = Enum.IsDefined(depth)
                && depth != TextureState.Storage
                && (isOutput || IsRead(depth));
            if (!validDepth)
                throw new ArgumentException($"Depth usage {depth} is not valid for a pass {role}.", nameof(depthUsage));
        }

        return new ResourceAccess(id, true, isOutput, usage, depthUsage, BufferAccess.None, description);
    }

    public static ResourceAccess Buffer(RenderResourceID id, BufferAccess usage, bool isOutput, GraphResource? description = null)
    {
        if (usage == BufferAccess.None)
            throw new ArgumentException("Buffer usage must name at least one access.", nameof(usage));
        if ((usage & ~(BufferAccess.AllReads | BufferAccess.AllWrites)) != 0)
            throw new ArgumentException($"Buffer usage {usage} contains undefined flags.", nameof(usage));
        if (!isOutput && (usage & BufferAccess.AllWrites) != 0)
            throw new ArgumentException($"Buffer usage {usage} writes, declare it with DeclareOutputBuffer.", nameof(usage));
        if (isOutput && (usage & BufferAccess.AllWrites) == 0)
            throw new ArgumentException($"Buffer output usage {usage} must include ShaderWrite or TransferWrite.", nameof(usage));

        return new ResourceAccess(id, false, isOutput, default, null, usage, description);
    }

    private static bool IsWrite(TextureState state)
        => state is TextureState.Storage or TextureState.Attachment or TextureState.TransferDst;

    private static bool IsRead(TextureState state)
        => state is TextureState.Sampled or TextureState.Storage or TextureState.TransferSrc or TextureState.DepthReadOnly;

    public TextureState? DepthState(TextureState colorState)
    {
        if (DepthUsage is TextureState depth)
            return depth;
        return colorState == TextureState.Storage ? null : colorState;
    }
}
