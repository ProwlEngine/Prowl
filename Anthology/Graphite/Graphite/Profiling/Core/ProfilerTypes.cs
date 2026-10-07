using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

public readonly struct ViewInfo
{
    public string Name { get; }
    public int Index { get; }
    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public ulong ExecutionId { get; }

    public ViewInfo(string name, int index, uint pixelWidth, uint pixelHeight, ulong executionId)
    {
        ExecutionId = executionId;
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
    public int ViewIndex { get; }
    public ulong ExecutionId { get; }
    public ReadOnlyMemory<RenderResourceID> Inputs { get; }
    public ReadOnlyMemory<RenderResourceID> Outputs { get; }

    public PassInfo(
        string name, int index, int viewIndex, ulong executionId,
        ReadOnlyMemory<RenderResourceID> inputs, ReadOnlyMemory<RenderResourceID> outputs)
    {
        Name = name;
        Index = index;
        ViewIndex = viewIndex;
        ExecutionId = executionId;
        Inputs = inputs;
        Outputs = outputs;
    }
}

/// <summary>Work one pass command buffer recorded. Draws counts direct draw calls, IndirectDraws indirect ones.</summary>
public readonly record struct PassStats(
    uint Draws,
    uint IndirectDraws,
    uint Dispatches,
    uint ShaderSwitches,
    uint PipelineBinds,
    uint ResourceSetBinds,
    uint Barriers);

public readonly struct PipelineBindInfo
{
    /// <summary>Shader program that owns the pipeline.</summary>
    public ShaderProgram Program { get; }

    /// <summary>Pipeline id, unique within the device.</summary>
    public ulong PipelineId { get; }

    public bool IsCompute { get; }

    /// <summary>Framebuffer output description of the variant. Null for compute.</summary>
    public OutputDescription? Outputs { get; }

    /// <summary>Primitive topology of the variant. Null for compute.</summary>
    public PrimitiveTopology? Topology { get; }

    public PipelineBindInfo(ShaderProgram program, ulong pipelineId, bool isCompute, OutputDescription? outputs, PrimitiveTopology? topology)
    {
        Program = program;
        PipelineId = pipelineId;
        IsCompute = isCompute;
        Outputs = outputs;
        Topology = topology;
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
    public ulong ExecutionId { get; }
    public PassInfo? Pass { get; }

    public CommandBufferInfo(ulong id, string name, ulong executionId, PassInfo? pass)
    {
        Id = id;
        Name = name;
        ExecutionId = executionId;
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
