// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Vector;

namespace Prowl.Runtime.AnimationNodes;

public sealed class Blend1DNode : AnimationGraphNode
{
    private readonly InputPin _parameter = NumberInput("Parameter", optional: false), _children = PoseInput("Poses", variadic: true);
    private readonly FlagSetting _loop = Toggle("Loop", true);

    public Blend1DNode()
    {
        Pins(_parameter, _children);
        Settings(_loop);
    }

    public override string Id => AnimationNodeIds.Blend1D;
    public override string DisplayName => "Blend 1D";
    public override string Category => "Blending";
    public override string Description => "Blends a line of poses by one number. Each input carries its threshold. The clips under it follow its timeline rather than their own looping.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        List<PinGroup> samples = ctx.Groups(r);
        if (samples.Count == 0) return ctx.Graph.AddReferencePose();

        var entries = new List<(int, float)>(samples.Count);
        foreach (PinGroup sample in samples) entries.Add((ctx.Input(sample, _children), sample.Entry.Value));
        return ctx.Graph.AddBlend1D(ctx.Input(r, _parameter), entries, r.Get(_loop));
    }
}

public sealed class Blend2DNode : AnimationGraphNode
{
    private readonly InputPin _x = NumberInput("X", optional: false), _y = NumberInput("Y", optional: false), _children = PoseInput("Poses", variadic: true);
    private readonly FlagSetting _loop = Toggle("Loop", true);

    public Blend2DNode()
    {
        Pins(_x, _y, _children);
        Settings(_loop);
    }

    public override string Id => AnimationNodeIds.Blend2D;
    public override string DisplayName => "Blend 2D";
    public override string Category => "Blending";
    public override string Description => "Blends a field of poses by two numbers. Each input carries its position. The clips under it follow its timeline rather than their own looping.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        List<PinGroup> samples = ctx.Groups(r);
        if (samples.Count == 0) return ctx.Graph.AddReferencePose();

        var points = new (int, Float2)[samples.Count];
        for (int i = 0; i < samples.Count; i++) points[i] = (ctx.Input(samples[i], _children), samples[i].Entry.Position);
        return ctx.Graph.AddBlend2D(ctx.Input(r, _x), ctx.Input(r, _y), points, r.Get(_loop));
    }
}

public sealed class CrossfadeNode : AnimationGraphNode
{
    private readonly InputPin _off = PoseInput("Off"), _on = PoseInput("On"), _active = FlagInput("Active", optional: false);
    private readonly NumberSetting _seconds = Number("Seconds", 0.2f);
    private readonly ChoiceSetting<EasingOp> _easing = Choice("Easing", EasingOp.EaseInOut);

    public CrossfadeNode()
    {
        Pins(_off, _on, _active);
        Settings(_seconds, _easing);
    }

    public override string Id => AnimationNodeIds.Crossfade;
    public override string DisplayName => "Crossfade";
    public override string Category => "Blending";
    public override string Description => "Fades from one pose to the other as a flag turns on, and back as it turns off.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int onWeight = ctx.Graph.AddFloatEase(ctx.Input(r, _active), r.Get(_seconds), r.Get(_easing));
        int offWeight = ctx.Graph.AddFloatMath(ctx.Graph.AddConstFloat(1f), onWeight, FloatMathOp.Subtract);
        return ctx.Graph.AddWeightedBlend(new[] { new WeightedPose(ctx.Input(r, _off), FloatInput.From(offWeight)), new WeightedPose(ctx.Input(r, _on), FloatInput.From(onWeight)) });
    }
}

public sealed class LayerBlendNode : AnimationGraphNode
{
    private readonly InputPin _basePose = PoseInput("Base"), _layer = PoseInput("Layer", variadic: true), _weight = NumberInput("Weight", variadic: true), _mask = MaskInput("Mask", variadic: true);
    private readonly FlagSetting _baseRootMotionOnly = Toggle("BaseRootMotionOnly", true, "Base Root Motion Only");

    public LayerBlendNode()
    {
        Pins(_basePose, _layer, _weight, _mask);
        Settings(_baseRootMotionOnly);
    }

    public override string Id => AnimationNodeIds.LayerBlend;
    public override string DisplayName => "Layer Stack";
    public override string Category => "Blending";
    public override string Description => "A stack of layers over a base pose, each with its own weight, mask and additive flag.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        List<PinGroup> layers = ctx.Groups(r);
        if (layers.Count == 0) return ctx.Graph.AddPassthrough(ctx.Input(r, _basePose));

