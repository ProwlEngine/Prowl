using System;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite;

internal sealed class CompositeGraphProfiler(IGraphProfiler[] sinks) : IGraphProfiler
{
    public void BeginExecution(ulong executionId) { }

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

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
        foreach (IGraphProfiler sink in sinks)
            sink.RecordPassRead(in pass, resource, texture, buffer);
    }

    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
        foreach (IGraphProfiler sink in sinks)
            sink.RecordPassWrite(in pass, resource, texture, buffer);
    }
}

internal sealed class CompositeGpuStatsProfiler(IGpuStatsProfiler[] sinks) : IGpuStatsProfiler
{
    public void BeginExecution(ulong executionId) { }

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
    public void BeginExecution(ulong executionId) { }

    public void EndExecution() { }

    public void OnViewBegin(in ViewCaptureInfo view)
    {
        foreach (ICaptureProfiler sink in sinks)
            sink.OnViewBegin(in view);
    }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        foreach (ICaptureProfiler sink in sinks)
            sink.OnPassEnd(in pass, references, capture);
    }

    public void OnViewEnd()
    {
        foreach (ICaptureProfiler sink in sinks)
            sink.OnViewEnd();
    }
}

internal sealed class CompositeCommandStreamProfiler(ICommandStreamProfiler[] sinks) : ICommandStreamProfiler
{
    public void BeginExecution(ulong executionId) { }

    public void EndExecution() { }

    public void BeginPassCommands(in PassInfo pass)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.BeginPassCommands(in pass);
    }

    public void EndPassCommands(in PassInfo pass)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.EndPassCommands(in pass);
    }

    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetFramebuffer(in framebuffer, in ops);
    }

    public void ClearColorTarget(uint index, Color color)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.ClearColorTarget(index, color);
    }

    public void ClearDepthStencil(float depth, byte stencil)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.ClearDepthStencil(depth, stencil);
    }

    public void SetPipeline(in PipelineBindInfo pipeline)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetPipeline(in pipeline);
    }

    public void SetViewport(in Viewport viewport)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetViewport(in viewport);
    }

    public void SetScissor(uint x, uint y, uint width, uint height)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetScissor(x, y, width, height);
    }

    public void SetStencilReference(uint reference)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetStencilReference(reference);
    }

    public void SetBlendConstants(Color constants)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetBlendConstants(constants);
    }

    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.BindVertexBuffers(bindings);
    }

    public void BindIndexBuffer(in IndexBindingUse binding)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.BindIndexBuffer(in binding);
    }

    public void SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.SetProperties(properties);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.DrawIndirect(in buffer, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.DrawIndexedIndirect(in buffer, offset, drawCount, stride);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(in ResourceVersion buffer, uint offset)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.DispatchIndirect(in buffer, offset);
    }

    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.UpdateBuffer(in after, offset, data);
    }

    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.UpdateTexture(in after, in region, data);
    }

    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.CopyBuffer(in source, sourceOffset, in destinationAfter, destinationOffset, sizeInBytes);
    }

    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.CopyTexture(in source, in sourceRegion, in destinationAfter, in destinationRegion, layerCount);
    }

    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.CopyTextureToBuffer(in source, in region, in destinationAfter, destinationOffset);
    }

    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.ResolveTexture(in source, in destinationAfter);
    }

    public void GenerateMips(in ResourceVersion textureAfter)
    {
        foreach (ICommandStreamProfiler sink in sinks)
            sink.GenerateMips(in textureAfter);
    }
}
