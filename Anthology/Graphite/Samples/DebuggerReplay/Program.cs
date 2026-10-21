using System;
using System.IO;
using System.Linq;
using Prowl.Echo;
using Prowl.Graphite.Debugger;
using Prowl.Graphite.Debugger.Serialization;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

namespace Prowl.Graphite.Samples.DebuggerReplay;

internal sealed class TexturedPass(RenderTexture target, GraphicsProgram program, Texture source, Sampler sampler) : IInstancePass
{
    public static readonly GraphKind Offscreen = GraphKind.Intern("Sample.Offscreen");

    private readonly RenderResourceID _target = RenderResourceID.Intern("textured_target");

    public string Name => "Textured";

    public GraphKind Kind => Offscreen;

    public void Setup(InstanceContextBuilder builder) => builder.DeclareImportedTexture(_target, target);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        PropertySet properties = new();
        properties.SetTexture("Tex", source);
        properties.SetSampler("Smp", sampler);
        cmd.SetFramebuffer(target, TargetLoadStoreOps.Clear(new Color(0.1f, 0.12f, 0.16f, 1f)));
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(properties);
        cmd.Draw(3);
    }
}

public static class Program
{
    private static int Main()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
        DeepRecording recording = Record(device);
        Console.WriteLine($"Recorded execution {recording.ExecutionId} with {recording.Blobs.Length} blobs.");

        DeepRecording loaded = RoundTrip(recording, out int size);
        Console.WriteLine($"Round trip through Echo: {size} bytes.");

        DeepInstance instance = loaded.Instances.Single(i => i.Key == new GraphInstanceKey(TexturedPass.Offscreen, 0));
        DeepPass pass = instance.Passes[0];
        Replayer replayer = new(device, loaded);
        ReplayResult result = replayer.Replay(new ReplayRequest { InstanceIndex = instance.Index, PassIndex = pass.Index, EventIndex = 0 });
        Console.WriteLine($"Replay of the draw in pass {pass.Name}: {result.Status}. {result.Reason}");
        if (result.Status == ReplayStatus.NotReplayable)
            return 1;

        RecordedCopy copy = pass.Copies.Single(c => c.Placement == Prowl.Graphite.Debugging.CopyPlacement.AfterPass);
        bool match = loaded.Blobs.Single(b => b.Ref == copy.Blob).Data.Equals(result.Outputs.Single().Data);
        Console.WriteLine(match ? "The replayed output matches the recorded output." : "The replayed output differs from the recorded output.");
        return match ? 0 : 1;
    }

    private static DeepRecording Record(GraphicsDevice device)
    {
        ResourceFactory factory = device.ResourceFactory;
        using GraphicsProgram program = factory.CreateGraphicsProgram(new ShaderDescription(CompileShader())
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
        using RenderTexture target = factory.CreateRenderTexture(new RenderTextureDescription(64, 64, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        using RenderGraph graph = RenderGraph.Build([new TexturedPass(target, program, source, sampler)], [new GraphScope(TexturedPass.Offscreen, GraphKind.Root)], "Replay sample");
        GraphInstances instances = graph.CreateInstances();
        instances.Add(TexturedPass.Offscreen, 0, extent: new GraphExtent(64, 64), name: "Offscreen");

        DeepRecording recording = new(device, DeepMode.Full);
        device.DispatchGraph(graph, instances, recording);
        recording.Wait();
        return recording;
    }

    private static DeepRecording RoundTrip(DeepRecording recording, out int size)
    {
        DebuggerSerialization.Register();
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            Serializer.Serialize(recording).WriteToBinary(writer);

        size = (int)stream.Length;
        stream.Position = 0;
        using BinaryReader reader = new(stream);
        return Serializer.Deserialize<DeepRecording>(EchoObject.ReadFromBinary(reader))!;
    }

    private static ShaderStageDescription[] CompileShader()
    {
        using GlslShaderCompiler compiler = new();
        ShaderPass shader = new() { State = new PassState(), Source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shader.glsl")), SourceName = "Shader.glsl" };
        return compiler.Compile(shader, [], GraphicsBackend.Vulkan).Stages;
    }
}
