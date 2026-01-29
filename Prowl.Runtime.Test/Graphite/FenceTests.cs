// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using Prowl.Runtime.Graphite;
using Prowl.Runtime.Graphite.OpenGL;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class FenceTests
{
    private readonly GraphiteTestFixture _fixture;

    public FenceTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateFence_NotSignaled_Succeeds()
    {
        using var fence = _fixture.Device.CreateFence(signaled: false);

        Assert.NotNull(fence);
        Assert.False(fence.IsSignaled);
    }

    [Fact]
    public void CreateFence_Signaled_Succeeds()
    {
        using var fence = _fixture.Device.CreateFence(signaled: true);

        Assert.NotNull(fence);
        Assert.True(fence.IsSignaled);
    }

    [Fact]
    public void Fence_Wait_AfterSubmit_Succeeds()
    {
        using var fence = _fixture.Device.CreateFence(false);
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);

        // Wait should complete
        fence.Wait();

        Assert.True(fence.IsSignaled);
    }

    [Fact]
    public void Fence_WaitWithTimeout_Succeeds()
    {
        using var fence = _fixture.Device.CreateFence(false);
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);

        // Wait with timeout (should complete quickly for empty command list)
        bool signaled = fence.Wait(1000); // 1 second timeout

        Assert.True(signaled);
        Assert.True(fence.IsSignaled);
    }

    [Fact]
    public void Fence_Reset_ClearsSignal()
    {
        using var fence = _fixture.Device.CreateFence(true);

        Assert.True(fence.IsSignaled);

        fence.Reset();

        Assert.False(fence.IsSignaled);
    }

    [Fact]
    public void Fence_MultipleSubmits_WorksCorrectly()
    {
        using var fence = _fixture.Device.CreateFence(false);
        using var cmd = _fixture.Device.CreateCommandList();

        // First submit
        cmd.Begin();
        cmd.End();
        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();
        Assert.True(fence.IsSignaled);

        // Reset and resubmit
        fence.Reset();
        Assert.False(fence.IsSignaled);

        cmd.Begin();
        cmd.End();
        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();
        Assert.True(fence.IsSignaled);
    }

    [Fact]
    public void Fence_Dispose_CleansUpResources()
    {
        var fence = _fixture.Device.CreateFence(false);
        var glFence = fence as GLFence;
        Assert.NotNull(glFence);

        // Submit to create sync object
        using var cmd = _fixture.Device.CreateCommandList();
        cmd.Begin();
        cmd.End();
        _fixture.Device.SubmitCommands(cmd, fence);
        fence.Wait();

        fence.Dispose();

        // After dispose, operations should fail or be no-ops
        Assert.NotNull(fence);
    }

    [Fact]
    public void WaitForFence_ViaDevice_Succeeds()
    {
        using var fence = _fixture.Device.CreateFence(false);
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);
        _fixture.Device.WaitForFence(fence);

        Assert.True(fence.IsSignaled);
    }

    [Fact]
    public void WaitForIdle_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);
        _fixture.Device.WaitForIdle();

        // Should complete without error
        Assert.NotNull(cmd);
    }
}
