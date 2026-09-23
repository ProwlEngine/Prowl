using System.Collections.Generic;

namespace Prowl.Motion;

// ---------------------------------------------------------------------------------------------
// Event condition nodes (read the per-frame sampled-events buffer, produce a bool)
// ---------------------------------------------------------------------------------------------

/// <summary>Which sampled events an event condition considers.</summary>
internal static class EventConditionFilter
{
    /// <summary>
    /// True for events that count: not ignored, carrying weight, and (unless inactive events are
    /// allowed) sampled on the active branch rather than the losing side of a transition.
    /// </summary>
    public static bool Accept(in SampledEvent e, bool includeInactiveBranch)
        => !e.IsIgnored && e.Weight > 0f && (includeInactiveBranch || e.IsFromActiveBranch);
}

/// <summary>
/// True when the sampled events contain the configured id event(s). With <see cref="MatchAll"/> set
/// every listed id must be present this frame; otherwise any single match suffices.
/// </summary>
public sealed class IdEventConditionDefinition : ValueNodeDefinition
{
    public IdEventConditionDefinition(IReadOnlyList<StringID> ids, bool matchAll)
    {
        Ids = new List<StringID>(ids).ToArray();
        MatchAll = matchAll;
    }

    public StringID[] Ids { get; }
    public bool MatchAll { get; }

    /// <summary>When true, events from the losing side of a transition also count.</summary>
    public bool IncludeInactiveBranch { get; init; }

    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly IdEventConditionDefinition _def;
        public Instance(IdEventConditionDefinition def) => _def = def;
        protected override bool KeepsNoState => true;

        protected override bool ReadsEvents => true;

        protected override ParameterValue Compute(GraphContext context)
        {
            if (_def.Ids.Length == 0)
                return ParameterValue.FromBool(false);

            Span<bool> found = _def.Ids.Length <= 16 ? stackalloc bool[_def.Ids.Length] : new bool[_def.Ids.Length];
            SampledEventsBuffer events = context.Events;
            for (int e = 0; e < events.Count; e++)
            {
                SampledEvent sampled = events[e];
                if (!EventConditionFilter.Accept(sampled, _def.IncludeInactiveBranch) || sampled.Event is not IdEvent id)
                    continue;
                for (int i = 0; i < _def.Ids.Length; i++)
                {
                    if (_def.Ids[i] != id.Id)
                        continue;
                    if (!_def.MatchAll)
                        return ParameterValue.FromBool(true);
                    found[i] = true;
                }
            }

            if (!_def.MatchAll)
                return ParameterValue.FromBool(false);
            for (int i = 0; i < found.Length; i++)
                if (!found[i])
                    return ParameterValue.FromBool(false);
            return ParameterValue.FromBool(true);
        }
    }
}

/// <summary>True when any sampled foot event satisfies the configured phase condition.</summary>
public sealed class FootEventConditionDefinition : ValueNodeDefinition
{
    public FootEventConditionDefinition(FootPhaseCondition phaseCondition) => PhaseCondition = phaseCondition;
    public FootPhaseCondition PhaseCondition { get; }

    /// <summary>When true, events from the losing side of a transition also count.</summary>
    public bool IncludeInactiveBranch { get; init; }

    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly FootEventConditionDefinition _def;
        public Instance(FootEventConditionDefinition def) => _def = def;
        protected override bool KeepsNoState => true;

        protected override bool ReadsEvents => true;

        protected override ParameterValue Compute(GraphContext context)
        {
            SampledEventsBuffer events = context.Events;
            for (int e = 0; e < events.Count; e++)
            {
                SampledEvent sampled = events[e];
                if (EventConditionFilter.Accept(sampled, _def.IncludeInactiveBranch) && sampled.Event is FootEvent foot && Matches(foot.Phase, _def.PhaseCondition))
                    return ParameterValue.FromBool(true);
            }
            return ParameterValue.FromBool(false);
        }

        private static bool Matches(FootPhase phase, FootPhaseCondition condition) => condition switch
        {
            FootPhaseCondition.LeftFootDown => phase == FootPhase.LeftFootDown,
            FootPhaseCondition.RightFootDown => phase == FootPhase.RightFootDown,
            FootPhaseCondition.LeftFootPassing => phase == FootPhase.LeftFootPassing,
            FootPhaseCondition.RightFootPassing => phase == FootPhase.RightFootPassing,
            FootPhaseCondition.LeftPhase => phase is FootPhase.RightFootPassing or FootPhase.LeftFootDown,
            FootPhaseCondition.RightPhase => phase is FootPhase.LeftFootPassing or FootPhase.RightFootDown,
            _ => false,
        };
    }
}

/// <summary>
/// Reads the most restrictive <see cref="TransitionEvent"/> rule sampled this frame and answers a
/// rule question (e.g. "is transitioning allowed here").
/// </summary>
public sealed class TransitionEventConditionDefinition : ValueNodeDefinition
{
    public TransitionEventConditionDefinition(TransitionRuleCondition ruleCondition, StringID requireId = default)
    {
        RuleCondition = ruleCondition;
        RequireId = requireId;
    }

    public TransitionRuleCondition RuleCondition { get; }
    public StringID RequireId { get; }

    /// <summary>When true, events from the losing side of a transition also count.</summary>
    public bool IncludeInactiveBranch { get; init; }

    public override AnimationValueType ValueType => AnimationValueType.Bool;
    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : ValueNodeInstance
    {
        private readonly TransitionEventConditionDefinition _def;
        public Instance(TransitionEventConditionDefinition def) => _def = def;
        protected override bool KeepsNoState => true;

        protected override bool ReadsEvents => true;

        protected override ParameterValue Compute(GraphContext context)
        {
            bool eventFound = false;
            TransitionRule mostRestrictive = TransitionRule.AllowTransition;
            SampledEventsBuffer events = context.Events;
            for (int e = 0; e < events.Count; e++)
            {
                SampledEvent sampled = events[e];
                if (!EventConditionFilter.Accept(sampled, _def.IncludeInactiveBranch) || sampled.Event is not TransitionEvent t)
                    continue;
                if (_def.RequireId.IsValid && _def.RequireId != t.OptionalId)
                    continue;
                eventFound = true;
                if (t.Rule > mostRestrictive)
                    mostRestrictive = t.Rule;
            }

            if (!eventFound)
                return ParameterValue.FromBool(false);

            bool result = _def.RuleCondition switch
            {
                TransitionRuleCondition.AnyAllowed => mostRestrictive != TransitionRule.BlockTransition,
                TransitionRuleCondition.FullyAllowed => mostRestrictive == TransitionRule.AllowTransition,
                TransitionRuleCondition.ConditionallyAllowed => mostRestrictive == TransitionRule.ConditionallyAllowTransition,
                TransitionRuleCondition.Blocked => mostRestrictive == TransitionRule.BlockTransition,
                _ => false,
            };
            return ParameterValue.FromBool(result);
        }
    }
}
