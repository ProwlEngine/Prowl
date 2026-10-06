using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Debugging;

/// <summary>Graph level capture tap. Members arrive with the graph hook step.</summary>
public interface ICaptureHook
{
}

/// <summary>
/// Observes every command recorded into a pass command buffer, in order. Spans and in-pass update bytes are only valid for the duration of the call.
/// </summary>
public interface ICommandStreamSink
{
    void BeginPassCommands(in PassInfo pass);

    void EndPassCommands(in PassInfo pass);

    void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops);

    void ClearColorTarget(uint index, Color color);

    void ClearDepthStencil(float depth, byte stencil);

    void SetPipeline(in PipelineBindInfo pipeline);

    void SetViewport(in Viewport viewport);

    void SetScissor(uint x, uint y, uint width, uint height);

    void SetStencilReference(uint reference);

    void SetBlendConstants(Color constants);

    void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings);

    void BindIndexBuffer(in IndexBindingUse binding);

    void ApplyPropertyDeltas(ReadOnlySpan<PropertyDelta> deltas);

    void ClearProperties();

    void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

    void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride);

    void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride);

    void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ);

    void DispatchIndirect(in ResourceVersion buffer, uint offset);

    void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data);

    void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data);

    void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes);

    void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount);

    void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset);

    void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter);

    void GenerateMips(in ResourceVersion textureAfter);
}

/// <summary>Tap for content writes outside a pass. Members arrive with the resource write sink step.</summary>
public interface IResourceWriteSink
{
}
