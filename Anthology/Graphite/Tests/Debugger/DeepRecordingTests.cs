using System.IO;
using System.Linq;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file readonly struct DeepView : IRenderView
{
    public uint PixelWidth => 8;
    public uint PixelHeight => 8;
    public int ViewId => 0;
}

file sealed class WritePass : IPass
{
    private readonly string _name;
    private readonly RenderResourceID _target;
    private readonly RenderTexture _texture;
    private readonly DeviceBuffer _external;
    private readonly byte _value;

    public WritePass(string name, RenderTexture texture, DeviceBuffer external, byte value)
    {
        _name = name;
        _target = RenderResourceID.Intern("deep_target");
        _texture = texture;
        _external = external;
        _value = value;
    }

    public string Name => _name;

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedTexture(_target, _texture);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.ClearColorTarget(0, new Color((byte)(_value * 50), 0, 0, 255));
        cmd.UpdateBuffer(_external, 0, new byte[] { _value, _value, _value, _value });
    }
}

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

    private static DeepRecording Record(GraphicsDevice device, DeepMode mode, params IPass[] passes)
    {
        using RenderPipeline pipeline = new(passes);
        DeepRecording deep = new(device, mode);
        device.DispatchGraph(pipeline, new DeepView[] { new() }, deep);
        deep.Wait();
        return deep;
    }

    [SkippableFact]
    public void Pass_RecordsCommandsReferencesAndUpdateBlobs()
    {
        using GraphicsDevice device = CreateDevice();
        DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));

        DeepRecording deep = Record(device, DeepMode.Full, new WritePass("Only", target, external, 5));

        DeepPass pass = Assert.Single(Assert.Single(deep.Views).Passes);
        Assert.Null(pass.NotReplayable);
        Assert.Equal(2, pass.References.Length);
        UpdateBufferCommand update = Assert.Single(pass.Commands.OfType<UpdateBufferCommand>());
        Assert.Equal(update.After.Version, pass.References.Single(r => r.Resource == update.After.Resource).Last);
        Assert.Contains(deep.Blobs, b => b.Ref == update.Data && b.Data.SequenceEqual(new byte[] { 5, 5, 5, 5 }));
        Assert.Contains(pass.Commands, c => c is SetFramebufferCommand);
        Assert.Contains(pass.Commands, c => c is ClearColorTargetCommand);
        Assert.Single(deep.Recording.Views);
    }

    [SkippableTheory]
    [InlineData(DeepMode.Full)]
    [InlineData(DeepMode.ReplayOnly)]
    public void Copies_AreTakenOncePerVersion(DeepMode mode)
    {
        using GraphicsDevice device = CreateDevice();
        DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));

        DeepRecording deep = Record(device, mode, new WritePass("First", target, external, 1), new WritePass("Second", target, external, 2));

        DeepPass[] passes = deep.Views[0].Passes.ToArray();
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
        DeepRecording deep = Record(device, DeepMode.Full, new WritePass("First", target, external, 1), new WritePass("Second", target, external, 2));

        DebuggerSerialization.Register();
        byte[] bytes = WriteBinary(Serializer.Serialize(deep));
        DeepRecording loaded = Serializer.Deserialize<DeepRecording>(ReadBinary(bytes))!;

        Assert.True(loaded.IsDone);
        Assert.Equal(deep.Mode, loaded.Mode);
        Assert.Equal(deep.Backend, loaded.Backend);
        Assert.Equal(deep.Features, loaded.Features);
        Assert.Equal(deep.ExecutionId, loaded.ExecutionId);
        Assert.Equal(nameof(RenderPipeline), loaded.Recording.GraphName);
        Assert.Equal(deep.Recording.Views, loaded.Recording.Views);
        Assert.Equal(deep.Recording.CommandBuffers, loaded.Recording.CommandBuffers);
        Assert.Equal(deep.Resources, loaded.Resources);
        Assert.Equal(deep.Programs, loaded.Programs);
        Assert.Equal(deep.Samplers, loaded.Samplers);
        Assert.Equal(deep.Blobs, loaded.Blobs);
        Assert.Equal(deep.Views, loaded.Views);
    }

    [Fact]
    public void EchoRoundTrip_KeepsProgramsAndCommands()
    {
        DebuggerSerialization.Register();
        RecordedProgram program = new(
            ProgramKey.FromBytes(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            false,
            EquatableArray.Create(new RecordedStage(ShaderStages.Vertex, "main", new BlobRef(EquatableArray.Create<byte>(1, 2), 2))),
            EquatableArray.Create(new ResourceLayoutDescription(new ResourceLayoutElementDescription { Name = "Albedo", BindingIndex = 3 })),
            null,
            null,
            null,
            EquatableArray.Create(new VertexLayoutDescription(0, 12, new VertexElementDescription("Position", VertexElementFormat.Float3))),
            1,
            1,
            1);
        EquatableArray<RecordedCommand> commands = EquatableArray.Create<RecordedCommand>(
            new SetPropertiesCommand(
                EquatableArray.Create(new RecordedProperty("Tint", PropertyKind.Uniform, UniformScalarType.Float1, EquatableArray.Create<byte>(0, 0, 128, 63), default, default, null, -1)),
                EquatableArray.Create("Old")),
            new DrawCommand(3, 1, 0, 0));

        EchoObject programData = ReadBinary(WriteBinary(Serializer.Serialize(program)));
        RecordedProgram loadedProgram = Serializer.Deserialize<RecordedProgram>(programData)!;
        EquatableArray<RecordedCommand> loadedCommands = Serializer.Deserialize<EquatableArray<RecordedCommand>>(ReadBinary(WriteBinary(Serializer.Serialize(commands))));

        Assert.Equal(program.Key, loadedProgram.Key);
        Assert.Equal("Albedo", PropertyID.ToString(loadedProgram.Layouts[0].Elements[0].Name));
        Assert.Equal("Position", VertexAttributeID.ToString(loadedProgram.VertexLayouts[0].Elements[0].Name));
        Assert.Equal(program, loadedProgram);
        Assert.Equal(commands, loadedCommands);
    }

    [SkippableFact]
    public void Replay_ReproducesAfterPassCopies()
    {
        using GraphicsDevice device = CreateDevice();
        using DeviceBuffer external = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        using RenderTexture target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        DeepRecording deep = Record(device, DeepMode.Full, new WritePass("First", target, external, 1), new WritePass("Second", target, external, 2));
        Replayer replayer = new(device, deep);

        foreach (DeepPass pass in deep.Views[0].Passes)
        {
            ReplayResult result = replayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 1 });

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
}
