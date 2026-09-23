using System.Collections.Generic;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>How the target state's time is aligned to the source when a transition starts.</summary>
public enum TransitionSync : byte
{
    /// <summary>The target starts from the beginning and plays on its own clock.</summary>
    Frozen,
    /// <summary>The target starts at the source's current sync position.</summary>
    MatchSourceTime,
    /// <summary>The target is phase locked to the source via their sync tracks for the whole transition.</summary>
    Synchronized,
}

/// <summary>Shapes the 0..1 transition blend weight over time.</summary>
public enum TransitionEasing : byte { Linear, EaseIn, EaseOut, EaseInOut }

/// <summary>A transition out of a state: a target state, a bool condition node (-1 = always), and a blend duration.</summary>
public sealed class TransitionInfo
{
    public int TargetStateIndex { get; set; }
    public int ConditionNodeIndex { get; set; } = -1;
    public float Duration { get; set; } = 0.2f;

    /// <summary>How the target state aligns to the source on start.</summary>
    public TransitionSync Sync { get; set; } = TransitionSync.Frozen;

    /// <summary>Easing applied to the cross-fade weight.</summary>
    public TransitionEasing Easing { get; set; } = TransitionEasing.Linear;

    /// <summary>If true, the transition duration is clamped so it does not outlive the source's remaining time.</summary>
    public bool ClampToSource { get; set; }

    /// <summary>
    /// Lets this transition start even while its target is still part of a running transition. The
    /// running blend is then frozen and faded out from its current pose.
    /// </summary>
    public bool CanBeForced { get; set; }
}

/// <summary>A state in a state machine: a content pose node plus its outgoing transitions.</summary>
public sealed class StateInfo
{
    public int PoseNodeIndex { get; set; }
    public string? Name { get; set; }
    public List<TransitionInfo> Transitions { get; } = new();
}

/// <summary>
/// A state machine pose node: a set of states (each wrapping a content pose node) and transitions
/// between them. Transitions cross-fade from the current state to the target over a duration when
/// their condition becomes true. A transition may start while
/// another is running, the running blend then becomes its source.
/// </summary>
public sealed class StateMachineDefinition : PoseNodeDefinition
{
    public List<StateInfo> States { get; } = new();
    public int DefaultStateIndex { get; set; }

    public override GraphNodeInstance CreateInstance() => new StateMachineInstance(this);
}

/// <summary>
/// What a state machine is doing, for an editor or a debug view. The instance itself is internal, so
/// this is how anything outside the library reads it, and it only reads.
/// </summary>
public interface IStateMachineState
{
    /// <summary>The state it is in, as an index into the machine's states, or -1 before it starts.</summary>
    int CurrentStateIndex { get; }

    bool IsTransitioning { get; }

    /// <summary>Seconds since the current state was entered, including the transition into it.</summary>
    float TimeInCurrentState { get; }

    /// <summary>Normalized progress [0,1] of the current state's content.</summary>
    float CurrentStateNormalizedTime { get; }
}

internal sealed class StateMachineInstance : PoseNodeInstance, IStateMachineState
{
    private readonly struct TransitionData
    {
        public TransitionData(TransitionInfo info, ValueNodeInstance? condition)
        {
            Target = info.TargetStateIndex;
            Condition = condition;
            Duration = float.IsFinite(info.Duration) ? MathF.Max(0f, info.Duration) : 0f;
            Sync = info.Sync;
            Easing = info.Easing;
            ClampToSource = info.ClampToSource;
            CanBeForced = info.CanBeForced;
        }

        public int Target { get; }
        public ValueNodeInstance? Condition { get; }
        public float Duration { get; }
        public TransitionSync Sync { get; }
        public TransitionEasing Easing { get; }
        public bool ClampToSource { get; }
        public bool CanBeForced { get; }
    }

    private sealed class StateRuntime
    {
        public PoseNodeInstance Content = null!;
        public TransitionData[] Transitions = null!;
        public float ElapsedTime;
    }

    private readonly StateMachineDefinition _def;
    private readonly List<Transition> _transitionPool = new();
    private StateRuntime[] _states = null!;
    private int _defaultState;
    private int _active = -1;
    private Transition? _transition;
    private Skeleton _skeleton = null!;
    private Dictionary<ValueNodeInstance, ControlParameterInstance[]>? _parametersRead;

