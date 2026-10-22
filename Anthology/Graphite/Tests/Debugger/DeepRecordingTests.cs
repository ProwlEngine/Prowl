using System;
using System.IO;
using System.Linq;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file static class Passes
{
    public static void Write(FrameGraph graph, string name, RenderTexture texture, DeviceBuffer external, byte value)
    {
        TextureHandle target = graph.Import(texture);
        graph.AddPass(name, pass => pass.Write(target), context =>
        {
            CommandBuffer cmd = context.Command;
            cmd.ClearColorTarget(0, new Color((byte)(value * 50), 0, 0, 255));
            cmd.UpdateBuffer(external, 0, new byte[] { value, value, value, value });
        });
    }

    public static void History(FrameGraph graph)
    {
        TextureHandle history = graph.Create("deep_history", GraphTextureDesc.Sized(8, 8, PixelFormat.R8_G8_B8_A8_UNorm), Lifetime.History(1));
        graph.AddPass("History", pass => pass.Write(history), _ => { });
    }
}

[System.Serializable]
public readonly record struct TestHandle(int Index, int Generation);

[System.Serializable]
public sealed record TestPayload(string Name, TestHandle Handle, TestHandle[] Others);

[Collection("GPU Tests")]
public class DeepRecordingTests
{
    private static GraphicsDevice CreateDevice() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));

    private static byte[] WriteBinary(EchoObject data)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            data.WriteToBinary(writer);
        return stream.ToArray();
    }

    private static EchoObject ReadBinary(byte[] bytes)
    {
        using BinaryReader reader = new(new MemoryStream(bytes));
        return EchoObject.ReadFromBinary(reader);
    }

    private static DeepRecording Record(GraphicsDevice device, DeepMode mode, Action<FrameGraph> add)
    {
        using FrameGraph graph = new(device, "Deep");
        add(graph);
        DeepRecording deep = new(device, mode);
        graph.Execute(deep);
        deep.Wait();
        return deep;
    }

    [SkippableFact]
    public void Pass_RecordsCommandsReferencesAndUpdateBlobs()
    {
        using GraphicsDevice device = CreateDevice();
        DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));

        DeepRecording deep = Record(device, DeepMode.Full, graph => Passes.Write(graph, "Only", target, external, 5));

        DeepPass pass = Assert.Single(Assert.Single(deep.Groups).Passes);
        Assert.Null(pass.NotReplayable);
        Assert.Equal(2, pass.References.Length);
        UpdateBufferCommand update = Assert.Single(pass.Commands.OfType<UpdateBufferCommand>());
        Assert.Equal(update.After.Version, pass.References.Single(r => r.Resource == update.After.Resource).Last);
        Assert.Contains(deep.Blobs, b => b.Ref == update.Data && b.Data.SequenceEqual(new byte[] { 5, 5, 5, 5 }));
        Assert.Contains(pass.Commands, c => c is SetFramebufferCommand);
        Assert.Contains(pass.Commands, c => c is ClearColorTargetCommand);
        Assert.Single(deep.Recording.Groups);
    }

    [SkippableTheory]
    [InlineData(DeepMode.Full)]
    [InlineData(DeepMode.ReplayOnly)]
    public void Copies_AreTakenOncePerVersion(DeepMode mode)
    {
        using GraphicsDevice device = CreateDevice();
        DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));

        DeepRecording deep = Record(device, mode, graph =>
        {
            Passes.Write(graph, "First", target, external, 1);
            Passes.Write(graph, "Second", target, external, 2);
        });

        DeepPass[] passes = deep.Groups[0].Passes.ToArray();
        RecordedCopy[] copies = passes.SelectMany(p => p.Copies).ToArray();
        Assert.Equal(copies.Length, copies.Select(c => c.Version).Distinct().Count());
        Assert.DoesNotContain(passes[1].Copies, c => c.Placement == CopyPlacement.BeforePass);
        Assert.Contains(passes[0].Copies, c => c.Placement == CopyPlacement.BeforePass && c.Version.Version == 0);
        Assert.Equal(mode == DeepMode.Full, copies.Any(c => c.Placement == CopyPlacement.AfterPass));
        Assert.All(copies, c => Assert.Contains(deep.Blobs, b => b.Ref == c.Blob));
    }

    [SkippableFact]
    public void EchoRoundTrip_ComparesEqual()
    {
        using GraphicsDevice device = CreateDevice();
        DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        DeepRecording deep = Record(device, DeepMode.Full, graph =>
        {
            Passes.Write(graph, "First", target, external, 1);
            Passes.Write(graph, "Second", target, external, 2);
        });

        DebuggerSerialization.Register();
        byte[] bytes = WriteBinary(Serializer.Serialize(deep));
        DeepRecording loaded = Serializer.Deserialize<DeepRecording>(ReadBinary(bytes))!;

        Assert.True(loaded.IsDone);
        Assert.Equal(deep.Mode, loaded.Mode);
        Assert.Equal(deep.Backend, loaded.Backend);
        Assert.Equal(deep.Features, loaded.Features);
        Assert.Equal(deep.Recording.DeviceName, loaded.Recording.DeviceName);
        Assert.Equal(deep.ExecutionId, loaded.ExecutionId);
        Assert.Equal("Deep", loaded.Recording.GraphName);
        Assert.Equal(deep.Recording.Groups, loaded.Recording.Groups);
        Assert.Equal(deep.Recording.CommandBuffers, loaded.Recording.CommandBuffers);
        Assert.NotNull(loaded.Recording.Counters);
        Assert.Equal(deep.Recording.Counters, loaded.Recording.Counters);
        Assert.Equal(deep.Resources, loaded.Resources);
        Assert.Equal(deep.Programs, loaded.Programs);
        Assert.Equal(deep.Samplers, loaded.Samplers);
        Assert.Equal(deep.Blobs, loaded.Blobs);
        Assert.Equal(deep.Groups, loaded.Groups);
    }

    [Fact]
    public void EchoRoundTrip_KeepsProgramsAndCommands()
    {
        DebuggerSerialization.Register();
        RecordedProgram program = new(
            ProgramKey.FromBytes(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            "Lit/Forward",
            false,
            EquatableArray.Create(new RecordedStage(ShaderStages.Vertex, "main", new BlobRef(EquatableArray.Create<byte>(1, 2), 2))),
            EquatableArray.Create(new ResourceLayoutDescription(new ResourceLayoutElementDescription { Name = "Albedo", BindingIndex = 3 })),
            null,
            null,
            null,
            EquatableArray.Create(new VertexLayoutDescription(0, 12, new VertexElementDescription("Position", VertexElementFormat.Float3))),
            1,
            1,
            1,
            new PushConstantRange(ShaderStages.Vertex | ShaderStages.Fragment, 16),
            EquatableArray.Create(new SpecializationConstant(2, 7)));
        EquatableArray<RecordedCommand> commands = EquatableArray.Create<RecordedCommand>(
            new SetPipelineCommand(program.Key, 42, false, null, PrimitiveTopology.TriangleList),
            new SetPushConstantsCommand(4, new BlobRef(EquatableArray.Create<byte>(3, 4), 8)),
            new SetDescriptorTableCommand(0,
                EquatableArray.Create(new ResourceLayoutElementDescription("Textures", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0) { ArrayCount = 64 }),
                EquatableArray.Create(new RecordedTableEntry(3, new RecordedProperty("Textures", PropertyKind.Texture, default, EquatableArray<byte>.Empty, default, default, null, -1, true)))),
            new SetPropertiesCommand(
                EquatableArray.Create(new RecordedProperty("Tint", PropertyKind.Uniform, UniformScalarType.Float1, EquatableArray.Create<byte>(0, 0, 128, 63), default, default, null, -1)),
                EquatableArray.Create("Old")),
            new BlitTextureCommand(default, default, EquatableArray.Create(new TextureBlit(0, 0, 8, 8, 0, 0, 4, 4))),
            new PushMarkerCommand("Group"),
            new DrawCommand(3, 1, 0, 0),
            new PopMarkerCommand());

        EchoObject programData = ReadBinary(WriteBinary(Serializer.Serialize(program)));
        RecordedProgram loadedProgram = Serializer.Deserialize<RecordedProgram>(programData)!;
        EquatableArray<RecordedCommand> loadedCommands = Serializer.Deserialize<EquatableArray<RecordedCommand>>(ReadBinary(WriteBinary(Serializer.Serialize(commands))));

        Assert.Equal(program.Key, loadedProgram.Key);
        Assert.Equal("Albedo", PropertyID.ToString(loadedProgram.Layouts[0].Elements[0].Name));
        Assert.Equal("Position", VertexAttributeID.ToString(loadedProgram.VertexLayouts[0].Elements[0].Name));
        Assert.Equal(program, loadedProgram);
        Assert.Equal(commands, loadedCommands);
    }

    [Fact]
    public void EchoRoundTrip_KeepsSerializablePayloads()
    {
        DebuggerSerialization.Register();
        PushMarkerCommand marker = new("Group", new TestPayload("Cube", new TestHandle(3, 1), [new TestHandle(4, 2)]));
        RecordedGroup group = new("Camera", 1, 0, EquatableArray<RecordedPass>.Empty);

        PushMarkerCommand loadedMarker = Serializer.Deserialize<PushMarkerCommand>(ReadBinary(WriteBinary(Serializer.Serialize(marker))))!;
        RecordedGroup loadedGroup = Serializer.Deserialize<RecordedGroup>(ReadBinary(WriteBinary(Serializer.Serialize(group))))!;

        TestPayload payload = Assert.IsType<TestPayload>(loadedMarker.Meta);
        Assert.Equal(("Cube", new TestHandle(3, 1)), (payload.Name, payload.Handle));
        Assert.Equal([new TestHandle(4, 2)], payload.Others);
        Assert.Equal(group, loadedGroup);
    }

    [SkippableFact]
    public void Replay_ReproducesAfterPassCopies()
    {
        using GraphicsDevice device = CreateDevice();
        using DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        using RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        DeepRecording deep = Record(device, DeepMode.Full, graph =>
        {
            Passes.Write(graph, "First", target, external, 1);
            Passes.Write(graph, "Second", target, external, 2);
        });
        Replayer replayer = new(device, deep);

        foreach (DeepPass pass in deep.Groups[0].Passes)
        {
            ReplayResult result = replayer.Replay(new ReplayRequest { GroupIndex = -1, PassIndex = pass.Index, EventIndex = 1 });

            Assert.True(result.Status == ReplayStatus.Reexecuted, result.Reason);
            Assert.NotEmpty(result.Outputs);
            foreach (ReplayOutput output in result.Outputs)
            {
                RecordedCopy copy = pass.Copies.Single(c => c.Placement == CopyPlacement.AfterPass && c.Version.Resource == output.Resource);
                Assert.Equal(copy.Regions, output.Regions);
                Assert.Equal(deep.Blobs.Single(b => b.Ref == copy.Blob).Data, output.Data);
            }
        }
    }

    [SkippableFact]
    public void HistoryResources_RecordRingSlotAndValidity()
    {
        using GraphicsDevice device = CreateDevice();
        using FrameGraph graph = new(device, "Test");

        RecordedGraphResource Run()
        {
            Passes.History(graph);
            DeepRecording deep = new(device, DeepMode.ReplayOnly);
            graph.Execute(deep);
            deep.Wait();
            return Assert.Single(deep.Groups[0].Resources);
        }

        RecordedGraphResource first = Run();
        RecordedGraphResource second = Run();

        Assert.Equal(1, first.HistoryDepth);
        Assert.False(first.HistoryValid);
        Assert.True(second.HistoryValid);
        Assert.NotEqual(first.HistorySlot, second.HistorySlot);
    }
}
