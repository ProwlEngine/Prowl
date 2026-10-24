using System;
using System.Diagnostics;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>Forwards a pass's commands and adds the time spent in the sink to the buffer, so pass CPU time can leave it out.</summary>
internal sealed class TimedPassCommandSink(IPassCommandSink sink, CommandBufferBase owner) : IPassCommandSink
{
    private Scope Time() => new(owner);

    private readonly ref struct Scope(CommandBufferBase owner)
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose() => owner.SinkTicks += Stopwatch.GetTimestamp() - _start;
    }

    public void End()
    {
        using (Time()) sink.End();
    }

    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
    {
        using (Time()) sink.SetFramebuffer(in framebuffer, in ops);
    }

    public void ClearColorTarget(uint index, Color color)
    {
        using (Time()) sink.ClearColorTarget(index, color);
    }

    public void ClearDepthStencil(float depth, byte stencil)
    {
        using (Time()) sink.ClearDepthStencil(depth, stencil);
    }

    public void SetPipeline(in PipelineBindInfo pipeline)
    {
        using (Time()) sink.SetPipeline(in pipeline);
    }

    public void SetViewport(in Viewport viewport)
    {
        using (Time()) sink.SetViewport(in viewport);
    }

    public void SetScissor(uint x, uint y, uint width, uint height)
    {
        using (Time()) sink.SetScissor(x, y, width, height);
    }

    public void SetStencilReference(uint reference)
    {
        using (Time()) sink.SetStencilReference(reference);
    }

    public void SetBlendConstants(Color constants)
    {
        using (Time()) sink.SetBlendConstants(constants);
    }

    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        using (Time()) sink.BindVertexBuffers(bindings);
    }

    public void BindIndexBuffer(in IndexBindingUse binding)
    {
        using (Time()) sink.BindIndexBuffer(in binding);
    }

    public void SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        using (Time()) sink.SetProperties(properties);
    }

    public void SetPushConstants(uint offset, ReadOnlySpan<byte> data)
    {
        using (Time()) sink.SetPushConstants(offset, data);
    }

    public void SetDescriptorTable(uint set, ReadOnlySpan<ResourceLayoutElementDescription> layout, ReadOnlySpan<DescriptorTableEntryState> entries)
    {
        using (Time()) sink.SetDescriptorTable(set, layout, entries);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        using (Time()) sink.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        using (Time()) sink.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
    {
        using (Time()) sink.DrawIndirect(in buffer, offset, drawCount, stride);
    }

    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride)
    {
        using (Time()) sink.DrawIndexedIndirect(in buffer, offset, drawCount, stride);
    }

    public void DrawIndirectCount(in ResourceVersion buffer, uint offset, in ResourceVersion countBuffer, uint countOffset, uint maxDrawCount, uint stride)
    {
        using (Time()) sink.DrawIndirectCount(in buffer, offset, in countBuffer, countOffset, maxDrawCount, stride);
    }

    public void DrawIndexedIndirectCount(in ResourceVersion buffer, uint offset, in ResourceVersion countBuffer, uint countOffset, uint maxDrawCount, uint stride)
    {
        using (Time()) sink.DrawIndexedIndirectCount(in buffer, offset, in countBuffer, countOffset, maxDrawCount, stride);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        using (Time()) sink.Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    public void DispatchIndirect(in ResourceVersion buffer, uint offset)
    {
        using (Time()) sink.DispatchIndirect(in buffer, offset);
    }

    public void ComputeBarrier()
    {
        using (Time()) sink.ComputeBarrier();
    }

    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        using (Time()) sink.UpdateBuffer(in after, offset, data);
    }

    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        using (Time()) sink.UpdateTexture(in after, in region, data);
    }

    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        using (Time()) sink.CopyBuffer(in source, sourceOffset, in destinationAfter, destinationOffset, sizeInBytes);
    }

    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
    {
        using (Time()) sink.CopyTexture(in source, in sourceRegion, in destinationAfter, in destinationRegion, layerCount);
    }

    public void BlitTexture(in ResourceVersion source, in ResourceVersion destinationAfter, ReadOnlySpan<TextureBlit> regions)
    {
        using (Time()) sink.BlitTexture(in source, in destinationAfter, regions);
    }

    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
    {
        using (Time()) sink.CopyTextureToBuffer(in source, in region, in destinationAfter, destinationOffset);
    }

    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter)
    {
        using (Time()) sink.ResolveTexture(in source, in destinationAfter);
    }

    public void GenerateMips(in ResourceVersion textureAfter)
    {
        using (Time()) sink.GenerateMips(in textureAfter);
    }

    public void PushMarker(string name, object? meta)
    {
        using (Time()) sink.PushMarker(name, meta);
    }

    public void PopMarker()
    {
        using (Time()) sink.PopMarker();
    }
}
