#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

internal sealed class SinkRecorder : ICommandStreamProfiler, IPassCommandSink
{
    public readonly List<string> Log = new();
    public readonly List<TargetLoadStoreOps> Ops = new();
    public readonly List<PropertyState[]> Tables = new();
    public readonly List<(ResourceVersion After, uint Offset, byte[] Data)> BufferUpdates = new();
    public readonly List<(ResourceVersion After, TextureRegion Region, byte[] Data)> TextureUpdates = new();
    public readonly List<VertexBindingUse[]> VertexBindings = new();
    public readonly List<IndexBindingUse> IndexBindings = new();
    public readonly List<PipelineBindInfo> Pipelines = new();
    public readonly List<(ResourceVersion Source, uint SourceOffset, ResourceVersion After, uint Offset, uint Size)> BufferCopies = new();

    public void BeginExecution(ulong executionId, string graphName) { }
    public void EndExecution() { }

    public IPassCommandSink? BeginPassCommands(in PassInfo pass)
    {
        Log.Add("BeginPass:" + pass.Name);
        return this;
    }

    public void End() => Log.Add("EndPass");

    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
    {
        Ops.Add(ops);
        Log.Add($"SetFramebuffer:{framebuffer.Colors.Length}:{framebuffer.Depth.HasValue}:{ops.Color.Load}");
    }

    public void ClearColorTarget(uint index, Color color) => Log.Add("ClearColorTarget");
    public void ClearDepthStencil(float depth, byte stencil) => Log.Add("ClearDepthStencil");

    public void SetPipeline(in PipelineBindInfo pipeline)
    {
        Pipelines.Add(pipeline);
        Log.Add(pipeline.IsCompute ? "SetPipeline:Compute" : "SetPipeline:Graphics");
    }

    public void SetViewport(in Viewport viewport) => Log.Add("SetViewport");
    public void SetScissor(uint x, uint y, uint width, uint height) => Log.Add("SetScissor");
    public void SetStencilReference(uint reference) => Log.Add("SetStencilReference");
    public void SetBlendConstants(Color constants) => Log.Add("SetBlendConstants");

    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        VertexBindings.Add(bindings.ToArray());
        Log.Add("BindVertexBuffers");
    }

    public void BindIndexBuffer(in IndexBindingUse binding)
    {
        IndexBindings.Add(binding);
        Log.Add("BindIndexBuffer");
    }

    public void SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        Tables.Add(properties.ToArray());
        Log.Add("Props:" + properties.Length);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) => Log.Add($"Draw:{vertexCount}:{instanceCount}:{firstVertex}:{firstInstance}");

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        => Log.Add($"DrawIndexed:{indexCount}:{instanceCount}:{firstIndex}:{vertexOffset}:{firstInstance}");

    public void SetPushConstants(uint offset, ReadOnlySpan<byte> data) => Log.Add($"Push:{offset}:{data.Length}");
    public void SetDescriptorTable(uint set, ReadOnlySpan<ResourceLayoutElementDescription> layout, ReadOnlySpan<DescriptorTableEntryState> entries) => Log.Add($"Table:{set}:{entries.Length}");
    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride) => Log.Add("DrawIndirect");
    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride) => Log.Add("DrawIndexedIndirect");
    public void DrawIndirectCount(in ResourceVersion buffer, uint offset, in ResourceVersion countBuffer, uint countOffset, uint maxDrawCount, uint stride) => Log.Add("DrawIndirectCount");
    public void DrawIndexedIndirectCount(in ResourceVersion buffer, uint offset, in ResourceVersion countBuffer, uint countOffset, uint maxDrawCount, uint stride) => Log.Add("DrawIndexedIndirectCount");
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ) => Log.Add($"Dispatch:{groupCountX}:{groupCountY}:{groupCountZ}");
    public void DispatchIndirect(in ResourceVersion buffer, uint offset) => Log.Add("DispatchIndirect");
    public void ComputeBarrier() => Log.Add("ComputeBarrier");

    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        BufferUpdates.Add((after, offset, data.ToArray()));
        Log.Add("UpdateBuffer");
    }

    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        TextureUpdates.Add((after, region, data.ToArray()));
        Log.Add("UpdateTexture");
    }

    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        BufferCopies.Add((source, sourceOffset, destinationAfter, destinationOffset, sizeInBytes));
        Log.Add("CopyBuffer");
    }

    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
        => Log.Add("CopyTexture");

    public void BlitTexture(in ResourceVersion source, in ResourceVersion destinationAfter, ReadOnlySpan<TextureBlit> regions) => Log.Add("BlitTexture");

    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
        => Log.Add("CopyTextureToBuffer");

    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter) => Log.Add("ResolveTexture");
    public void GenerateMips(in ResourceVersion textureAfter) => Log.Add("GenerateMips");
    public void PushMarker(string name, object? meta) => Log.Add(meta == null ? "PushMarker:" + name : "PushMarker:" + name + ":" + meta);
    public void PopMarker() => Log.Add("PopMarker");
}