    public StateMachineInstance(StateMachineDefinition def) => _def = def;

    public int CurrentStateIndex => _active;
    public bool IsTransitioning => _transition is not null;

    /// <summary>Seconds since the current state was entered, including the transition into it.</summary>
    public float TimeInCurrentState => _active >= 0 ? _states[_active].ElapsedTime : 0f;

    /// <summary>Normalized progress [0,1] of the current state's content (clip phase, etc.).</summary>
    public float CurrentStateNormalizedTime => _active >= 0 ? _states[_active].Content.NormalizedTime : 0f;

    public override SyncTrack SyncTrack => _transition?.SyncTrack ?? (_active >= 0 ? _states[_active].Content.SyncTrack : SyncTrack.Default);

    public override void Bind(GraphBindContext context)
    {
        _skeleton = context.Skeleton;
        Pose = new Pose(context.Skeleton);

        int count = _def.States.Count;
        _states = new StateRuntime[count];
        for (int s = 0; s < count; s++)
        {
            StateInfo info = _def.States[s];
            var state = new StateRuntime { Content = context.PoseNode(info.PoseNodeIndex) };
            state.Transitions = new TransitionData[info.Transitions.Count];
            for (int t = 0; t < info.Transitions.Count; t++)
            {
                TransitionInfo transition = info.Transitions[t];
                if (transition.TargetStateIndex < 0 || transition.TargetStateIndex >= count)
                    throw context.Error($"state {s} has a transition to state {transition.TargetStateIndex}, which does not exist.");
                ValueNodeInstance? condition = context.OptionalValueNode(transition.ConditionNodeIndex, ValueInputKind.Number, dependency: false);
                state.Transitions[t] = new TransitionData(transition, condition);
            }
            _states[s] = state;
        }

        if (count > 0 && (_def.DefaultStateIndex < 0 || _def.DefaultStateIndex >= count))
            throw context.Error($"the default state {_def.DefaultStateIndex} does not exist.");
        _defaultState = _def.DefaultStateIndex;
    }

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        _transition = null;
        _active = -1;
        if (_states.Length == 0)
            return;

