using System;

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

public readonly struct PipelineBindInfo
{
    public string ShaderName { get; }
    public bool IsCompute { get; }
    public ShaderStages Stages { get; }

    public PipelineBindInfo(string shaderName, bool isCompute, ShaderStages stages)
    {
        ShaderName = shaderName;
        IsCompute = isCompute;
        Stages = stages;
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
