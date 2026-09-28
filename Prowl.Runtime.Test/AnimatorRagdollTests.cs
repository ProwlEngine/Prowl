// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Runtime.AnimationNodes;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Xunit;

using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime.Test;

/// <summary>The Ragdoll node's puppet: how it is made, how closely what it shows follows the animation, and how it stays whole.</summary>
public class AnimatorRagdollTests : RuntimeTestBase
{
    private (Scene Scene, Animator Animator, GameObject Root) SetupRagdoll(bool controller = false, float active = 1f)
    {
        (MotionSkeleton skeleton, GameObject root) = TestHumanoid.Build();
        Avatar avatar = Avatar.CreateAutomatic(skeleton, out _, "Character");

        Scene scene = CreateScene();
        scene.Add(root);
        if (controller) root.AddComponent<CharacterController>();

        var graph = new AnimationGraph();
        GraphNodeRecord pose = graph.AddNode(AnimationNodeIds.ReferencePose);
        GraphNodeRecord ragdoll = graph.AddNode(AnimationNodeIds.Ragdoll);
        ragdoll.Wire(pose.Id);
        ragdoll.Properties["Total Mass"] = NodeValue.FromNumber(60f);
        ragdoll.Properties["Default"] = NodeValue.FromNumber(active);
        graph.RootNode = ragdoll.Id;

        var animator = root.AddComponent<Animator>();
        animator.Avatar = new AssetRef<Avatar>(avatar);
        animator.Graph = new AssetRef<AnimationGraph>(graph);
        scene.Enable();
        return (scene, animator, root);
    }

    private static GameObject? Puppet(Scene scene)
        => scene.AllObjects.FirstOrDefault(o => o.Name == "Character Ragdoll");

    [Fact]
    public void ARagdollNode_BuildsAHiddenPuppet_AndLeavesTheBonesAlone()
    {
        (Scene scene, _, GameObject root) = SetupRagdoll();
        Tick(scene, 3);

        GameObject? puppet = Puppet(scene);
        Assert.NotNull(puppet);
        Assert.Equal(HideFlags.HideAndDontSave, puppet!.HideFlags);
        Assert.Empty(root.GetComponentsInChildren<Rigidbody3D>());

        var bodies = puppet.GetComponentsInChildren<Rigidbody3D>().ToList();
        Assert.Equal(16, bodies.Count);
        Assert.Equal(60f, bodies.Sum(b => b.Mass), 2);
        Assert.All(bodies, b => Assert.Equal(Jitter2.Dynamics.MotionType.Dynamic, b.MotionType));
    }

    // The puppet stands inside the controller's capsule, so a controller that could hit it would push itself away.
    [Fact]
    public void TheCharacterControllerIgnoresThePuppet()
    {
        (Scene scene, _, GameObject root) = SetupRagdoll(controller: true);
        Tick(scene, 3);
        Assert.NotNull(Puppet(scene));

        var hits = new List<ShapeCastHit>();
        Assert.Equal(0, root.GetComponent<CharacterController>().OverlapNow(hits));
    }

    [Fact]
    public void ALimpRagdoll_KeepsItsJoints()
    {
        (Scene scene, _, _) = SetupRagdoll(active: 0f);
        scene.Physics.UseMultithreading = false;
        Tick(scene, 90);

        GameObject puppet = Puppet(scene)!;
        Assert.All(puppet.GetComponentsInChildren<PhysicsConstraint>(), c => Assert.True(c.Active, $"{c.GameObject.Name} {c.GetType().Name}"));

        Float3 Part(string name) => puppet.Children.First(g => g.Name == name).Transform.Position;
        Assert.InRange(Float3.Distance(Part("LeftUpperArm"), Part("LeftLowerArm")), 0.25f, 0.35f);
        Assert.InRange(Float3.Distance(Part("LeftUpperLeg"), Part("LeftLowerLeg")), 0.4f, 0.5f);
    }

