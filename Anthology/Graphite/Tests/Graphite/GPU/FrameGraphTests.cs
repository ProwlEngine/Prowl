#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

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
    public void Passes_RunInOrderAdded()
    {
        List<string> order = new();
        using FrameGraph graph = NewGraph();
        BufferHandle buffer = graph.Create(Buf, GraphBufferDesc.Structured(4, 4));
        BufferHandle v1 = default;
        graph.AddPass("A", pass => v1 = pass.Write(buffer), _ => order.Add("A"));
        graph.AddPass("B", pass => pass.Write(v1), _ => order.Add("B"));
        Run(graph, GD);

        Assert.Equal(new[] { "A", "B" }, order);
    }

    [Fact]
    public void WritingAStaleHandle_ThrowsNamingTheNewerWriter()
    {
        using FrameGraph graph = NewGraph();
        BufferHandle v0 = graph.Create(Buf, GraphBufferDesc.Structured(4, 4));
        graph.AddPass("A", pass => pass.Write(v0), _ => { });

        RenderGraphException ex = Assert.Throws<RenderGraphException>(() => graph.AddPass("B", pass => pass.Write(v0), _ => { }));
        Assert.Contains("'A'", ex.Message);
    }

    [Fact]
    public void ReadingATransientBeforeAnyWrite_Throws()
    {
        using FrameGraph graph = NewGraph();
        TextureHandle color = graph.Create(ColorId, SmallColor);
        Assert.Throws<RenderGraphException>(() => graph.AddPass("Early", pass => pass.Read(color), _ => { }));
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
            context => context.Command.ClearColorTarget(0, new Color(255, 0, 0, 255)));
        Run(graph, GD);

        Assert.Equal(Enumerable.Repeat((byte)9, 4), ReadBytes(destination).Take(4));
        Assert.All(ReadTexture<uint>(target.ColorTextures[0]).Data, texel => Assert.Equal(0xFF0000FFu, texel));
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
    public void AnImport_KeepsItsStateAcrossExecutions()
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        for (byte frame = 1; frame <= 2; frame++)
        {
            byte value = frame;
            using FrameGraph graph = NewGraph();
            BufferHandle import = graph.Import(buffer);
            graph.AddPass("Fill", pass => pass.Write(import, BufferAccess.TransferWrite),
                context => context.Command.UpdateBuffer(buffer, frame == 1 ? 0u : 4u, new byte[] { value, value, value, value }));
            Run(graph, GD);
        }

        byte[] bytes = ReadBytes(buffer);
        Assert.Equal(new byte[] { 1, 1, 1, 1, 2, 2, 2, 2 }, bytes.Take(8));
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

    [Fact]
    public void LastLayout_IsTheSameObjectWhileDeclarationsDoNotChange()
    {
        using FrameGraph graph = NewGraph();

        GraphLayout Frame(string passName)
        {
            BufferHandle buffer = graph.Create(Buf, GraphBufferDesc.Structured(4, 4));
            graph.AddPass(passName, pass => pass.Write(buffer), _ => { });
            Run(graph, GD);
            return graph.LastLayout!;
        }

        GraphLayout first = Frame("Pass");
        Assert.Same(first, Frame("Pass"));
        Assert.NotSame(first, Frame("Renamed"));
    }

    private byte[] ReadBytes(DeviceBuffer buffer)
    {
        DeviceBuffer readback = GetReadback(buffer);
        byte[] bytes = GD.Map(readback).Slice(0, (int)buffer.SizeInBytes).ToArray();
        GD.Unmap(readback);
        return bytes;
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameGraphTests : FrameGraphTests<VulkanDeviceCreator> { }
#endif
