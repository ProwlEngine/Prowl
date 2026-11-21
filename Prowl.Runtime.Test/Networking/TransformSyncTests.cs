using Prowl.Runtime.Networking;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class TransformSyncTests : RuntimeTestBase
{
    private static TransformUpdate Pose(float x) =>
        new(TransformChannels.Pose, 0, new Float3(x, 0, 0), Quaternion.Identity, Float3.One);

    [Theory]
    [InlineData((int)TransformChannels.Position)]
    [InlineData((int)TransformChannels.Rotation)]
    [InlineData((int)TransformChannels.Scale)]
    public void SmallJitterSettlesAndAccumulatedMovementResumes(int channelValue)
    {
        TransformChannels channel = (TransformChannels)channelValue;
        NetworkConfiguration previous = Networking.Networking.Configuration;
        try
        {
            Networking.Networking.Configure(new()
            {
                TransformPositionThreshold = 0.01f,
                TransformRotationThresholdDegrees = 0.5f,
                TransformScaleThreshold = 0.01f
            });
            GameObject go = CreateGameObject();
            TransformSync sync = new();
            void Move(float amount)
            {
                if (channel == TransformChannels.Position) go.Transform.LocalPosition = new Float3(amount, 0, 0);
                if (channel == TransformChannels.Scale) go.Transform.LocalScale = Float3.One + new Float3(amount, 0, 0);
                if (channel == TransformChannels.Rotation)
                    go.Transform.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, amount * 50 * Maths.Deg2Rad);
            }

            Assert.NotNull(sync.Capture(go, 0, 0));
            for (int tick = 1; tick <= 12; tick++)
            {
                Move(tick % 2 == 0 ? 0.002f : -0.002f);
                TransformUpdate? update = sync.Capture(go, tick, tick - 1);
                if (tick == 3)
                {
                    Assert.True(sync.Settled);
                    Assert.NotNull(update);
                    Assert.True((update.Value.Channels & TransformChannels.Exact) != 0);
                    Assert.Equal(go.Transform.LocalPosition, update.Value.Position);
                    Assert.Equal(go.Transform.LocalRotation, update.Value.Rotation);
                    Assert.Equal(go.Transform.LocalScale, update.Value.Scale);
                }
                else Assert.Null(update);
            }

            // Each increment is below the threshold; the total must eventually wake replication.
            Move(0.002f);
            Assert.Null(sync.Capture(go, 13, 12));
            Move(0.006f);
            Assert.Null(sync.Capture(go, 14, 13));
            Move(0.010f);
            TransformUpdate resumed = sync.Capture(go, 15, 14)!.Value;
            Assert.False(sync.Settled);
            Assert.True((resumed.Channels & channel) != 0);
            Assert.True((resumed.Channels & TransformChannels.Resumed) != 0);
        }
        finally { Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void EquivalentQuaternionSignsDoNotKeepRotationAwake()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Capture(go, 0, 0);
        go.Transform.LocalRotation = new Quaternion(0, 0, 0, -1);
        Assert.Null(sync.Capture(go, 1, 0));
        Assert.Null(sync.Capture(go, 2, 1));
        Assert.NotNull(sync.Capture(go, 3, 2));
        Assert.True(sync.Settled);
        go.Transform.LocalRotation = Quaternion.Identity;
        Assert.Null(sync.Capture(go, 4, 3));
    }

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
    public void ClearingHistoryCancelsPendingPlayback()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Receive(go, 1, 0, 10, Pose(10));
        sync.ClearHistory();
        Assert.False(sync.HasPendingInterpolation);
    }

    [Fact]
    public void ATeleportTagsTheNextRecordsWholeAndWithoutResumed()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Capture(go, 0, 0);
        for (int tick = 1; tick <= 3; tick++) sync.Capture(go, tick, tick - 1);
        Assert.True(sync.Settled);

        go.Transform.LocalPosition = new Float3(50, 0, 0);
        sync.Teleport();
        TransformUpdate first = sync.Capture(go, 4, 3)!.Value;
        Assert.Equal(TransformChannels.Pose | TransformChannels.Teleport, first.Channels);
        go.Transform.LocalPosition = new Float3(51, 0, 0);
        TransformUpdate second = sync.Capture(go, 5, 4)!.Value;
        Assert.NotEqual(0, (int)(second.Channels & TransformChannels.Teleport));
        Assert.Equal(first.Teleport, second.Teleport);

        sync.Teleport();
        Assert.NotEqual(first.Teleport, sync.Capture(go, 6, 5)!.Value.Teleport);
    }

    [Fact]
    public void ATeleportsTagEndsOnceTheObjectHasComeToRest()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Capture(go, 0, 0);
        for (int tick = 1; tick <= 3; tick++) sync.Capture(go, tick, tick - 1);

        go.Transform.LocalPosition = new Float3(50, 0, 0);
        sync.Teleport();
        Assert.NotEqual(0, (int)(sync.Capture(go, 4, 3)!.Value.Channels & TransformChannels.Teleport));

        // The pose it rests at carries the tag, and arrives for certain, so nothing after it needs to.
        TransformUpdate? rest = null;
        for (int tick = 5; tick <= 12 && !sync.Settled; tick++) rest = sync.Capture(go, tick, tick - 1) ?? rest;
        Assert.True(sync.Settled);
        Assert.NotEqual(0, (int)(rest!.Value.Channels & TransformChannels.Teleport));
        Assert.Null(sync.Capture(go, 13, 12));

        go.Transform.LocalPosition = new Float3(51, 0, 0);
        TransformUpdate moving = sync.Capture(go, 20, 19)!.Value;
        Assert.Equal(0, (int)(moving.Channels & TransformChannels.Teleport));
        Assert.NotEqual(0, (int)(moving.Channels & TransformChannels.Resumed));
    }

    [Fact]
    public void AReceiverSnapsOnceForEachTeleport()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Receive(go, 1, 0, 10, Pose(0));
        sync.Receive(go, 2, 0.1, 10.1, Pose(1));

        TransformUpdate jump = Pose(100) with { Channels = TransformChannels.Pose | TransformChannels.Teleport, Teleport = 7 };
        sync.Receive(go, 3, 0.2, 10.2, jump);
        Assert.Equal(100f, go.Transform.LocalPosition.X);
        Assert.Single(sync.Samples);
        Assert.False(sync.HasPendingInterpolation);

        sync.Receive(go, 4, 0.3, 10.3, jump with { Position = new Float3(101, 0, 0) });
        Assert.Equal(2, sync.Samples.Count);
        Assert.Equal(100f, go.Transform.LocalPosition.X);
    }

    [Fact]
    public void DisabledInterpolationAppliesImmediatelyWithoutPendingPlayback()
    {
        GameObject go = CreateGameObject();
        NetworkTransform transform = go.AddComponent<NetworkTransform>();
        transform.Interpolate = false;
        TransformSync sync = transform.Sync;
        sync.Receive(go, 1, 0, 10, Pose(10));
        Assert.Equal(10f, go.Transform.LocalPosition.X);
        Assert.False(sync.HasPendingInterpolation);
    }

    [Fact]
    public void ARestPoseStampedEarlierThanTheLastUpdateIsStillTaken()
    {
        GameObject go = CreateGameObject();
        TransformSync sync = new();
        sync.Receive(go, 1, 1.0, 10, Pose(10));

        // Its tick says it is newer. Only its time, put on another clock, fell behind.
        TransformUpdate rest = Pose(20) with { Channels = TransformChannels.Pose | TransformChannels.Exact };
        Assert.NotNull(sync.Receive(go, 2, 0.9, 10.1, rest));
        Assert.Equal(20f, sync.Samples[^1].Position.X);
        Assert.True(sync.Samples[^1].Time >= sync.Samples[0].Time);

        // A moving update in the same state is still dropped, as before.
        Assert.Null(sync.Receive(go, 3, 0.8, 10.2, Pose(30)));
    }
}
