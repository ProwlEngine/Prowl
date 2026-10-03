// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Motion;

namespace Prowl.Runtime.AnimationNodes;

public sealed class CharacterMotionNode : AnimationGraphNode
{
    private readonly ChoiceSetting<CharacterMotionValue> _read = Choice("Read", CharacterMotionValue.Speed);
    private readonly NumberSetting _smoothing = Number("Smoothing", 0.1f);

    public CharacterMotionNode()
    {
        Settings(_read, _smoothing);
    }

    public override string Id => AnimationNodeIds.CharacterMotion;
    public override string DisplayName => "Character Motion";
    public override string Category => "Locomotion";
    public override string Description => "How the character is moving and turning, read from where it goes each frame.";

    public override NodePinKind Output => NodePinKind.Number;

    public override NodePinKind OutputOf(AnimationGraph? asset, GraphNodeRecord r)
        => r.Get(_read) == CharacterMotionValue.Velocity ? NodePinKind.Vector : NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddCharacterMotion(r.Get(_read), r.Get(_smoothing));
}

public sealed class FootGroundingNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _leftY = NumberInput("Left Y"), _rightY = NumberInput("Right Y"), _weight = NumberInput("Weight");
    private readonly InputPin _leftNormal = VectorInput("Left Normal", optional: true), _rightNormal = VectorInput("Right Normal", optional: true);
    private readonly FlagSetting _findGround = Toggle("Find Ground", true), _adjustHips = Toggle("Adjust Hips", true);
    private readonly NumberSetting _stepUp = Number("Max Step Up", 0.5f), _stepDown = Number("Max Step Down", 0.5f);
    private readonly NumberSetting _footSmoothing = Number("Foot Smoothing", 0.04f), _hipsSmoothing = Number("Hips Smoothing", 0.08f);
    private readonly NumberSetting _footAngle = Number("Max Foot Angle", 45f), _liftHeight = Number("Foot Lift Height", 0.1f);

    public FootGroundingNode()
    {
        Pins(_pose, _leftY, _rightY, _weight, _leftNormal, _rightNormal);
        Settings(_findGround, _stepUp, _stepDown, _adjustHips, _footSmoothing, _hipsSmoothing, _footAngle, _liftHeight);
    }

    public override string Id => AnimationNodeIds.FootGrounding;
    public override string DisplayName => "Foot Grounding";
    public override string Category => "Locomotion";
    public override string Description => "Moves the feet by the height of the ground under them, drops the hips so both legs reach, and tilts planted feet onto slopes. The ground is found by the node, or wired in as world heights.";

    public override bool RequiresHumanoid => true;

    protected override bool PinShown(GraphNodeRecord r, InputPin pin)
        => pin == _pose || pin == _weight || !r.Get(_findGround);

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNode(new FootGroundingDefinition(ctx.Input(r, _pose), ctx.Input(r, _leftY), ctx.Input(r, _rightY),
            ctx.Input(r, _weight), ctx.Input(r, _leftNormal), ctx.Input(r, _rightNormal))
        {
            ProbeGround = r.Get(_findGround),
            MaxStepUp = r.Get(_stepUp),
            MaxStepDown = r.Get(_stepDown),
            AdjustHips = r.Get(_adjustHips),
            FootSmoothing = r.Get(_footSmoothing),
            HipsSmoothing = r.Get(_hipsSmoothing),
            MaxFootAngle = r.Get(_footAngle),
            FootLiftHeight = r.Get(_liftHeight),
        });
}

public sealed class FootLockNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _locked = FlagInput("Locked", optional: false);
    private readonly TextSetting _upper = BoneName("Upper"), _mid = BoneName("Mid"), _end = BoneName("End");

    public FootLockNode()
    {
        Pins(_pose, _locked);
        Settings(_upper, _mid, _end);
    }

    public override string Id => AnimationNodeIds.FootLock;
    public override string DisplayName => "Foot Lock";
    public override string Category => "Locomotion";
    public override string Description => "Pins a planted foot in world space so it stops sliding.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFootLock(ctx.Input(r, _pose), ctx.BoneId(r, _upper), ctx.BoneId(r, _mid), ctx.BoneId(r, _end), ctx.Input(r, _locked));
}

