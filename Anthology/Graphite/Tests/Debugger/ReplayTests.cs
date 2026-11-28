using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file static class Passes
{
    private static readonly Prowl.Vector.Color Black = Prowl.Vector.Color.FromBytes(0, 0, 0, 255);

    public static void Events(FrameGraph graph, RenderTexture target, RenderTexture storage, GraphicsProgram program, ComputeProgram compute, Texture source, Sampler sampler)
    {
        TextureHandle targetHandle = graph.Import(target);
        TextureHandle storageHandle = graph.Import(storage);
        graph.AddPass("Events", pass =>
        {
            pass.Write(targetHandle);
            pass.Write(storageHandle, TextureState.Storage);
        }, context =>
        {
            CommandBuffer cmd = context.Command;
            PropertySet properties = new();
            properties.SetTexture("Tex", source);
            properties.SetSampler("Smp", sampler);
            cmd.SetFramebuffer(target, TargetLoadStoreOps.Clear(Black));
            cmd.SetShader(program);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(properties);
            cmd.PushDebugGroup("First");
            cmd.Draw(3);
            cmd.PopDebugGroup();
            cmd.ClearColorTarget(0, Prowl.Vector.Color.FromBytes(0, 0, 255, 255));
            cmd.SetScissor(0, 0, 4, 8);
            cmd.Draw(3);

            PropertySet computeProperties = new();
            computeProperties.SetTexture("ComputeOutput", storage.ColorTextures[0]);
            cmd.SetComputeShader(compute);
            cmd.SetProperties(computeProperties);
            cmd.Dispatch(1, 1, 1);
        });
    }

    public static void Sample(FrameGraph graph, RenderTexture target, GraphicsProgram program, Texture source, Sampler sampler, string name = "Sample")
    {
        TextureHandle handle = graph.Import(target);
        graph.AddPass(name, pass => pass.Write(handle), context =>
        {
            CommandBuffer cmd = context.Command;
            PropertySet properties = new();
            properties.SetTexture("Tex", source);
            properties.SetSampler("Smp", sampler);
            cmd.SetFramebuffer(target, TargetLoadStoreOps.Clear(Black));
            cmd.SetShader(program);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(properties);
            cmd.Draw(3);
        });
    }
}

[System.Serializable]
public readonly record struct TestHandle(int Index, int Generation);

[System.Serializable]
public sealed record TestPayload(string Name, TestHandle Handle, TestHandle[] Others);

[Collection("GPU Tests")]
public class ReplayTests
{
    [Fact]
    public void Record_ProgramsDifferingOnlyInSpecializationsStayDistinct()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram first = SampleProgram(factory, [new SpecializationConstant(0, 1)]);
        using GraphicsProgram second = SampleProgram(factory, [new SpecializationConstant(0, 2)]);
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture a = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderTexture b = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        DeepRecording deep = Record(device, DeepMode.Full, graph =>
        {
            Passes.Sample(graph, a, first, source, sampler, "A");
            Passes.Sample(graph, b, second, source, sampler, "B");
        });

