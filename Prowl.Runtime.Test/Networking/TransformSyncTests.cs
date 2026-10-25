using Prowl.Runtime.Networking;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class TransformSyncTests : RuntimeTestBase
{
    private static TransformUpdate Pose(float x) =>
        new(TransformChannels.Pose, 0, new Float3(x, 0, 0), Quaternion.Identity, Float3.One);

    [Fact]
    public void PlaybackFinishesBeforeSleepingAndNewSamplesResumeIt()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        TimeSpan previousDelay = Networking.Networking.InterpolationDelay;
        Networking.Networking.InterpolationDelay = TimeSpan.FromSeconds(0.5);
        try
        {
            sync.Receive(go, 1, 0, 10, Pose(0));
            sync.Receive(go, 2, 1, 11, Pose(10));
            sync.Interpolate(go, 11);
            Assert.Equal(5f, go.Transform.LocalPosition.X, 4);
            Assert.True(sync.HasPendingInterpolation);

            sync.Interpolate(go, 11.5);
            Assert.Equal(10f, go.Transform.LocalPosition.X);
            Assert.False(sync.HasPendingInterpolation);
            Assert.Single(sync.Samples);

            // Sleeping must avoid writing the final pose again.
            go.Transform.LocalPosition = new Float3(99, 0, 0);
            sync.Interpolate(go, 12);
            Assert.Equal(99f, go.Transform.LocalPosition.X);

            // An outdated packet must not wake playback.
            Assert.Null(sync.Receive(go, 1, 0, 12, Pose(0)));
            Assert.False(sync.HasPendingInterpolation);

            sync.Receive(go, 3, 2, 12, Pose(20));
            sync.Interpolate(go, 12);
            Assert.Equal(15f, go.Transform.LocalPosition.X, 4);
            Assert.True(sync.HasPendingInterpolation);
            sync.Interpolate(go, 12.5);
            Assert.Equal(20f, go.Transform.LocalPosition.X);
            Assert.False(sync.HasPendingInterpolation);
        }
        finally
        {
            Networking.Networking.InterpolationDelay = previousDelay;
        }
    }

    [Fact]
    public void ClearingHistoryAndSnappingCancelPendingPlayback()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Receive(go, 1, 0, 10, Pose(10));
        sync.ClearHistory();
        Assert.False(sync.HasPendingInterpolation);

        sync.Receive(go, 2, 1, 11, Pose(20));
        sync.Snap(go, 3, new Float3(30, 0, 0), Quaternion.Identity, Float3.One);
        sync.Interpolate(go, 20);
        Assert.Equal(30f, go.Transform.LocalPosition.X);
        Assert.False(sync.HasPendingInterpolation);
    }

    [Fact]
    public void DisabledInterpolationAppliesImmediatelyWithoutPendingPlayback()
    {
        GameObject go = CreateGameObject();
        go.Network.Flags = NetworkFlags.NoInterpolation;
        TransformSync sync = new();
        sync.Receive(go, 1, 0, 10, Pose(10));
        Assert.Equal(10f, go.Transform.LocalPosition.X);
        Assert.False(sync.HasPendingInterpolation);
    }
}
