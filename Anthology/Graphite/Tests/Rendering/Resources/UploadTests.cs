using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file readonly struct NoView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 1;
    public uint PixelHeight => 1;
    public int ViewId => 0;
}

file sealed class UploadPass(Uploader uploader, DeviceBuffer[] destinations) : IPass
{
    public string Name => "Upload";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < destinations.Length; i++)
            builder.DeclareImportedBuffer(RenderResourceID.Intern("Destination" + i), destinations[i], BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

public sealed class UploadTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Uploader _uploader;

    public UploadTests()
    {
        InternalShaders.Register();
        _uploader = new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
    }

    public void Dispose()
    {
        _uploader.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private DeviceBuffer Buffer(uint words) => _device.ResourceFactory.CreateBuffer(new BufferDescription(words * 4, BufferUsage.StructuredBufferReadWrite));

    private void Flush(params DeviceBuffer[] destinations)
    {
        using RenderPipeline pipeline = new([new UploadPass(_uploader, destinations)]);
        _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        _device.WaitForIdle();
    }

    private uint[] Read(DeviceBuffer buffer)
    {
        using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
        using RenderPipeline pipeline = new([new CopyPass(buffer, readback)]);
        _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        _device.WaitForIdle();
        uint[] words = _device.Map<uint>(readback).ToArray();
        _device.Unmap(readback);
        return words;
    }

    [Fact]
    public void Writes_LandAtTheirOffsets_AcrossSeveralDestinations()
    {
        using DeviceBuffer a = Buffer(64);
        using DeviceBuffer b = Buffer(64);

        _uploader.Write(a, 3, [1, 2, 3]);
        _uploader.Write(b, 0, [7]);
        _uploader.Write(a, 60, [9, 8, 7, 6]);
        Flush(a, b);

        uint[] first = Read(a);
        Assert.Equal([1u, 2u, 3u], first[3..6]);
        Assert.Equal([9u, 8u, 7u, 6u], first[60..64]);
        Assert.Equal(0u, first[6]);
        Assert.Equal(7u, Read(b)[0]);
        Assert.Equal(0u, _uploader.QueuedWords);
    }

    [Fact]
    public void BigWrites_SplitIntoItems_AndStillCopyEveryWord()
    {
        const uint words = Uploader.MaxItemWords * 3 + 17;
        using DeviceBuffer buffer = Buffer(words + 5);
        uint[] data = Enumerable.Range(1, (int)words).Select(i => (uint)i).ToArray();

        _uploader.Write(buffer, 5, data);
        Flush(buffer);

        Assert.Equal(data, Read(buffer)[5..]);
    }

    [Fact]
    public void FramesInFlight_EachUseTheirOwnUploadBuffers()
    {
        using DeviceBuffer buffer = Buffer(4);
        using RenderPipeline pipeline = new([new UploadPass(_uploader, [buffer])]);
        for (uint frame = 0; frame < 8; frame++)
        {
            _uploader.Write(buffer, frame % 4, [frame + 100]);
            _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        }
        _device.WaitForIdle();

        Assert.Equal([104u, 105u, 106u, 107u], Read(buffer));
    }

    [Fact]
    public void Meshes_UploadIntoAPage_AndFreedRangesReturnOnceRetired()
    {
        using GeometryPool pool = new(_device, 1024, 1024);
        Float3[] positions = [new(1, 2, 3), new(4, 5, 6), new(7, 8, 9)];
        MeshData data = new()
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2 },
        };

        GpuMesh first = pool.Create(data);
        GpuMesh second = pool.Create(data);
        List<GpuMesh> changed = new();
        Assert.True(pool.Apply(_uploader, 0, long.MaxValue, changed));
        Assert.Equal([first, second], changed);
        Flush(pool.Pages[0].Vertices, pool.Pages[0].Indices);

        MeshPlacement placement = second.Placement!;
        uint offset = placement.StreamOffsets[(int)VertexSemantic.Position];
        Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f }, Read(pool.Pages[0].Vertices)[(int)offset..(int)(offset + 9)].Select(BitConverter.UInt32BitsToSingle));
        Assert.Equal([0u, 1u, 2u], Read(pool.Pages[0].Indices)[(int)placement.IndexBase..(int)(placement.IndexBase + 3)]);

        uint used = pool.Pages[0].VertexWords.Used;
        first.Dispose();
        changed.Clear();
        Assert.False(pool.Apply(_uploader, 5, long.MaxValue, changed));
        Assert.Null(first.Placement);

        pool.Reclaim(4);
        Assert.Equal(used, pool.Pages[0].VertexWords.Used);
        pool.Reclaim(5);
        Assert.True(pool.Pages[0].VertexWords.Used < used);
        Assert.Equal(first.Id, pool.Create(data).Id);
    }

    [Fact]
    public void MeshesBiggerThanAPage_GetADedicatedPage()
    {
        using GeometryPool pool = new(_device, 16, 16);
        Float3[] positions = Enumerable.Range(0, 30).Select(i => new Float3(i, 0, 0)).ToArray();
        GpuMesh mesh = pool.Create(new MeshData
        {
            VertexCount = 30,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = Enumerable.Range(0, 30).Select(i => (uint)i).ToArray(),
        });

        Assert.True(pool.Apply(_uploader, 0, long.MaxValue, new List<GpuMesh>()));
        Assert.Single(pool.Pages);
        Assert.True(pool.Pages[0].VertexWords.Capacity >= 90);
        Assert.NotNull(mesh.Placement);
    }
}

file sealed class CopyPass(DeviceBuffer source, DeviceBuffer destination) : IPass
{
    public string Name => "Copy";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(RenderResourceID.Intern("CopySource"), source, BufferAccess.TransferRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("CopyDestination"), destination, BufferAccess.TransferWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => cmd.CopyBuffer(source, 0, destination, 0, source.SizeInBytes);
}
