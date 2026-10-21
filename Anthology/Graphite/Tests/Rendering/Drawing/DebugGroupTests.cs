using System;
using System.Collections.Generic;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tests;

internal sealed class PayloadSink : ICommandStreamProfiler, IPassCommandSink
{
    public readonly List<object> Payloads = new();

    public void BeginExecution(ulong executionId, string graphName) { }
    public void EndExecution() { }
    public IPassCommandSink? BeginPassCommands(in PassInfo pass) => this;
    public void PushMarker(string name, object? meta)
    {
        if (meta != null)
            Payloads.Add(meta);
    }

    public void End() { }
    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops) { }
    public void ClearColorTarget(uint index, Color color) { }
    public void ClearDepthStencil(float depth, byte stencil) { }
    public void SetPipeline(in PipelineBindInfo pipeline) { }
    public void SetViewport(in Viewport viewport) { }
    public void SetScissor(uint x, uint y, uint width, uint height) { }
    public void SetStencilReference(uint reference) { }
    public void SetBlendConstants(Color constants) { }
    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings) { }
    public void BindIndexBuffer(in IndexBindingUse binding) { }
    public void SetProperties(ReadOnlySpan<PropertyState> properties) { }
    public void SetPushConstants(uint offset, ReadOnlySpan<byte> data) { }
    public void SetDescriptorTable(uint set, ReadOnlySpan<ResourceLayoutElementDescription> layout, ReadOnlySpan<DescriptorTableEntryState> entries) { }
    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) { }
    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance) { }
    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride) { }
    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride) { }
    public void DrawIndirectCount(in ResourceVersion buffer, uint offset, in ResourceVersion countBuffer, uint countOffset, uint maxDrawCount, uint stride) { }
    public void DrawIndexedIndirectCount(in ResourceVersion buffer, uint offset, in ResourceVersion countBuffer, uint countOffset, uint maxDrawCount, uint stride) { }
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ) { }
    public void DispatchIndirect(in ResourceVersion buffer, uint offset) { }
    public void ComputeBarrier() { }
    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data) { }
    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data) { }
    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes) { }
    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount) { }
    public void BlitTexture(in ResourceVersion source, in ResourceVersion destinationAfter, ReadOnlySpan<TextureBlit> regions) { }
    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset) { }
    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter) { }
    public void GenerateMips(in ResourceVersion textureAfter) { }
    public void PopMarker() { }
}
