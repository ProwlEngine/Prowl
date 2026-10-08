using System.Linq;
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
        cmd.ClearColorTarget(0, new Color(255, 0, 0, 255));
        cmd.UpdateBuffer(_external, 0, new byte[] { _value, _value, _value, _value });
    }
}

public class DeepRecordingTests
{
    private static GraphicsDevice CreateDevice() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));

    private static DeepRecording Record(GraphicsDevice device, DeepMode mode, params IPass[] passes)
    {
        Recorder recorder = new(device);
        using RenderPipeline pipeline = new(passes);
        recorder.BeginDeepRecording(mode);
        device.DispatchGraph(pipeline, new DeepView[] { new() });
        DeepRecording deep = recorder.EndDeepRecording();
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

        DeepPass pass = Assert.Single(Assert.Single(Assert.Single(deep.Executions).Views).Passes);
        Assert.Null(pass.NotReplayable);
        Assert.Equal(2, pass.References.Length);
        UpdateBufferCommand update = Assert.Single(pass.Commands.OfType<UpdateBufferCommand>());
        Assert.Equal(update.After.Version, pass.References.Single(r => r.Resource == update.After.Resource).Last);
        Assert.Contains(deep.Blobs, b => b.Ref == update.Data && b.Data.SequenceEqual(new byte[] { 5, 5, 5, 5 }));
        Assert.Contains(pass.Commands, c => c is SetFramebufferCommand);
        Assert.Contains(pass.Commands, c => c is ClearColorTargetCommand);
        Assert.Single(deep.Recording.Executions);
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

        DeepPass[] passes = deep.Executions.Single().Views[0].Passes.ToArray();
        RecordedCopy[] copies = passes.SelectMany(p => p.Copies).ToArray();
        Assert.Equal(copies.Length, copies.Select(c => c.Version).Distinct().Count());
        Assert.DoesNotContain(passes[1].Copies, c => c.Placement == CopyPlacement.BeforePass);
        Assert.Contains(passes[0].Copies, c => c.Placement == CopyPlacement.BeforePass && c.Version.Version == 0);
        Assert.Equal(mode == DeepMode.Full, copies.Any(c => c.Placement == CopyPlacement.AfterPass));
        Assert.All(copies, c => Assert.Contains(deep.Blobs, b => b.Ref == c.Blob));
    }
}