    // The puppet trails the animation by a step, but what shows is the animation moved by what physics did,
    // so a running character's shown hips stay with it.
    [Fact]
    public void AnActiveRagdoll_KeepsPaceWithAMovingCharacter()
    {
        (Scene scene, _, GameObject root) = SetupRagdoll();
        scene.Physics.UseMultithreading = false;
        Tick(scene, 3);

        const float speed = 4f;
        for (int frame = 0; frame < 60; frame++)
        {
            root.Transform.Position += new Float3(0f, 0f, speed * FixedDeltaTime);
            Tick(scene, 1);
        }

        Float3 hips = TestHumanoid.Hips(root).Position;
        Assert.InRange(root.Transform.Position.Z - hips.Z, -0.02f, 0.02f);
    }

    // A sprint: 2 Hz, thighs swing from 75 degrees forward to 15 back, within a hip's range, knees only
    // ever fold back, arms pump.
    private static AnimationClip RunClip(MotionSkeleton skeleton, Avatar avatar)
    {
        const int frames = 31;
        var poses = new Pose[frames];
        for (int f = 0; f < frames; f++)
        {
            float phase = f / (float)(frames - 1) * MathF.PI * 2f;
            var pose = new Pose(skeleton);
            pose.SetToReferencePose();
            void Swing(string bone, float degrees) => TestHumanoid.Turn(pose, skeleton, bone, TestHumanoid.About(1f, 0f, 0f, degrees));
            Swing("LeftUpperLeg", -30f - 45f * MathF.Sin(phase));
            Swing("RightUpperLeg", -30f - 45f * MathF.Sin(phase + MathF.PI));
            Swing("LeftLowerLeg", 55f + 50f * MathF.Sin(phase - 1f));
            Swing("RightLowerLeg", 55f + 50f * MathF.Sin(phase + MathF.PI - 1f));
            Swing("LeftUpperArm", 50f * MathF.Sin(phase + MathF.PI));
            Swing("RightUpperArm", 50f * MathF.Sin(phase));
            poses[f] = pose;
        }
        var clip = new Motion.AnimationClip(skeleton, poses, (frames - 1) / 60f);
        return AnimationClip.FromSkeletal(clip, new AssetRef<Avatar>(avatar), "Run");
    }

    // Where the floor's top sits, just above the ankles, so Foot Grounding lifts the whole body by it.
    private const float FloorTop = 0.105f;

    // A character standing on a floor, its graph pose, then optionally Foot Grounding, then a Ragdoll
    // whose Active reads the "Active" parameter.
    private (Scene Scene, Animator Animator, GameObject Root) SetupStandingRagdoll(bool footGrounding = false)
    {
        (MotionSkeleton skeleton, GameObject root) = TestHumanoid.Build();
        Avatar avatar = Avatar.CreateAutomatic(skeleton, out _, "Character");
        Scene scene = CreateScene();
        scene.Add(root);

        var floor = CreateGameObject("Floor");
        floor.Transform.Position = new Float3(0f, FloorTop - 0.5f, 0f);
        floor.AddComponent<BoxCollider>().Size = new Float3(100f, 1f, 100f);
        scene.Add(floor);

        var graph = new AnimationGraph();
        graph.Parameters.Add(new GraphParameterRecord { Name = "Active", Kind = NodeValueKind.Number, Number = 1f });
        GraphNodeRecord pose = graph.AddNode(AnimationNodeIds.ReferencePose);
        if (footGrounding)
        {
            GraphNodeRecord grounding = graph.AddNode(AnimationNodeIds.FootGrounding);
            grounding.Wire(pose.Id);
            pose = grounding;
        }
        GraphNodeRecord active = graph.AddNode(AnimationNodeIds.Parameter);
        active.Properties["Name"] = NodeValue.FromText("Active");
        GraphNodeRecord ragdoll = graph.AddNode(AnimationNodeIds.Ragdoll);
        ragdoll.Wire(pose.Id);
        ragdoll.Wire(active.Id);
        ragdoll.Properties["Blend"] = NodeValue.FromNumber(0f);
        graph.RootNode = ragdoll.Id;

        var animator = root.AddComponent<Animator>();
        animator.Avatar = new AssetRef<Avatar>(avatar);
        animator.Graph = new AssetRef<AnimationGraph>(graph);
        scene.Enable();
        scene.Physics.UseMultithreading = false;
        return (scene, animator, root);
    }

