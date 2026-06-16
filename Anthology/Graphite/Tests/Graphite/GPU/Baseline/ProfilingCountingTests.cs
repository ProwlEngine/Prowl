using Xunit;

namespace Prowl.Graphite.Tests;

// Verifies the live profiling counters on a real device: that creating/destroying resources
// moves the gauges by the right counts and bytes, that overlapping buffer roles do not
// double-count the DeviceBuffer total, that per-frame flows capture and then zero each
// BeginFrame, and that ResetProfile clears everything.
//
// The value-type plumbing (ProfileSnapshot / ProfileBinGroup) is covered separately by the
// CPU-side CPU/ProfilingTests. These run only when the library was built with PROFILE_USAGE;
// otherwise GetProfile returns a zeroed snapshot and the tests skip.
public abstract class ProfilingCountingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private bool ProfilingEnabled()
    {
        GD.ResetProfile();
        using DeviceBuffer probe = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        bool enabled = GD.GetProfile().Live[AllocBin.DeviceBuffer].Count > 0;
        GD.ResetProfile();
        return enabled;
    }

    [SkippableFact]
    public void CreateBuffer_BumpsLiveAllocationAndRole()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        using DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        ProfileSnapshot p = GD.GetProfile();
        Assert.Equal(1, p.Live[AllocBin.DeviceBuffer].Count);
        Assert.Equal(256, p.Live[AllocBin.DeviceBuffer].Bytes);
        Assert.Equal(1, p.BufferMem[BufferRoleBin.Uniform].Count);
        Assert.Equal(256, p.BufferMem[BufferRoleBin.Uniform].Bytes);
    }

    [SkippableFact]
    public void MultiRoleBuffer_OverlapsRoles_ButDeviceBufferIsDedupTotal()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        using DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(
            new BufferDescription(512, BufferUsage.VertexBuffer | BufferUsage.IndexBuffer));

        ProfileSnapshot p = GD.GetProfile();

        // One physical buffer: the DeviceBuffer bin is the non-double-counted total.
        Assert.Equal(1, p.Live[AllocBin.DeviceBuffer].Count);
        Assert.Equal(512, p.Live[AllocBin.DeviceBuffer].Bytes);

        // The same bytes appear under every matching role; summing roles would double-count.
        Assert.Equal(512, p.BufferMem[BufferRoleBin.Vertex].Bytes);
        Assert.Equal(512, p.BufferMem[BufferRoleBin.Index].Bytes);
    }

    [SkippableFact]
    public void DisposeBuffer_DecrementsLiveGauge()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        buffer.Dispose();

        ProfileSnapshot p = GD.GetProfile();
        Assert.Equal(0, p.Live[AllocBin.DeviceBuffer].Count);
        Assert.Equal(0, p.Live[AllocBin.DeviceBuffer].Bytes);
        Assert.Equal(0, p.BufferMem[BufferRoleBin.Uniform].Bytes);
    }

    [SkippableFact]
    public void CreateTexture_BumpsLiveTextureCount()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        using Texture texture = GD.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            16, 16, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        ProfileSnapshot p = GD.GetProfile();
        Assert.Equal(1, p.Live[AllocBin.Texture].Count);
        Assert.True(p.Live[AllocBin.Texture].Bytes >= 0);
    }

    [SkippableFact]
    public void UpdateBuffer_RecordsBufferOp_InLastFrameFlow()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        using DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(buffer, 0, new byte[256]);

        // Flow counters are frozen into the last-frame view at BeginFrame.
        Frame frame = GD.BeginFrame();
        GD.EndFrame(frame);

        ProfileSnapshot p = GD.GetProfile();
        Assert.Equal(1, p.BufferOps[BufferOpBin.Update].Count);
        Assert.Equal(256, p.BufferOps[BufferOpBin.Update].Bytes);
    }

    [SkippableFact]
    public void MapUnmap_RecordsBufferOps_InLastFrameFlow()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        using DeviceBuffer staging = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.Staging));
        GD.Map<byte>(staging, MapMode.Write);
        GD.Unmap(staging);

        Frame frame = GD.BeginFrame();
        GD.EndFrame(frame);

        ProfileSnapshot p = GD.GetProfile();
        Assert.Equal(1, p.BufferOps[BufferOpBin.Map].Count);
        Assert.Equal(1, p.BufferOps[BufferOpBin.Unmap].Count);
    }

    [SkippableFact]
    public void FrameFlow_ZeroesEachBeginFrame()
    {
        Skip.IfNot(ProfilingEnabled());

        GD.ResetProfile();
        using DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

        // First frame captures the allocation flow.
        GD.EndFrame(GD.BeginFrame());
        Assert.Equal(1, GD.GetProfile().Allocated[AllocBin.DeviceBuffer].Count);

        // The next frame has no allocations, so the flow zeroes.
        GD.EndFrame(GD.BeginFrame());
        Assert.Equal(0, GD.GetProfile().Allocated[AllocBin.DeviceBuffer].Count);
    }

    [SkippableFact]
    public void ResetProfile_ClearsLiveGaugesEvenWhileResident()
    {
        Skip.IfNot(ProfilingEnabled());

        using DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        GD.ResetProfile();

        ProfileSnapshot p = GD.GetProfile();
        Assert.Equal(0, p.Live[AllocBin.DeviceBuffer].Count);
        Assert.Equal(0, p.BufferMem[BufferRoleBin.Uniform].Bytes);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanProfilingCountingTests : ProfilingCountingTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11ProfilingCountingTests : ProfilingCountingTests<D3D11DeviceCreator> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLProfilingCountingTests : ProfilingCountingTests<OpenGLDeviceCreator> { }
#endif
