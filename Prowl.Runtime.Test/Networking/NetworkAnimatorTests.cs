using Prowl.Motion;
using Prowl.Runtime.Networking;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using Prowl.Warp;

using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkAnimatorTests
{
    private static (GameObject Root, Animator Animator, NetworkAnimator Network) Create()
    {
        var root = new GameObject("Actor");
        var animator = root.AddComponent<Animator>();
        animator.Graph = new AnimationGraph
        {
            Parameters =
            [
                new() { Name = "Speed", Kind = NodeValueKind.Number },
                new() { Name = "Crouch", Kind = NodeValueKind.Flag },
                new() { Name = "State", Kind = NodeValueKind.Integer },
                new() { Name = "Direction", Kind = NodeValueKind.Vector },
                new() { Name = "Weapon", Kind = NodeValueKind.Id },
                new() { Name = "Jump", Kind = NodeValueKind.Flag, Trigger = true },
                new() { Name = "Hand", Kind = NodeValueKind.Target }
            ]
        };
        var network = root.AddComponent<NetworkAnimator>();
        network.Bind(animator);
        return (root, animator, network);
    }

    [Fact]
    public void GraphParametersAndPlaybackSpeedRoundTripWhileIkTargetsStayLocal()
    {
        var source = Create();
        var replica = Create();
        using var sourceRoot = source.Root;
        using var replicaRoot = replica.Root;
        source.Animator.SetFloat("Speed", 4.5f);
        source.Animator.SetBool("Crouch", true);
        source.Animator.SetInt("State", 2);
        source.Animator.SetVector("Direction", new Float3(1, 2, 3));
        source.Animator.SetId("Weapon", new StringID("Physgun"));
        source.Animator.Speed = 1.2f;
        var hand = Target.FromWorld(new Transform3D(new Float3(8, 9, 10), Quaternion.Identity, Float3.One));
        replica.Animator.SetTarget("Hand", hand);
        source.Network.CaptureState();
        Copy(source.Network, replica.Network, initial: true);
        replica.Network.OnNetworkSpawn();
        replica.Network.ApplyState();
        Assert.Equal(4.5f, replica.Animator.GetFloat("Speed"));
        Assert.True(replica.Animator.GetBool("Crouch"));
        Assert.Equal(2, replica.Animator.GetInt("State"));
        Assert.Equal(new Float3(1, 2, 3), replica.Animator.GetVector("Direction"));
        Assert.Equal(new StringID("Physgun"), replica.Animator.GetId("Weapon"));
        Assert.Equal(1.2f, replica.Animator.Speed);
        Assert.True(replica.Animator.GetTarget("Hand").TryGetTransform(null!, out var pose));
        Assert.Equal(new Float3(8, 9, 10), pose.position);
    }

    [Fact]
    public void TriggerEventsSurviveMultipleWritesAndDoNotReplayForLateJoiners()
    {
        var source = Create();
        var replica = Create();
        using var sourceRoot = source.Root;
        using var replicaRoot = replica.Root;
        source.Animator.SetTrigger("Jump");
        Copy(source.Network, replica.Network, initial: true);
        replica.Network.OnNetworkSpawn();
        replica.Network.ApplyState();
        Assert.False(replica.Animator.GetBool("Jump"));

        source.Animator.SetTrigger("Jump");
        source.Animator.SetTrigger("Jump");
        Copy(source.Network, replica.Network, initial: false);
        replica.Network.ApplyState();
        Assert.True(replica.Animator.GetBool("Jump"));
        replica.Animator.ResetTrigger("Jump");
        replica.Network.ApplyState();
        Assert.True(replica.Animator.GetBool("Jump"));
        replica.Animator.ResetTrigger("Jump");
        replica.Network.ApplyState();
        Assert.False(replica.Animator.GetBool("Jump"));
    }

    [Fact]
    public void ParametersApplyToAnAnimatorRebuiltAfterTheInitialSnapshot()
    {
        var source = Create();
        var replica = Create();
        using var sourceRoot = source.Root;
        using var replicaRoot = replica.Root;
        source.Animator.SetFloat("Speed", 7.5f);
        source.Network.CaptureState();
        Copy(source.Network, replica.Network, initial: true);
        replica.Network.OnNetworkSpawn();
        var child = new GameObject("New model");
        child.SetParent(replica.Root, false);
        var replacement = child.AddComponent<Animator>();
        replacement.Graph = replica.Animator.Graph;
        replica.Network.Bind(replacement);
        replica.Network.ApplyState();
        Assert.Equal(7.5f, replacement.GetFloat("Speed"));
    }

    private static void Copy(NetworkAnimator source, NetworkAnimator replica, bool initial)
    {
        foreach (SyncMember member in NetworkMembers.Of(source).Syncs)
        {
            using var writer = new NetWriter();
            member.Write(source, writer);
            var reader = new NetReader(writer.WrittenSpan);
            NetworkMembers.Of(replica).Sync(member.Slot)!.Read(replica, ref reader, initial);
            Assert.True(reader.IsAtEnd);
        }
    }
}
