// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Vector;

namespace Prowl.Runtime.AnimationNodes;

public sealed class AimConstraintNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _target = TargetInput();
    private readonly TextSetting _bone = BoneName("Bone");
    private readonly VectorSetting _axis = new("Axis", Float3.UnitY);

    public AimConstraintNode()
    {
        Pins(_pose, _target);
        Settings(_bone, _axis);
    }

    public override string Id => AnimationNodeIds.AimConstraint;
    public override string DisplayName => "Aim Constraint";
    public override string Category => "Rig";
    public override string Description => "Points one bone's axis at a target.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddAimConstraint(ctx.Input(r, _pose), ctx.BoneId(r, _bone), ctx.Input(r, _target), r.Get(_axis));
}

public sealed class CopyConstraintNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _target = TargetInput();
    private readonly TextSetting _bone = BoneName("Bone"), _source = BoneName("Source");
    private readonly ChoiceSetting<CopySource> _from = Choice("From", CopySource.Bone);
    private readonly ChoiceSetting<TransformChannels> _channels = Choice("Channels", TransformChannels.PositionAndRotation);

    public CopyConstraintNode()
    {
        Pins(_pose, _target);
        Settings(_bone, _from, _source, _channels);
    }

    public override string Id => AnimationNodeIds.CopyConstraint;
    public override string DisplayName => "Copy Transform";
    public override string Category => "Rig";
    public override string Description => "Puts a bone where another bone is, or where a target says.";

    protected override bool PinShown(GraphNodeRecord r, InputPin pin)
        => pin != _target || r.Get(_from) == CopySource.Target;

    protected override bool SettingShown(GraphNodeRecord r, NodeSetting setting)
        => setting != _source || r.Get(_from) == CopySource.Bone;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => r.Get(_from) == CopySource.Target
        ? ctx.Graph.AddCopyConstraint(ctx.Input(r, _pose), ctx.BoneId(r, _bone), ctx.Input(r, _target), r.Get(_channels))
        : ctx.Graph.AddCopyConstraint(ctx.Input(r, _pose), ctx.BoneId(r, _bone), ctx.BoneId(r, _source), r.Get(_channels));
}

/// <summary>Where a Copy Transform node takes the transform it copies.</summary>
public enum CopySource
{
    Bone,
    Target,
}

public sealed class IKRigNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _target = TargetInput(variadic: true), _weight = NumberInput("Weight", variadic: true);

    public IKRigNode()
    {
        Pins(_pose, _target, _weight);
    }

    public override string Id => AnimationNodeIds.IKRig;
    public override string DisplayName => "IK Rig";
    public override string Category => "Rig";
    public override string Description => "Solves several effectors at once. Each input names its effector and its bone chain.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        var infos = new List<IKEffectorInfo>();
        foreach (PinGroup effector in ctx.Groups(r))
        {
            int[] chain = ctx.BoneChain(effector.Entry.Text);
            if (chain.Length < 2) continue;
            infos.Add(new IKEffectorInfo(effector.Entry.Name, chain, ctx.Input(effector, _target), FloatInput.From(ctx.Input(effector, _weight), effector.Entry.Value)));
        }
        return infos.Count == 0 ? ctx.Graph.AddPassthrough(ctx.Input(r, _pose)) : ctx.Graph.AddIKRig(ctx.Input(r, _pose), infos);
    }
}

public sealed class LookAtNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _target = TargetInput();
    private readonly NumberSetting _clamp = Driven("Clamp", 0.3f), _body = Driven("Body", 0.4f), _head = Driven("Head", 1f), _eyes = Driven("Eyes", 1f), _weight = Driven("Weight", 1f);

    public LookAtNode()
    {
        Pins(_pose, _target);
        Settings(_clamp, _body, _head, _eyes, _weight);
    }

    public override string Id => AnimationNodeIds.LookAt;
    public override string DisplayName => "Look At";
    public override string Category => "Rig";
    public override string Description => "Turns the spine, head and eyes toward a target.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNode(new LookAtDefinition(ctx.Input(r, _pose), ctx.Input(r, _target),
        ctx.Get(r, _weight), ctx.Get(r, _clamp), ctx.Get(r, _body), ctx.Get(r, _head), ctx.Get(r, _eyes)));
}

