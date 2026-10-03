// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Motion;

namespace Prowl.Runtime.AnimationNodes;

public sealed class FootEventNode : AnimationGraphNode
{
    private readonly ChoiceSetting<FootPhaseCondition> _phase = Choice("Phase", FootPhaseCondition.LeftFootDown);

    public FootEventNode()
    {
        Settings(_phase);
    }

    public override string Id => AnimationNodeIds.FootEvent;
    public override string DisplayName => "Foot Phase";
    public override string Category => "Events";
    public override string Description => "True while the clip says a foot is in the chosen phase.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddFootEventCondition(r.Get(_phase));
}

public sealed class IdEventNode : AnimationGraphNode
{
    private readonly TextSetting _names = TextList("Names");
    private readonly FlagSetting _matchAll = Toggle("MatchAll", label: "Match All");

    public IdEventNode()
    {
        Settings(_names, _matchAll);
    }

    public override string Id => AnimationNodeIds.IdEvent;
    public override string DisplayName => "Event Fired";
    public override string Category => "Events";
    public override string Description => "True on a frame the clip fired one of these named events.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddIdEventCondition(ctx.Ids(r, _names), r.Get(_matchAll));
}

public sealed class TransitionEventNode : AnimationGraphNode
{
    private readonly ChoiceSetting<TransitionRuleCondition> _rule = Choice("Rule", TransitionRuleCondition.AnyAllowed);
    private readonly TextSetting _requireId = Name("RequireId", "Require Id");

    public TransitionEventNode()
    {
        Settings(_rule, _requireId);
    }

    public override string Id => AnimationNodeIds.TransitionEvent;
    public override string DisplayName => "Transition Window";
    public override string Category => "Events";
    public override string Description => "True while the clip marks a good moment to leave it.";

    public override NodePinKind Output => NodePinKind.Flag;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddTransitionEventCondition(r.Get(_rule), ctx.Id(r, _requireId));
}
