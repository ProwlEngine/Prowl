// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionGraph = Prowl.Motion.AnimationGraph;

namespace Prowl.Runtime.AnimationNodes;

public sealed class AnimationPoseNode : AnimationGraphNode
{
    private readonly InputPin _time = NumberInput("Time", optional: false);
    private readonly AssetSetting _clip = Asset("Clip", NodeValueKind.Clip);

    public AnimationPoseNode()
    {
        Pins(_time);
        Settings(_clip);
    }

    public override string Id => AnimationNodeIds.AnimationPose;
    public override string DisplayName => "Animation Pose";
    public override string Category => "Sources";
    public override string Description => "Samples one clip at a time a value node picks, with no clock of its own.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Clip(r, _clip) is { } resolved ? ctx.Graph.AddAnimationPose(resolved, ctx.Input(r, _time)) : ctx.Graph.AddReferencePose();
}

public sealed class ClipNode : AnimationGraphNode
{
    private readonly InputPin _reverse = FlagInput("Reverse"), _restart = FlagInput("Restart");
    private readonly AssetSetting _clip = Asset("Clip", NodeValueKind.Clip);
    private readonly ChoiceSetting<ClipLooping> _looping = Choice("Looping", ClipLooping.FromClip);
    private readonly NumberSetting _speed = Number("Speed", 1f), _start = Number("Start");
    private readonly FlagSetting _randomStart = Toggle("Random Start");
    private readonly IntegerSetting _seed = Count("Seed");

    public ClipNode()
    {
        Pins(_reverse, _restart);
        Settings(_clip, _looping, _speed, _start, _randomStart, _seed);
    }

    public override string Id => AnimationNodeIds.Clip;
    public override string DisplayName => "Clip";
    public override string Category => "Sources";
    public override string Description => "Plays one animation clip.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        AnimationClip? asset = ctx.ClipAsset(r, _clip);
        AnimationClipBase? resolved = ctx.Clip(r, _clip);
        if (resolved == null) return ctx.Graph.AddReferencePose();

        return ctx.Graph.AddNode(new ClipNodeDefinition(resolved)
        {
            // What the clip was imported as, unless the node says otherwise.
            Loop = r.Get(_looping) switch { ClipLooping.Loop => true, ClipLooping.Once => false, _ => !asset.IsValid() || asset!.Loop },
            SpeedMultiplier = r.Get(_speed),
            StartTime = r.Get(_start),
            RandomStart = r.Get(_randomStart),
            RandomSeed = (uint)r.Get(_seed),
            PlayInReverseNodeIndex = ctx.Input(r, _reverse),
            ResetTimeNodeIndex = ctx.Input(r, _restart),
        });
    }
}

public sealed class ExternalGraphSlotNode : AnimationGraphNode
{
    private readonly InputPin _fallback = PoseInput("Fallback");
    internal static readonly TextSetting Slot = new("Slot");

    public ExternalGraphSlotNode()
    {
        Pins(_fallback);
        Settings(Slot);
    }

    public override string Id => AnimationNodeIds.ExternalGraphSlot;
    public override string DisplayName => "Graph Slot";
    public override string Category => "Sources";
    public override string Description => "A named hole the game plugs a running graph into, falling back to its input.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        string name = r.Get(Slot);
        return name.Length == 0 ? ctx.Graph.AddPassthrough(ctx.Input(r, _fallback)) : ctx.Graph.AddExternalGraphSlot(name, ctx.Input(r, _fallback));
    }
}

public sealed class ExternalPoseNode : AnimationGraphNode
{
    /// <summary>What the game finds the node by, through <see cref="Animator.GetExternalPose"/>.</summary>
    internal static readonly TextSetting NameSetting = Name("Name");

    private readonly AssetSetting _source = Asset("Source", NodeValueKind.Avatar);
    private readonly ChoiceSetting<PoseTransferMode> _mode = Choice("Mode", PoseTransferMode.Auto);

    public ExternalPoseNode()
    {
        Settings(NameSetting, _source, _mode);
    }

    public override string Id => AnimationNodeIds.ExternalPose;
    public override string DisplayName => "External Pose";
    public override string Category => "Sources";
    public override string Description => "A pose the game writes in each frame: a ragdoll, a hit reaction, a tracked performer.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        Avatar? avatar = ctx.AvatarAsset(r, _source);
        if (avatar.IsNotValid()) return ctx.Graph.AddExternalPose(ctx.Skeleton, PoseTransferMode.Copy);