public sealed class RagdollNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly WiredNumber _active = new("Active", 1f);
    private readonly NumberSetting _muscle = Driven("Muscle Strength", 1f), _pin = Driven("Pin Strength", 0.5f), _blend = Driven("Blend", 1f), _mass = Number("Total Mass", 70f);
    private readonly ChoiceSetting<RagdollPinning> _pinning = Choice("Pinning", RagdollPinning.All);
    private readonly FlagSetting _handsAndFeet = Toggle("Hands and Feet");

    public RagdollNode()
    {
        Pins(_pose, _active.Pin);
        Settings(_active, _muscle, _pin, _pinning, _blend, _mass, _handsAndFeet);
    }

    public override string Id => AnimationNodeIds.Ragdoll;
    public override string DisplayName => "Ragdoll";
    public override string Category => "Rig";
    public override string Description => "Makes a physics ragdoll of the humanoid that follows the pose, and shows the pose it took. Muscles turn the joints toward the animation, the pin holds every part or only the hips to it. Active slides from following (1) to limp (0).";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNode(new Definition(ctx.Input(r, _pose))
        {
            Active = ctx.Get(r, _active),
            Muscle = ctx.Get(r, _muscle),
            Pin = ctx.Get(r, _pin),
            Pinning = r.Get(_pinning),
            Blend = ctx.Get(r, _blend),
            TotalMass = r.Get(_mass),
            HandsAndFeet = r.Get(_handsAndFeet),
        });

    private sealed class Definition(int child) : PoseNodeDefinition
    {
        public int Child { get; } = child;
        public FloatInput Active { get; init; }
        public FloatInput Muscle { get; init; }
        public FloatInput Pin { get; init; }
        public FloatInput Blend { get; init; }
        public RagdollPinning Pinning { get; init; }
        public float TotalMass { get; init; }
        public bool HandsAndFeet { get; init; }

        public override GraphNodeInstance CreateInstance() => new Instance(this);
    }

    // Drives the puppet of the animator running the graph, then shows the pose its bodies took.
    private sealed class Instance(Definition def) : PassthroughPoseNodeInstance
    {
        private BoundFloat _active, _muscle, _pin, _blend;
        private AnimatorRagdoll? _prepared;

        public override void Bind(GraphBindContext context)
        {
            BindChild(context, def.Child);
            _active = BoundFloat.Bind(context, def.Active);
            _muscle = BoundFloat.Bind(context, def.Muscle);
            _pin = BoundFloat.Bind(context, def.Pin);
            _blend = BoundFloat.Bind(context, def.Blend);
        }

        protected override void OnUpdate(GraphContext context)
        {
            base.OnUpdate(context);
            if (context.Host is not Animator { Ragdoll: { } ragdoll }) return;

            if (!ReferenceEquals(_prepared, ragdoll))
            {
                ragdoll.EnsureBodies(def.TotalMass, def.HandsAndFeet);
                _prepared = ragdoll;
            }

            float active = Maths.Clamp(_active.Get(context), 0f, 1f);
            ragdoll.Drive(Pose, context.WorldTransform, Maths.Clamp(_muscle.Get(context), 0f, 1f) * active,
                Maths.Clamp(_pin.Get(context), 0f, 1f) * active, def.Pinning);

            float blend = Maths.Clamp(_blend.Get(context), 0f, 1f);
            if (blend > 0f) ragdoll.Show(Pose, context, blend);
        }
    }
}

/// <summary>Which of a ragdoll's parts the pin holds to the animation.</summary>
public enum RagdollPinning
{
    /// <summary>Every part, so a push barely carries on through the rest of the body.</summary>
    All,
    /// <summary>Only the hips, so the rest of the body swings on its muscles.</summary>
    Hips,
}

public sealed class SpringBonesNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly TextSetting _root = BoneName("Root");
    private readonly IntegerSetting _count = Count("Count", 3);
    private readonly NumberSetting _stiffness = Driven("Stiffness", 40f), _damping = Driven("Damping", 6f);
    private readonly VectorSetting _gravity = new("Gravity");

    public SpringBonesNode()
    {
        Pins(_pose);
        Settings(_root, _count, _stiffness, _damping, _gravity);
    }

    public override string Id => AnimationNodeIds.SpringBones;
    public override string DisplayName => "Spring Bones";
    public override string Category => "Rig";
    public override string Description => "Gives a chain secondary motion that lags and settles.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNode(new SpringBonesDefinition(ctx.Input(r, _pose), ctx.BoneId(r, _root), Math.Max(1, r.Get(_count)))
        {
            Stiffness = ctx.Get(r, _stiffness),
            Damping = ctx.Get(r, _damping),
            Gravity = r.Get(_gravity),
        });
}

public sealed class TwistDistributionNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly TextSetting _driver = BoneName("Driver"), _twist = BoneList("Twist");
    private readonly VectorSetting _axis = new("Axis", Float3.UnitX);

    public TwistDistributionNode()
    {
        Pins(_pose);
        Settings(_driver, _twist, _axis);
    }

    public override string Id => AnimationNodeIds.TwistDistribution;
    public override string DisplayName => "Twist Distribution";
    public override string Category => "Rig";
    public override string Description => "Spreads a bone's roll across twist bones, written as \"bone:share\" pairs.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        (StringID Bone, float Weight)[] shares = ctx.Bones(r, _twist);
        return shares.Length == 0
            ? ctx.Graph.AddPassthrough(ctx.Input(r, _pose))
            : ctx.Graph.AddTwistDistribution(ctx.Input(r, _pose), ctx.BoneId(r, _driver), shares, r.Get(_axis));
    }
}

public sealed class TwoBoneIKNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _target = TargetInput();
    private readonly WiredNumber _weight = new("Weight", 1f);
    private readonly TextSetting _upper = BoneName("Upper"), _mid = BoneName("Mid"), _end = BoneName("End");

    public TwoBoneIKNode()
    {
        Pins(_pose, _target, _weight.Pin);
        Settings(_upper, _mid, _end, _weight);
    }

    public override string Id => AnimationNodeIds.TwoBoneIK;
    public override string DisplayName => "Two Bone IK";
    public override string Category => "Rig";
    public override string Description => "Bends a limb so its end reaches a target.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int upperBone = ctx.BoneIndex(r, _upper), midBone = ctx.BoneIndex(r, _mid), endBone = ctx.BoneIndex(r, _end);
        if (upperBone < 0 || midBone < 0 || endBone < 0) return ctx.Graph.AddPassthrough(ctx.Input(r, _pose));

        return ctx.Graph.AddTwoBoneIK(ctx.Input(r, _pose), ctx.Input(r, _target), upperBone, midBone, endBone, ctx.Get(r, _weight));
    }
}
