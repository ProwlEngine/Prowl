using System;
using System.Collections.Generic;

using Prowl.Motion;

namespace Prowl.Runtime.Networking;

/// <summary>
/// Replicates an animator's graph parameters, playback speed and trigger events from the object's controller.
/// Place on the network root; the animator may live on a locally constructed child. IK targets remain local.
/// </summary>
[AddComponentMenu("Networking/Network Animator")]
[DisallowMultipleComponent]
public sealed class NetworkAnimator : Component, INetworkEvents
{
    public Animator? Animator;

    private Animator? _bound;
    private readonly NetDictionary<string, AnimatorParameterValue> _parameters = new();
    private readonly NetDictionary<string, uint> _triggers = new();
    private readonly Dictionary<string, uint> _seenTriggers = new();
    private readonly Dictionary<string, uint> _pendingTriggers = new();
    private readonly Dictionary<string, bool> _triggerLevels = new();
    private double _nextSample;
    private float _speed = 1f;
    private bool _applying;

    private static readonly SyncCollectionMember<NetworkAnimator, NetDictionary<string, AnimatorParameterValue>> s_parameters =
        new(1, "Parameters", SyncFlags.None, static c => c._parameters);
    private static readonly SyncCollectionMember<NetworkAnimator, NetDictionary<string, uint>> s_triggers =
        new(2, "Triggers", SyncFlags.None, static c => c._triggers);
    private static readonly SyncMember<NetworkAnimator, float> s_speed =
        new(3, "Speed", SyncFlags.None, static c => c._speed, static (c, v) => c._speed = v);

    static NetworkAnimator() => NetSerializer<AnimatorParameterValue>.Register(AnimatorParameterValue.Write, AnimatorParameterValue.Read);

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
        s_parameters.Bind(this);
        s_triggers.Bind(this);
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
            _nextSample = Time.TimeSinceStartup + 1.0 / Math.Max(1, Networking.TickRate);
            CaptureState();
        }
    }

    internal void CaptureState()
    {
        if (_bound.IsNotValid()) return;
        s_parameters.Bind(this);
        s_triggers.Bind(this);
        s_speed.Set(this, ref _speed, _bound!.Speed);
        if (_bound.Graph.IsNotValid()) return;
        foreach (GraphParameterRecord parameter in _bound.Graph!.Parameters)
        {
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
            _parameters[parameter.Name] = new(value);
        }
    }

    internal void ApplyState()
    {
        if (_bound.IsNotValid()) return;
        _applying = true;
        try { ApplyParameters(); }
        finally { _applying = false; }
    }

    private void ApplyParameters()
    {
        _bound!.Speed = _speed;
        foreach ((string name, AnimatorParameterValue value) in _parameters) value.Apply(_bound, name);
        foreach ((string name, uint revision) in _triggers)
        {
            uint seen = _seenTriggers.GetValueOrDefault(name);
            uint added = unchecked(revision - seen);
            if (added != 0)
            {
                _pendingTriggers[name] = _pendingTriggers.GetValueOrDefault(name) + added;
                _seenTriggers[name] = revision;
            }
        }
        // A graph may consume a trigger during evaluation. Keep successive events for successive evaluations.
        foreach (string name in new List<string>(_pendingTriggers.Keys))
        {
            uint count = _pendingTriggers[name];
            if (count == 0) continue;
            _bound.SetTrigger(name);
            if (count == 1) _pendingTriggers.Remove(name);
            else _pendingTriggers[name] = count - 1;
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
        if (_applying || IsProxy) return;
        s_triggers.Bind(this);
        _triggers[name] = unchecked(_triggers.GetValueOrDefault(name) + 1);
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
        foreach ((string name, uint revision) in _triggers) _seenTriggers[name] = revision;
        _nextSample = 0;
    }
}