    private static Transform PuppetPart(Scene scene, string name) => Puppet(scene)!.Children.First(g => g.Name == name).Transform;

    private static float Degrees(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) * Maths.Rad2Deg;

    // Foot Grounding's ground probe looks past the ragdoll's own bodies.
    [Fact]
    public void FootGrounding_DoesNotStandOnTheRagdoll()
    {
        (Scene scene, Animator animator, GameObject root) = SetupStandingRagdoll(footGrounding: true);
        Tick(scene, 5);

        float worst = 0f;
        for (int frame = 0; frame < 300; frame++)
        {
            Tick(scene, 1);
            worst = MathF.Max(worst, Degrees(PuppetPart(scene, "Hips").Rotation, TestHumanoid.Hips(root).Rotation));
        }

        Assert.True(worst < 1f, $"hips strayed {worst:F1} degrees");
        Assert.InRange(PuppetPart(scene, "Hips").Position.Y, 1f + FloorTop - 0.02f, 1f + FloorTop + 0.02f);
    }

    [Fact]
    public void ATeleportedCharacter_TakesItsRagdollAlong()
    {
        (Scene scene, _, GameObject root) = SetupStandingRagdoll();
        Tick(scene, 30);

        root.Transform.Position += new Float3(20f, 0f, 0f);
        Tick(scene, 30);

        Transform hips = PuppetPart(scene, "Hips");
        Assert.True(Float3.Distance(hips.Position, TestHumanoid.Hips(root).Position) < 0.05f);
        Assert.All(Puppet(scene)!.Children, g => Assert.True(float.IsFinite(g.Transform.Position.X)));
    }

    // Snapping from limp back to active closes the whole pose at once, and the joints hold.
    [Fact]
    public void ARagdollSnappedBackToActive_StandsBackUpInOnePiece()
    {
        (Scene scene, Animator animator, GameObject root) = SetupStandingRagdoll();
        Tick(scene, 5);
        animator.GraphInstance!.SetFloat("Active", 0f);
        Tick(scene, 120);
        animator.GraphInstance!.SetFloat("Active", 1f);
        Tick(scene, 180);

        Float3 Part(string name) => PuppetPart(scene, name).Position;
        Assert.InRange(Float3.Distance(Part("LeftUpperArm"), Part("LeftLowerArm")), 0.25f, 0.35f);
        Assert.True(Degrees(PuppetPart(scene, "Hips").Rotation, TestHumanoid.Hips(root).Rotation) < 3f);
        Assert.True(Float3.Distance(Part("Hips"), TestHumanoid.Hips(root).Position) < 0.05f);
    }

    // A walk at 1 Hz: hips twist and bob, the spine counter twists, legs swing within a hip's range with
    // knees bending, arms swing.
    private static AnimationClip WalkClip(MotionSkeleton skeleton, Avatar avatar)
    {
        const int frames = 61;
        var poses = new Pose[frames];
        for (int f = 0; f < frames; f++)
        {
            float t = f / (float)(frames - 1) * MathF.PI * 2f;
            var pose = new Pose(skeleton);
            pose.SetToReferencePose();
            void Set(string bone, Quaternion rotation, Float3 shift = default) => TestHumanoid.Turn(pose, skeleton, bone, rotation, shift);
            Quaternion Ax(float x, float y, float z, float degrees) => TestHumanoid.About(x, y, z, degrees);
            Set("Hips", Ax(0, 1, 0, 10f * MathF.Sin(t)) * Ax(0, 0, 1, 3f * MathF.Sin(t)), new Float3(0f, -0.03f * MathF.Abs(MathF.Cos(t)), 0f));
            Set("Spine", Ax(0, 1, 0, -12f * MathF.Sin(t)));
            Set("LeftShoulder", Ax(0, 0, 1, 8f * MathF.Sin(t)));
            Set("RightShoulder", Ax(0, 0, 1, 8f * MathF.Sin(t)));
            Set("Neck", Ax(1, 0, 0, 6f * MathF.Sin(t * 2f)));
            Set("LeftUpperLeg", Ax(1, 0, 0, -10f - 25f * MathF.Sin(t)));
            Set("RightUpperLeg", Ax(1, 0, 0, -10f + 25f * MathF.Sin(t)));
            Set("LeftLowerLeg", Ax(1, 0, 0, 25f + 25f * MathF.Sin(t - 1.2f)));
            Set("RightLowerLeg", Ax(1, 0, 0, 25f + 25f * MathF.Sin(t + MathF.PI - 1.2f)));
            Set("LeftUpperArm", Ax(0, 0, 1, 70f) * Ax(0, 1, 0, 25f * MathF.Sin(t)));
            Set("RightUpperArm", Ax(0, 0, 1, -70f) * Ax(0, 1, 0, 25f * MathF.Sin(t)));
            poses[f] = pose;
        }
        var clip = new Motion.AnimationClip(skeleton, poses, 1f);
        return AnimationClip.FromSkeletal(clip, new AssetRef<Avatar>(avatar), "Walk");
    }

