using System;
using System.IO;
using System.Linq;
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
        cmd.Draw(3);
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

public class ReplayTests
{
    [SkippableFact]
    public void Replay_RebuildsProgramAndPropertiesForADraw()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording deep = RecordSample(device);

        DeepExecution execution = deep.Executions.Single();
        DeepPass pass = execution.Views[0].Passes[0];
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ExecutionId = execution.ExecutionId, ViewIndex = 0, PassIndex = pass.Index, EventIndex = 0 });

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

        DeepExecution execution = deep.Executions.Single();
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ExecutionId = execution.ExecutionId, ViewIndex = 0, PassIndex = execution.Views[0].Passes[0].Index });

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
            Compile("ComputeTextureGenerator.slang")[0],
            [new ResourceLayoutDescription { Set = 0, Elements = [new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0)] }],
            4, 1, 1));
        using Texture storage = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage));
        using RenderPipeline pipeline = new(new IPass[] { new UndeclaredPass(compute, storage) });
        DeepRecording deep = Record(device, new Recorder(device), pipeline, DeepMode.Full);

        ReplayResult result = ReplayFirstEvent(device, deep);

        Assert.Equal(ReplayStatus.NotReplayable, result.Status);
        Assert.Contains("Undeclared GPU write", result.Reason);
    }

    private static ReplayResult ReplayFirstEvent(GraphicsDevice device, DeepRecording deep)
    {
        DeepExecution execution = deep.Executions.Single();
        return new Replayer(device, deep).Replay(new ReplayRequest { ExecutionId = execution.ExecutionId, ViewIndex = 0, PassIndex = execution.Views[0].Passes[0].Index, EventIndex = 0 });
    }

    private static DeepRecording Edit(DeepRecording deep, Func<DeepResult, DeepResult> edit)
        => new(deep.Mode, deep.Backend, deep.Features, deep.Recording, edit(deep.Result));

    private static DeepRecording RecordSample(GraphicsDevice device)
    {
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile("FullScreenTriSampleTexture2D.slang"))
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
        using RenderTexture target = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderPipeline pipeline = new(new IPass[] { new SamplePass(target, program, source, sampler) });
        return Record(device, new Recorder(device), pipeline, DeepMode.Full);
    }

    [SkippableFact]
    public void ReplayOnly_ReexecutedPassesMatchFullCopies()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile("FullScreenTriSampleTexture2D.slang"))
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

        Recorder recorder = new(device);
        DeepRecording full = Record(device, recorder, pipeline, DeepMode.Full);
        DeepRecording only = Record(device, recorder, pipeline, DeepMode.ReplayOnly);

        Replayer fullReplayer = new(device, full);
        Replayer onlyReplayer = new(device, only);
        DeepView fullView = full.Executions.Single().Views[0];
        DeepView onlyView = only.Executions.Single().Views[0];
        Assert.Equal(2, onlyView.Passes.Length);
        for (int i = 0; i < fullView.Passes.Length; i++)
        {
            ReplayResult expected = fullReplayer.Replay(new ReplayRequest { ExecutionId = full.Executions.Single().ExecutionId, ViewIndex = 0, PassIndex = fullView.Passes[i].Index });
            ReplayResult actual = onlyReplayer.Replay(new ReplayRequest { ExecutionId = only.Executions.Single().ExecutionId, ViewIndex = 0, PassIndex = onlyView.Passes[i].Index });
            Assert.True(actual.Status == ReplayStatus.Reexecuted, actual.Reason);
            Assert.Equal(expected.Outputs.Single().Data, actual.Outputs.Single().Data);
        }
    }

    [SkippableFact]
    public void EventReplay_UpToLastEventEqualsPassOutput()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile("FullScreenTriSampleTexture2D.slang"))
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
            Compile("ComputeTextureGenerator.slang")[0],
            [new ResourceLayoutDescription { Set = 0, Elements = [new ResourceLayoutElementDescription("ComputeOutput", ResourceKind.TextureReadWrite, ShaderStages.Compute, 0)] }],
            4, 1, 1));
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(source, Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray());
        using Sampler sampler = factory.CreateSampler(SamplerDescription.Linear);
        using RenderTexture target = factory.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderTexture storage = factory.CreateRenderTexture(new RenderTextureDescription(4, 1, new[] { PixelFormat.R32_G32_B32_A32_Float }, depth: false, storage: true));
        using RenderPipeline pipeline = new(new IPass[] { new EventsPass(target, storage, program, compute, source, sampler) });

        DeepRecording deep = Record(device, new Recorder(device), pipeline, DeepMode.Full);
        DeepExecution execution = deep.Executions.Single();
        DeepPass pass = execution.Views[0].Passes[0];
        Replayer replayer = new(device, deep);
        ReplayRequest Request(int? last) => new() { ExecutionId = execution.ExecutionId, ViewIndex = 0, PassIndex = pass.Index, EventIndex = last };

        ReplayResult whole = replayer.Replay(Request(null));
        ReplayResult last = replayer.Replay(Request(3));
        ReplayResult first = replayer.Replay(Request(0));

        Assert.True(last.Status == ReplayStatus.Reexecuted, last.Reason);
        Assert.Equal(2, last.Outputs.Length);
        Assert.Equal(whole.Outputs.Select(o => o.Data), last.Outputs.Select(o => o.Data));
        Assert.NotEqual(whole.Outputs[0].Data, first.Outputs[0].Data);
    }

    private static DeepRecording Record(GraphicsDevice device, Recorder recorder, RenderPipeline pipeline, DeepMode mode)
    {
        recorder.BeginDeepRecording(mode);
        device.DispatchGraph(pipeline, new ReplayView[] { new() });
        DeepRecording deep = recorder.EndDeepRecording();
        deep.Wait();
        return deep;
    }

    private static ShaderStageDescription[] Compile(string file)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Shaders");
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
        compiler.BeginSession([new DirectoryInfo(directory)], path => File.ReadAllBytes(Path.IsPathRooted(path) ? path : Path.Combine(directory, path)));
        ShaderPass shader = new() { State = new PassState(), InlineSlang = File.ReadAllText(Path.Combine(directory, file)) };
        ShaderDescription description = compiler.Compile(shader, [], GraphicsBackend.Vulkan);
        compiler.EndSession();
        return description.Stages;
    }
}
