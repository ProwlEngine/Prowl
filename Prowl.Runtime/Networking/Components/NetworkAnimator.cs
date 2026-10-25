using System;
using System.Collections.Generic;

using Prowl.Motion;

namespace Prowl.Runtime.Networking;

/// <summary>
/// Replicates an animator's graph parameters, playback speed and trigger events from the object's controller.
/// Place on the network root; the animator may live on a locally constructed child. IK targets remain local.
/// Parameters travel by their index in the graph, so every peer must run the same graph.
/// </summary>
[AddComponentMenu("Networking/Network Animator")]
[DisallowMultipleComponent]
public sealed class NetworkAnimator : Component, INetworkEvents
{
    public Animator? Animator;

    /// <summary>Smallest change to a number or vector parameter worth sending. Smaller drift is sent once the value settles.</summary>
    public float ParameterTolerance = 0.01f;

    /// <summary>Seconds between parameter samples, at least one network tick. Triggers are sent on the next tick regardless.</summary>
    public float SendInterval = 0.1f;

    private Animator? _bound;
    private double _nextSample;
    private readonly Dictionary<int, ParameterValue> _lastSamples = new();
    private readonly Dictionary<int, uint> _seenTriggers = new();
    private readonly Dictionary<int, uint> _pendingTriggers = new();
    private readonly Dictionary<string, bool> _triggerLevels = new();
    // Plain values rather than network collections, so they ride in snapshots: only the newest state matters.
    private AnimatorParameters _parameters;
    private AnimatorTriggers _triggers;
    private float _speed = 1f;
    private bool _applying;

    private static readonly SyncMember<NetworkAnimator, AnimatorParameters> s_parameters =
        new(1, "Parameters", SyncFlags.None, static c => c._parameters, static (c, v) => c._parameters = v);
    private static readonly SyncMember<NetworkAnimator, AnimatorTriggers> s_triggers =
        new(2, "Triggers", SyncFlags.None, static c => c._triggers, static (c, v) => c._triggers = v);
    private static readonly SyncMember<NetworkAnimator, float> s_speed =
        new(3, "Speed", SyncFlags.None, static c => c._speed, static (c, v) => c._speed = v);

    static NetworkAnimator()
    {
        NetSerializer<AnimatorParameters>.Register(AnimatorParameters.Write, AnimatorParameters.Read);
        NetSerializer<AnimatorTriggers>.Register(AnimatorTriggers.Write, AnimatorTriggers.Read);
    }

    public override void OnEnable()
    {
        if (Animator.IsValid()) Bind(Animator!);
    }

    public override void OnDisable() => Unbind();

