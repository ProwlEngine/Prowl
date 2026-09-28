// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Motion;

namespace Prowl.Runtime.AnimationNodes;

public sealed class ConditionSelectorNode : AnimationGraphNode
{
    private readonly InputPin _pose = PoseInput(variadic: true), _condition = FlagInput("Condition", variadic: true);

    public ConditionSelectorNode()
    {
        Pins(_pose, _condition);
    }

    public override string Id => AnimationNodeIds.ConditionSelector;
    public override string DisplayName => "Choose Pose by Condition";
    public override string Category => "Logic";
    public override string Description => "Shows the first input whose condition reads true.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        List<PinGroup> options = ctx.Groups(r);
        if (options.Count == 0) return ctx.Graph.AddReferencePose();

        var entries = new List<(int, int)>(options.Count);
        foreach (PinGroup option in options) entries.Add((ctx.Input(option, _pose), ctx.Input(option, _condition)));
        return ctx.Graph.AddConditionSelector(entries);
    }
}

public sealed class RandomSelectorNode : AnimationGraphNode
{
    private readonly InputPin _children = PoseInput("Poses", variadic: true);
    private readonly IntegerSetting _seed = Count("Seed");

    public RandomSelectorNode()
    {
        Pins(_children);
        Settings(_seed);
    }

    public override string Id => AnimationNodeIds.RandomSelector;
    public override string DisplayName => "Choose Pose at Random";
    public override string Category => "Logic";
    public override string Description => "Picks one input at random each time it starts. Each input carries its weight.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        List<PinGroup> options = ctx.Groups(r);
        if (options.Count == 0) return ctx.Graph.AddReferencePose();

        var poses = new int[options.Count];
        var weights = new float[options.Count];
        for (int i = 0; i < options.Count; i++)
        {
            poses[i] = ctx.Input(options[i], _children);
            weights[i] = options[i].Entry.Value;
        }
        return ctx.Graph.AddRandomSelector(poses, weights, (uint)r.Get(_seed));
    }
}

public sealed class SelectorNode : AnimationGraphNode
{
    private readonly InputPin _index = IntegerInput("Index", optional: false), _children = PoseInput("Poses", variadic: true);

    public SelectorNode()
    {
        Pins(_index, _children);
    }

    public override string Id => AnimationNodeIds.Selector;
    public override string DisplayName => "Choose Pose by Index";
    public override string Category => "Logic";
    public override string Description => "Shows whichever input an integer picks.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int[] options = ctx.Inputs(r, _children);
        return options.Length == 0 ? ctx.Graph.AddReferencePose() : ctx.Graph.AddSelector(ctx.Input(r, _index), options);
    }
}

public sealed class SequenceNode : AnimationGraphNode
{
    private readonly InputPin _children = PoseInput("Poses", variadic: true);
    private readonly FlagSetting _loop = Toggle("Loop");

    public SequenceNode()
    {
        Pins(_children);
        Settings(_loop);
    }

    public override string Id => AnimationNodeIds.Sequence;
    public override string DisplayName => "Sequence";
    public override string Category => "Logic";
    public override string Description => "Plays its inputs one after another.";

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int[] steps = ctx.Inputs(r, _children);
        return steps.Length == 0 ? ctx.Graph.AddReferencePose() : ctx.Graph.AddSequence(steps, r.Get(_loop));
    }
}

public sealed class StateFinishedNode : AnimationGraphNode
{
    private readonly InputPin _machine = PoseInput("State Machine");
    private readonly ChoiceSetting<StateDoneMeasure> _by = Choice("By", StateDoneMeasure.Progress);
    private readonly NumberSetting _threshold = Number("Threshold", 0.99f), _seconds = Number("Seconds", 1f);

    public StateFinishedNode()
    {
        Pins(_machine);
        Settings(_by, _threshold, _seconds);
    }

    public override string Id => AnimationNodeIds.StateFinished;
    public override string DisplayName => "State Done";
    public override string Category => "Logic";
    public override string Description => "True once the current state has played through, or has run for a while. Inside a state, it reads that state's machine unless wired.";

    public override NodePinKind Output => NodePinKind.Flag;

    protected override bool SettingShown(GraphNodeRecord r, NodeSetting setting)
        => setting == _threshold ? r.Get(_by) == StateDoneMeasure.Progress
        : setting != _seconds || r.Get(_by) == StateDoneMeasure.Seconds;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int read = StateMachineNode.MachineOf(ctx, r, _machine);
        if (read < 0) return ctx.Graph.AddConstBool(false);
        return r.Get(_by) == StateDoneMeasure.Seconds
            ? ctx.Graph.AddStateTimeElapsed(read, r.Get(_seconds))
            : ctx.Graph.AddStateFinished(read, r.Get(_threshold));
    }
}

/// <summary>How a State Done node decides the current state is done.</summary>
public enum StateDoneMeasure
{
    /// <summary>A fraction of the state's length.</summary>
    Progress,
    Seconds,
}

public sealed class StateMachineNode : AnimationGraphNode
{
    public override string Id => AnimationNodeIds.StateMachine;
    public override string DisplayName => "State Machine";
    public override string Category => "Logic";
    public override string Description => "States and the transitions between them.";

