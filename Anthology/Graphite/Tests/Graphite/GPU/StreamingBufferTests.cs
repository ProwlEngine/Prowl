using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;

// StreamingBuffer is the sanctioned way to rewrite per-frame data without racing the
// frames-in-flight system: it holds one backing DeviceBuffer per ring slot and exposes the
// active frame's through Current. This suite covers the rotation contract, the per-slot
// identity, and the property that actually motivates the type -- writing Current every frame
// must never orphan, where writing a single shared DeviceBuffer does (see BufferSafetyTests).
public abstract class StreamingBufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void BackingBufferCount_MatchesMaxFramesInFlight()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        Assert.Equal((int)GD.MaxFramesInFlight, sb.BufferCount);
    }

    [Fact]
    public void Description_IsMirroredOntoEveryBacking()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        Assert.Equal(256u, sb.SizeInBytes);
        Assert.Equal(BufferUsage.UniformBuffer, sb.Usage);

        for (uint i = 0; i < sb.BufferCount; i++)
        {
            Assert.Equal(256u, sb[i].SizeInBytes);
            Assert.Equal(BufferUsage.UniformBuffer, sb[i].Usage);
        }
    }

    [Fact]
    public void BackingBuffers_AreDistinctInstances()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        HashSet<DeviceBuffer> seen = [];
        for (uint i = 0; i < sb.BufferCount; i++)
            Assert.True(seen.Add(sb[i]));
    }

    [Fact]
    public void Current_WithoutActiveFrame_Throws()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        // Current resolves through GraphicsDevice.CurrentFrame, which requires an open frame.
        Assert.Throws<RenderException>(() => sb.Current);
    }

    [Fact]
    public void Current_TracksTheActiveFrameRingSlot()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        for (uint i = 0; i < GD.MaxFramesInFlight; i++)
        {
            Frame frame = GD.BeginFrame();
            Assert.Same(sb[frame.RingSlot], sb.Current);
            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }
    }

    [Fact]
    public void Current_RotatesThroughEverySlot_ThenRepeats()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        List<DeviceBuffer> firstCycle = [];
        for (uint i = 0; i < GD.MaxFramesInFlight; i++)
        {
            Frame frame = GD.BeginFrame();
            firstCycle.Add(sb.Current);
            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }

        // A full cycle must visit every backing buffer exactly once.
        Assert.Equal(GD.MaxFramesInFlight, (uint)new HashSet<DeviceBuffer>(firstCycle).Count);

        // The next cycle must land on the same buffers in the same order.
        for (uint i = 0; i < GD.MaxFramesInFlight; i++)
        {
            Frame frame = GD.BeginFrame();
            Assert.Same(firstCycle[(int)i], sb.Current);
            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }
    }

    [Fact]
    public void Name_SuffixesEveryBackingWithItsSlot()
    {
        using StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        sb.Name = "PerFrame";

        for (uint i = 0; i < sb.BufferCount; i++)
            Assert.Equal($"PerFrame[{i}]", sb[i].Name);
    }

    [Fact]
    public void Dispose_DisposesEveryBacking()
    {
        StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        DeviceBuffer[] backings = new DeviceBuffer[sb.BufferCount];
        for (uint i = 0; i < sb.BufferCount; i++)
            backings[i] = sb[i];

        sb.Dispose();

        foreach (DeviceBuffer backing in backings)
            Assert.True(backing.IsDisposed);
    }

    [SkippableFact]
    public void Dispose_ReleasesEveryBackingFromTheLiveGauge()
    {
        Skip.IfNot(ProfilingEnabled());

        StreamingBuffer sb = RF.CreateStreamingBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        long afterCreate = GD.GetProfile().Live[AllocBin.DeviceBuffer].Count;

        sb.Dispose();

        long afterDispose = GD.GetProfile().Live[AllocBin.DeviceBuffer].Count;
        Assert.Equal(GD.MaxFramesInFlight, (uint)(afterCreate - afterDispose));
    }

    [Fact]
    public void PerFrameWrites_NeverOrphan()
    {
        // The whole point of the type: Current belongs to the ring slot the GPU has already
        // finished with, so rewriting it every frame is safe and must never trigger the
        // implicit-reallocation path. BufferSafetyTests proves a plain DeviceBuffer does orphan
        // under the same access pattern.
        using StreamingBuffer sb = RF.CreateStreamingBuffer(
            new BufferDescription(256, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        List<string> warnings = [];
        GraphicsDeviceWarningHandler previous = GD.OnWarning;
        GD.OnWarning = message => warnings.Add(message);

        try
        {
            for (int i = 0; i < GD.MaxFramesInFlight * 4; i++)
            {
                Frame frame = GD.BeginFrame();
                GD.UpdateBuffer(sb.Current, 0, new uint[] { (uint)i, 0, 0, 0 });
                GD.EndFrame(frame);
            }
            GD.WaitForIdle();
        }
        finally
        {
            GD.OnWarning = previous;
        }

        Assert.Empty(warnings);
    }

    private bool ProfilingEnabled()
    {
        GD.ResetProfile();
        using DeviceBuffer probe = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        return GD.GetProfile().Live[AllocBin.DeviceBuffer].Count > 0;
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanStreamingBufferTests : StreamingBufferTests<VulkanDeviceCreator> { }
#endif
