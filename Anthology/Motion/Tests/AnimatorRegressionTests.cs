using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class AnimatorRegressionTests
{
    private sealed class Player : SimpleAnimator
    {
        public Player(Skeleton skeleton) : base(skeleton) { }
        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
    }

    [Fact]
    public void CrossFade_DuringACrossFade_BlendsFromTheCurrentMix()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var player = new Player(skeleton);
        player.Play(TestClips.Const(skeleton, 0f));
        player.Update(0.1f);
        player.CrossFade(TestClips.Const(skeleton, 10f), 1f);
        for (int i = 0; i < 9; i++)
            player.Update(0.1f);
        float before = player.Pose.GetTransform(0).position.Z;

        player.CrossFade(TestClips.Const(skeleton, 0f), 1f);
        player.Update(0.1f);
        float after = player.Pose.GetTransform(0).position.Z;

        Assert.Equal(9.0, (double)before, 2);
        Assert.InRange(after, 8.5f, 9.5f);
    }

    [Fact]
    public void NegativeSpeed_PlaysRootMotionBackward()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var player = new Player(skeleton) { Speed = -1f };
        player.Play(TestClips.Ramp(skeleton, rootTravel: 1f));

        for (int i = 0; i < 5; i++)
        {
            player.Update(0.1f);
            Assert.Equal(-0.1, (double)player.RootMotionDelta.position.Z, 3);
        }
    }

    [Fact]
    public void Secondaries_FollowTheDestinationClipDuringACrossFade()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        Skeleton weaponA = TestSkeletons.MakeChain();
        Skeleton weaponB = TestSkeletons.MakeChain();
        var clipA = new AnimationClip(skeleton, new[] { TestClips.At(skeleton, 0f), TestClips.At(skeleton, 0f) }, 1f, secondaryClips: new[] { TestClips.Const(weaponA, 1f) });
        var clipB = new AnimationClip(skeleton, new[] { TestClips.At(skeleton, 0f), TestClips.At(skeleton, 0f) }, 1f, secondaryClips: new[] { TestClips.Const(weaponB, 2f) });
        var player = new Player(skeleton);

        player.Play(clipA);
        player.Update(0.1f);
        player.CrossFade(clipB, 1f);
        player.Update(0.1f);

        Assert.Same(clipB, player.CurrentClip);
        Assert.Same(weaponB, Assert.Single(player.SecondarySkeletons));
    }
}