    // The Any State is not a real state: its transitions leave every other state, gated only by their target's Enter.
    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int machine = ctx.Graph.AddStateMachine();
        ctx.Publish(r, machine);
        if (r.States.Count == 0) return machine;

        var indexByName = new Dictionary<string, int>(r.States.Count);
        var stateIndices = new int[r.States.Count];
        var ends = new StateEnds[r.States.Count];
        for (int i = 0; i < r.States.Count; i++)
        {
            GraphStateRecord state = r.States[i];
            stateIndices[i] = -1;
            if (state.IsAny) continue;

            ends[i] = ctx.State(machine, state);
            int pose = ends[i].Pose >= 0 ? ends[i].Pose : ctx.Graph.AddReferencePose();

            int index = ctx.Graph.AddState(machine, pose, state.Name);
            indexByName[state.Name] = i;
            stateIndices[i] = index;
            if (state.IsDefault) ctx.Graph.SetStateMachineDefault(machine, index);
        }
        ctx.MapStates(r, stateIndices);

        var fromAny = new List<(GraphTransitionRecord Transition, int Target)>();
        foreach (GraphStateRecord any in r.States)
        {
            if (!any.IsAny) continue;
            foreach (GraphTransitionRecord transition in any.Transitions)
                if (TargetState(transition, indexByName) is int target) fromAny.Add((transition, target));
        }

        for (int i = 0; i < r.States.Count; i++)
        {
            if (r.States[i].IsAny) continue;

            // Transitions from the Any State come first, so a jump or a hit wins over a state's own ways out.
            foreach ((GraphTransitionRecord transition, int target) in fromAny)
                if (target != i) AddTransition(ctx, machine, stateIndices, ends, i, target, transition, Gate.Open);

            foreach (GraphTransitionRecord transition in r.States[i].Transitions)
                if (TargetState(transition, indexByName) is int target)
                    AddTransition(ctx, machine, stateIndices, ends, i, target, transition, ends[i].Exit);
        }
        return machine;
    }

    /// <summary>The machine a state query reads: the one wired to it, or else the machine whose state it sits in.</summary>
    internal static int MachineOf(GraphCompileContext ctx, GraphNodeRecord record, InputPin machine)
    {
        if (ctx.ExpectChild(record, machine, AnimationNodeIds.StateMachine, "a state machine")) return ctx.Input(record, machine);
        return ctx.IsWired(record, machine) ? -1 : ctx.CurrentMachine;
    }

    private static int? TargetState(GraphTransitionRecord transition, Dictionary<string, int> indexByName)
    {
        if (indexByName.TryGetValue(transition.To, out int target)) return target;
        Debug.LogWarning($"[AnimationGraph] A transition leads to '{transition.To}', which is not a state of this machine.");
        return null;
    }

    private static void AddTransition(GraphCompileContext ctx, int machine, int[] stateIndices, StateEnds[] ends,
        int from, int to, GraphTransitionRecord transition, Gate leave)
    {
        if (ctx.Both(leave, ends[to].Enter) is not { } condition) return;

        TransitionInfo info = ctx.Graph.AddTransition(machine, stateIndices[from], stateIndices[to], condition, transition.Duration);
        info.Easing = transition.Easing;
        info.Sync = transition.Sync;
        info.ClampToSource = transition.ClampToSource;
        info.CanBeForced = transition.CanInterrupt;
    }
}

public sealed class StateOutputNode : AnimationGraphNode
{
    internal static readonly InputPin Enter = FlagInput("Enter");
    internal static readonly InputPin Exit = FlagInput("Exit");

    private readonly InputPin _pose = PoseInput();

    public StateOutputNode()
    {
        Pins(_pose, Enter, Exit);
    }

    public override string Id => AnimationNodeIds.StateOutput;
    public override string DisplayName => "State Output";
    public override string Category => "Logic";
    public override string Description => "What a state plays, whether it wants to be entered, and whether it is willing to be left.";

    public override bool Hidden => true;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
        => ctx.Graph.AddPassthrough(ctx.Input(r, _pose));
}

public sealed class StateQueryNode : AnimationGraphNode
{
    private readonly InputPin _machine = PoseInput("State Machine");
    private readonly ChoiceSetting<StateQuery> _query = Choice("Query", StateQuery.NormalizedTime);

    public StateQueryNode()
    {
        Pins(_machine);
        Settings(_query);
    }

    public override string Id => AnimationNodeIds.StateQuery;
    public override string DisplayName => "State Query";
    public override string Category => "Logic";
    public override string Description => "How long a state machine has been in its state, or how far through it is. Inside a state, it reads that state's machine unless wired.";

    public override NodePinKind Output => NodePinKind.Number;

    public override int Build(GraphCompileContext ctx, GraphNodeRecord r)
    {
        int read = StateMachineNode.MachineOf(ctx, r, _machine);
        return read >= 0 ? ctx.Graph.AddStateQuery(read, r.Get(_query)) : ctx.Graph.AddConstFloat(0f);
    }
}
