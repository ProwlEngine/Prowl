using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class UploadPass(Uploader uploader, DeviceBuffer[] destinations) : IExecutionPass
{
    public string Name => "Upload";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < destinations.Length; i++)
            builder.DeclareImportedBuffer(RenderResourceID.Intern("Destination" + i), destinations[i], BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

[Collection("GPU Tests")]
public sealed class UploadTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GraphStore _store = new();
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
        _store.Dispose();
        _device.Dispose();
    }

    private DeviceBuffer Buffer(uint words) => _device.ResourceFactory.CreateBuffer(new BufferDescription(words * 4, BufferUsage.StructuredBufferReadWrite));

    private void Flush(params DeviceBuffer[] destinations)
    {
        using RenderGraph graph = RenderGraph.Build([new UploadPass(_uploader, destinations)]);
        _device.DispatchGraph(graph, graph.CreateInstances());
        _device.WaitForIdle();
    }

    private uint[] Read(DeviceBuffer buffer)
    {
        using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
        using RenderGraph graph = RenderGraph.Build([new CopyPass(buffer, readback)]);
        _device.DispatchGraph(graph, graph.CreateInstances());
        _device.WaitForIdle();
        uint[] words = _device.Map<uint>(readback).ToArray();
        _device.Unmap(readback);
        return words;
    }

    [Fact]
    public void WritesAfterAFence_LandAfterTheOnesBeforeIt()
    {
        const uint words = 1 << 16;
        using DeviceBuffer buffer = Buffer(words);
        for (uint round = 1; round <= 32; round++)
        {
            _uploader.Write(buffer, 0, Enumerable.Repeat(round, (int)words).ToArray());
            _uploader.Fence();
        }
        Flush(buffer);
        Assert.All(Read(buffer), word => Assert.Equal(32u, word));
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
    public void Destinations_AreForgottenOnceRecorded()
    {
        using DeviceBuffer buffer = Buffer(4);
        _uploader.Write(buffer, 0, [1]);
        Assert.Equal(1, _uploader.DestinationCount);
        Flush(buffer);
        Assert.Equal(0, _uploader.DestinationCount);
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
        using RenderGraph graph = RenderGraph.Build([new UploadPass(_uploader, [buffer])]);
        for (uint frame = 0; frame < 8; frame++)
        {
            _uploader.Write(buffer, frame % 4, [frame + 100]);
            _device.DispatchGraph(graph, graph.CreateInstances());
        }
        _device.WaitForIdle();

        Assert.Equal([104u, 105u, 106u, 107u], Read(buffer));
    }

    [Fact]
    public void Meshes_UploadIntoAPage_AndFreedRangesReturnOnceRetired()
    {
        GeometryPool pool = new(_device, _store, 1024, 1024);
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
        GeometryPool pool = new(_device, _store, 16, 16);
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
    private static MeshData Positions(int vertices)
    {
        Float3[] positions = Enumerable.Range(0, vertices).Select(i => new Float3(i, i * 2, -i)).ToArray();
        return new MeshData
        {
            VertexCount = (uint)vertices,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = Enumerable.Range(0, vertices).Select(i => (uint)i).ToArray(),
        };
    }

    [Fact]
    public void Records_HaveTheirStd430Sizes()
    {
        Assert.Equal(96, Marshal.SizeOf<ChunkRecord>());
        Assert.Equal(32, Marshal.SizeOf<MeshRecord>());
    }

    [Fact]
    public void ChunkRecords_ReadBackInGlsl_FieldForField()
    {
        GeometryPool pool = new(_device, _store, 1 << 16, 1 << 16);
        pool.Create(Positions(3));
        GpuMesh mesh = pool.Create(Positions(3 * 2048 + 6));
        pool.Apply(_uploader, 0, long.MaxValue, new List<GpuMesh>());
        Flush(pool.Pages[0].Vertices, pool.Pages[0].Indices, pool.Chunks.Buffer, pool.Meshes.Buffer);

        const string source = """
            Compute
            {
                layout(local_size_x = 1) in;

                struct Graphite_Chunk
                {
                    uint streamOffsets[12];
                    vec3 boundsMin;
                    uint streamsPresent;
                    vec3 boundsMax;
                    uint page;
                    uint firstIndex;
                    uint indexCount;
                    uint mesh;
                    uint subMesh;
                };

                struct Graphite_Mesh { vec3 boundsMin; uint firstChunk; vec3 boundsMax; uint chunkCount; };

                layout(set = 0, binding = 0, std430) readonly buffer Graphite_Chunks { Graphite_Chunk chunks[]; };
                layout(set = 0, binding = 1, std430) readonly buffer Graphite_Meshes { Graphite_Mesh meshes[]; };
                layout(set = 0, binding = 2, std430) buffer Output { float values[]; };
                layout(push_constant) uniform Push { uint mesh; } push;

                void main()
                {
                    Graphite_Mesh m = meshes[push.mesh];
                    Graphite_Chunk c = chunks[m.firstChunk + 1u];
                    values[0] = float(m.chunkCount);
                    values[1] = m.boundsMax.x;
                    values[2] = m.boundsMin.z;
                    values[3] = float(c.streamOffsets[0]);
                    values[4] = float(c.streamsPresent);
                    values[5] = c.boundsMin.x;
                    values[6] = c.boundsMax.y;
                    values[7] = float(c.page);
                    values[8] = float(c.firstIndex);
                    values[9] = float(c.indexCount);
                    values[10] = float(c.mesh);
                    values[11] = float(c.subMesh);
                }
            }
            """;
        using ComputeProgram program = _device.ResourceFactory.CreateComputeProgram(
            _compiler.CompileCompute(new ShaderDef.ShaderPass { State = new ShaderDef.PassState(), Source = source }, [], _device.BackendType));
        using DeviceBuffer output = Buffer(12);
        PropertySet properties = new();
        properties.SetBuffer("Graphite_Chunks", pool.Chunks.Buffer);
        properties.SetBuffer("Graphite_Meshes", pool.Meshes.Buffer);
        properties.SetBuffer("Output", output);
        using RenderGraph graph = RenderGraph.Build([new DispatchPass(program, properties, (uint)mesh.Id, [pool.Chunks.Buffer, pool.Meshes.Buffer], output)]);
        _device.DispatchGraph(graph, graph.CreateInstances());
        _device.WaitForIdle();

        MeshPlacement placement = mesh.Placement!;
        float[] values = Read(output).Select(BitConverter.UInt32BitsToSingle).ToArray();
        Assert.Equal(new float[]
        {
            2, 6149, -6149,
            placement.StreamOffsets[0], 1, 6144, 12298, 0,
            placement.IndexBase + 6144, 6, mesh.Id, 0,
        }, values);
    }

    [Fact]
    public void Tables_GrowIntoANewBuffer_KeepingEveryRecord()
    {
        GpuTable table = new(_device, _store, "Test.GrowTable", 1, 4);
        for (uint i = 0; i < 4; i++)
            table.Set(i, i + 10);
        Assert.False(table.Flush(_uploader));
        DeviceBuffer first = table.Buffer;

        table.Set(9, 99u);
        Assert.True(table.Flush(_uploader));
        Assert.NotSame(first, table.Buffer);
        Flush(table.Buffer);

        uint[] words = Read(table.Buffer);
        Assert.Equal([10u, 11u, 12u, 13u, 0u, 0u, 0u, 0u, 0u, 99u], words[..10]);
    }

    [Fact]
    public void Tables_OnlyUploadDirtyRecords()
    {
        GpuTable table = new(_device, _store, "Test.DirtyTable", 1, 16);
        for (uint i = 0; i < 16; i++)
            table.Set(i, i);
        table.Flush(_uploader);
        Flush(table.Buffer);

        table.Set(3, 30u);
        table.Set(2, 20u);
        table.Set(9, 90u);
        table.Set(3, 31u);
        table.Flush(_uploader);
        Assert.Equal(3u, _uploader.QueuedWords);
        Flush(table.Buffer);

        Assert.Equal([0u, 1u, 20u, 31u, 4u, 5u, 6u, 7u, 8u, 90u, 10u], Read(table.Buffer)[..11]);
    }

    [Fact]
    public void DisposedMeshes_ClearTheirMeshRecord_AndTheirChunksAreReused()
    {
        GeometryPool pool = new(_device, _store, 1 << 16, 1 << 16);
        GpuMesh first = pool.Create(Positions(3));
        pool.Apply(_uploader, 0, long.MaxValue, new List<GpuMesh>());
        uint chunk = first.Placement!.FirstChunk;
        Assert.Equal(1u, pool.Meshes.Get<MeshRecord>((uint)first.Id).ChunkCount);

        first.Dispose();
        pool.Apply(_uploader, 1, long.MaxValue, new List<GpuMesh>());
        Assert.Equal(0u, pool.Meshes.Get<MeshRecord>((uint)first.Id).ChunkCount);

        pool.Reclaim(1);
        GpuMesh second = pool.Create(Positions(3));
        pool.Apply(_uploader, 1, long.MaxValue, new List<GpuMesh>());
        Assert.Equal(chunk, second.Placement!.FirstChunk);
        Flush(pool.Pages[0].Vertices, pool.Pages[0].Indices, pool.Chunks.Buffer, pool.Meshes.Buffer);
    }
}

file sealed class CopyPass(DeviceBuffer source, DeviceBuffer destination) : IExecutionPass
{
    public string Name => "Copy";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(RenderResourceID.Intern("CopySource"), source, BufferAccess.TransferRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("CopyDestination"), destination, BufferAccess.TransferWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => cmd.CopyBuffer(source, 0, destination, 0, source.SizeInBytes);
}

file sealed class DispatchPass(ComputeProgram program, PropertySet properties, uint push, DeviceBuffer[] inputs, DeviceBuffer output) : IExecutionPass
{
    public string Name => "Dispatch";

    public void Setup(RenderContextBuilder builder)
    {
        for (int i = 0; i < inputs.Length; i++)
            builder.DeclareImportedBuffer(RenderResourceID.Intern("DispatchInput" + i), inputs[i], BufferAccess.ShaderRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("DispatchOutput"), output, BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetComputeShader(program);
        cmd.SetProperties(properties);
        cmd.SetPushConstants(push);
        cmd.Dispatch(1, 1, 1);
    }
}
