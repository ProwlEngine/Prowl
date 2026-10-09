using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>Whether a graph resource is a texture or a buffer.</summary>
public enum GraphResourceKind : byte
{
    Texture,
    Buffer,
}

/// <summary>Public copy of a pass's declared access to a graph resource.</summary>
public readonly record struct PassResourceAccess(
    RenderResourceID Id,
    GraphResourceKind Kind,
    bool IsOutput,
    TextureState TextureUsage,
    TextureState? DepthUsage,
    BufferAccess BufferUsage);

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
    public int ViewIndex { get; }

    /// <summary>Every resource access the pass declared, inputs and outputs.</summary>
    public ReadOnlyMemory<PassResourceAccess> Accesses { get; }

    public PassInfo(string name, int index, int viewIndex, ReadOnlyMemory<PassResourceAccess> accesses)
    {
        Name = name;
        Index = index;
        ViewIndex = viewIndex;
        Accesses = accesses;
    }

    /// <summary>The declared accesses that are not outputs.</summary>
    public IEnumerable<PassResourceAccess> GetInputs() => Filter(Accesses, output: false);

    /// <summary>The declared accesses that are outputs.</summary>
    public IEnumerable<PassResourceAccess> GetOutputs() => Filter(Accesses, output: true);

    private static IEnumerable<PassResourceAccess> Filter(ReadOnlyMemory<PassResourceAccess> accesses, bool output)
    {
        for (int i = 0; i < accesses.Length; i++)
        {
            PassResourceAccess access = accesses.Span[i];
            if (access.IsOutput == output)
                yield return access;
        }
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