        var infos = new List<LayerInfo>(layers.Count);
        foreach (PinGroup entry in layers)
            infos.Add(new LayerInfo(ctx.Input(entry, _layer), FloatInput.From(ctx.Input(entry, _weight), entry.Entry.Value), ctx.Input(entry, _mask), entry.Entry.Flag));
        return ctx.Graph.AddLayerBlend(ctx.Input(r, _basePose), infos, r.Get(_baseRootMotionOnly));
    }
}

public sealed class LayerNode : AnimationGraphNode
{
    private readonly InputPin _basePose = PoseInput("Base"), _layerPose = PoseInput("Layer"), _mask = MaskInput("Mask");
    private readonly WiredNumber _weight = new("Weight", 1f);
    private readonly FlagSetting _additive = Toggle("Additive");

    public LayerNode()
    {
        Pins(_basePose, _layerPose, _weight.Pin, _mask);
        Settings(_weight, _additive);
    }

    public override string Id => AnimationNodeIds.Layer;
    public override string DisplayName => "Layer";
    public override string Category => "Blending";
    public override string Description => "Blends a layer over a base pose through an optional mask, or adds it on top.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        return ctx.Graph.AddLayerBlend(ctx.Input(r, _basePose), new[]
        {
            new LayerInfo(ctx.Input(r, _layerPose), ctx.Get(r, _weight), ctx.Input(r, _mask), r.Get(_additive)),
        });
    }
}

public sealed class MakeAdditiveNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(), _reference = PoseInput("Reference");

    public MakeAdditiveNode()
    {
        Pins(_pose, _reference);
    }

    public override string Id => AnimationNodeIds.MakeAdditive;
    public override string DisplayName => "Make Additive";
    public override string Category => "Blending";
    public override string Description => "Turns a pose into the difference from a reference.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddMakeAdditive(ctx.Input(r, _pose), ctx.Input(r, _reference));
}

public sealed class MuscleLayerNode : AnimationGraphNode
{
    private readonly InputPin _basePose = PoseInput("Base"), _layerPose = PoseInput("Layer"), _weight = NumberInput("Weight"), _reference = PoseInput("Reference");
    private readonly FlagSetting _additive = Toggle("Additive");
    private readonly AssetSetting _mask = Asset("Mask", NodeValueKind.Mask);

    public MuscleLayerNode()
    {
        Pins(_basePose, _layerPose, _weight, _reference);
        Settings(_additive, _mask);
    }

    public override string Id => AnimationNodeIds.MuscleLayer;
    public override string DisplayName => "Muscle Layer";
    public override string Category => "Blending";
    public override string Description => "Layers in muscle space, so the body keeps its meaning where a bone blend would not.";

    public override bool RequiresHumanoid => true;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        // Unwired measures against the rig itself.
        int measured = ctx.IsWired(r, _reference) ? ctx.Input(r, _reference) : -1;
        return ctx.Graph.AddMuscleLayer(ctx.Input(r, _basePose), ctx.Input(r, _layerPose), FloatInput.From(ctx.Input(r, _weight), 1f),
            ctx.HumanMask(r, _mask), r.Get(_additive), measured);
    }
}

public sealed class VelocityBlendNode : AnimationGraphNode
{
    private readonly InputPin _speed = NumberInput("Speed", optional: false), _children = PoseInput("Poses", variadic: true);
    private readonly FlagSetting _loop = Toggle("Loop", true);

    public VelocityBlendNode()
    {
        Pins(_speed, _children);
        Settings(_loop);
    }

    public override string Id => AnimationNodeIds.VelocityBlend;
    public override string DisplayName => "Velocity Blend";
    public override string Category => "Blending";
    public override string Description => "Blends travelling clips by the speed each one actually covers. The clips under it follow its timeline rather than their own looping.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int[] clips = ctx.Inputs(r, _children);
        return clips.Length == 0 ? ctx.Graph.AddReferencePose() : ctx.Graph.AddVelocityBlend(ctx.Input(r, _speed), clips, r.Get(_loop));
    }
}

public sealed class WeightedBlendNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(variadic: true), _weight = NumberInput("Weight", variadic: true);

    public WeightedBlendNode()
    {
        Pins(_pose, _weight);
    }

    public override string Id => AnimationNodeIds.WeightedBlend;
    public override string DisplayName => "Weighted Blend";
    public override string Category => "Blending";
    public override string Description => "Mixes any number of poses by weight, normalized across them.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        List<PinGroup> layers = ctx.Groups(r);
        if (layers.Count == 0) return ctx.Graph.AddReferencePose();

        var inputs = new List<WeightedPose>(layers.Count);
        foreach (PinGroup layer in layers) inputs.Add(new WeightedPose(ctx.Input(layer, _pose), FloatInput.From(ctx.Input(layer, _weight), layer.Entry.Value)));
        return ctx.Graph.AddWeightedBlend(inputs);
    }
}