public sealed class OrientationWarpNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _direction = VectorInput("Direction"), _degrees = NumberInput("Degrees", optional: false);
    private readonly ChoiceSetting<WarpSteering> _steerBy = Choice("Steer By", WarpSteering.Direction);

    public OrientationWarpNode()
    {
        Pins(_pose, _direction, _degrees);
        Settings(_steerBy);
    }

    public override string Id => AnimationNodeIds.OrientationWarp;
    public override string DisplayName => "Orientation Warp";
    public override string Category => "Locomotion";
    public override string Description => "Bends a travelling clip so it heads where it is steered: toward a direction, or by an angle in degrees.";

    protected override bool PinShown(GraphNodeRecord r, InputPin pin)
        => pin == _direction ? r.Get(_steerBy) == WarpSteering.Direction
        : pin != _degrees || r.Get(_steerBy) == WarpSteering.Angle;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        if (!ctx.ExpectChild(r, _pose, AnimationNodeIds.Clip, "a clip")) return ctx.Input(r, _pose);
        return r.Get(_steerBy) == WarpSteering.Angle
            ? ctx.Graph.AddOrientationWarpAngle(ctx.Input(r, _pose), ctx.Input(r, _degrees))
            : ctx.Graph.AddOrientationWarp(ctx.Input(r, _pose), ctx.Input(r, _direction));
    }
}

/// <summary>What an Orientation Warp is steered by.</summary>
public enum WarpSteering
{
    /// <summary>A direction to head in.</summary>
    Direction,
    /// <summary>An angle in degrees from the way the clip heads on its own.</summary>
    Angle,
}

public sealed class RootMotionFilterNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly ChoiceSetting<RootMotionChannels> _keep = Choice("Keep", RootMotionChannels.All);
    private readonly NumberSetting _travel = Driven("Travel Scale", 1f), _turn = Driven("Turn Scale", 1f), _maxSpeed = Driven("Max Speed"), _maxTurn = Driven("Max Turn");

    public RootMotionFilterNode()
    {
        Pins(_pose);
        Settings(_keep, _travel, _turn, _maxSpeed, _maxTurn);
    }

    public override string Id => AnimationNodeIds.RootMotionFilter;
    public override string DisplayName => "Root Motion";
    public override string Category => "Locomotion";
    public override string Description => "Keeps, scales and caps the root motion: which channels survive, how much travel and turn, and how fast either may go.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int filtered = ctx.Graph.AddNode(new RootMotionFilterDefinition(ctx.Input(r, _pose), r.Get(_keep))
        {
            TravelScale = ctx.Get(r, _travel),
            TurnScale = ctx.Get(r, _turn),
        });

        // A cap of zero is no cap.
        FloatInput speedCap = ctx.Get(r, _maxSpeed), turnCap = ctx.Get(r, _maxTurn);
        bool capped = speedCap.IsDriven || turnCap.IsDriven || speedCap.Constant > 0f || turnCap.Constant > 0f;
        return capped ? ctx.Graph.AddRootMotionOverride(filtered, 1f, speedCap, turnCap) : filtered;
    }
}

public sealed class StrideWarpNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _speed = NumberInput("Speed", optional: false);
    // A natural speed of zero is measured from the clip's root motion.
    private readonly NumberSetting _natural = Driven("NaturalSpeed", label: "Natural Speed"), _min = Driven("MinScale", 0.5f, "Min Scale"), _max = Driven("MaxScale", 2f, "Max Scale");

    public StrideWarpNode()
    {
        Pins(_pose, _speed);
        Settings(_natural, _min, _max);
    }

    public override string Id => AnimationNodeIds.StrideWarp;
    public override string DisplayName => "Stride Warp";
    public override string Category => "Locomotion";
    public override string Description => "Bends playback rate so the clip covers ground at the speed asked for.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddNode(new StrideWarpDefinition(ctx.Input(r, _pose), ctx.Input(r, _speed))
        {
            NaturalSpeed = ctx.Get(r, _natural),
            MinScale = ctx.Get(r, _min),
            MaxScale = ctx.Get(r, _max),
        });
}

public sealed class TargetWarpNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _displacement = VectorInput("Displacement");

    public TargetWarpNode()
    {
        Pins(_pose, _displacement);
    }

    public override string Id => AnimationNodeIds.TargetWarp;
    public override string DisplayName => "Target Warp";
    public override string Category => "Locomotion";
    public override string Description => "Stretches a clip's travel so it lands on a displacement.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        return ctx.ExpectChild(r, _pose, AnimationNodeIds.Clip, "a clip")
            ? ctx.Graph.AddTargetWarp(ctx.Input(r, _pose), ctx.Input(r, _displacement))
            : ctx.Input(r, _pose);
    }
}

public sealed class TurnWarpNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _degrees = NumberInput("Degrees", optional: false);

    public TurnWarpNode()
    {
        Pins(_pose, _degrees);
    }

    public override string Id => AnimationNodeIds.TurnWarp;
    public override string DisplayName => "Turn Warp";
    public override string Category => "Locomotion";
    public override string Description => "Makes a clip that turns on the spot turn by the angle asked, in degrees, read when it starts.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        return ctx.ExpectChild(r, _pose, AnimationNodeIds.Clip, "a clip")
            ? ctx.Graph.AddTurnWarp(ctx.Input(r, _pose), ctx.Input(r, _degrees))
            : ctx.Input(r, _pose);
    }
}
