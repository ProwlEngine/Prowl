// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Motion;
using Prowl.Runtime.AnimationNodes;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Xunit;

using MotionGraph = Prowl.Motion.AnimationGraph;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime.Test;

/// <summary>The graph asset: node records compiled through the registry into a Motion graph.</summary>
public class AnimationGraphTests : RuntimeTestBase
{
    private static MotionSkeleton BuildSkeleton() => new(
        new[] { new StringID("Rig"), new StringID("Hips"), new StringID("Spine") },
        new[] { MotionSkeleton.InvalidIndex, 0, 1 },
        new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        });

    // A clip holding the spine at a chosen height, so a blend between two reads off as a number.
    private static AnimationClip HeldClip(MotionSkeleton skeleton, Avatar avatar, float height, string name)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(2, new Transform3D(new Float3(0f, height, 0f), Quaternion.Identity, Float3.One));

        var clip = new Motion.AnimationClip(skeleton, new[] { pose, pose }, 1f);
        return AnimationClip.FromSkeletal(clip, new AssetRef<Avatar>(avatar), name);
    }

    // ---- the registry ---------------------------------------------------------------------------

    [Fact]
    public void EveryNodeTypeIsUniqueAndNamed()
    {
        IReadOnlyList<AnimationGraphNode> all = AnimationNodeRegistry.All;

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.All(all, type =>
        {
            Assert.False(string.IsNullOrWhiteSpace(type.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(type.Category));
        });
    }

    public sealed class ProjectNode : AnimationGraphNode
    {
        private readonly InputPin _pose = PoseInput();

        public ProjectNode() => Pins(_pose);

        public override string Id => "test.projectNode";
        public override string DisplayName => "Project Node";
        public override string Category => "Tests";

        public override int Build(GraphCompileContext ctx, GraphNodeRecord r) => ctx.Graph.AddPassthrough(ctx.Input(r, _pose));
    }

    [Fact]
    public void ANodeDefinedOutsideTheEngine_IsFoundWithoutRegistering()
    {
        AnimationGraphNode? found = AnimationNodeRegistry.Get("test.projectNode");

        Assert.IsType<ProjectNode>(found);
        Assert.Contains(found, AnimationNodeRegistry.All);
    }

    /// <summary>
    /// Every registered type has to survive a compile and a bind on its own. A node whose inputs are
    /// unwired falls back to something inert rather than throwing, which is what the editor shows while
    /// a graph is half built.
    /// </summary>
    [Fact]
    public void EveryNodeTypeCompilesOnItsOwn()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");
        var failures = new List<string>();

        foreach (AnimationGraphNode type in AnimationNodeRegistry.All)
        {
            var asset = new AnimationGraph { Name = type.Id };
            GraphNodeRecord record = Place(asset, type);
            asset.RootNode = Reach(asset, record, type.Output, depth: 0);

            MotionGraph? compiled = asset.Compile(skeleton, avatar.Runtime);
            Assert.True(compiled != null, $"'{type.Id}' did not compile");

            // A muscle space node needs a rig mapped to the human body, which this one is not.
            if (type.RequiresHumanoid) continue;

            try { compiled!.CreateInstance(skeleton); }
            catch (GraphValidationException ex) { failures.Add($"{type.Id}: {ex.Message}"); }
        }

        Assert.True(failures.Count == 0, string.Join(System.Environment.NewLine, failures));
    }

    private static GraphNodeRecord Place(AnimationGraph asset, AnimationGraphNode type)
    {
        GraphNodeRecord record = asset.AddNode(type.Id);
        foreach (NodeSetting setting in type.Properties)
            record.Properties[setting.Key] = setting.CreateValue();
        return record;
    }

    /// <summary>
    /// Wires a node up to the graph's root. A pose node is the root; anything else is fed into a node
    /// that takes its kind, found from the pin descriptions themselves, and that one is wired up in
    /// turn. It is the pin kinds doing the work, which is the point.
    /// </summary>
    private static string Reach(AnimationGraph asset, GraphNodeRecord record, NodePinKind kind, int depth)
    {
        if (kind == NodePinKind.Pose) return record.Id;
        Assert.True(depth < 4, $"nothing in the registry consumes a {kind}");

        (AnimationGraphNode consumer, int pin) = Consumer(kind);
        GraphNodeRecord holder = Place(asset, consumer);

        for (int i = 0; i < consumer.Inputs.Count; i++)
        {
            if (i == pin) { holder.Wire(record.Id); continue; }

            // A pose pin gets a real pose, so a node that needs at least one option has one.
            holder.Wire(consumer.Inputs[i].Kind == NodePinKind.Pose ? asset.AddNode(AnimationNodeIds.ReferencePose).Id : string.Empty);
        }

        return Reach(asset, holder, consumer.Output, depth + 1);
    }

    private static (AnimationGraphNode Type, int Pin) Consumer(NodePinKind kind)
    {
        // A pose node ends the chain, so prefer one.
        foreach (bool poseOnly in new[] { true, false })
            foreach (AnimationGraphNode type in AnimationNodeRegistry.All)
            {
                if (poseOnly && !type.IsPose) continue;
                if (type.RequiresHumanoid) continue;

                for (int i = 0; i < type.Inputs.Count; i++)
                    if (type.Inputs[i].Kind == kind) return (type, i);
            }

        Assert.Fail($"no node takes a {kind}");
        return default;
    }

    // ---- compiling ------------------------------------------------------------------------------

    [Fact]
    public void AGraphWithNoRootCompilesToNothing()
    {
        var asset = new AnimationGraph();
        asset.AddNode(AnimationNodeIds.ReferencePose);

        Assert.Null(asset.Compile(BuildSkeleton()));
    }

    [Fact]
    public void ACycleIsReportedRatherThanHanging()
    {
        var asset = new AnimationGraph();
        GraphNodeRecord a = asset.AddNode(AnimationNodeIds.Passthrough, "a");
        GraphNodeRecord b = asset.AddNode(AnimationNodeIds.Passthrough, "b");
        a.Wire("b");
        b.Wire("a");
        asset.RootNode = "a";

        Assert.NotNull(asset.Compile(BuildSkeleton()));
    }

    [Fact]
    public void TheCompiledGraphIsCachedPerRig()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        var asset = new AnimationGraph();
        asset.RootNode = asset.AddNode(AnimationNodeIds.ReferencePose).Id;

        MotionGraph? first = asset.Compile(skeleton);
        Assert.Same(first, asset.Compile(skeleton));

        asset.Invalidate();
        Assert.NotSame(first, asset.Compile(skeleton));
    }

    // ---- a graph that actually animates -----------------------------------------------------------

    private (Scene Scene, Animator Animator, GameObject Root) BlendGraph(float parameter)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Blend" };
        asset.Parameters.Add(new GraphParameterRecord { Name = "Speed", Kind = NodeValueKind.Number, Number = parameter });

        GraphNodeRecord low = asset.AddNode(AnimationNodeIds.Clip, "low");
        low.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 1f, "Low")));

        GraphNodeRecord high = asset.AddNode(AnimationNodeIds.Clip, "high");
        high.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 3f, "High")));

        GraphNodeRecord speed = asset.AddNode(AnimationNodeIds.Parameter, "speed");
        speed.Properties["Name"] = NodeValue.FromText("Speed");

        GraphNodeRecord blend = asset.AddNode(AnimationNodeIds.Blend1D, "blend");
        blend.Wire("speed");
        blend.Wire("low").Value = 0f;
        blend.Wire("high").Value = 1f;
        asset.RootNode = blend.Id;

        return Rigged(asset, avatar);
    }

    private (Scene Scene, Animator Animator, GameObject Root) Rigged(AnimationGraph asset, Avatar avatar)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Rig");
        var hips = new GameObject("Hips");
        var spine = new GameObject("Spine");
        hips.SetParent(root, false);
        spine.SetParent(hips, false);
        scene.Add(root);

        var animator = root.AddComponent<Animator>();
        animator.Avatar = new AssetRef<Avatar>(avatar);
        animator.Graph = new AssetRef<AnimationGraph>(asset);

        scene.Enable();
        return (scene, animator, root);
    }

    // A node's own name is only a label, so it never takes the name a slot is found by.
    [Fact]
    public void ASlotNamedLikeAnotherNode_StillCompiles_AndTakesAGraph()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var host = new AnimationGraph { Name = "Host" };
        HeldClipNode(host, skeleton, avatar, "fallback", 1f).Name = "Upper";
        GraphNodeRecord slot = host.AddNode(AnimationNodeIds.ExternalGraphSlot, "slot");
        slot.Wire("fallback");
        slot.Properties["Slot"] = NodeValue.FromText("Upper");
        GraphNodeRecord twin = host.AddNode(AnimationNodeIds.ExternalGraphSlot, "twin");
        twin.Wire("slot");
        twin.Properties["Slot"] = NodeValue.FromText("Upper");
        host.RootNode = twin.Id;

        var plugged = new AnimationGraph { Name = "Plugged" };
        HeldClipNode(plugged, skeleton, avatar, "held", 3f);
        plugged.RootNode = "held";

        (Scene scene, Animator animator, GameObject root) = Rigged(host, avatar);
        animator.SetGraphSlot("Upper", plugged);
        Update(scene, 2);

        Assert.True(animator.IsGraph);
        Assert.Equal(3f, Spine(root).LocalPosition.Y, 1);
    }

    [Fact]
    public void EveryKindOfParameter_IsReadBeforeBinding_AndCarriedAcrossARebind()
    {
        (Scene scene, Animator animator, _) = BlendGraph(0f);
        AnimationGraph asset = animator.Graph.Res!;
        asset.Parameters.Add(new GraphParameterRecord { Name = "Aim", Kind = NodeValueKind.Target });
        asset.Parameters.Add(new GraphParameterRecord { Name = "Tag", Kind = NodeValueKind.Id });
        asset.Invalidate();

        var aim = Target.FromWorld(new Transform3D(new Float3(1f, 2f, 3f), Quaternion.Identity, Float3.One));
        animator.SetTarget("Aim", aim);
        animator.SetId("Tag", new StringID("Hit"));
        animator.SetFloat("Speed", 0.5f);
        Assert.Equal(0.5f, animator.GetFloat("Speed"));

        Update(scene, 1);
        asset.Invalidate();
        System.Threading.Thread.Sleep(200);
        Update(scene, 1);

        Assert.True(animator.GetTarget("Aim").IsSet);
        Assert.Equal(new StringID("Hit"), animator.GetId("Tag"));
        Assert.Equal(0.5f, animator.GetFloat("Speed"), 3);
        Assert.Equal(0f, animator.GetFloat("Missing"));
    }

    // What cannot be loaded is missing, so the graph starts at once without it.
    [Fact]
    public void AGraphReadingAMissingClip_StartsOnTheFirstUpdate()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Missing" };
        GraphNodeRecord clip = asset.AddNode(AnimationNodeIds.Clip, "clip");
        clip.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(Guid.NewGuid()));
        asset.RootNode = clip.Id;

        (Scene scene, Animator animator, _) = Rigged(asset, avatar);
        Update(scene, 1);

        Assert.True(animator.IsGraph);
    }

    [Fact]
    public void AHumanoidMappingThatDoesNotWork_PlaysAsAGenericRig()
    {
        (MotionSkeleton skeleton, _) = TestHumanoid.Build();
        var description = new HumanDescription();
        foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>())
            if (skeleton.GetBoneIndex(new StringID(bone.ToString())) is int index and >= 0)
                description.SetSkeletonBoneIndex(bone, index);
        description.SetSkeletonBoneIndex(HumanBodyBone.Chest, description.GetSkeletonBoneIndex(HumanBodyBone.UpperChest));

        Avatar avatar = Avatar.CreateHumanoid(skeleton, description, "Broken");

        Assert.NotNull(avatar.Runtime);
        Assert.False(avatar.IsHuman);
    }

    [Fact]
    public void AGraphPluggedIntoASlot_PlaysInPlaceOfTheFallback_AndSurvivesUntilTakenOut()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var host = new AnimationGraph { Name = "Host" };
        HeldClipNode(host, skeleton, avatar, "fallback", 1f);
        GraphNodeRecord slot = host.AddNode(AnimationNodeIds.ExternalGraphSlot, "slot");
        slot.Wire("fallback");
        slot.Properties["Slot"] = NodeValue.FromText("Upper");
        host.RootNode = slot.Id;

        var plugged = new AnimationGraph { Name = "Plugged" };
        HeldClipNode(plugged, skeleton, avatar, "held", 3f);
        plugged.RootNode = "held";

        (Scene scene, Animator animator, GameObject root) = Rigged(host, avatar);
        animator.SetGraphSlot("Upper", plugged);
        Update(scene, 2);
        Assert.Equal(3f, Spine(root).LocalPosition.Y, 1);

        animator.Rebind();
        Update(scene, 2);
        Assert.Equal(3f, Spine(root).LocalPosition.Y, 1);

        // An edit to the plugged graph is picked up once it has settled.
        HeldClipNode(plugged, skeleton, avatar, "higher", 5f);
        plugged.RootNode = "higher";
        plugged.Invalidate();
        System.Threading.Thread.Sleep(200);
        Update(scene, 2);
        Assert.Equal(5f, Spine(root).LocalPosition.Y, 1);

        animator.SetGraphSlot("Upper", null);
        Update(scene, 2);
        Assert.Equal(1f, Spine(root).LocalPosition.Y, 1);
    }

    [Fact]
    public void TheGameWritesAnExternalPoseByName_AndClearingItReturnsToTheReferencePose()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");
        var asset = new AnimationGraph { Name = "External" };
        GraphNodeRecord external = asset.AddNode(AnimationNodeIds.ExternalPose, "external");
        external.Properties["Name"] = NodeValue.FromText("Hit");
        asset.RootNode = external.Id;

        (Scene scene, Animator animator, GameObject root) = Rigged(asset, avatar);
        Update(scene, 1);

        Assert.Null(animator.GetExternalPose("Missing"));

        Pose pose = animator.GetExternalPose("Hit")!;
        pose.SetTransform(2, new Transform3D(new Float3(0f, 4f, 0f), Quaternion.Identity, Float3.One));
        Update(scene, 1);
        Assert.Equal(4f, Spine(root).LocalPosition.Y, 3);

        // A rebind keeps showing what the game last wrote.
        animator.Rebind();
        Update(scene, 1);
        Assert.Equal(4f, Spine(root).LocalPosition.Y, 3);

        animator.ClearExternalPose("Hit");
        Update(scene, 1);
        Assert.Equal(1f, Spine(root).LocalPosition.Y, 3);
    }

    private static Transform Spine(GameObject root) => root.Children[0].Children[0].Transform;

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.5f, 2f)]
    [InlineData(1f, 3f)]
    public void AGraphBlendsBetweenClipsByParameter(float parameter, float expected)
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(parameter);

        Update(scene, 2);

        Assert.True(animator.IsGraph);
        Assert.Equal(expected, Spine(root).LocalPosition.Y, 1);
    }

    [Fact]
    public void ScriptCanDriveTheParameterAfterBinding()
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(0f);
        Update(scene, 2);

        animator.SetFloat("Speed", 1f);
        Update(scene, 2);

        Assert.Equal(3.0, Spine(root).LocalPosition.Y, 1);
    }

    /// <summary>
    /// An edit made while the game runs has to reach the animators already running the graph, once
    /// the edit has settled. They built their graph when they bound, so without this they would play
    /// the old one until the scene was reloaded.
    /// </summary>
    [Fact]
    public void AnEditReachesAnAnimatorAlreadyRunningTheGraph()
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(1f);
        Update(scene, 2);
        Assert.Equal(3.0, Spine(root).LocalPosition.Y, 1);

        // The high clip's threshold moves from 1 to 2, so a parameter of 1 now sits halfway between.
        AnimationGraph asset = animator.Graph.Res!;
        asset.Find("blend")!.Inputs[2].Value = 2f;
        asset.Invalidate();

        System.Threading.Thread.Sleep(200);
        Update(scene, 2);

        Assert.Equal(2.0, Spine(root).LocalPosition.Y, 1);
    }

    [Fact]
    public void PickingUpAnEditKeepsTheParametersTheGameSet()
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(1f);
        Update(scene, 2);

        animator.SetFloat("Speed", 0f);
        Update(scene, 2);
        Assert.Equal(1.0, Spine(root).LocalPosition.Y, 1);

        // An edit that changes nothing about the blend, so all that could move the spine is the rebind
        // dropping the parameter back to its authored default of 1.
        AnimationGraph asset = animator.Graph.Res!;
        asset.Invalidate();
        System.Threading.Thread.Sleep(200);
        Update(scene, 2);

        Assert.Equal(0f, animator.GetFloat("Speed"), 3);
        Assert.Equal(1.0, Spine(root).LocalPosition.Y, 1);
    }

    /// <summary>
    /// Saving reimports the graph, which throws the old instance away and loads a new one. A running
    /// animator has to follow it straight away, or it keeps playing a graph nothing can see any more.
    /// </summary>
    [Fact]
    public void AnAnimatorFollowsTheGraphWhenItIsReplaced()
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(1f);
        Update(scene, 2);

        AnimationGraph old = animator.Graph.Res!;
        var saved = Serializer.Deserialize<AnimationGraph>(Serializer.Serialize(typeof(object), old))!;
        saved.Find("blend")!.Inputs[2].Value = 2f;
        animator.Graph = new AssetRef<AnimationGraph>(saved);

        Update(scene, 2);

        Assert.Equal(2.0, Spine(root).LocalPosition.Y, 1);
    }

    [Fact]
    public void AnEditStillBeingMadeDoesNotRebindEveryFrame()
    {
        (Scene scene, Animator animator, GameObject _) = BlendGraph(1f);
        Update(scene, 2);
        var before = animator.GraphInstance;

        animator.Graph.Res!.Invalidate();
        Update(scene, 1);

        Assert.Same(before, animator.GraphInstance);
    }

    // Setup code runs before the rig has necessarily streamed in, so an early write has to survive.
    [Fact]
    public void AParameterSetBeforeBindingStillLands()
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(0f);

        animator.SetFloat("Speed", 1f);
        Update(scene, 2);

        Assert.Equal(3.0, Spine(root).LocalPosition.Y, 1);
    }

    /// <summary>
    /// A state whose own graph plays a node, with its Enter and Exit either fixed by their toggles or
    /// wired to a node.
    /// </summary>
    private static GraphStateRecord State(AnimationGraph asset, string name, string pose, bool isDefault = false,
        bool enter = false, bool exit = true, string enterNode = "", string exitNode = "")
    {
        var state = new GraphStateRecord { Id = name + "_state", Name = name, IsDefault = isDefault };
        GraphNodeRecord output = asset.AddNode(AnimationNodeIds.StateOutput, name + "_out");
        output.Owner = state.Id;
        output.Wire(pose);
        output.Wire(enterNode).Flag = enter;
        output.Wire(exitNode).Flag = exit;
        return state;
    }

    [Fact]
    public void AStateMachineStartsInItsDefaultState()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "States" };
        GraphNodeRecord idle = asset.AddNode(AnimationNodeIds.Clip, "idle");
        idle.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 1f, "Idle")));
        GraphNodeRecord run = asset.AddNode(AnimationNodeIds.Clip, "run");
        run.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 3f, "Run")));

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        // Run wants in the moment it can be reached, so staying in Idle proves the default took and the
        // way out is what holds it: Idle never lets go.
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true, exit: false));
        machine.States.Add(State(asset, "Run", "run", enter: true));
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Run", Duration = 0.1f });
        asset.RootNode = machine.Id;

        MotionGraph? compiled = asset.Compile(skeleton, avatar.Runtime);
        Assert.NotNull(compiled);

        AnimationGraphInstance instance = compiled!.CreateInstance(skeleton);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(1.0, instance.Pose.GetTransform(2).position.Y, 1);
    }

    // ---- what a debug view reads back -----------------------------------------------------------

    [Fact]
    public void TheCompileMapFindsTheRunningNodeBehindARecord()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Mapped" };
        GraphNodeRecord clip = asset.AddNode(AnimationNodeIds.Clip, "clip");
        clip.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 2f, "Hold")));
        asset.RootNode = clip.Id;

        MotionGraph? compiled = asset.Compile(skeleton, avatar.Runtime);
        AnimationGraphInstance instance = compiled!.CreateInstance(skeleton);
        instance.Update(1f / 60f, Transform3D.Identity);

        int index = NodesOf(asset, instance)[clip.Id];
        Assert.IsAssignableFrom<PoseNodeInstance>(instance.GetNodeInstance(index));
        Assert.Null(instance.TryGetNodeInstance(9999));

        // An edit drops the map with the graph it described, so nothing reads stale indices.
        asset.Invalidate();
        Assert.False(asset.TryGetCompiledMaps(instance.Graph, out _, out _));
    }

    private static IReadOnlyDictionary<string, int> NodesOf(AnimationGraph asset, AnimationGraphInstance instance)
    {
        Assert.True(asset.TryGetCompiledMaps(instance.Graph, out IReadOnlyDictionary<string, int> nodes, out _));
        return nodes;
    }

    [Fact]
    public void AValueNodeReadsBackOnlyWhenItRan()
    {
        (Scene scene, Animator animator, GameObject _) = BlendGraph(0.5f);
        Update(scene, 2);

        AnimationGraph asset = animator.Graph.Res!;
        AnimationGraphInstance instance = animator.GraphInstance!;

        IReadOnlyDictionary<string, int> nodes = NodesOf(asset, instance);
        int speed = nodes["speed"];
        Assert.True(instance.TryReadValueNode(speed, out ParameterValue value));
        Assert.Equal(0.5f, value.AsFloat(), 3);

        // A pose node is not a value node, and asking for one must not tick anything.
        Assert.False(instance.TryReadValueNode(nodes["low"], out _));
        Assert.False(instance.TryReadValueNode(9999, out _));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void AStateMachineSaysWhichStateItIsIn(bool takeTheTransition, int expectedState)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "States" };
        GraphNodeRecord idle = asset.AddNode(AnimationNodeIds.Clip, "idle");
        idle.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 1f, "Idle")));
        GraphNodeRecord run = asset.AddNode(AnimationNodeIds.Clip, "run");
        run.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, 3f, "Run")));

        GraphNodeRecord condition = asset.AddNode(AnimationNodeIds.ConstBool, "condition");
        condition.Properties["Value"] = NodeValue.FromFlag(takeTheTransition);

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true));
        machine.States.Add(State(asset, "Run", "run", enterNode: "condition"));
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Run", Duration = 0.05f });
        asset.RootNode = machine.Id;

        MotionGraph? compiled = asset.Compile(skeleton, avatar.Runtime);
        AnimationGraphInstance instance = compiled!.CreateInstance(skeleton);

        Assert.True(asset.TryGetCompiledMaps(instance.Graph, out IReadOnlyDictionary<string, int> nodes, out IReadOnlyDictionary<string, int[]> machines));
        int[] states = machines[machine.Id];
        Assert.Equal(machine.States.Count, states.Length);

        var running = (IStateMachineState)instance.GetNodeInstance(nodes[machine.Id]);
        for (int i = 0; i < 20; i++) instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(states[expectedState], running.CurrentStateIndex);
        Assert.False(running.IsTransitioning);
    }

    // ---- review fixes -------------------------------------------------------------------------

    private static float SpineAfter(AnimationGraph asset, MotionSkeleton skeleton, Avatar avatar, int frames = 20)
    {
        MotionGraph? compiled = asset.Compile(skeleton, avatar.Runtime);
        Assert.NotNull(compiled);

        AnimationGraphInstance instance = compiled!.CreateInstance(skeleton);
        for (int i = 0; i < frames; i++) instance.Update(1f / 60f, Transform3D.Identity);
        return instance.Pose.GetTransform(2).position.Y;
    }

    private static GraphNodeRecord HeldClipNode(AnimationGraph asset, MotionSkeleton skeleton, Avatar avatar, string id, float height)
    {
        GraphNodeRecord node = asset.AddNode(AnimationNodeIds.Clip, id);
        node.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldClip(skeleton, avatar, height, id)));
        return node;
    }

    [Fact]
    public void TwoNodesWithTheSameNameStillCompile()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Names" };
        asset.Parameters.Add(new GraphParameterRecord { Name = "Speed", Kind = NodeValueKind.Number, Number = 0.5f });
        HeldClipNode(asset, skeleton, avatar, "low", 1f).Name = "Walk";
        HeldClipNode(asset, skeleton, avatar, "high", 3f).Name = "Walk";
        asset.AddNode(AnimationNodeIds.Parameter, "speed").Properties["Name"] = NodeValue.FromText("Speed");

        GraphNodeRecord blend = asset.AddNode(AnimationNodeIds.Blend1D, "blend");
        blend.Wire("speed");
        blend.Wire("low").Value = 0f;
        blend.Wire("high").Value = 1f;
        asset.RootNode = blend.Id;

        Assert.Equal(2.0, SpineAfter(asset, skeleton, avatar, 2), 1);
    }

    [Fact]
    public void AnUnwiredInputInTheMiddleOfABlendIsSkipped()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Gap" };
        asset.Parameters.Add(new GraphParameterRecord { Name = "Speed", Kind = NodeValueKind.Number, Number = 0.5f });
        HeldClipNode(asset, skeleton, avatar, "low", 1f);
        HeldClipNode(asset, skeleton, avatar, "high", 3f);
        asset.AddNode(AnimationNodeIds.Parameter, "speed").Properties["Name"] = NodeValue.FromText("Speed");

        // The empty input sits at the parameter's value, so were it kept it would win the blend outright.
        GraphNodeRecord blend = asset.AddNode(AnimationNodeIds.Blend1D, "blend");
        blend.Wire("speed");
        blend.Wire("low").Value = 0f;
        blend.Wire("").Value = 0.5f;
        blend.Wire("high").Value = 1f;
        asset.RootNode = blend.Id;

        Assert.Equal(2.0, SpineAfter(asset, skeleton, avatar, 2), 1);
    }

    [Fact]
    public void AnInputWeightedZeroAddsNothing()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Weights" };
        HeldClipNode(asset, skeleton, avatar, "low", 1f);
        HeldClipNode(asset, skeleton, avatar, "high", 3f);

        GraphNodeRecord blend = asset.AddNode(AnimationNodeIds.WeightedBlend, "blend");
        blend.Wire("low").Value = 1f;
        blend.Wire("");
        blend.Wire("high").Value = 0f;
        blend.Wire("");
        asset.RootNode = blend.Id;

        Assert.Equal(1.0, SpineAfter(asset, skeleton, avatar, 2), 1);
    }

    /// <summary>
    /// A state that lets go only once it has run a while asks about the machine it is in. Unwired
    /// inside a state, the query reads that machine, and that must compile rather than be taken for a cycle.
    /// </summary>
    [Theory]
    [InlineData(0.05f, 30, 3.0)]
    [InlineData(10f, 30, 1.0)]
    public void AStateCanHoldOnUntilItHasRunAWhile(float seconds, int frames, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Own" };
        HeldClipNode(asset, skeleton, avatar, "idle", 1f);
        HeldClipNode(asset, skeleton, avatar, "run", 3f);

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true, exitNode: "elapsed"));
        machine.States.Add(State(asset, "Run", "run", enter: true));
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Run", Duration = 0.05f });

        GraphNodeRecord elapsed = asset.AddNode(AnimationNodeIds.StateFinished, "elapsed");
        elapsed.Owner = machine.States[0].Id;
        elapsed.Wire(string.Empty);
        elapsed.Properties["By"] = NodeValue.FromText(nameof(StateDoneMeasure.Seconds));
        elapsed.Properties["Seconds"] = NodeValue.FromNumber(seconds);
        asset.RootNode = machine.Id;

        Assert.Equal(expected, SpineAfter(asset, skeleton, avatar, frames), 1);
    }

    [Theory]
    [InlineData(true, true, 3.0)]
    [InlineData(true, false, 1.0)]
    [InlineData(false, true, 1.0)]
    public void ATransitionFiresOnlyWhenOneStateLetsGoAndTheOtherWantsIn(bool exit, bool enter, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Gates" };
        HeldClipNode(asset, skeleton, avatar, "idle", 1f);
        HeldClipNode(asset, skeleton, avatar, "run", 3f);

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true, exit: exit));
        machine.States.Add(State(asset, "Run", "run", enter: enter));
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Run", Duration = 0.05f });
        asset.RootNode = machine.Id;

        Assert.Equal(expected, SpineAfter(asset, skeleton, avatar, 20), 1);
    }

    [Fact]
    public void AnEmbeddedSubGraphPlaysItsOwnNodes()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Embedded" };
        GraphNodeRecord sub = asset.AddNode(AnimationNodeIds.SubGraph, "sub");
        sub.Properties["Embedded"] = NodeValue.FromFlag(true);

        HeldClipNode(asset, skeleton, avatar, "inner", 2f).Owner = sub.Id;
        GraphNodeRecord output = asset.AddNode(AnimationNodeIds.GraphOutput, "out");
        output.Owner = sub.Id;
        output.Wire("inner");
        asset.RootNode = sub.Id;

        Assert.Equal(2.0, SpineAfter(asset, skeleton, avatar, 2), 1);
    }

    /// <summary>
    /// A state that plays a graph asset is built into the machine's graph, so its Enter can read the
    /// machine's parameters by name like any node of the machine's own.
    /// </summary>
    [Theory]
    [InlineData(false, 1.0)]
    [InlineData(true, 3.0)]
    public void AStateCanPlayAGraphAssetThatReadsTheParameters(bool go, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var run = new AnimationGraph { Name = "Run" };
        run.Parameters.Add(new GraphParameterRecord { Name = "Go", Kind = NodeValueKind.Flag });
        HeldClipNode(run, skeleton, avatar, "run", 3f);
        run.AddNode(AnimationNodeIds.Parameter, "go").Properties["Name"] = NodeValue.FromText("Go");
        GraphNodeRecord output = run.AddNode(AnimationNodeIds.StateOutput, "out");
        output.Wire("run");
        output.Wire("go");
        output.Wire("").Flag = true;

        var asset = new AnimationGraph { Name = "Machine" };
        asset.Parameters.Add(new GraphParameterRecord { Name = "Go", Kind = NodeValueKind.Flag, Flag = go });
        HeldClipNode(asset, skeleton, avatar, "idle", 1f);

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true));
        machine.States.Add(new GraphStateRecord { Id = "runState", Name = "Run", Graph = new AssetRef<AnimationGraph>(run) });
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Run", Duration = 0.05f });
        asset.RootNode = machine.Id;

        Assert.Equal(expected, SpineAfter(asset, skeleton, avatar, 20), 1);
    }

    [Fact]
    public void AnEditToASubGraphChangesTheGraphRunningIt()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var inner = new AnimationGraph { Name = "Inner" };
        inner.RootNode = HeldClipNode(inner, skeleton, avatar, "clip", 2f).Id;

        var outer = new AnimationGraph { Name = "Outer" };
        GraphNodeRecord sub = outer.AddNode(AnimationNodeIds.SubGraph, "sub");
        sub.Properties["Graph"] = NodeValue.FromGraph(new AssetRef<AnimationGraph>(inner));
        outer.RootNode = sub.Id;

        MotionGraph? before = outer.Compile(skeleton, avatar.Runtime);
        int version = outer.DeepVersion;

        inner.Invalidate();

        Assert.NotEqual(version, outer.DeepVersion);
        Assert.NotSame(before, outer.Compile(skeleton, avatar.Runtime));
    }

    [Fact]
    public void EachRigKeepsItsOwnCompileAndMaps()
    {
        MotionSkeleton first = BuildSkeleton();
        MotionSkeleton second = BuildSkeleton();
        var asset = new AnimationGraph();
        asset.RootNode = asset.AddNode(AnimationNodeIds.ReferencePose, "pose").Id;

        MotionGraph? a = asset.Compile(first);
        MotionGraph? b = asset.Compile(second);

        Assert.NotSame(a, b);
        Assert.Same(a, asset.Compile(first));
        Assert.True(asset.TryGetCompiledMaps(a!, out var nodes, out _));
        Assert.True(nodes.ContainsKey("pose"));
        Assert.True(asset.TryGetCompiledMaps(b!, out _, out _));

        asset.Invalidate();
        Assert.False(asset.TryGetCompiledMaps(a!, out _, out _));
    }

    [Fact]
    public void TheLastWriteBeforeBindingIsTheOneThatLands()
    {
        (Scene scene, Animator animator, GameObject root) = BlendGraph(0f);

        animator.SetFloat("Speed", 0.5f);
        animator.SetFloat("Speed", 1f);
        Update(scene, 2);

        Assert.Equal(3.0, Spine(root).LocalPosition.Y, 1);
    }

    [Fact]
    public void ABlendReportsTheShareEachInputCarries()
    {
        (Scene scene, Animator animator, GameObject _) = BlendGraph(0.25f);
        Update(scene, 2);

        AnimationGraph asset = animator.Graph.Res!;
        AnimationGraphInstance instance = animator.GraphInstance!;
        Assert.True(asset.TryGetCompiledMaps(instance.Graph, out var nodes, out _));

        var blend = Assert.IsAssignableFrom<IBlendWeights>(instance.TryGetNodeInstance(nodes["blend"]));
        Assert.Equal(0.75f, blend.WeightOf(nodes["low"]), 2);
        Assert.Equal(0.25f, blend.WeightOf(nodes["high"]), 2);
        Assert.Equal(0f, blend.WeightOf(nodes["speed"]), 3);
    }

    /// <summary>A clip's events fire in a running graph, which is what every event reading node relies on.</summary>
    [Fact]
    public void AClipsEventsFireWhileItPlays()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();

        AnimationClip clip = AnimationClip.FromSkeletal(new Motion.AnimationClip(skeleton, new[] { pose, pose }, 1f),
            new AssetRef<Avatar>(avatar), "Step", new[] { new ClipEvent { Kind = ClipEventKind.Named, Time = 0.5f, Name = "Footstep" } });

        var asset = new AnimationGraph { Name = "Events" };
        asset.RootNode = asset.AddNode(AnimationNodeIds.Clip, "clip").Id;
        asset.Find("clip")!.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(clip));

        AnimationGraphInstance instance = asset.Compile(skeleton, avatar.Runtime)!.CreateInstance(skeleton);
        var firedAt = new List<int>();
        for (int frame = 0; frame < 60; frame++)
        {
            instance.Update(1f / 60f, Transform3D.Identity);
            if (instance.Events.ContainsId(new StringID("Footstep"))) firedAt.Add(frame);
        }

        int only = Assert.Single(firedAt);
        Assert.InRange(only, 28, 31);
    }
    // A clip whose spine rises from nothing to full height over one second.
    private static AnimationClip RampClip(MotionSkeleton skeleton, Avatar avatar, string name)
    {
        var low = new Pose(skeleton);
        low.SetToReferencePose();
        low.SetTransform(2, new Transform3D(Float3.Zero, Quaternion.Identity, Float3.One));

        var high = new Pose(skeleton);
        high.SetToReferencePose();
        high.SetTransform(2, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One));

        return AnimationClip.FromSkeletal(new Motion.AnimationClip(skeleton, new[] { low, high }, 1f),
            new AssetRef<Avatar>(avatar), name);
    }

    private static float SpineHeight(AnimationGraphInstance instance) => (float)instance.Pose.GetTransform(2).position.Y;

    /// <summary>A clip can start part way in, which is what a randomly offset idle relies on.</summary>
    [Fact]
    public void AClipCanStartPartWayThrough()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Offset" };
        GraphNodeRecord clip = asset.AddNode(AnimationNodeIds.Clip, "clip");
        clip.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(RampClip(skeleton, avatar, "Ramp")));
        clip.Properties["Start"] = NodeValue.FromNumber(0.5f);
        asset.RootNode = clip.Id;

        AnimationGraphInstance instance = asset.Compile(skeleton, avatar.Runtime)!.CreateInstance(skeleton);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.InRange(SpineHeight(instance), 0.45f, 0.6f);
    }

    /// <summary>The looping choice overrides what the clip was imported as, in both directions.</summary>
    [Theory]
    [InlineData("Loop", true)]
    [InlineData("Once", false)]
    public void AClipFollowsTheLoopingItIsSetTo(string looping, bool wraps)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        AnimationClip source = RampClip(skeleton, avatar, "Ramp");
        source.Loop = !wraps;

        var asset = new AnimationGraph { Name = "Looping" };
        GraphNodeRecord clip = asset.AddNode(AnimationNodeIds.Clip, "clip");
        clip.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(source));
        clip.Properties["Looping"] = NodeValue.FromText(looping);
        asset.RootNode = clip.Id;

        AnimationGraphInstance instance = asset.Compile(skeleton, avatar.Runtime)!.CreateInstance(skeleton);
        for (int frame = 0; frame < 75; frame++) instance.Update(1f / 60f, Transform3D.Identity);

        // A quarter of a second past the end is a quarter of the way up again, or still held at the top.
        Assert.InRange(SpineHeight(instance), wraps ? 0.15f : 0.9f, wraps ? 0.35f : 1.01f);
    }

    /// <summary>
    /// A setting exposed as an input is read from the graph rather than from the node, which is what
    /// lets a number node tune it while the game runs.
    /// </summary>
    [Fact]
    public void AnExposedSettingIsReadFromWhatDrivesIt()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");
        AnimationClip ramp = RampClip(skeleton, avatar, "Ramp");

        float Run(bool exposed)
        {
            var asset = new AnimationGraph { Name = "Driven" };
            GraphNodeRecord clip = asset.AddNode(AnimationNodeIds.Clip, "clip");
            clip.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(ramp));

            GraphNodeRecord half = asset.AddNode(AnimationNodeIds.ConstFloat, "half");
            half.Properties["Value"] = NodeValue.FromNumber(0.5f);

            GraphNodeRecord smooth = asset.AddNode(AnimationNodeIds.PoseSmoothing, "smooth");
            smooth.Inputs.Add(new GraphInputRecord { Node = clip.Id });
            smooth.Properties["HalfLife"] = NodeValue.FromNumber(0f);
            if (exposed) smooth.PropertyInputs["HalfLife"] = half.Id;
            asset.RootNode = smooth.Id;

            AnimationGraphInstance instance = asset.Compile(skeleton, avatar.Runtime)!.CreateInstance(skeleton);
            for (int frame = 0; frame < 30; frame++) instance.Update(1f / 60f, Transform3D.Identity);
            return SpineHeight(instance);
        }

        float direct = Run(false);
        float driven = Run(true);

        Assert.InRange(direct, 0.45f, 0.55f);
        Assert.True(driven < direct - 0.05f, $"smoothing did not lag: {driven} against {direct}");
    }

    // A rig with every bone the human body needs, named nothing like the humanoid bones it maps to.
    private static (MotionSkeleton Skeleton, Avatar Avatar, int Spine) HumanoidRig()
    {
        (string Name, string Parent, HumanBodyBone Bone, Float3 Offset)[] layout =
        {
            ("b_root", "", HumanBodyBone.Hips, new Float3(0f, 1f, 0f)),
            ("b_belly", "b_root", HumanBodyBone.Spine, new Float3(0f, 0.2f, 0f)),
            ("b_skull", "b_belly", HumanBodyBone.Head, new Float3(0f, 0.5f, 0f)),
            ("b_armL", "b_belly", HumanBodyBone.LeftUpperArm, new Float3(0.2f, 0.4f, 0f)),
            ("b_elbowL", "b_armL", HumanBodyBone.LeftLowerArm, new Float3(0.3f, 0f, 0f)),
            ("b_handL", "b_elbowL", HumanBodyBone.LeftHand, new Float3(0.3f, 0f, 0f)),
            ("b_armR", "b_belly", HumanBodyBone.RightUpperArm, new Float3(-0.2f, 0.4f, 0f)),
            ("b_elbowR", "b_armR", HumanBodyBone.RightLowerArm, new Float3(-0.3f, 0f, 0f)),
            ("b_handR", "b_elbowR", HumanBodyBone.RightHand, new Float3(-0.3f, 0f, 0f)),
            ("b_legL", "b_root", HumanBodyBone.LeftUpperLeg, new Float3(0.1f, -0.1f, 0f)),
            ("b_kneeL", "b_legL", HumanBodyBone.LeftLowerLeg, new Float3(0f, -0.45f, 0f)),
            ("b_footL", "b_kneeL", HumanBodyBone.LeftFoot, new Float3(0f, -0.45f, 0.05f)),
            ("b_legR", "b_root", HumanBodyBone.RightUpperLeg, new Float3(-0.1f, -0.1f, 0f)),
            ("b_kneeR", "b_legR", HumanBodyBone.RightLowerLeg, new Float3(0f, -0.45f, 0f)),
            ("b_footR", "b_kneeR", HumanBodyBone.RightFoot, new Float3(0f, -0.45f, 0.05f)),
        };

        var ids = new StringID[layout.Length];
        var parents = new int[layout.Length];
        var pose = new Transform3D[layout.Length];
        var description = new HumanDescription();

        for (int i = 0; i < layout.Length; i++)
        {
            ids[i] = new StringID(layout[i].Name);
            parents[i] = layout[i].Parent.Length == 0
                ? MotionSkeleton.InvalidIndex
                : System.Array.FindIndex(layout, e => e.Name == layout[i].Parent);
            pose[i] = new Transform3D(layout[i].Offset, Quaternion.Identity, Float3.One);
            description.SetSkeletonBoneIndex(layout[i].Bone, i);
        }

        var skeleton = new MotionSkeleton(ids, parents, pose);
        return (skeleton, Avatar.CreateHumanoid(skeleton, description, "Human"), 1);
    }

    // A clip holding one bone at a chosen height, for a rig whose spine is not bone 2.
    private static AnimationClip HeldAt(MotionSkeleton skeleton, Avatar avatar, int bone, float height, string name)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(bone, new Transform3D(new Float3(0f, height, 0f), Quaternion.Identity, Float3.One));

        return AnimationClip.FromSkeletal(new Motion.AnimationClip(skeleton, new[] { pose, pose }, 1f),
            new AssetRef<Avatar>(avatar), name);
    }

    /// <summary>
    /// A bone written as a humanoid bone is the bone the rig maps it to, so the same graph masks the
    /// right bone whatever that rig happens to call it.
    /// </summary>
    [Theory]
    [InlineData("b_belly", 2.0)]
    [InlineData("@Spine", 2.0)]
    [InlineData("@LeftEye", 1.0)]
    public void AHumanoidBoneNameResolvesThroughTheRig(string bone, double height)
    {
        (MotionSkeleton skeleton, Avatar avatar, int spine) = HumanoidRig();
        Motion.Avatar? runtime = avatar.Runtime;
        Assert.NotNull(runtime);

        var asset = new AnimationGraph { Name = "Bones" };
        GraphNodeRecord mask = asset.AddNode(AnimationNodeIds.BoneMask, "mask");
        mask.Properties["Bones"] = NodeValue.FromText(bone);

        GraphNodeRecord low = asset.AddNode(AnimationNodeIds.Clip, "low");
        low.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldAt(skeleton, avatar, spine, 1f, "Low")));

        GraphNodeRecord high = asset.AddNode(AnimationNodeIds.Clip, "high");
        high.Properties["Clip"] = NodeValue.FromClip(new AssetRef<AnimationClip>(HeldAt(skeleton, avatar, spine, 2f, "High")));

        GraphNodeRecord layers = asset.AddNode(AnimationNodeIds.LayerBlend, "layers");
        layers.Inputs.Add(new GraphInputRecord { Node = low.Id });
        layers.Inputs.Add(new GraphInputRecord { Node = high.Id, Value = 1f });
        layers.Inputs.Add(new GraphInputRecord());
        layers.Inputs.Add(new GraphInputRecord { Node = mask.Id });
        asset.RootNode = layers.Id;

        AnimationGraphInstance instance = asset.Compile(skeleton, runtime)!.CreateInstance(skeleton);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(height, instance.Pose.GetTransform(spine).position.Y, 1);
    }

    /// <summary>
    /// A transition from the Any State leaves whatever state the machine is in, once the state it leads
    /// to wants in, without an arrow from each one.
    /// </summary>
    [Theory]
    [InlineData(false, 3.0)]
    [InlineData(true, 5.0)]
    public void TheAnyStateLeavesWhicheverStateTheMachineIsIn(bool hit, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Any" };
        asset.Parameters.Add(new GraphParameterRecord { Name = "Hit", Kind = NodeValueKind.Flag, Flag = hit });
        asset.AddNode(AnimationNodeIds.Parameter, "hit").Properties["Name"] = NodeValue.FromText("Hit");
        HeldClipNode(asset, skeleton, avatar, "idle", 1f);
        HeldClipNode(asset, skeleton, avatar, "run", 3f);
        HeldClipNode(asset, skeleton, avatar, "flinch", 5f);

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true));
        machine.States.Add(State(asset, "Run", "run", enter: true));
        machine.States.Add(State(asset, "Flinch", "flinch", enterNode: "hit"));
        machine.States.Add(new GraphStateRecord { Id = "any", Name = "Any State", IsAny = true });
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Run", Duration = 0f });
        machine.States[3].Transitions.Add(new GraphTransitionRecord { To = "Flinch", Duration = 0f });
        asset.RootNode = machine.Id;

        // The machine moves on to Run first, and only the Any State leads out of Run.
        Assert.Equal(expected, SpineAfter(asset, skeleton, avatar, frames: 6), 1);
    }

    /// <summary>A trigger declared on the graph turns itself off once a transition fires on it.</summary>
    [Fact]
    public void ATriggerParameterIsSpentByTheTransitionThatUsesIt()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Trigger" };
        asset.Parameters.Add(new GraphParameterRecord { Name = "Jump", Kind = NodeValueKind.Flag, Trigger = true });
        asset.AddNode(AnimationNodeIds.Parameter, "jump").Properties["Name"] = NodeValue.FromText("Jump");
        HeldClipNode(asset, skeleton, avatar, "idle", 1f);
        HeldClipNode(asset, skeleton, avatar, "air", 3f);

        GraphNodeRecord machine = asset.AddNode(AnimationNodeIds.StateMachine, "sm");
        machine.States.Add(State(asset, "Idle", "idle", isDefault: true));
        machine.States.Add(State(asset, "Air", "air", enterNode: "jump"));
        machine.States[0].Transitions.Add(new GraphTransitionRecord { To = "Air", Duration = 0f });
        asset.RootNode = machine.Id;

        AnimationGraphInstance instance = asset.Compile(skeleton, avatar.Runtime)!.CreateInstance(skeleton);
        instance.SetBool("Jump", true);
        instance.Update(1f / 60f, Transform3D.Identity);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.False(instance.GetBool("Jump"));
        Assert.Equal(3.0, instance.Pose.GetTransform(2).position.Y, 1);
    }

    /// <summary>A Parameter node reads a name a Virtual Parameter node gives a value, as if it were declared.</summary>
    [Fact]
    public void AParameterNodeReadsAVirtualParameter()
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Virtual" };
        asset.AddNode(AnimationNodeIds.ConstFloat, "one").Properties["Value"] = NodeValue.FromNumber(1f);
        GraphNodeRecord named = asset.AddNode(AnimationNodeIds.VirtualParameter, "named");
        named.Wire("one");
        named.Properties["Name"] = NodeValue.FromText("Blend");
        asset.AddNode(AnimationNodeIds.Parameter, "read").Properties["Name"] = NodeValue.FromText("Blend");

        HeldClipNode(asset, skeleton, avatar, "low", 1f);
        HeldClipNode(asset, skeleton, avatar, "high", 3f);
        GraphNodeRecord blend = asset.AddNode(AnimationNodeIds.Blend1D, "blend");
        blend.Wire("read");
        blend.Wire("low").Value = 0f;
        blend.Wire("high").Value = 1f;
        asset.RootNode = blend.Id;

        Assert.Equal(3.0, SpineAfter(asset, skeleton, avatar), 1);
    }

    /// <summary>A crossfade settles on the pose its flag picks.</summary>
    [Theory]
    [InlineData(false, 1.0)]
    [InlineData(true, 3.0)]
    public void ACrossfadeSettlesOnThePoseItsFlagPicks(bool on, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Crossfade" };
        asset.AddNode(AnimationNodeIds.ConstBool, "flag").Properties["Value"] = NodeValue.FromFlag(on);
        HeldClipNode(asset, skeleton, avatar, "off", 1f);
        HeldClipNode(asset, skeleton, avatar, "on", 3f);
        GraphNodeRecord fade = asset.AddNode(AnimationNodeIds.Crossfade, "fade");
        fade.Wire("off");
        fade.Wire("on");
        fade.Wire("flag");
        fade.Properties["Seconds"] = NodeValue.FromNumber(0.05f);
        asset.RootNode = fade.Id;

        Assert.Equal(expected, SpineAfter(asset, skeleton, avatar), 1);
    }

    /// <summary>A node that works two ways shows only the pins and settings of the way it is set.</summary>
    [Fact]
    public void AChoiceDecidesWhichPinsAndSettingsANodeShows()
    {
        AnimationGraphNode warp = AnimationNodeRegistry.Get(AnimationNodeIds.OrientationWarp)!;
        var byAngle = new GraphNodeRecord { Type = warp.Id };
        byAngle.Properties["Steer By"] = NodeValue.FromText("Angle");

        Assert.True(warp.ShowsPin(new GraphNodeRecord { Type = warp.Id }, 1));
        Assert.False(warp.ShowsPin(new GraphNodeRecord { Type = warp.Id }, 2));
        Assert.False(warp.ShowsPin(byAngle, 1));
        Assert.True(warp.ShowsPin(byAngle, 2));

        AnimationGraphNode done = AnimationNodeRegistry.Get(AnimationNodeIds.StateFinished)!;
        var bySeconds = new GraphNodeRecord { Type = done.Id };
        bySeconds.Properties["By"] = NodeValue.FromText("Seconds");
        Assert.False(done.ShowsProperty(bySeconds, "Threshold"));
        Assert.True(done.ShowsProperty(bySeconds, "Seconds"));

        AnimationGraphNode info = AnimationNodeRegistry.Get(AnimationNodeIds.TargetInfo)!;
        var isSet = new GraphNodeRecord { Type = info.Id };
        isSet.Properties["Info"] = NodeValue.FromText("IsSet");
        Assert.Equal(NodePinKind.Flag, info.OutputOf(new AnimationGraph(), isSet));
        Assert.Equal(NodePinKind.Number, info.OutputOf(new AnimationGraph(), new GraphNodeRecord { Type = info.Id }));
    }

    /// <summary>Choose Number reads a wired value beside the condition that holds, and its default otherwise.</summary>
    [Theory]
    [InlineData(true, 7.0)]
    [InlineData(false, 2.0)]
    public void ChooseNumberReadsItsWiredValueOrItsDefault(bool condition, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var asset = new AnimationGraph { Name = "Choose" };
        asset.AddNode(AnimationNodeIds.ConstBool, "flag").Properties["Value"] = NodeValue.FromFlag(condition);
        asset.AddNode(AnimationNodeIds.ConstFloat, "seven").Properties["Value"] = NodeValue.FromNumber(7f);
        GraphNodeRecord choose = asset.AddNode(AnimationNodeIds.PickNumber, "choose");
        choose.Wire("flag").Value = 99f;
        choose.Wire("seven");
        choose.Properties["Default"] = NodeValue.FromNumber(2f);
        choose.Properties["Easing"] = NodeValue.FromText("None");

        GraphNodeRecord speed = asset.AddNode(AnimationNodeIds.SpeedScale, "speed");
        speed.Wire("held");
        speed.Wire("choose");
        HeldClipNode(asset, skeleton, avatar, "held", 1f);
        asset.RootNode = speed.Id;

        MotionGraph? compiled = asset.Compile(skeleton, avatar.Runtime);
        AnimationGraphInstance instance = compiled!.CreateInstance(skeleton);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(expected, instance.EvaluateValueNode(NodesOf(asset, instance)["choose"]).AsFloat(), 3);
    }

    /// <summary>
    /// A sub graph asset's parameters are ports on the node: what is wired in is what the inner graph
    /// reads, and a port left empty keeps the inner parameter's default.
    /// </summary>
    [Theory]
    [InlineData(true, 3.0)]
    [InlineData(false, 1.0)]
    public void ASubGraphReadsWhatIsWiredIntoItsParameterPorts(bool wired, double expected)
    {
        MotionSkeleton skeleton = BuildSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var inner = new AnimationGraph { Name = "Inner" };
        inner.Parameters.Add(new GraphParameterRecord { Name = "Blend", Kind = NodeValueKind.Number, Number = 0f });
        inner.AddNode(AnimationNodeIds.Parameter, "read").Properties["Name"] = NodeValue.FromText("Blend");
        HeldClipNode(inner, skeleton, avatar, "low", 1f);
        HeldClipNode(inner, skeleton, avatar, "high", 3f);
        GraphNodeRecord blend = inner.AddNode(AnimationNodeIds.Blend1D, "blend");
        blend.Wire("read");
        blend.Wire("low").Value = 0f;
        blend.Wire("high").Value = 1f;
        inner.RootNode = blend.Id;

        var outer = new AnimationGraph { Name = "Outer" };
        outer.AddNode(AnimationNodeIds.ConstFloat, "one").Properties["Value"] = NodeValue.FromNumber(1f);
        GraphNodeRecord sub = outer.AddNode(AnimationNodeIds.SubGraph, "sub");
        sub.Properties["Graph"] = NodeValue.FromGraph(new AssetRef<AnimationGraph>(inner));
        if (wired) sub.PropertyInputs["Blend"] = "one";
        outer.RootNode = sub.Id;

        InputPin port = Assert.Single(AnimationNodeRegistry.Get(AnimationNodeIds.SubGraph)!.NamedInputs(sub));
        Assert.Equal(("Blend", NodePinKind.Number), (port.Name, port.Kind));
        Assert.Equal(expected, SpineAfter(outer, skeleton, avatar), 1);
    }

    /// <summary>A mask's channel entries decide how much of a layer reaches each float channel.</summary>
    [Fact]
    public void AnAvatarMaskWeighsFloatChannelsByName()
    {
        var skeleton = new MotionSkeleton(new[] { new StringID("Root") }, new[] { MotionSkeleton.InvalidIndex },
            new[] { Transform3D.Identity }, -1, new[] { new StringID("Smile"), new StringID("Blink") });
        var mask = new AvatarMask { DefaultChannelWeight = 0f };
        mask.Channels.Add(new AvatarMask.ChannelWeight { Channel = "Blink", Weight = 1f });

        BoneMask built = mask.GetBoneMask(skeleton);

        Assert.Equal(0f, built.GetChannelWeight(0));
        Assert.Equal(1f, built.GetChannelWeight(1));
        Assert.Equal(1f, mask.GetHumanMask().GetChannelWeight(new StringID("Blink")));
        Assert.Equal(0f, mask.GetHumanMask().GetChannelWeight(new StringID("Smile")));
    }
}