        _active = _defaultState;
        EnterState(context, _active, initialTime);
        CopyTimingFrom(_states[_active].Content);
    }

    protected override void OnShutdown(GraphContext context)
    {
        if (_active < 0)
            return;
        if (_transition is not null)
        {
            ShutdownSources(context, _transition);
            Release(_transition);
            _transition = null;
        }
        ExitState(context, _active);
        _active = -1;
    }

    protected override void OnUpdate(GraphContext context)
    {
        if (_active < 0)
        {
            Pose.SetToReferencePose();
            RootMotionDelta = Transform3D.Identity;
            return;
        }

        if (_transition is not null && _transition.IsComplete(context.DeltaTime))
            EndTransition(context);

        int firstEvent = context.Events.Count;
        if (_transition is null)
        {
            PoseNodeInstance content = _states[_active].Content;
            content.Update(context);
            CopyResultFrom(content);
        }
        else
        {
            _transition.Update(context);
            Pose.CopyFrom(_transition.Pose);
            RootMotionDelta = _transition.RootMotionDelta;
            CopyTimingFrom(_transition);
        }

        _states[_active].ElapsedTime += MathF.Abs(context.DeltaTime);

        if (context.IsActiveBranch)
            EvaluateTransitions(context, firstEvent);
    }

    private void EvaluateTransitions(GraphContext context, int firstEvent)
    {
        TransitionData[] transitions = _states[_active].Transitions;
        for (int t = 0; t < transitions.Length; t++)
        {
            TransitionData info = transitions[t];
            if (!info.CanBeForced && _transition is not null && _transition.Involves(info.Target))
                continue;
            if (info.Condition is not null && !info.Condition.GetValue(context).AsBool())
                continue;

            StartTransition(context, info, firstEvent);
            if (info.Condition is not null)
                foreach (ControlParameterInstance parameter in ParametersRead(info.Condition))
                    parameter.Consume(context);
            return;
        }
    }

    /// <summary>
    /// Every parameter a condition reads, however indirectly, found the first time it fires and kept,
    /// so a transition can turn off the triggers it fired on without searching again.
    /// </summary>
    private ControlParameterInstance[] ParametersRead(ValueNodeInstance condition)
    {
        _parametersRead ??= new Dictionary<ValueNodeInstance, ControlParameterInstance[]>();
        if (_parametersRead.TryGetValue(condition, out ControlParameterInstance[]? known)) return known;

        ControlParameterInstance[] result = ControlParameterInstance.ReadBy(condition);
        _parametersRead[condition] = result;
        return result;
    }

    private void StartTransition(GraphContext context, TransitionData info, int firstEvent)
    {
        var sourceRange = context.Events.RangeFrom(firstEvent);
        context.Events.MarkFromInactiveBranch(sourceRange);

        float sourcePrevious = PreviousTime;
        float sourceCurrent = NormalizedTime;
        float sourceDuration = Duration;
        bool sourceBackward = PlayingBackward;
        SyncTrack sourceTrack = SyncTrack;
        bool targetIsLive = info.Target == _active || (_transition is not null && _transition.Involves(info.Target));

        Transition transition = Rent();
        transition.Pose.CopyFrom(Pose);

        if (targetIsLive)
        {
            if (_transition is not null)
            {
                ShutdownSources(context, _transition);
                Release(_transition);
                _transition = null;
            }
            ExitState(context, _active);
            transition.SetFrozenSource(Pose);
        }
        else if (_transition is not null)
        {
            transition.SetSource(_transition);
        }
        else
        {
            transition.SetSource(_active, _states[_active].Content);
        }

        float duration = info.Duration;
        if (info.ClampToSource && sourceDuration > 1e-4f && !transition.SourceIsFrozen)
        {
            // What is left of the source runs to its start when it plays backward, and to its end otherwise.
            float left = sourceBackward ? sourceCurrent : 1f - sourceCurrent;
            duration = MathF.Min(duration, MathF.Max(left * sourceDuration, 0f));
        }

        _active = info.Target;
        PoseNodeInstance target = _states[_active].Content;
        SyncTrackTimeRange sourceSyncRange = GraphSync.DirectedRange(sourceTrack, sourcePrevious, sourceCurrent, PlayingBackward);
        bool synchronized = info.Sync == TransitionSync.Synchronized && !transition.SourceIsFrozen;

        if (synchronized)
        {
            EnterState(context, _active, sourceSyncRange.Start);
            GraphSync.UpdateSynchronized(context, target, sourceSyncRange);
        }
        else if (info.Sync == TransitionSync.MatchSourceTime && !transition.SourceIsFrozen)
        {
            EnterState(context, _active, sourceTrack.GetTime(sourceCurrent));
            GraphSync.UpdateFrozen(context, target);
        }
        else
        {
            EnterState(context, _active, null);
            target.Update(context);
        }

        transition.Begin(target, _active, duration, info.Easing, synchronized, sourceRange, sourceSyncRange, context.DeltaTime);
        transition.Blend(context, RootMotionDelta, target);

        if (duration <= 0f)
        {
            ShutdownSources(context, transition);
            Release(transition);
            _transition = null;
            CopyResultFrom(target);
        }
        else
        {
            _transition = transition;
            Pose.CopyFrom(transition.Pose);
            RootMotionDelta = transition.RootMotionDelta;
            CopyTimingFrom(transition);
        }
    }

    private void CopyTimingFrom(Transition transition)
    {
        PreviousTime = transition.PreviousTime;
        NormalizedTime = transition.CurrentTime;
        Duration = transition.Duration;
        LoopCount = transition.Target.LoopCount;
        PlayingBackward = transition.Target.PlayingBackward;
    }

    private void EndTransition(GraphContext context)
    {
        ShutdownSources(context, _transition!);
        Release(_transition!);
        _transition = null;
    }

    private void EnterState(GraphContext context, int state, SyncTrackTime? initialTime)
    {
        _states[state].ElapsedTime = 0f;
        _states[state].Content.Initialize(context, initialTime);
        foreach (TransitionData transition in _states[state].Transitions)
            transition.Condition?.Initialize(context);
    }

    private void ExitState(GraphContext context, int state)
    {
        foreach (TransitionData transition in _states[state].Transitions)
            transition.Condition?.Shutdown(context);
        _states[state].Content.Shutdown(context);
    }

    // Shuts down everything feeding a transition (states and nested transitions), not its target.
    private void ShutdownSources(GraphContext context, Transition transition)
    {
        if (transition.SourceTransition is { } nested)
        {
            ShutdownSources(context, nested);
            ExitState(context, nested.TargetState);
            Release(nested);
        }
        else if (transition.SourceState >= 0)
        {
            ExitState(context, transition.SourceState);
        }
        transition.ClearSource();
    }

    private Transition Rent()
    {
        if (_transitionPool.Count == 0)
            return new Transition(this, _skeleton);
        Transition t = _transitionPool[^1];
        _transitionPool.RemoveAt(_transitionPool.Count - 1);
        return t;
    }

    private void Release(Transition transition)
    {
        transition.ClearSource();
        _transitionPool.Add(transition);
    }

    /// <summary>
    /// A running cross fade from a source (a state, a nested transition, or a frozen pose) into a
    /// target state.
    /// </summary>
    private sealed class Transition
    {
        private readonly StateMachineInstance _owner;
        private readonly Pose _frozen;
        private readonly SyncTrack _blendedTrack = new();
        private PoseNodeInstance _target = null!;
        private PoseNodeInstance? _sourceState;
        private float _progress;
        private float _duration;
        private float _blendWeight;
        private float _previousTime;
        private float _currentTime;
        private float _blendedDuration;
        private TransitionEasing _easing;
        private bool _synchronized;
        private bool _timedOnBlendedTrack;
        private SampledEventRange _sourceEvents;

        public Transition(StateMachineInstance owner, Skeleton skeleton)
        {
            _owner = owner;
            Pose = new Pose(skeleton);
            _frozen = new Pose(skeleton);
        }

        public Pose Pose { get; }
        public Transform3D RootMotionDelta { get; private set; } = Transform3D.Identity;
        public int TargetState { get; private set; } = -1;
        public int SourceState { get; private set; } = -1;
        public Transition? SourceTransition { get; private set; }
        public bool SourceIsFrozen { get; private set; }

        public PoseNodeInstance Target => _target;

        /// <summary>The track the reported times are measured on: the blended track while a sync range drives the blend.</summary>
        public SyncTrack SyncTrack => _timedOnBlendedTrack ? _blendedTrack : _target.SyncTrack;

        public float PreviousTime => _timedOnBlendedTrack ? _previousTime : _target.PreviousTime;
        public float CurrentTime => _timedOnBlendedTrack ? _currentTime : _target.NormalizedTime;
        public float Duration => _timedOnBlendedTrack ? _blendedDuration : _target.Duration;

        public void SetSource(int state, PoseNodeInstance content)
        {
            SourceState = state;
            _sourceState = content;
        }

        public void SetSource(Transition transition) => SourceTransition = transition;

        public void SetFrozenSource(Pose pose)
        {
            _frozen.CopyFrom(pose);
            SourceIsFrozen = true;
        }

        public void ClearSource()
        {
            SourceState = -1;
            _sourceState = null;
            SourceTransition = null;
            SourceIsFrozen = false;
        }

        /// <summary>True if the state is this transition's target or anywhere in its source chain.</summary>
        public bool Involves(int state) => TargetState == state || SourceState == state || (SourceTransition?.Involves(state) ?? false);

        public bool IsComplete(float deltaTime) => _duration <= 0f || _progress + MathF.Abs(deltaTime) / _duration >= 1f;

        /// <summary>Starts the blend. The frame it starts on already counts toward its progress.</summary>
        public void Begin(PoseNodeInstance target, int targetState, float duration, TransitionEasing easing, bool synchronized, SampledEventRange sourceEvents, SyncTrackTimeRange sourceRange, float deltaTime)
        {
            _target = target;
            TargetState = targetState;
            _duration = duration;
            _easing = easing;
            _synchronized = synchronized;
            _progress = duration > 0f ? Math.Clamp(MathF.Abs(deltaTime) / duration, 0f, 1f) : 1f;
            _blendWeight = Ease(_progress, easing);
            _sourceEvents = sourceEvents;
            UpdateTiming(synchronized ? sourceRange : null, 0f);
        }

        /// <summary>Blends the already evaluated source (the owner's current output) with the freshly started target.</summary>
        public void Blend(GraphContext context, Transform3D sourceRootMotion, PoseNodeInstance target)
        {
            Blender.Blend(Pose, Pose, target.Pose, _blendWeight);
            RootMotionDelta = SourceIsFrozen ? target.RootMotionDelta : Blender.BlendRootMotionDeltas(sourceRootMotion, target.RootMotionDelta, _blendWeight);
            context.Events.BlendRanges(_sourceEvents, target.SampledEventRange, _blendWeight);
        }

        public void Update(GraphContext context)
        {
            if (SourceTransition is { } nested && nested.IsComplete(context.DeltaTime))
            {
                _owner.ShutdownSources(context, nested);
                SourceState = nested.TargetState;
                _sourceState = _owner._states[SourceState].Content;
                SourceTransition = null;
                _owner.Release(nested);
            }

            SyncTrackTimeRange? range = null;
            if (context.SyncRange.HasValue)
                range = context.SyncRange;
            else if (_synchronized)
                range = GraphSync.UpdateRange(context, _blendedTrack, _currentTime, _blendedDuration);

            _progress = _duration > 0f ? Math.Clamp(_progress + MathF.Abs(context.DeltaTime) / _duration, 0f, 1f) : 1f;
            _blendWeight = Ease(_progress, _easing);

            int sourceStart = context.Events.Count;
            Pose sourcePose;
            Transform3D sourceRootMotion;
            float sourceDuration;
            if (SourceIsFrozen)
            {
                sourcePose = _frozen;
                sourceRootMotion = Transform3D.Identity;
                sourceDuration = 0f;
            }
            else if (SourceTransition is { } source)
            {
                BranchState saved = context.BranchState;
                SyncTrackTimeRange? savedRange = context.SyncRange;
                context.BranchState = BranchState.Inactive;
                if (range.HasValue)
                    context.SyncRange = range;
                source.Update(context);
                context.BranchState = saved;
                context.SyncRange = savedRange;
                sourcePose = source.Pose;
                sourceRootMotion = source.RootMotionDelta;
                sourceDuration = source.Duration;
            }
            else
            {
                GraphSync.UpdateInactive(context, _sourceState!, range);
                sourcePose = _sourceState!.Pose;
                sourceRootMotion = _sourceState.RootMotionDelta;
                sourceDuration = _sourceState.Duration;
            }
            SampledEventRange sourceEvents = context.Events.RangeFrom(sourceStart);

            if (range.HasValue)
                GraphSync.UpdateSynchronized(context, _target, range.Value);
            else
                _target.Update(context);

            Blender.Blend(Pose, sourcePose, _target.Pose, _blendWeight);
            RootMotionDelta = SourceIsFrozen ? _target.RootMotionDelta : Blender.BlendRootMotionDeltas(sourceRootMotion, _target.RootMotionDelta, _blendWeight);
            context.Events.BlendRanges(sourceEvents, _target.SampledEventRange, _blendWeight);

            UpdateTiming(range, sourceDuration);
        }

        // With a sync range the blend is timed on the blended track, otherwise the target's own clock is reported.
        private void UpdateTiming(SyncTrackTimeRange? range, float sourceDuration)
        {
            _timedOnBlendedTrack = range.HasValue && !SourceIsFrozen;
            if (!_timedOnBlendedTrack)
            {
                _blendedDuration = sourceDuration + (_target.Duration - sourceDuration) * _blendWeight;
                return;
            }

            SyncTrack sourceTrack = SourceSyncTrack;
            float sourceSyncDuration = SourceDuration;
            float timing = GraphSync.TimingWeight(sourceSyncDuration, _target.Duration, _blendWeight);
            _blendedTrack.SetToBlend(sourceTrack, _target.SyncTrack, timing);
            _blendedDuration = SyncTrack.CalculateDurationSynchronized(sourceSyncDuration, _target.Duration, sourceTrack.EventCount, _target.SyncTrack.EventCount, _blendedTrack.EventCount, timing);
            GraphSync.Resolve(_blendedTrack, range!.Value, out _previousTime, out _currentTime, out _, out _);
        }

        private SyncTrack SourceSyncTrack => SourceTransition?.SyncTrack ?? _sourceState?.SyncTrack ?? SyncTrack.Default;

        private float SourceDuration => SourceTransition?.Duration ?? _sourceState?.Duration ?? 0f;

        private static float Ease(float x, TransitionEasing easing) => easing switch
        {
            TransitionEasing.EaseIn => x * x,
            TransitionEasing.EaseOut => 1f - (1f - x) * (1f - x),
            TransitionEasing.EaseInOut => x * x * (3f - 2f * x),
            _ => x,
        };
    }
}
