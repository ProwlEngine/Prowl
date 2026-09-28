// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Motion;

namespace Prowl.Runtime.AnimationNodes;

public sealed class InertialBlendNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _trigger = FlagInput("Trigger");
    private readonly NumberSetting _seconds = Number("Seconds", 0.2f);

    public InertialBlendNode()
    {
        Pins(_pose, _trigger);
        Settings(_seconds);
    }

    public override string Id => AnimationNodeIds.InertialBlend;
    public override string DisplayName => "Inertial Blend";
    public override string Category => "Modify";
    public override string Description => "Smooths a sudden pose change out over time.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddInertialBlend(ctx.Input(r, _pose), r.Get(_seconds), ctx.Input(r, _trigger));
}

public sealed class MirrorNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _enabled = FlagInput("Enabled");

    public MirrorNode()
    {
        Pins(_pose, _enabled);
    }

    public override string Id => AnimationNodeIds.Mirror;
    public override string DisplayName => "Mirror";
    public override string Category => "Modify";
    public override string Description => "Swaps left and right, while Enabled reads true or is left unwired.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddMirror(ctx.Input(r, _pose), ctx.Input(r, _enabled));
}

public sealed class PassthroughNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();

    public PassthroughNode()
    {
        Pins(_pose);
    }

    public override string Id => AnimationNodeIds.Passthrough;
    public override string DisplayName => "Passthrough";
    public override string Category => "Modify";
    public override string Description => "Hands its input straight on, for keeping a graph tidy.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddPassthrough(ctx.Input(r, _pose));
}

public sealed class PoseSmoothingNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly NumberSetting _halfLife = Driven("HalfLife", 0.1f, "Half Life");

    public PoseSmoothingNode()
    {
        Pins(_pose);
        Settings(_halfLife);
    }

    public override string Id => AnimationNodeIds.PoseSmoothing;
    public override string DisplayName => "Pose Smoothing";
    public override string Category => "Modify";
    public override string Description => "Damps the pose so it lags and settles.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddPoseSmoothing(ctx.Input(r, _pose), ctx.Get(r, _halfLife));
}

public sealed class PoseSnapshotNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _hold = FlagInput("Hold", optional: false);

    public PoseSnapshotNode()
    {
        Pins(_pose, _hold);
    }

    public override string Id => AnimationNodeIds.PoseSnapshot;
    public override string DisplayName => "Pose Snapshot";
    public override string Category => "Modify";
    public override string Description => "Freezes the pose while a flag reads true.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddPoseSnapshot(ctx.Input(r, _pose), ctx.Input(r, _hold));
}

public sealed class SpeedScaleNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();
    private readonly WiredNumber _speed = new("Speed", 1f);

    public SpeedScaleNode()
    {
        Pins(_pose, _speed.Pin);
        Settings(_speed);
    }

    public override string Id => AnimationNodeIds.SpeedScale;
    public override string DisplayName => "Speed Scale";
    public override string Category => "Modify";
    public override string Description => "Plays its input faster or slower.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        return ctx.Graph.AddSpeedScale(ctx.Input(r, _pose), ctx.Get(r, _speed));
    }
}
