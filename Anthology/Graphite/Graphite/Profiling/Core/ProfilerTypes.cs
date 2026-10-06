using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

public readonly struct ViewInfo
{
    public string Name { get; }
    public int Index { get; }
    public uint PixelWidth { get; }
    public uint PixelHeight { get; }

    public ViewInfo(string name, int index, uint pixelWidth, uint pixelHeight)
    {
        Name = name;
        Index = index;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }
}

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

    /// <summary>Topology at draw time. Needed to turn VertexOrIndexCount into a primitive count.</summary>
    public PrimitiveTopology Topology { get; }

    public DrawCallInfo(DrawKind kind, uint vertexOrIndexCount, uint instanceCount, uint drawCount, bool isIndirect, PrimitiveTopology topology)
    {
        Kind = kind;
        VertexOrIndexCount = vertexOrIndexCount;
        InstanceCount = instanceCount;
        DrawCount = drawCount;
        IsIndirect = isIndirect;
        Topology = topology;
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

    /// <summary>Bound GraphicsProgram or ComputeProgram.</summary>
    public ShaderProgram Program { get; }

    public PipelineBindInfo(string shaderName, bool isCompute, ShaderStages stages, ShaderProgram program)
    {
        ShaderName = shaderName;
        IsCompute = isCompute;
        Stages = stages;
        Program = program;
    }
}

/// <summary>
/// Identity of the CommandBuffer that issued a profiler event, captured by value - the underlying object is pooled/reused so don't trust the live object later.
/// </summary>
public readonly struct CommandBufferInfo
{
    /// <summary>Fresh id per rental, not per pooled object.</summary>
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

/// <summary>
/// GPU-reported vertex, primitive and fragment counts from a pipeline-statistics query.
/// Hardware numbers including indirect draws; fragment invocations over target pixels estimates overdraw.
/// </summary>
public readonly struct GpuVertexStats
{
    public ulong InputAssemblyVertices { get; }
    public ulong InputAssemblyPrimitives { get; }
    public ulong ClippingInvocations { get; }
    public ulong ClippingPrimitives { get; }
    public ulong FragmentShaderInvocations { get; }

    public GpuVertexStats(ulong inputAssemblyVertices, ulong inputAssemblyPrimitives, ulong clippingInvocations, ulong clippingPrimitives, ulong fragmentShaderInvocations)
    {
        InputAssemblyVertices = inputAssemblyVertices;
        InputAssemblyPrimitives = inputAssemblyPrimitives;
        ClippingInvocations = clippingInvocations;
        ClippingPrimitives = clippingPrimitives;
        FragmentShaderInvocations = fragmentShaderInvocations;
    }
}
