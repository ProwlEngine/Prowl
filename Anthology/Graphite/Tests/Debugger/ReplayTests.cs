using System;
using System.IO;
using System.Linq;
using Prowl.Echo;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file readonly struct ReplayView : IRenderView
{
    public uint PixelWidth => 8;
    public uint PixelHeight => 8;
    public int ViewId => 0;
}

file sealed class EventsPass(RenderTexture target, RenderTexture storage, GraphicsProgram program, ComputeProgram compute, Texture source, Sampler sampler) : IPass
{
    private readonly RenderResourceID _target = RenderResourceID.Intern("events_target");
    private readonly RenderResourceID _storage = RenderResourceID.Intern("events_storage");

    public string Name => "Events";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedTexture(_target, target);
        builder.DeclareImportedTexture(_storage, storage, TextureState.Storage);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PropertySet properties = new();
        properties.SetTexture("Tex", source);
        properties.SetSampler("Smp", sampler);
        cmd.SetFramebuffer(target, TargetLoadStoreOps.Clear(new Prowl.Vector.Color(0, 0, 0, 255)));
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(properties);
        cmd.PushDebugGroup("First");
        cmd.Draw(3);
        cmd.PopDebugGroup();
        cmd.ClearColorTarget(0, new Prowl.Vector.Color(0, 0, 255, 255));
        cmd.SetScissor(0, 0, 4, 8);
        cmd.Draw(3);

        PropertySet computeProperties = new();
        computeProperties.SetTexture("ComputeOutput", storage.ColorTextures[0]);
        cmd.SetComputeShader(compute);
        cmd.SetProperties(computeProperties);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class ImportedBufferPass(DeviceBuffer data, DeviceBuffer result, ComputeProgram compute) : IPass
{
    private readonly RenderResourceID _data = RenderResourceID.Intern("imported_data");
    private readonly RenderResourceID _result = RenderResourceID.Intern("imported_result");

    public string Name => "ImportedBuffers";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(_data, data, BufferAccess.ShaderRead);
        builder.DeclareImportedBuffer(_result, result, BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PropertySet properties = new();
        properties.SetBuffer("Input", data);
        properties.SetBuffer("Output", result);
        cmd.SetComputeShader(compute);
        cmd.SetProperties(properties);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class UndeclaredPass(ComputeProgram compute, Texture storage) : IPass
{
    public string Name => "Undeclared";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PropertySet properties = new();
        properties.SetTexture("ComputeOutput", storage);
        cmd.SetComputeShader(compute);
        cmd.SetProperties(properties);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class SamplePass(RenderTexture target, GraphicsProgram program, Texture source, Sampler sampler, string name = "Sample") : IPass
{
    private readonly RenderResourceID _target = RenderResourceID.Intern("sample_target_" + name);

    public string Name => name;

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedTexture(_target, target);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PropertySet properties = new();
        properties.SetTexture("Tex", source);
        properties.SetSampler("Smp", sampler);
        cmd.SetFramebuffer(target, TargetLoadStoreOps.Clear(new Prowl.Vector.Color(0, 0, 0, 255)));
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(properties);
        cmd.Draw(3);
    }
}

file sealed class IndirectPass(RenderTexture target, GraphicsProgram program, Texture source, Sampler sampler, DeviceBuffer arguments) : IPass
{
    private readonly RenderResourceID _target = RenderResourceID.Intern("indirect_target");
    private readonly RenderResourceID _arguments = RenderResourceID.Intern("indirect_arguments");

    public string Name => "Indirect";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedTexture(_target, target);
        builder.DeclareImportedBuffer(_arguments, arguments, BufferAccess.Indirect);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PropertySet properties = new();
        properties.SetTexture("Tex", source);
        properties.SetSampler("Smp", sampler);
        cmd.SetFramebuffer(target, TargetLoadStoreOps.Clear(new Prowl.Vector.Color(0, 0, 0, 255)));
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(properties);
        cmd.DrawIndirect(arguments, 0, 2, 16);
    }
}

file sealed class BlitPass(RenderTexture source, RenderTexture destination, string sourceId) : IPass
{
    private readonly RenderResourceID _destination = RenderResourceID.Intern("blit_destination");

    public string Name => "Blit";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareInputTexture(RenderResourceID.Intern(sourceId), TextureState.TransferSrc);
        builder.DeclareImportedTexture(_destination, destination, TextureState.TransferDst);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
        => cmd.BlitTexture(source.ColorTextures[0], destination.ColorTextures[0], [new TextureBlit(0, 0, 8, 8, 0, 0, 8, 8)]);
}

public class ReplayTests
{
    [SkippableFact]
    public void Replay_BlitIsRecordedAndReexecuted()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = SampleProgram(factory);
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture drawn = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderTexture blitted = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderPipeline pipeline = new(new IPass[] { new SamplePass(drawn, program, source, sampler, "Drawn"), new BlitPass(drawn, blitted, "sample_target_Drawn") });
        DeepRecording deep = Record(device, pipeline, DeepMode.Full);

        DeepPass pass = deep.Views[0].Passes.Single(p => p.Name == "Blit");
        Assert.Contains(pass.Commands, c => c is BlitTextureCommand);
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0 });

        Assert.True(result.Status == ReplayStatus.Reexecuted, result.Reason);
        RecordedCopy copy = pass.Copies.Single(c => c.Placement == CopyPlacement.AfterPass);
        Assert.Equal(deep.Blobs.Single(b => b.Ref == copy.Blob).Data, result.Outputs.Single().Data);
    }

    [SkippableFact]
    public void Replay_IndirectSlicesAndPatchesChangeWhatIsDrawn()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = SampleProgram(factory);
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Repeat((byte)200, 64).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture target = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using DeviceBuffer arguments = factory.CreateBuffer(new BufferDescription(32, BufferUsage.IndirectBuffer));
        device.UpdateBuffer(arguments, 0, new uint[] { 3, 1, 0, 0, 3, 1, 0, 0 });
        using RenderPipeline pipeline = new(new IPass[] { new IndirectPass(target, program, source, sampler, arguments) });
        DeepRecording deep = Record(device, pipeline, DeepMode.Full);
        DeepPass pass = deep.Views[0].Passes[0];
        Replayer replayer = new(device, deep);

        static bool Drawn(ReplayResult result) => result.Outputs.Single().Data.Where((b, i) => i % 4 != 3).Any(b => b != 0);
        ReplayResult whole = replayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0 });
        ReplayResult none = replayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0, Indirect = new IndirectSlice(0, 0) });
        ReplayResult second = replayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0, Indirect = new IndirectSlice(1, 1) });
        TraceResourceId buffer = deep.Resources.Single(r => r.Buffer is { } b && b.SizeInBytes == 32).Id;
        ReplayResult emptied = replayer.Replay(new ReplayRequest
        {
            ViewIndex = 0,
            PassIndex = pass.Index,
            EventIndex = 0,
            Patches = [new BufferPatch(buffer, 0, new byte[32])],
        });

        Assert.True(whole.Status == ReplayStatus.Reexecuted, whole.Reason);
        Assert.True(Drawn(whole));
        Assert.False(Drawn(none));
        Assert.True(Drawn(second));
        Assert.True(emptied.Status == ReplayStatus.Reexecuted, emptied.Reason);
        Assert.False(Drawn(emptied));
    }

    [SkippableFact]
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
        using RenderPipeline pipeline = new(new IPass[] { new SamplePass(a, first, source, sampler, "A"), new SamplePass(b, second, source, sampler, "B") });
        DeepRecording deep = Record(device, pipeline, DeepMode.Full);

        Assert.Equal(2, deep.Programs.Length);
        Assert.Equal(new[] { 1u, 2u }, deep.Programs.Select(p => p.Specializations.Single().Value).Order());
        DeepPass pass = deep.Views[0].Passes.Single(p => p.Name == "B");
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0 });
        Assert.True(result.Status == ReplayStatus.Reexecuted, result.Reason);
    }

    [SkippableFact]
    public void Replay_RebuildsProgramAndPropertiesForADraw()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = RecordSample(device);

        DeepPass pass = deep.Views[0].Passes[0];
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0 });

        Assert.True(result.Status == ReplayStatus.Reexecuted, result.Reason);
        ReplayOutput output = Assert.Single(result.Outputs);
        RecordedCopy copy = pass.Copies.Single(c => c.Placement == CopyPlacement.AfterPass);
        Assert.Equal(deep.Blobs.Single(b => b.Ref == copy.Blob).Data, output.Data);
        Assert.Contains(output.Data.Where((b, i) => i % 4 != 3), b => b != 0);
    }

    [SkippableFact]
    public void Replay_FullWithoutEventRestoresFromCopies()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = RecordSample(device);

        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ViewIndex = 0, PassIndex = deep.Views[0].Passes[0].Index });

        Assert.Equal(ReplayStatus.Restored, result.Status);
        Assert.Single(result.Outputs);
    }

    [SkippableFact]
    public void Replay_UnsupportedFormatReportsReason()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = Edit(RecordSample(device), result => result with
        {
            Resources = result.Resources
                .Select(r => r.Texture is { } texture && (texture.Usage & TextureUsage.RenderTarget) != 0
                    ? r with { Texture = texture with { Format = PixelFormat.BC1_Rgb_UNorm } }
                    : r)
                .ToEquatableArray(),
        });

        ReplayResult result = ReplayFirstEvent(device, deep);

        Assert.Equal(ReplayStatus.NotReplayable, result.Status);
        Assert.Contains("does not support", result.Reason);
    }

    [SkippableFact]
    public void Replay_MissingProgramReportsReason()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = Edit(RecordSample(device), result => result with { Programs = EquatableArray<RecordedProgram>.Empty });

        ReplayResult result = ReplayFirstEvent(device, deep);

        Assert.Equal(ReplayStatus.NotReplayable, result.Status);
        Assert.Contains("missing from the recording", result.Reason);
    }

    [SkippableFact]
    public void Replay_UndeclaredWriteReportsReason()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        using ComputeProgram compute = device.ResourceFactory.CreateComputeProgram(new ComputeDescription(
            Compile("ComputeTextureGenerator.glsl")[0],
            [new ResourceLayoutDescription { Set = 0, Elements = [new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0)] }],
            4, 1, 1));
        using Texture storage = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage));
        using RenderPipeline pipeline = new(new IPass[] { new UndeclaredPass(compute, storage) });
        DeepRecording deep = Record(device, pipeline, DeepMode.Full);

        ReplayResult result = ReplayFirstEvent(device, deep);

        Assert.Equal(ReplayStatus.NotReplayable, result.Status);
        Assert.Contains("Undeclared GPU write", result.Reason);
    }

    [SkippableFact]
    public void Replay_ImportedBuffersAreRebuiltAndTheDispatchReexecuted()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        using ComputeProgram compute = device.ResourceFactory.CreateComputeProgram(new ComputeDescription(
            Compile("BufferAddOne.glsl")[0],
            [new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Input", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute, 0),
                new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1))],
            4, 1, 1));
        using DeviceBuffer data = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        using DeviceBuffer result = device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        device.UpdateBuffer(data, 0, new uint[] { 5, 6, 7, 8 });
        using RenderPipeline pipeline = new(new IPass[] { new ImportedBufferPass(data, result, compute) });
        DeepRecording deep = Record(device, pipeline, DeepMode.Full);

        DeepPass pass = deep.Views[0].Passes[0];
        ReplayResult replay = new Replayer(device, deep).Replay(new ReplayRequest { ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0 });

        Assert.True(replay.Status == ReplayStatus.Reexecuted, replay.Reason);
        ReplayOutput output = Assert.Single(replay.Outputs);
        Assert.Equal(new uint[] { 6, 7, 8, 9 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(output.Data.AsSpan()).ToArray());
    }

    [SkippableFact]
    public void Replay_AfterEchoRoundTripMatchesTheOriginal()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = RecordSample(device);

        DebuggerSerialization.Register();
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            Serializer.Serialize(deep).WriteToBinary(writer);

        stream.Position = 0;
        using BinaryReader reader = new(stream);
        DeepRecording loaded = Serializer.Deserialize<DeepRecording>(EchoObject.ReadFromBinary(reader))!;

        ReplayResult expected = ReplayFirstEvent(device, deep);
        ReplayResult actual = ReplayFirstEvent(device, loaded);
        Assert.True(actual.Status == ReplayStatus.Reexecuted, actual.Reason);
        Assert.Equal(expected.Outputs, actual.Outputs);
    }

    private static ReplayResult ReplayFirstEvent(GraphicsDevice device, DeepRecording deep)
    {
        return new Replayer(device, deep).Replay(new ReplayRequest { ViewIndex = 0, PassIndex = deep.Views[0].Passes[0].Index, EventIndex = 0 });
    }

    private static DeepRecording Edit(DeepRecording deep, Func<DeepResult, DeepResult> edit)
        => new(deep.Mode, deep.Recording, edit(deep.Result));

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
        using RenderPipeline pipeline = new(new IPass[] { new SamplePass(target, program, source, sampler) });
        return Record(device, pipeline, DeepMode.Full);
    }

    [SkippableFact]
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
        using RenderPipeline pipeline = new(new IPass[]
        {
            new SamplePass(first, program, source, sampler, "First"),
            new SamplePass(second, program, source, sampler, "Second"),
        });

        DeepRecording full = Record(device, pipeline, DeepMode.Full);
        DeepRecording only = Record(device, pipeline, DeepMode.ReplayOnly);

        Replayer fullReplayer = new(device, full);
        Replayer onlyReplayer = new(device, only);
        DeepView fullView = full.Views[0];
        DeepView onlyView = only.Views[0];
        Assert.Equal(2, onlyView.Passes.Length);
        for (int i = 0; i < fullView.Passes.Length; i++)
        {
            ReplayResult expected = fullReplayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = fullView.Passes[i].Index });
            ReplayResult actual = onlyReplayer.Replay(new ReplayRequest { ViewIndex = 0, PassIndex = onlyView.Passes[i].Index });
            Assert.True(actual.Status == ReplayStatus.Reexecuted, actual.Reason);
            Assert.Equal(expected.Outputs.Single().Data, actual.Outputs.Single().Data);
        }
    }

    [SkippableFact]
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
        using RenderPipeline pipeline = new(new IPass[] { new EventsPass(target, storage, program, compute, source, sampler) });

        DeepRecording deep = Record(device, pipeline, DeepMode.Full);
        DeepPass pass = deep.Views[0].Passes[0];
        Assert.Contains(pass.Commands, c => c is PushMarkerCommand { Name: "First" });
        Replayer replayer = new(device, deep);
        ReplayRequest Request(int? last) => new() { ViewIndex = 0, PassIndex = pass.Index, EventIndex = last };

        ReplayResult whole = replayer.Replay(Request(null));
        ReplayResult last = replayer.Replay(Request(3));
        ReplayResult first = replayer.Replay(Request(0));

        Assert.True(last.Status == ReplayStatus.Reexecuted, last.Reason);
        Assert.Equal(2, last.Outputs.Length);
        Assert.Equal(whole.Outputs.Select(o => o.Data), last.Outputs.Select(o => o.Data));
        Assert.NotEqual(whole.Outputs[0].Data, first.Outputs[0].Data);
    }

    private static DeepRecording Record(GraphicsDevice device, RenderPipeline pipeline, DeepMode mode)
    {
        DeepRecording deep = new(device, mode);
        device.DispatchGraph(pipeline, new ReplayView[] { new() }, deep);
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
