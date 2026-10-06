using System.Linq;
using Prowl.Graphite.Debugger.Recording;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

public class FrameRecorderTests
{
    private static PassInfo Pass(string name, int index, ulong execution, int view = 0)
        => new(name, index, view, execution, default, default);

    private static void RecordFrame(FrameRecorder recorder, ulong execution, double milliseconds)
    {
        ViewInfo view = new("Main", 0, 64, 32, execution);
        recorder.BeginView(view);
        PassInfo first = Pass("A", 0, execution);
        PassInfo second = Pass("B", 1, execution);
        recorder.BeginPass(first);
        recorder.EndPass(first, new PassStats(1, 0, 0, 1, 1, 0, 0));
        recorder.BeginPass(second);
        recorder.EndPass(second, new PassStats(0, 0, 2, 1, 1, 0, 0));
        recorder.EndView(view);
        recorder.RecordExecutionTime(new CommandBufferInfo(1, "A", execution, first), false, milliseconds);
        recorder.RecordExecutionTime(new CommandBufferInfo(2, "B", execution, second), false, milliseconds * 2);
        recorder.RecordGpuVertexStats(new CommandBufferInfo(1, "A", execution, first), new GpuVertexStats(3, 1, 3, 1, 100));
        recorder.RecordExecutionResolved(execution);
    }

    [Fact]
    public void Frame_EntersRingOnlyWhenResolved()
    {
        FrameRecorder recorder = new();
        ViewInfo view = new("Main", 0, 64, 32, 5);
        recorder.BeginView(view);
        recorder.BeginPass(Pass("A", 0, 5));
        Assert.Equal(0, recorder.Count);
        Assert.Null(recorder.Latest);

        recorder.RecordExecutionResolved(5);
        Assert.Equal(1, recorder.Count);
        Assert.Equal(5ul, recorder.Latest!.ExecutionId);
    }

    [Fact]
    public void Frame_KeepsViewsPassesStatsAndTimings()
    {
        FrameRecorder recorder = new();
        RecordFrame(recorder, 9, 1.5);

        LightFrame frame = recorder.Latest!;
        LightView view = Assert.Single(frame.Views);
        Assert.Equal("Main", view.Name);
        Assert.Equal(64u, view.PixelWidth);
        Assert.Equal(new[] { "A", "B" }, view.Passes.Select(p => p.Name));
        Assert.Equal(1u, view.Passes[0].Stats.Draws);
        Assert.Equal(2u, view.Passes[1].Stats.Dispatches);
        Assert.Equal(1.5, frame.PassGpuMilliseconds(0, 0));
        Assert.Equal(3.0, frame.PassGpuMilliseconds(0, 1));
        Assert.Equal(4.5, frame.TotalGpuMilliseconds);
        Assert.Equal(100ul, frame.CommandBuffers[0].VertexStats!.Value.FragmentShaderInvocations);
        Assert.Null(frame.CommandBuffers[1].VertexStats);
    }

    [Fact]
    public void Frame_SumsTimingsOfOneCommandBuffer()
    {
        FrameRecorder recorder = new();
        PassInfo pass = Pass("A", 0, 3);
        recorder.BeginView(new ViewInfo("Main", 0, 8, 8, 3));
        recorder.BeginPass(pass);
        recorder.RecordExecutionTime(new CommandBufferInfo(1, "A", 3, pass), false, 1.0);
        recorder.RecordExecutionTime(new CommandBufferInfo(1, "A", 3, pass), false, 0.5);
        recorder.RecordExecutionResolved(3);

        Assert.Single(recorder.Latest!.CommandBuffers);
        Assert.Equal(1.5, recorder.Latest.PassGpuMilliseconds(0, 0));
    }

    [Fact]
    public void Frame_KeepsNonPassCommandBuffersUnattributed()
    {
        FrameRecorder recorder = new();
        recorder.BeginView(new ViewInfo("Main", 0, 8, 8, 4));
        recorder.RecordExecutionTime(new CommandBufferInfo(7, "Upload", 4, null), true, 0.25);
        recorder.RecordExecutionResolved(4);

        LightCommandBuffer upload = Assert.Single(recorder.Latest!.CommandBuffers);
        Assert.True(upload.IsTransfer);
        Assert.Null(upload.PassIndex);
        Assert.Equal(0.25, recorder.Latest.TotalGpuMilliseconds);
    }

    [Fact]
    public void Frames_OverlappingExecutionsStaySeparate()
    {
        FrameRecorder recorder = new();
        PassInfo a = Pass("A", 0, 1);
        PassInfo b = Pass("B", 0, 2);
        recorder.BeginView(new ViewInfo("Main", 0, 8, 8, 1));
        recorder.BeginView(new ViewInfo("Main", 0, 8, 8, 2));
        recorder.BeginPass(a);
        recorder.BeginPass(b);
        recorder.RecordExecutionTime(new CommandBufferInfo(1, "A", 1, a), false, 1.0);
        recorder.RecordExecutionTime(new CommandBufferInfo(2, "B", 2, b), false, 2.0);
        recorder.RecordExecutionResolved(2);
        recorder.RecordExecutionResolved(1);

        Assert.Equal(new ulong[] { 2, 1 }, recorder.Frames().Select(f => f.ExecutionId));
        Assert.Equal("B", recorder.Frames()[0].Views[0].Passes[0].Name);
        Assert.Equal(2.0, recorder.Frames()[0].TotalGpuMilliseconds);
        Assert.Equal(1.0, recorder.Frames()[1].TotalGpuMilliseconds);
    }