    /// <summary>Connects an animator, including one rebuilt at runtime. Existing replicated state applies before its next evaluation.</summary>
    public void Bind(Animator animator)
    {
        ArgumentNullException.ThrowIfNull(animator);
        Unbind();
        Animator = _bound = animator;
        animator.BeforeEvaluate += Synchronize;
        animator.ParameterWritten += ParameterWritten;
        animator.TriggerWritten += TriggerWritten;
        _nextSample = 0;
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.BeforeEvaluate -= Synchronize;
            _bound.ParameterWritten -= ParameterWritten;
            _bound.TriggerWritten -= TriggerWritten;
        }
        _bound = null;
    }

    private void Synchronize()
    {
        if (_bound.IsNotValid()) return;
        if (IsProxy) ApplyState();
        else if (Time.TimeSinceStartup >= _nextSample)
        {
            // No faster than the network sends; a sample in between would never be seen.
            _nextSample = Time.TimeSinceStartup + Math.Max(SendInterval, 1.0 / Math.Max(1, Networking.TickRate));
            CaptureState();
        }
    }

    internal void CaptureState()
    {
        if (_bound.IsNotValid()) return;
        s_speed.Set(this, ref _speed, _bound!.Speed);
        if (_bound.Graph.IsNotValid()) return;
        List<GraphParameterRecord> parameters = _bound.Graph!.Parameters;
        AnimatorParameterValue[]? changed = null;
        for (int index = 0; index < parameters.Count; index++)
        {
            GraphParameterRecord parameter = parameters[index];
            if (parameter.Trigger || parameter.Kind == NodeValueKind.Target) continue;
            ParameterValue value = parameter.Kind switch
            {
                NodeValueKind.Number => ParameterValue.FromFloat(_bound.GetFloat(parameter.Name)),
                NodeValueKind.Flag => ParameterValue.FromBool(_bound.GetBool(parameter.Name)),
                NodeValueKind.Integer => ParameterValue.FromInt(_bound.GetInt(parameter.Name)),
                NodeValueKind.Vector => ParameterValue.FromVector(_bound.GetVector(parameter.Name)),
                NodeValueKind.Id => ParameterValue.FromId(_bound.GetId(parameter.Name)),
                _ => default
            };
            if (ShouldSend(index, value)) (changed ??= _parameters.ToArray(parameters.Count))[index] = new(value);
        }
        if (changed != null) s_parameters.Set(this, ref _parameters, new AnimatorParameters(changed));
    }

    // Continuous inputs such as velocity jitter every frame. Send them past the tolerance, or exactly once they stop moving.
    private bool ShouldSend(int index, ParameterValue value)
    {
        bool settled = _lastSamples.TryGetValue(index, out ParameterValue last) && Within(last, value, 0f);
        _lastSamples[index] = value;
        AnimatorParameterValue sent = _parameters[index];
        return !sent.IsSet || !Within(sent.Value, value, settled ? 0f : ParameterTolerance);
    }

    private static bool Within(ParameterValue a, ParameterValue b, float tolerance) => a.Type == b.Type && a.Type switch
    {
        AnimationValueType.Float => MathF.Abs(a.Float - b.Float) <= tolerance,
        AnimationValueType.Vector => MathF.Abs(a.Vector.X - b.Vector.X) <= tolerance
            && MathF.Abs(a.Vector.Y - b.Vector.Y) <= tolerance && MathF.Abs(a.Vector.Z - b.Vector.Z) <= tolerance,
        _ => new AnimatorParameterValue(a).Equals(new AnimatorParameterValue(b))
    };

    internal void ApplyState()
    {
        if (_bound.IsNotValid() || _bound!.Graph.IsNotValid()) return;
        _applying = true;
        try { ApplyParameters(); }
        finally { _applying = false; }
    }

    private void ApplyParameters()
    {
        _bound!.Speed = _speed;
        List<GraphParameterRecord> parameters = _bound.Graph!.Parameters;
        for (int index = 0; index < Math.Min(_parameters.Count, parameters.Count); index++)
            _parameters[index].Apply(_bound, parameters[index].Name);
        for (int index = 0; index < _triggers.Count; index++)
        {
            uint revision = _triggers[index];
            uint added = unchecked(revision - _seenTriggers.GetValueOrDefault(index));
            if (added != 0)
            {
                _pendingTriggers[index] = _pendingTriggers.GetValueOrDefault(index) + added;
                _seenTriggers[index] = revision;
            }
        }
        // A graph may consume a trigger during evaluation. Keep successive events for successive evaluations.
        foreach (int index in new List<int>(_pendingTriggers.Keys))
        {
            uint count = _pendingTriggers[index];
            if (count == 0) continue;
            if (index < parameters.Count) _bound.SetTrigger(parameters[index].Name);
            if (count == 1) _pendingTriggers.Remove(index);
            else _pendingTriggers[index] = count - 1;
        }
    }

    private void ParameterWritten(string name, ParameterValue value)
    {
        if (_applying || IsProxy || value.Type != AnimationValueType.Bool || _bound.IsNotValid() || _bound!.Graph.IsNotValid()) return;
        if (!_bound.Graph!.Parameters.Exists(p => p.Name == name && p.Trigger)) return;
        bool previous = _triggerLevels.GetValueOrDefault(name);
        _triggerLevels[name] = value.Bool;
        if (value.Bool && !previous) TriggerWritten(name);
    }

    private void TriggerWritten(string name)
    {
        if (_applying || IsProxy || _bound.IsNotValid() || _bound!.Graph.IsNotValid()) return;
        int index = _bound.Graph!.Parameters.FindIndex(p => p.Name == name);
        if (index < 0) return;
        s_triggers.Set(this, ref _triggers, _triggers.Fired(index));
    }

    public void OnNetworkSpawn() => ResetTriggerBaseline();
    public void OnNetworkAuthorityGained()
    {
        ResetTriggerBaseline();
        if (_parameters.Count > 0) ApplyState();
    }
    public void OnNetworkAuthorityLost() => ResetTriggerBaseline();

    private void ResetTriggerBaseline()
    {
        _seenTriggers.Clear();
        _pendingTriggers.Clear();
        _triggerLevels.Clear();
        _lastSamples.Clear();
        _nextSample = 0;
        for (int index = 0; index < _triggers.Count; index++) _seenTriggers[index] = _triggers[index];
    }
}