    // Two copies of the character play the same clip under a player moved after the animation ticks, in a
    // game at 144 fps with uneven frames and physics at 60: one plain, one through a Ragdoll node with
    // Blend 1. Returns the worst angle any shown bone strays from the plain copy's, and how much the
    // ragdoll's hips twist back and forth on top of the animation: the frame to frame change in their yaw
    // error. What is shown is what counts, so nothing here looks at the bodies themselves.
    private (float Worst, float Shake) RagdollFollowing(Func<MotionSkeleton, Avatar, AnimationClip> makeClip, float speed, float muscle, float pin,
        RagdollPinning pinning = RagdollPinning.All)
    {
        Scene scene = CreateScene();
        var floor = CreateGameObject("Floor");
        floor.Transform.Position = new Float3(0f, -0.5f, 0f);
        floor.AddComponent<BoxCollider>().Size = new Float3(400f, 1f, 400f);
        scene.Add(floor);

        (MotionSkeleton skeleton, _) = TestHumanoid.Build();
        Avatar avatar = Avatar.CreateAutomatic(skeleton, out _, "Character");
        AnimationClip clip = makeClip(skeleton, avatar);

        (GameObject Player, Dictionary<string, Transform> Bones) Spawn(float x, bool ragdoll)
        {
            GameObject player = CreateGameObject(ragdoll ? "Ragdolled" : "Plain");
            player.Transform.Position = new Float3(x, 0f, 0f);
            scene.Add(player);
            (_, GameObject character) = TestHumanoid.Build();
            character.SetParent(player, false);

            var graph = new AnimationGraph();
            GraphNodeRecord clipNode = graph.AddNode(AnimationNodeIds.Clip);
            clipNode.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(clip));
            clipNode.Properties["Looping"] = NodeValue.FromText("Loop");
            graph.RootNode = clipNode.Id;
            if (ragdoll)
            {
                GraphNodeRecord node = graph.AddNode(AnimationNodeIds.Ragdoll);
                node.Wire(clipNode.Id);
                node.Properties["Muscle Strength"] = NodeValue.FromNumber(muscle);
                node.Properties["Pin Strength"] = NodeValue.FromNumber(pin);
                node.Properties["Pinning"] = NodeValue.FromText(pinning.ToString());
                graph.RootNode = node.Id;
            }

            var animator = character.AddComponent<Animator>();
            animator.Avatar = new AssetRef<Avatar>(avatar);
            animator.Graph = new AssetRef<AnimationGraph>(graph);

            var bones = new Dictionary<string, Transform>();
            void Walk(GameObject go)
            {
                bones[go.Name] = go.Transform;
                foreach (GameObject child in go.Children) Walk(child);
            }
            Walk(character);
            return (player, bones);
        }

        var plain = Spawn(-3f, false);
        var ragdolled = Spawn(3f, true);
        scene.Enable();
        scene.Physics.UseMultithreading = false;

        static float Yaw(Quaternion q)
        {
            Float3 forward = q * new Float3(0f, 0f, 1f);
            return MathF.Atan2(forward.X, forward.Z) * 180f / MathF.PI;
        }