    [Fact]
    public void Ring_DropsOldestFramesBeyondCapacity()
    {
        FrameRecorder recorder = new(3);
        for (ulong i = 1; i <= 5; i++)
        {
            RecordFrame(recorder, i, i);
        }

        Assert.Equal(3, recorder.Count);
        Assert.Equal(new ulong[] { 3, 4, 5 }, recorder.Frames().Select(f => f.ExecutionId));
        Assert.Equal(5ul, recorder.Latest!.ExecutionId);
    }

    [Fact]
    public void Clear_EmptiesRingAndPending()
    {
        FrameRecorder recorder = new();
        RecordFrame(recorder, 1, 1);
        recorder.BeginView(new ViewInfo("Main", 0, 8, 8, 2));
        recorder.Clear();
        recorder.RecordExecutionResolved(2);

        Assert.Equal(0, recorder.Count);
        Assert.Empty(recorder.Frames());
    }

    [Fact]
    public void Frames_SavedAsPlainDataCompareEqual()
    {
        FrameRecorder first = new();
        FrameRecorder second = new();
        RecordFrame(first, 1, 1.0);
        RecordFrame(second, 1, 1.0);
        Assert.Equal(first.Latest, second.Latest);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new FrameRecorder(0));
    }
}

public class FrameRecorderGpuTests
{
    private readonly struct SampleView : IRenderView
    {
        public SampleView(uint width, uint height)
        {
            PixelWidth = width;
            PixelHeight = height;
        }

        public uint PixelWidth { get; }
        public uint PixelHeight { get; }
        public int ViewId => 0;
    }

    private sealed class ClearPass : RasterPass
    {
        private readonly RenderResourceID _id;

        public ClearPass(RenderResourceID id) => _id = id;

        public override string Name => "SampleClear";

        public override void Setup(RenderContextBuilder builder)
            => SetTarget(builder, _id, GraphTextureDesc.ViewSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));

        public override void Render(RenderContext context, CommandBuffer cmd)
        {
        }
    }

    private sealed class CopyPass : IPass
    {
        private readonly RenderResourceID _id;
        private readonly DeviceBuffer _readback;
        private TextureHandle _handle;

        public CopyPass(RenderResourceID id, DeviceBuffer readback)
        {
            _id = id;
            _readback = readback;
        }

        public string Name => "SampleCopy";

        public void Setup(RenderContextBuilder builder) => _handle = builder.DeclareInputTexture(_id, TextureState.TransferSrc);

        public void Render(RenderContext context, CommandBuffer cmd)
        {
            RenderTexture target = context.GetRenderTexture(_handle);
            cmd.CopyTextureToBuffer(target.ColorTextures[0], _readback, 0, TextureRegion.Whole(target.ColorTextures[0]));
        }
    }

    [Fact]
    public void SampleRun_FillsRingWithCompleteFramesMatchingTheirPasses()
    {
        FrameRecorder recorder = new(4);
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true) { Profiler = recorder });

        const uint size = 64;
        DeviceBuffer readback = device.ResourceFactory.CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));
        RenderResourceID id = RenderResourceID.Intern("debugger_sample_target");
        using RenderPipeline pipeline = new([new ClearPass(id), new CopyPass(id, readback)]);
        SampleView[] views = [new(size, size)];

        ulong[] executions = new ulong[6];
        for (int i = 0; i < executions.Length; i++)
        {
            executions[i] = device.DispatchGraph(pipeline, views).Id;
        }

        device.WaitForIdle();

        Assert.Equal(4, recorder.Count);
        LightFrame[] frames = recorder.Frames().ToArray();
        Assert.Equal(executions[2..], frames.Select(f => f.ExecutionId));

        foreach (LightFrame frame in frames)
        {
            LightView view = Assert.Single(frame.Views);
            Assert.Equal(size, view.PixelWidth);
            Assert.Equal(new[] { "SampleClear", "SampleCopy" }, view.Passes.Select(p => p.Name));

            foreach (LightPass pass in view.Passes)
            {
                LightCommandBuffer timed = Assert.Single(frame.CommandBuffers, c => c.ViewIndex == pass.ViewIndex && c.PassIndex == pass.Index);
                Assert.Equal(pass.Name, timed.Name);
                Assert.True(timed.GpuMilliseconds >= 0);
                Assert.Equal(timed.GpuMilliseconds, frame.PassGpuMilliseconds(pass.ViewIndex, pass.Index));
            }

            Assert.All(frame.CommandBuffers.Where(c => c.PassIndex is not null), c => Assert.Contains(view.Passes, p => p.Index == c.PassIndex));
        }
    }
}