        MotionAvatar? runtime = avatar!.Runtime;
        if (runtime is { IsHuman: true }) return ctx.Graph.AddExternalPose(runtime, r.Get(_mode));
        if (avatar.Skeleton is { } skeleton) return ctx.Graph.AddExternalPose(skeleton, r.Get(_mode));
        return ctx.Graph.AddExternalPose(ctx.Skeleton, PoseTransferMode.Copy);
    }
}

public sealed class GraphOutputNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput();

    public GraphOutputNode()
    {
        Pins(_pose);
    }

    public override string Id => AnimationNodeIds.GraphOutput;
    public override string DisplayName => "Graph Output";
    public override string Category => "Sources";
    public override string Description => "What an embedded sub graph plays.";

    public override bool Hidden => true;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddPassthrough(ctx.Input(r, _pose));
}

public sealed class ReferencePoseNode : AnimationGraphNode
{
    public override string Id => AnimationNodeIds.ReferencePose;
    public override string DisplayName => "Reference Pose";
    public override string Category => "Sources";
    public override string Description => "The rig as it was authored.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddReferencePose();
}

public sealed class SubGraphNode : AnimationGraphNode
{
    /// <summary>Whether the node plays its own nodes rather than a graph asset.</summary>
    internal static readonly FlagSetting Embedded = new("Embedded");
    internal static readonly AssetSetting GraphAssetSetting = new("Graph", NodeValueKind.Graph);

    public SubGraphNode()
    {
        Settings(Embedded, GraphAssetSetting);
    }

    public override string Id => AnimationNodeIds.SubGraph;
    public override string DisplayName => "Sub Graph";
    public override string Category => "Sources";
    public override string Description => "Runs another graph as one node, either its own nodes or a graph asset. An asset's parameters are ports: wire in what each should read, or leave one at its default.";

    public override IReadOnlyList<InputPin> NamedInputs(GraphNodeRecord r)
    {
        if (GraphAsset(r) is not { } asset) return Array.Empty<InputPin>();

        var pins = new List<InputPin>(asset.Parameters.Count);
        foreach (GraphParameterRecord parameter in asset.Parameters)
            if (parameter.Name.Length > 0) pins.Add(new InputPin(parameter.Name, PinKindOf(parameter.Kind)));
        return pins;
    }

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        if (r.Get(Embedded))
        {
            GraphNodeRecord? output = ctx.Asset.OwnedNode(r.Id, AnimationNodeIds.GraphOutput);
            int pose = output == null ? -1 : ctx.Node(output.Id);
            return pose < 0 ? ctx.Graph.AddReferencePose() : pose;
        }

        MotionGraph? inner = ctx.SubGraph(r, GraphAssetSetting);
        if (inner == null) return ctx.Graph.AddReferencePose();

        int node = ctx.Graph.AddReferencedGraph(inner);
        LinkParameters(ctx, r, node);
        return node;
    }

    /// <summary>The graph the node plays as an asset, or null while it plays its own nodes.</summary>
    private AnimationGraph? GraphAsset(GraphNodeRecord r)
    {
        if (r.Get(Embedded)) return null;
        AnimationGraph? asset = r.Get(GraphAssetSetting)?.Graph.Res;
        return asset.IsValid() ? asset : null;
    }

    // Hands each wired port to the parameter of that name inside the sub graph.
    private void LinkParameters(GraphCompileContext ctx, GraphNodeRecord r, int subGraph)
    {
        foreach (InputPin pin in NamedInputs(r))
        {
            if (!r.PropertyInputs.TryGetValue(pin.Name, out string? source) || source.Length == 0) continue;

            if (ctx.OutputKind(source) != pin.Kind)
            {
                Debug.LogWarning($"[AnimationGraph] A sub graph's '{pin.Name}' takes {pin.Kind} but is wired to something else, so it keeps its default.");
                continue;
            }

            int value = ctx.Node(source);
            if (value >= 0) ctx.Graph.LinkGraphParameter(subGraph, value, pin.Name);
        }
    }
}

public sealed class ZeroPoseNode : AnimationGraphNode
{
    public override string Id => AnimationNodeIds.ZeroPose;
    public override string DisplayName => "Zero Pose";
    public override string Category => "Sources";
    public override string Description => "The additive identity: no rotation, no translation.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddZeroPose();
}