public abstract class FrameGraphTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static readonly RenderResourceID ColorId = RenderResourceID.Intern("frame_graph_test_color");
    private static readonly RenderResourceID Hist = RenderResourceID.Intern("frame_graph_test_history");
    private static readonly RenderResourceID Buf = RenderResourceID.Intern("frame_graph_test_buffer");

    private static GraphTextureDesc SmallColor => GraphTextureDesc.Sized(4, 4, PixelFormat.R8_G8_B8_A8_UNorm);

    private FrameGraph NewGraph() => new(GD, "Test");

    private static void Run(FrameGraph graph, GraphicsDevice gd, params IProfiler[] profilers)
    {
        graph.Execute(profilers);
        gd.WaitForIdle();
    }

    [Fact]
    public void FirstWriteClears_LaterWritesLoad_AndAnUnreadTransientIsNotStored()
    {
        SinkRecorder sink = new();
        using FrameGraph graph = NewGraph();
        TextureHandle color = graph.Create(ColorId, SmallColor);
        TextureHandle v1 = default, v2 = default;
        graph.AddPass("First", pass => v1 = pass.Write(color), _ => { });
        graph.AddPass("Second", pass => v2 = pass.Write(v1), _ => { });
        graph.AddPass("Third", pass => pass.Read(v2), _ => { });
        Run(graph, GD, sink);

        Assert.Equal(new[] { LoadAction.Clear, LoadAction.Load }, sink.Ops.Take(2).Select(ops => ops.Color.Load));
        Assert.All(sink.Ops.Take(2), ops => Assert.Equal(StoreAction.Store, ops.Color.Store));

        sink = new SinkRecorder();
        using FrameGraph unread = NewGraph();
        TextureHandle target = unread.Create(ColorId, SmallColor);
        unread.AddPass("Only", pass => pass.Write(target), _ => { });
        Run(unread, GD, sink);
        Assert.Equal(StoreAction.DontCare, sink.Ops.Single().Color.Store);
    }

    [Fact]
    public void Synchronization_OrdersWritesAndReads_AndImportsRestAfterExecute()
    {
        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(4, 4, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));

        using FrameGraph graph = NewGraph();
        BufferHandle src = graph.Import(source), dst = graph.Import(destination);
        TextureHandle texture = graph.Import(target);
        BufferHandle written = default;
        graph.AddPass("Update", pass => written = pass.Write(src, BufferAccess.TransferWrite),
            context => context.Command.UpdateBuffer(source, 0, new byte[] { 9, 9, 9, 9 }));
        graph.AddPass("Copy", pass =>
        {
            pass.Read(written, BufferAccess.TransferRead);
            pass.Write(dst, BufferAccess.TransferWrite);
        }, context => context.Command.CopyBuffer(source, 0, destination, 0, 16));
        graph.AddPass("Paint", pass => pass.Write(texture),
            context => context.Command.ClearColorTarget(0, Color.FromBytes(255, 0, 0, 255)));
        Run(graph, GD);

        Assert.Equal(Enumerable.Repeat((byte)9, 4), ReadBytes(destination).Take(4));
        Assert.All(ReadTexture<uint>(target.ColorTextures[0]).Data, texel => Assert.Equal(0xFF0000FFu, texel));

        DeviceBuffer imported = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        for (byte frame = 1; frame <= 2; frame++)
        {
            byte value = frame;
            using FrameGraph fillGraph = NewGraph();
            BufferHandle fillImport = fillGraph.Import(imported);
            fillGraph.AddPass("Fill", pass => pass.Write(fillImport, BufferAccess.TransferWrite),
                context => context.Command.UpdateBuffer(imported, frame == 1 ? 0u : 4u, new byte[] { value, value, value, value }));
            Run(fillGraph, GD);
        }

        byte[] fillBytes = ReadBytes(imported);
        Assert.Equal(new byte[] { 1, 1, 1, 1, 2, 2, 2, 2 }, fillBytes.Take(8));
    }

    [Fact]
    public void History_IsNullOnFirstUse_ThenHoldsEarlierFrames_AndIsEmptyAfterReallocation()
    {
        using FrameGraph graph = NewGraph();
        List<RenderTexture?> seen = new();

        void Frame(int size)
        {
            TextureHandle history = graph.Create(Hist, GraphTextureDesc.Sized(size, size, PixelFormat.R8_G8_B8_A8_UNorm), Lifetime.History(1));
            TextureHandle written = default;
            graph.AddPass("Write", pass => written = pass.Write(history), context => seen.Add(context.History(written)));
            Run(graph, GD);
        }

        Frame(4);
        Frame(4);
        Frame(8);

        Assert.Null(seen[0]);
        Assert.NotNull(seen[1]);
        Assert.Null(seen[2]);
    }

    [Fact]
    public void History_IsFreedAfter120UnusedExecutions()
    {
        using FrameGraph graph = NewGraph();
        RenderTexture? last = null;

        void Frame(bool useHistory)
        {
            if (useHistory)
            {
                TextureHandle history = graph.Create(Hist, SmallColor, Lifetime.History(1));
                TextureHandle written = default;
                graph.AddPass("Write", pass => written = pass.Write(history), context => last = context.History(written));
            }
            else
            {
                graph.AddPass("Idle", _ => { }, _ => { });
            }
            Run(graph, GD);
        }

        Frame(true);
        Frame(true);
        Assert.NotNull(last);

        for (int i = 0; i < 121; i++)
            Frame(false);

        Frame(true);
        Assert.Null(last);
    }

    [Fact]
    public void Validation_ThrowsOnABindTheOtherPassDidNotDeclare()
    {
        DeviceBuffer owned = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer other = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        using FrameGraph graph = NewGraph();
        BufferHandle ownedHandle = graph.Import(owned), otherHandle = graph.Import(other);
        graph.AddPass("Declares", pass => pass.Write(otherHandle, BufferAccess.TransferWrite), _ => { });
        graph.AddPass("Sneaks", pass => pass.Write(ownedHandle, BufferAccess.TransferWrite),
            context => context.Command.UpdateBuffer(other, 0, new byte[4]));

        RenderGraphException ex = Assert.Throws<RenderGraphException>(() => graph.Execute());
        Assert.Contains("Sneaks", ex.Message);
        GD.WaitForIdle();
    }

    private byte[] ReadBytes(DeviceBuffer buffer)
    {
        DeviceBuffer readback = GetReadback(buffer);
        byte[] bytes = GD.Map(readback).Slice(0, (int)buffer.SizeInBytes).ToArray();
        GD.Unmap(readback);
        return bytes;
    }

    [Fact]
    public void TransferUpdates_AreRecordedInOrderWithCopies()
    {
        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(16, BufferUsage.VertexBuffer));
        DeviceBuffer readback = RF.CreateBuffer(new BufferDescription(16, BufferUsage.Staging));

        GD.Record(transfer =>
        {
            transfer.UpdateBuffer(source, 0, new uint[] { 1, 2, 3, 4 });
            transfer.CopyBuffer(source, 0, readback, 0, 16);
            transfer.UpdateBuffer(source, 0, new uint[] { 9, 9, 9, 9 });
        }).Wait();

        Span<uint> map = GD.Map<uint>(readback);
        uint[] result = [map[0], map[1], map[2], map[3]];
        GD.Unmap(readback);

        Assert.Equal(new uint[] { 1, 2, 3, 4 }, result);

        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        DeviceBuffer texelReadback = CreateTexelReadbackBuffer<uint>(2, 2);

        uint[] initial = [0xFF0000FF, 0xFF0000FF, 0xFF0000FF, 0xFF0000FF];
        uint[] overwritten = [0xFFFF0000, 0xFFFF0000, 0xFFFF0000, 0xFFFF0000];

        GD.Record(transfer =>
        {
            transfer.UpdateTexture<uint>(texture, initial);
            transfer.CopyTextureToBuffer(texture, texelReadback, 0, TextureRegion.Whole(texture));
            transfer.UpdateTexture<uint>(texture, overwritten);
        }).Wait();

        TexelData<uint> texelMap = ReadTexels<uint>(texelReadback, 2, 2);
        uint topLeft = texelMap[0, 0];
        uint bottomRight = texelMap[1, 1];

        Assert.Equal(0xFF0000FFu, topLeft);
        Assert.Equal(0xFF0000FFu, bottomRight);

        Texture staging = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        GD.Record(transfer =>
        {
            transfer.UpdateTexture<uint>(staging, [0x11111111, 0x22222222, 0x33333333, 0x44444444], new TextureRegion(1, 2, 0, 2, 2, 1));
        }).Wait();

        TexelData<uint> regionMap = ReadTexture<uint>(staging);
        uint[] regionResult = [regionMap[1, 2], regionMap[2, 2], regionMap[1, 3], regionMap[2, 3]];

        Assert.Equal(new uint[] { 0x11111111, 0x22222222, 0x33333333, 0x44444444 }, regionResult);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameGraphTests : FrameGraphTests<VulkanDeviceCreator> { }
#endif
