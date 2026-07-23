using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

public readonly struct PassInfo
{
    public string Name { get; }
    public int Index { get; }
    public ReadOnlyMemory<RenderResourceID> Inputs { get; }
    public ReadOnlyMemory<RenderResourceID> Outputs { get; }

    public PassInfo(string name, int index, ReadOnlyMemory<RenderResourceID> inputs, ReadOnlyMemory<RenderResourceID> outputs)
    {
        Name = name;
        Index = index;
        Inputs = inputs;
        Outputs = outputs;
    }
}

public enum DrawKind { Draw, DrawIndexed, DrawIndirect, DrawIndexedIndirect }

public readonly struct DrawCallInfo
{
    public DrawKind Kind { get; }
    public uint VertexOrIndexCount { get; }
    public uint InstanceCount { get; }
    public uint DrawCount { get; }
    public bool IsIndirect { get; }

    public DrawCallInfo(DrawKind kind, uint vertexOrIndexCount, uint instanceCount, uint drawCount, bool isIndirect)
    {
        Kind = kind;
        VertexOrIndexCount = vertexOrIndexCount;
        InstanceCount = instanceCount;
        DrawCount = drawCount;
        IsIndirect = isIndirect;
    }
}

public readonly struct DispatchCallInfo
{
    public uint GroupCountX { get; }
    public uint GroupCountY { get; }
    public uint GroupCountZ { get; }
    public bool IsIndirect { get; }

    public DispatchCallInfo(uint groupCountX, uint groupCountY, uint groupCountZ, bool isIndirect)
    {
        GroupCountX = groupCountX;
        GroupCountY = groupCountY;
        GroupCountZ = groupCountZ;
        IsIndirect = isIndirect;
    }
}

/// <summary>
/// One buffer binding observed at draw time: which buffer, which byte range of it (a whole
/// DeviceBuffer isn't always one logical resource - e.g. a transient ring buffer serves many
/// independent sub-allocations across a frame, one per draw), and the buffer's ContentVersion as of
/// this draw, so a consumer can tell whether two draws sharing a buffer actually saw the same bytes.
/// </summary>
public readonly struct BufferBindingInfo
{
    public string Name { get; }
    public DeviceBuffer Buffer { get; }
    public uint Offset { get; }
    public uint SizeInBytes { get; }
    public uint ContentVersion { get; }
    public bool ReadOnly { get; }

    public BufferBindingInfo(string name, DeviceBuffer buffer, uint offset, uint sizeInBytes, uint contentVersion, bool readOnly)
    {
        Name = name;
        Buffer = buffer;
        Offset = offset;
        SizeInBytes = sizeInBytes;
        ContentVersion = contentVersion;
        ReadOnly = readOnly;
    }
}

/// <summary>
/// Buffers bound for the draw call that just recorded: resolved vertex/index buffers plus any
/// buffer-kind entries in the active PropertySet at draw time. Only reported when the profiler
/// requests a capture (IProfiler.RequestCapture), since resolving this is not free.
/// </summary>
public readonly struct DrawBufferInfo
{
    public IReadOnlyList<BufferBindingInfo> VertexBuffers { get; }
    public BufferBindingInfo? IndexBuffer { get; }
    public IReadOnlyList<BufferBindingInfo> BoundBuffers { get; }

    public DrawBufferInfo(IReadOnlyList<BufferBindingInfo> vertexBuffers, BufferBindingInfo? indexBuffer, IReadOnlyList<BufferBindingInfo> boundBuffers)
    {
        VertexBuffers = vertexBuffers;
        IndexBuffer = indexBuffer;
        BoundBuffers = boundBuffers;
    }
}

public readonly struct PipelineBindInfo
{
    public string ShaderName { get; }
    public bool IsCompute { get; }
    public ShaderStages Stages { get; }

    /// <summary>The bound GraphicsProgram or ComputeProgram instance. Typed as object since IProfiler
    /// doesn't reference either type; a consumer that wants full pipeline state casts this itself.</summary>
    public object Program { get; }

    public PipelineBindInfo(string shaderName, bool isCompute, ShaderStages stages, object program)
    {
        ShaderName = shaderName;
        IsCompute = isCompute;
        Stages = stages;
        Program = program;
    }
}

/// <summary>
/// Identity of the CommandBuffer that issued a profiler event, captured by value at record time - the
/// underlying CommandBuffer object is pooled/reused, so consumers reading this later (e.g. after a
/// deferred GPU-timing resolve) can't rely on the live object still representing the same rental.
/// </summary>
public readonly struct CommandBufferInfo
{
    /// <summary>Monotonic id stamped fresh on every rental. Unique per rental, not per pooled object.</summary>
    public ulong Id { get; }
    public string Name { get; }
    public PassInfo? Pass { get; }

    public CommandBufferInfo(ulong id, string name, PassInfo? pass)
    {
        Id = id;
        Name = name;
        Pass = pass;
    }
}

public enum BarrierBin { TextureTransition, BufferTransition, MemoryBarrier }

public enum SubmitKind { Graphics, Transfer }

public readonly struct ProfilerSubmitInfo
{
    public SubmitKind Kind { get; }
    public string Name { get; }
    public uint CommandBufferCount { get; }

    public ProfilerSubmitInfo(SubmitKind kind, string name, uint commandBufferCount)
    {
        Kind = kind;
        Name = name;
        CommandBufferCount = commandBufferCount;
    }
}
