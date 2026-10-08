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
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile())
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

        Recorder recorder = new(device);
        using RenderPipeline pipeline = new(new IPass[] { new SamplePass(target, program, source, sampler) });
        recorder.BeginDeepRecording(DeepMode.Full);
        device.DispatchGraph(pipeline, new ReplayView[] { new() });
        DeepRecording deep = recorder.EndDeepRecording();
        deep.Wait();

        DeepExecution execution = deep.Executions.Single();
        DeepPass pass = execution.Views[0].Passes[0];
        ReplayResult result = new Replayer(device, deep).Replay(new ReplayRequest { ExecutionId = execution.ExecutionId, ViewIndex = 0, PassIndex = pass.Index });

        Assert.True(result.Status == ReplayStatus.Reexecuted, result.Reason);
        ReplayOutput output = Assert.Single(result.Outputs);
        RecordedCopy copy = pass.Copies.Single(c => c.Placement == CopyPlacement.AfterPass);
        Assert.Equal(deep.Blobs.Single(b => b.Ref == copy.Blob).Data, output.Data);
        Assert.Contains(output.Data.Where((b, i) => i % 4 != 3), b => b != 0);
    }

    [SkippableFact]
    public void ReplayOnly_ReexecutedPassesMatchFullCopies()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(Compile())
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

    private static DeepRecording Record(GraphicsDevice device, Recorder recorder, RenderPipeline pipeline, DeepMode mode)
    {
        recorder.BeginDeepRecording(mode);
        device.DispatchGraph(pipeline, new ReplayView[] { new() });
        DeepRecording deep = recorder.EndDeepRecording();
        deep.Wait();
        return deep;
    }

    private static ShaderStageDescription[] Compile()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Shaders");
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
        compiler.BeginSession([new DirectoryInfo(directory)], path => File.ReadAllBytes(Path.IsPathRooted(path) ? path : Path.Combine(directory, path)));
        ShaderPass shader = new() { State = new PassState(), InlineSlang = File.ReadAllText(Path.Combine(directory, "FullScreenTriSampleTexture2D.slang")) };
        ShaderDescription description = compiler.Compile(shader, [], GraphicsBackend.Vulkan);
        compiler.EndSession();
        return description.Stages;
    }
}