        TimeData time = Time.CurrentTime;
        var random = new Random(7);
        float worst = 0f, shake = 0f, previous = 0f, beforeThat = 0f;
        int samples = 0;
        try
        {
            for (int frame = 0; frame < 144 * 4; frame++)
            {
                time.FrameCount++;
                time.DeltaTime = (1f / 144f) * (0.5f + (float)random.NextDouble());
                Time.FixedAccumulator += time.DeltaTime;
                for (int steps = 0; Time.FixedAccumulator >= Time.FixedDeltaTime && steps < 3; steps++)
                {
                    scene.FixedUpdate();
                    Time.FixedAccumulator -= Time.FixedDeltaTime;
                }
                scene.Update();
                plain.Player.Transform.Position += new Float3(0f, 0f, speed * time.DeltaTime);
                ragdolled.Player.Transform.Position += new Float3(0f, 0f, speed * time.DeltaTime);
                EngineObject.ProcessDestroyed();
                if (frame < 144) continue;

                foreach ((string name, Transform bone) in plain.Bones)
                    worst = MathF.Max(worst, Degrees(ragdolled.Bones[name].Rotation, bone.Rotation));

                float error = Yaw(ragdolled.Bones["Hips"].Rotation) - Yaw(plain.Bones["Hips"].Rotation);
                shake += MathF.Abs(error - 2f * previous + beforeThat);
                samples++;
                beforeThat = previous;
                previous = error;
            }
        }
        finally
        {
            Time.FixedAccumulator = 0f;
        }
        return (worst, shake / samples);
    }

    [Theory]
    [InlineData(1f, 0.5f, 1.5f)]
    [InlineData(1f, 1f, 1.5f)]
    public void AWalkingRagdoll_ShowsTheAnimationClosely(float muscle, float pin, float maxDegrees)
    {
        (float worst, float shake) = RagdollFollowing(WalkClip, 1.4f, muscle, pin);
        Assert.True(worst < maxDegrees, $"worst rotation error {worst:F1} degrees");
        Assert.True(shake < 0.04f, $"hips shake {shake:F3} degrees per frame");
    }

    // A fast clip keyed every few frames, so its speed changes at every key.
    [Theory]
    [InlineData(1f, 0.5f, 3f)]
    [InlineData(1f, 1f, 6f)]
    public void ASprintingRagdoll_ShowsTheAnimationClosely(float muscle, float pin, float maxDegrees)
    {
        (float worst, _) = RagdollFollowing(RunClip, 4f, muscle, pin);
        Assert.True(worst < maxDegrees, $"worst rotation error {worst:F1} degrees");
    }

    // Hanging from the hips alone the body sways more, but still follows.
    [Fact]
    public void AWalkingRagdollPinnedByTheHips_FollowsTheAnimation()
    {
        (float worst, _) = RagdollFollowing(WalkClip, 1.4f, 1f, 1f, RagdollPinning.Hips);
        Assert.True(worst < 5f, $"worst rotation error {worst:F1} degrees");
    }

    // Standing with the arms hanging, where the animation lays them against the chest, past where a joint
    // limit centred on the T pose would let them go.
    private static AnimationClip ArmsDownClip(MotionSkeleton skeleton, Avatar avatar)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        TestHumanoid.Turn(pose, skeleton, "LeftUpperArm", TestHumanoid.About(0f, 0f, 1f, 90f));
        TestHumanoid.Turn(pose, skeleton, "RightUpperArm", TestHumanoid.About(0f, 0f, 1f, -90f));
        var clip = new Motion.AnimationClip(skeleton, new[] { pose, pose }, 1f);
        return AnimationClip.FromSkeletal(clip, new AssetRef<Avatar>(avatar), "Arms Down");
    }

    [Fact]
    public void ArmsTheAnimationRestsOnTheChest_HangWhereItPutsThem()
    {
        (float worst, _) = RagdollFollowing(ArmsDownClip, 0f, 1f, 0.5f);
        Assert.True(worst < 1f, $"worst rotation error {worst:F1} degrees");
    }

    [Fact]
    public void DisablingTheAnimator_RemovesThePuppet()
    {
        (Scene scene, Animator animator, _) = SetupRagdoll();
        Tick(scene, 3);

        animator.Enabled = false;
        Tick(scene, 1);

        Assert.Null(Puppet(scene));
    }
}
