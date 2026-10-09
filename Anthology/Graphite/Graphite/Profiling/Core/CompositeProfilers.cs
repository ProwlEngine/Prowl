using System;
using System.Collections.Generic;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite;

internal sealed class CompositeGraphProfiler(IGraphProfiler[] sinks) : IGraphProfiler
{
    public void BeginExecution(ulong executionId, string graphName) { }

    public void EndExecution() { }

    public void BeginView(in ViewInfo view)
    {
        foreach (IGraphProfiler sink in sinks)
            sink.BeginView(in view);
    }

    public void EndView(in ViewInfo view)
    {
        foreach (IGraphProfiler sink in sinks)
            sink.EndView(in view);
    }

    public void BeginPass(in PassInfo pass)
    {
        foreach (IGraphProfiler sink in sinks)
            sink.BeginPass(in pass);
    }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        foreach (IGraphProfiler sink in sinks)
            sink.EndPass(in pass, in stats);
    }
}

internal sealed class CompositeGpuStatsProfiler(IGpuStatsProfiler[] sinks) : IGpuStatsProfiler
{
    public void BeginExecution(ulong executionId, string graphName) { }

    public void EndExecution() { }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds)
    {
        foreach (IGpuStatsProfiler sink in sinks)
            sink.RecordExecutionTime(in commandBuffer, milliseconds);
    }

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
    {
        foreach (IGpuStatsProfiler sink in sinks)
            sink.RecordGpuVertexStats(in commandBuffer, in stats);
    }
}

internal sealed class CompositeCaptureProfiler(ICaptureProfiler[] sinks) : ICaptureProfiler
{
    public void BeginExecution(ulong executionId, string graphName) { }

    public void EndExecution() { }

    public void DescribeView(in ViewCaptureInfo view)
    {
        foreach (ICaptureProfiler sink in sinks)
            sink.DescribeView(in view);
    }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        foreach (ICaptureProfiler sink in sinks)
            sink.OnPassEnd(in pass, references, capture);
    }
}

internal sealed class CompositeCommandStreamProfiler(ICommandStreamProfiler[] profilers) : ICommandStreamProfiler
{
    public void BeginExecution(ulong executionId, string graphName) { }

    public void EndExecution() { }

    public IPassCommandSink? BeginPassCommands(in PassInfo pass)
    {
        List<IPassCommandSink> sinks = new(profilers.Length);
        foreach (ICommandStreamProfiler profiler in profilers)
        {
            if (profiler.BeginPassCommands(in pass) is { } sink)
                sinks.Add(sink);
        }

        return sinks.Count switch
        {
            0 => null,
            1 => sinks[0],
            _ => new CompositePassCommandSink(sinks.ToArray()),
        };
    }
}

internal sealed class CompositePassCommandSink(IPassCommandSink[] sinks) : IPassCommandSink
{
    public void End()
    {
        foreach (IPassCommandSink sink in sinks)
            sink.End();
    }

    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetFramebuffer(in framebuffer, in ops);
    }

    public void ClearColorTarget(uint index, Color color)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.ClearColorTarget(index, color);
    }

    public void ClearDepthStencil(float depth, byte stencil)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.ClearDepthStencil(depth, stencil);
    }

    public void SetPipeline(in PipelineBindInfo pipeline)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetPipeline(in pipeline);
    }

    public void SetViewport(in Viewport viewport)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetViewport(in viewport);
    }

    public void SetScissor(uint x, uint y, uint width, uint height)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetScissor(x, y, width, height);
    }

    public void SetStencilReference(uint reference)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetStencilReference(reference);
    }

    public void SetBlendConstants(Color constants)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetBlendConstants(constants);
    }

    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.BindVertexBuffers(bindings);
    }

    public void BindIndexBuffer(in IndexBindingUse binding)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.BindIndexBuffer(in binding);
    }

    public void SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.SetProperties(properties);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.DrawIndirect(in buffer, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.DrawIndexedIndirect(in buffer, offset, drawCount, stride);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(in ResourceVersion buffer, uint offset)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.DispatchIndirect(in buffer, offset);
    }

    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.UpdateBuffer(in after, offset, data);
    }

    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.UpdateTexture(in after, in region, data);
    }

    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.CopyBuffer(in source, sourceOffset, in destinationAfter, destinationOffset, sizeInBytes);
    }

    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.CopyTexture(in source, in sourceRegion, in destinationAfter, in destinationRegion, layerCount);
    }

    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.CopyTextureToBuffer(in source, in region, in destinationAfter, destinationOffset);
    }

    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.ResolveTexture(in source, in destinationAfter);
    }

    public void GenerateMips(in ResourceVersion textureAfter)
    {
        foreach (IPassCommandSink sink in sinks)
            sink.GenerateMips(in textureAfter);
    }
}