        Assert.Equal(2, deep.Programs.Length);
        Assert.Equal(new[] { 1u, 2u }, deep.Programs.Select(p => p.Specializations.Single().Value).Order());
        DeepPass pass = deep.Groups[0].Passes.Single(p => p.Name == "B");
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { GroupIndex = -1, PassIndex = pass.Index, EventIndex = 0 });
        Assert.True(result.Status == ReplayStatus.Reexecuted, result.Reason);
    }

    [Fact]
    public void Replay_AfterEchoRoundTripMatchesTheOriginal()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = RecordSample(device);

        string path = Path.GetTempFileName();
        DeepRecording loaded;
        try
        {
            DebuggerSerialization.Save(deep, path);
            loaded = DebuggerSerialization.Load(path);
        }
        finally
        {
            File.Delete(path);
        }

        ReplayResult expected = ReplayFirstEvent(device, deep);
        ReplayResult actual = ReplayFirstEvent(device, loaded);
        Assert.True(actual.Status == ReplayStatus.Reexecuted, actual.Reason);
        Assert.Equal(expected.Outputs, actual.Outputs);
    }

    [Fact]
    public void EchoRoundTrip_KeepsProgramsAndCommands()
    {
        DebuggerSerialization.Register();
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = RecordSample(device);
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
        PushMarkerCommand marker = new("Group", new TestPayload("Cube", new TestHandle(3, 1), [new TestHandle(4, 2)]));
        RecordedGroup group = new("Camera", 1, 0, EquatableArray<RecordedPass>.Empty);

        DeepRecording loaded = RoundTrip(deep);
        RecordedProgram loadedProgram = RoundTrip(program);
        EquatableArray<RecordedCommand> loadedCommands = RoundTrip(commands);
        PushMarkerCommand loadedMarker = RoundTrip(marker);
        RecordedGroup loadedGroup = RoundTrip(group);

        Assert.True(loaded.IsDone);
        Assert.Equal(deep.Mode, loaded.Mode);
        Assert.Equal(deep.Backend, loaded.Backend);
        Assert.Equal(deep.Features, loaded.Features);
        Assert.Equal(deep.ExecutionId, loaded.ExecutionId);
        Assert.Equal(deep.Recording.Groups, loaded.Recording.Groups);
        Assert.Equal(deep.Recording.CommandBuffers, loaded.Recording.CommandBuffers);
        Assert.Equal(deep.Recording.Counters, loaded.Recording.Counters);
        Assert.Equal(deep.Resources, loaded.Resources);
        Assert.Equal(deep.Programs, loaded.Programs);
        Assert.Equal(deep.Samplers, loaded.Samplers);
        Assert.Equal(deep.Blobs, loaded.Blobs);
        Assert.Equal(deep.Groups, loaded.Groups);
        Assert.Equal(program.Key, loadedProgram.Key);
        Assert.Equal("Albedo", PropertyID.ToString(loadedProgram.Layouts[0].Elements[0].Name));
        Assert.Equal("Position", VertexAttributeID.ToString(loadedProgram.VertexLayouts[0].Elements[0].Name));
        Assert.Equal(program, loadedProgram);
        Assert.Equal(commands, loadedCommands);
        TestPayload payload = Assert.IsType<TestPayload>(loadedMarker.Meta);
        Assert.Equal(("Cube", new TestHandle(3, 1)), (payload.Name, payload.Handle));
        Assert.Equal([new TestHandle(4, 2)], payload.Others);
        Assert.Equal(group, loadedGroup);
    }

    private static T RoundTrip<T>(T value)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            Serializer.Serialize(value).WriteToBinary(writer);
        stream.Position = 0;
        using BinaryReader reader = new(stream);
        return Serializer.Deserialize<T>(EchoObject.ReadFromBinary(reader))!;
    }

    private static ReplayResult ReplayFirstEvent(GraphicsDevice device, DeepRecording deep)
    {
        return new Replayer(device, deep).Replay(new ReplayRequest { GroupIndex = -1, PassIndex = deep.Groups[0].Passes[0].Index, EventIndex = 0 });
    }

    private static GraphicsProgram SampleProgram(ResourceFactory factory, SpecializationConstant[]? specializations = null)
        => factory.CreateGraphicsProgram(new ShaderDescription(Compile("FullScreenTriSampleTexture2D.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0),
                    new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment, 1)),
            ],
            Specializations = specializations,
        });

    private static DeepRecording RecordSample(GraphicsDevice device)
    {
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = SampleProgram(factory);
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture target = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        return Record(device, DeepMode.Full, graph => Passes.Sample(graph, target, program, source, sampler));
    }

    [Fact]
    public void ReplayOnly_ReexecutedPassesMatchFullCopies()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile("FullScreenTriSampleTexture2D.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0),
                    new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment, 1)),
            ],
        });
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture first = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderTexture second = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        void Add(FrameGraph graph)
        {
            Passes.Sample(graph, first, program, source, sampler, "First");
            Passes.Sample(graph, second, program, source, sampler, "Second");
        }

        DeepRecording full = Record(device, DeepMode.Full, Add);
        DeepRecording only = Record(device, DeepMode.ReplayOnly, Add);

        Replayer fullReplayer = new(device, full);
        Replayer onlyReplayer = new(device, only);
        DeepGroup fullView = full.Groups[0];
        DeepGroup onlyView = only.Groups[0];
        Assert.Equal(2, onlyView.Passes.Length);
        for (int i = 0; i < fullView.Passes.Length; i++)
        {
            ReplayResult expected = fullReplayer.Replay(new ReplayRequest { GroupIndex = -1, PassIndex = fullView.Passes[i].Index });
            ReplayResult actual = onlyReplayer.Replay(new ReplayRequest { GroupIndex = -1, PassIndex = onlyView.Passes[i].Index });
            Assert.True(actual.Status == ReplayStatus.Reexecuted, actual.Reason);
            Assert.Equal(expected.Outputs.Single().Data, actual.Outputs.Single().Data);
        }
    }

    [Fact]
    public void EventReplay_UpToLastEventEqualsPassOutput()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile("FullScreenTriSampleTexture2D.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts =
            [
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0),
                    new ResourceLayoutElementDescription("Smp", ResourceKind.Sampler, ShaderStages.Fragment, 1)),
            ],
        });
        using ComputeProgram compute = factory.CreateComputeProgram(new ComputeDescription(
            Compile("ComputeTextureGenerator.glsl")[0],
            [new ResourceLayoutDescription { Set = 0, Elements = [new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0)] }],
            4, 1, 1));
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture target = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderTexture storage = factory.CreateRenderTexture(new RenderTextureDescription(4, 1, new[] { PixelFormat.R32_G32_B32_A32_Float }, depth: false, storage: true));
        DeepRecording deep = Record(device, DeepMode.Full, graph => Passes.Events(graph, target, storage, program, compute, source, sampler));
        DeepPass pass = deep.Groups[0].Passes[0];
        Assert.Contains(pass.Commands, c => c is PushMarkerCommand { Name: "First" });
        Replayer replayer = new(device, deep);
        ReplayRequest Request(int? last) => new() { GroupIndex = -1, PassIndex = pass.Index, EventIndex = last };

        ReplayResult whole = replayer.Replay(Request(null));
        ReplayResult last = replayer.Replay(Request(3));
        ReplayResult first = replayer.Replay(Request(0));
        ReplayResult none = replayer.Replay(Request(-1));

        Assert.True(none.Status == ReplayStatus.Reexecuted, none.Reason);
        Assert.NotEqual(first.Outputs[0].Data, none.Outputs[0].Data);
        Assert.True(last.Status == ReplayStatus.Reexecuted, last.Reason);
        Assert.Equal(2, last.Outputs.Length);
        Assert.Equal(whole.Outputs.Select(o => o.Data), last.Outputs.Select(o => o.Data));
        Assert.NotEqual(whole.Outputs[0].Data, first.Outputs[0].Data);
    }

    private static DeepRecording Record(GraphicsDevice device, DeepMode mode, Action<FrameGraph> add)
    {
        using FrameGraph graph = new(device, "Test");
        add(graph);
        DeepRecording deep = new(device, mode);
        graph.Execute(deep);
        deep.Wait();
        return deep;
    }

    private static ShaderStageDescription[] Compile(string file)
    {
        using GlslShaderCompiler compiler = new();
        ShaderPass shader = new() { State = new PassState(), Source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shaders", file)), SourceName = file };
        return compiler.Compile(shader, [], GraphicsBackend.Vulkan).Stages;
    }
}
