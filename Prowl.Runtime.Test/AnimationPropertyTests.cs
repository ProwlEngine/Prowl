// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class AnimationPropertyTests : RuntimeTestBase
{
    public class TestReceiver : Component
    {
        public float Value = 1f;
        public int RangeEnterCount;
        public int RangeStayCount;
        public int RangeExitCount;
        public float LastProgress;

        public void OnEnter() => RangeEnterCount++;
        public void OnStay(float p) { RangeStayCount++; LastProgress = p; }
        public void OnExit() => RangeExitCount++;
    }

    [Fact]
    public void ExternalObjectBindingSlot_AnimatesSceneObjectOutsideAnimatorHierarchy()
    {
        SceneInstance scene = CreateScene();
        GameObject director = CreateGameObject("CutsceneDirector");
        GameObject externalCam = CreateGameObject("MainCamera");
        GameObject camChild = CreateGameObject("Lens");
        camChild.SetParent(externalCam, false);

        TestReceiver lensComp = camChild.AddComponent<TestReceiver>();
        scene.Add(director);
        scene.Add(externalCam);

        var clip = new AnimationClip
        {
            Name = "Cutscene",
            WrapMode = SceneClipWrapMode.Once,
            Loop = false,
        };
        clip.SetCurveKey("@Camera", "Transform", "LocalPosition.Y", 0f, 0f);
        clip.SetCurveKey("@Camera", "Transform", "LocalPosition.Y", 1f, 20f);
        clip.SetCurveKey("@Camera/Lens", nameof(TestReceiver), nameof(TestReceiver.Value), 0f, 2f);
        clip.SetCurveKey("@Camera/Lens", nameof(TestReceiver), nameof(TestReceiver.Value), 1f, 10f);

        Animator animator = director.AddComponent<Animator>();
        animator.SetObjectBinding("Camera", externalCam);
        animator.Clips.Add(clip);
        scene.Activate();

        Assert.True(SceneClipBinding.IsTrackTargetValid(director, "@Camera", "Transform", "LocalPosition.Y"));
        Assert.True(SceneClipBinding.IsTrackTargetValid(director, "@Camera/Lens", nameof(TestReceiver), nameof(TestReceiver.Value)));
        Assert.False(SceneClipBinding.IsTrackTargetValid(director, "@UnboundSlot", "Transform", "LocalPosition.Y"));

        animator.Sample(clip, 0.5f);

        Assert.InRange(externalCam.Transform.LocalPosition.Y, 9.5f, 10.5f);
        Assert.InRange(lensComp.Value, 5.5f, 6.5f);
    }

    [Fact]
    public void IsTrackTargetValid_DetectsRenamedOrMissingChildPaths()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Character");
        GameObject hand = CreateGameObject("Hand");
        hand.SetParent(root, false);
        scene.Add(root);

        Assert.True(SceneClipBinding.IsTrackTargetValid(root, "Hand", "Transform", "LocalPosition.X"));
        Assert.False(SceneClipBinding.IsTrackTargetValid(root, "MissingChild", "Transform", "LocalPosition.X"));
        Assert.False(SceneClipBinding.IsTrackTargetValid(root, "Hand", "NonExistentComponent", "Value"));

        // Renaming the child causes the old relative path to report as missing
        hand.Name = "RightHand";
        Assert.False(SceneClipBinding.IsTrackTargetValid(root, "Hand", "Transform", "LocalPosition.X"));
        Assert.True(SceneClipBinding.IsTrackTargetValid(root, "RightHand", "Transform", "LocalPosition.X"));
    }

    [Fact]
    public void AdditiveAndRestoreStateAndRangedEvents_WorkEndToEnd()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Actor");
        root.Transform.LocalPosition = new Float3(10f, 0f, 0f);
        TestReceiver comp = root.AddComponent<TestReceiver>();
        scene.Add(root);

        var clip = new AnimationClip
        {
            Name = "AdditiveAttack",
            WrapMode = SceneClipWrapMode.Once,
            Loop = false,
            CompletionMode = SceneClipCompletionMode.RestoreState,
        };
        SceneCurveTrack posX = clip.SetCurveKey("", "Transform", "LocalPosition.X", 0f, 0f);
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 0.5f, 5f);
        posX.BlendMode = SceneTrackBlendMode.Additive;

        clip.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = 0.1f,
            EndTime = 0.4f,
            FunctionName = nameof(TestReceiver.OnEnter),
            StayFunctionName = nameof(TestReceiver.OnStay),
            ExitFunctionName = nameof(TestReceiver.OnExit),
        });

        Animator animator = root.AddComponent<Animator>();
        animator.Clips.Add(clip);
        scene.Activate();

        // Mid-clip (~0.25s): additive offset applied and ranged event active
        Update(scene, 15);
        Assert.InRange(root.Transform.LocalPosition.X, 11.5f, 13.5f);
        Assert.Equal(1, comp.RangeEnterCount);
        Assert.True(comp.RangeStayCount >= 5);
        Assert.Equal(0, comp.RangeExitCount);

        // Past end (~0.65s total): ranged event exited and RestoreState reverted position back to 10
        Update(scene, 25);
        Assert.Equal(1, comp.RangeExitCount);
        Assert.Equal(10f, root.Transform.LocalPosition.X, 2);
    }

    [Fact]
    public void RestartingActiveAdditiveClip_PreservesOriginalBaseline()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Actor");
        root.Transform.LocalPosition = new Float3(10f, 0f, 0f);
        scene.Add(root);

        var clip = new AnimationClip
        {
            Name = "AdditiveLoop",
            WrapMode = SceneClipWrapMode.Loop,
            CompletionMode = SceneClipCompletionMode.RestoreState,
        };
        SceneCurveTrack posX = clip.SetCurveKey("", "Transform", "LocalPosition.X", 0f, 0f);
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 6f);
        posX.BlendMode = SceneTrackBlendMode.Additive;

        Animator animator = root.AddComponent<Animator>();
        animator.Clips.Add(clip);
        scene.Activate();

        Update(scene, 30); // ~0.5s -> X ~ 13
        Assert.InRange(root.Transform.LocalPosition.X, 12f, 14f);

        // Restarting the same clip mid-playback must not capture 13 as the new baseline!
        animator.Play(clip);
        animator.Stop();
        Assert.Equal(10f, root.Transform.LocalPosition.X, 2);
    }

    [Fact]
    public void SetCurveKey_PreservesCustomBezierTangentsOnExistingKeys()
    {
        var clip = new AnimationClip();
        SceneCurveTrack track = clip.GetOrCreateCurveTrack("", "Transform", "LocalPosition.X");
        track.Curve.AddKey(new Keyframe(0f, 0f, 7.5f, 7.5f, CurveInterpolation.CubicSpline));
        track.Curve.AddKey(new Keyframe(2f, 4f, -3.25f, -3.25f, CurveInterpolation.CubicSpline));

        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 2f);

        Assert.Equal(3, track.Curve.Count);
        Assert.Equal(7.5f, track.Curve[0].OutTangent, 3);
        Assert.Equal(-3.25f, track.Curve[2].InTangent, 3);
    }
}
