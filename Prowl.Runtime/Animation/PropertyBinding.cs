// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime;

/// <summary>Describes an animatable property or field discovered on a GameObject, Transform, or Component.</summary>
public readonly struct AnimatablePropertyDescriptor
{
    public string ComponentType { get; init; }
    public string PropertyPath { get; init; }
    public string DisplayName { get; init; }
    public Type ValueType { get; init; }
    public bool IsObjectReference { get; init; }
    public bool IsString { get; init; }
    public string[] SubChannels { get; init; }
}

/// <summary>Describes a callable component method discovered on a <see cref="GameObject"/> for <see cref="AnimationClipEvent"/> authoring.</summary>
public readonly struct AnimatableEventMethodDescriptor
{
    public string ComponentType { get; init; }
    public string MethodName { get; init; }
    public string DisplayName { get; init; }
    public ProwlActionArgType ArgType { get; init; }
    public string ParameterName { get; init; }
    public Type? ParameterType { get; init; }
    public bool IsUserScript { get; init; }
}

/// <summary>
/// Binds an <see cref="AnimationClip"/>'s property tracks to a live <see cref="GameObject"/> hierarchy
/// and evaluates or records properties at any time.
/// Groups sub-channel tracks (such as <c>LocalPosition.X</c>, <c>.Y</c>, <c>.Z</c> or <c>Color.R</c>, <c>.G</c>, <c>.B</c>, <c>.A</c>)
/// into a single property read-modify-write per frame so setters and dirty notifications run cleanly.
/// </summary>
public sealed class AnimationClipBinding
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly GameObject _root;
    private readonly AnimationClip _clip;
    private readonly List<BoundPropertyGroup> _curveGroups = [];
    private readonly List<BoundMaterialPropertyGroup> _materialGroups = [];
    private readonly List<BoundObjectTrack> _objectTracks = [];
    private readonly List<BoundStringTrack> _stringTracks = [];
    private BoundEvent[] _boundEvents = [];
    private readonly HashSet<int> _activeRangedEvents = [];

    private static readonly ConcurrentDictionary<(Type Type, string Name, ProwlActionArgType ArgType), (MethodInfo? Method, int ParamCount)> s_eventMethodCache = new();
    private static readonly ConcurrentDictionary<MethodInfo, Action<Component, EngineObject?>> s_objectEventCallers = new();
    private static readonly ConcurrentDictionary<(Type Type, string Path), MemberAccessChain> s_accessChainCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Path), (Func<object, float>? Get, Action<object, float>? Set)> s_scalarAccessorCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Path), (Func<object, Float4>? Get, Action<object, Float4>? Set)> s_vec4AccessorCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Path), (Func<object, EngineObject?>? Get, Action<object, EngineObject?>? Set)> s_objectAccessorCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Path), (Func<object, string?>? Get, Action<object, string>? Set)> s_stringAccessorCache = new();

    public GameObject Root => _root;
    public AnimationClip Clip => _clip;

    public int BoundCurveGroupCount => _curveGroups.Count;
    public int BoundMaterialGroupCount => _materialGroups.Count;
    public int BoundObjectTrackCount => _objectTracks.Count;
    public int BoundStringTrackCount => _stringTracks.Count;
    public bool HasActiveRangedEvents => _activeRangedEvents.Count > 0;

    public AnimationClipBinding(GameObject root, AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clip);
        _root = root;
        _clip = clip;
        BuildBindings();
    }

    /// <summary>Evaluates all bound curve, object, and string tracks at <paramref name="time"/> and applies them to the hierarchy.</summary>
    public void Sample(float time)
    {
        if (_root.IsNotValid()) return;
        bool hasSolo = _clip.HasSoloTrack;

        for (int i = 0; i < _curveGroups.Count; i++)
            _curveGroups[i].Apply(time, 1f, hasSolo);

        for (int i = 0; i < _materialGroups.Count; i++)
            _materialGroups[i].Apply(time, 1f, hasSolo);

        for (int i = 0; i < _objectTracks.Count; i++)
            _objectTracks[i].Apply(time, hasSolo);

        for (int i = 0; i < _stringTracks.Count; i++)
            _stringTracks[i].Apply(time, hasSolo);
    }

    /// <summary>
    /// Evaluates all bound tracks at <paramref name="time"/> with blend <paramref name="weight"/> in [0, 1].
    /// When <paramref name="weight"/> is below 1, continuous numeric/vector/color/quaternion channels blend smoothly
    /// from the current value on the target toward the clip's evaluated value.
    /// </summary>
    public void SampleWeighted(float time, float weight)
    {
        if (_root.IsNotValid() || weight <= 1e-5f) return;
        float w = Math.Clamp(weight, 0f, 1f);
        bool hasSolo = _clip.HasSoloTrack;

        for (int i = 0; i < _curveGroups.Count; i++)
            _curveGroups[i].Apply(time, w, hasSolo);

        for (int i = 0; i < _materialGroups.Count; i++)
            _materialGroups[i].Apply(time, w, hasSolo);

        if (w >= 0.5f)
        {
            for (int i = 0; i < _objectTracks.Count; i++)
                _objectTracks[i].Apply(time, hasSolo);

            for (int i = 0; i < _stringTracks.Count; i++)
                _stringTracks[i].Apply(time, hasSolo);
        }
    }

    /// <summary>Captures the current property values on the target hierarchy as the pre-animated baseline for Additive tracks and <see cref="AnimationClipCompletionMode.RestoreState"/>.</summary>
    public void CaptureBaseline()
    {
        if (_root.IsNotValid()) return;
        for (int i = 0; i < _curveGroups.Count; i++)
            _curveGroups[i].CaptureBaseline();
        for (int i = 0; i < _materialGroups.Count; i++)
            _materialGroups[i].CaptureBaseline();
        for (int i = 0; i < _objectTracks.Count; i++)
            _objectTracks[i].CaptureBaseline();
        for (int i = 0; i < _stringTracks.Count; i++)
            _stringTracks[i].CaptureBaseline();
    }

    /// <summary>Restores all bound properties on the target hierarchy to their pre-animated baseline values.</summary>
    public void RestoreBaseline()
    {
        if (_root.IsNotValid()) return;
        for (int i = 0; i < _curveGroups.Count; i++)
            _curveGroups[i].RestoreBaseline();
        for (int i = 0; i < _materialGroups.Count; i++)
            _materialGroups[i].RestoreBaseline();
        for (int i = 0; i < _objectTracks.Count; i++)
            _objectTracks[i].RestoreBaseline();
        for (int i = 0; i < _stringTracks.Count; i++)
            _stringTracks[i].RestoreBaseline();
    }

    /// <summary>
    /// Fires any instant <see cref="AnimationClipEvent"/> whose timestamp falls between <paramref name="previousTime"/> and <paramref name="currentTime"/>,
    /// and evaluates Enter, Stay (passing normalized 0..1 progress), and Exit callbacks for ranged events (<see cref="AnimationClipEvent.IsRange"/>).
    /// Handles loop wrap-around when <paramref name="looped"/> is true.
    /// </summary>
    public void FireEvents(float previousTime, float currentTime, bool forward, bool looped)
    {
        if (_root.IsNotValid() || _clip.PropertyEvents.Count == 0) return;

        float duration = _clip.Duration;
        for (int i = 0; i < _clip.PropertyEvents.Count; i++)
        {
            AnimationClipEvent ev = _clip.PropertyEvents[i];
            if (!ev.IsRange)
            {
                if (ShouldFire(ev.Time, previousTime, currentTime, duration, forward, looped))
                    InvokeBoundEnter(i, ev);
            }
            else
            {
                EvaluateRangedEvent(i, ev, previousTime, currentTime, duration, forward, looped);
            }
        }
    }

    /// <summary>Exits any currently active ranged events (e.g. when playback stops, transitions away, or switches clips).</summary>
    public void ResetActiveEvents(bool fireExitCallbacks = true)
    {
        if (_activeRangedEvents.Count == 0) return;

        if (!fireExitCallbacks || _root.IsNotValid())
        {
            _activeRangedEvents.Clear();
            return;
        }

        // Snapshot indices before clearing so re-entrant callbacks cannot mutate the set mid-iteration.
        Span<int> activeIndices = _activeRangedEvents.Count <= 32 ? stackalloc int[_activeRangedEvents.Count] : new int[_activeRangedEvents.Count];
        int count = 0;
        foreach (int idx in _activeRangedEvents)
            activeIndices[count++] = idx;
        _activeRangedEvents.Clear();

        for (int i = 0; i < count; i++)
        {
            int idx = activeIndices[i];
            if (idx >= 0 && idx < _clip.PropertyEvents.Count)
            {
                AnimationClipEvent ev = _clip.PropertyEvents[idx];
                if (ev.IsRange && !string.IsNullOrEmpty(ev.ExitFunctionName))
                    InvokeBoundExit(idx, ev);
            }
        }
    }

    private void EvaluateRangedEvent(int index, AnimationClipEvent ev, float prev, float curr, float duration, bool forward, bool looped)
    {
        float start = ev.Time;
        float end = ev.EndTime;
        float span = MathF.Max(1e-5f, end - start);

        bool wasActive = _activeRangedEvents.Contains(index);
        bool isInsideAtCurr = curr >= start - 1e-5f && curr <= end + 1e-5f;
        bool crossedWholeRange = RangeCrossed(start, end, prev, curr, duration, forward, looped);

        if (looped && wasActive)
        {
            // Wrapped across loop boundary while inside the range: finish previous cycle and re-enter if still inside
            if (!string.IsNullOrEmpty(ev.ExitFunctionName))
                InvokeBoundExit(index, ev);
            _activeRangedEvents.Remove(index);
            wasActive = false;
        }

        if (!wasActive)
        {
            if (isInsideAtCurr)
            {
                // Entered the range and stayed inside it at the end of this step
                _activeRangedEvents.Add(index);
                if (!string.IsNullOrEmpty(ev.FunctionName))
                    InvokeBoundEnter(index, ev);
                if (!string.IsNullOrEmpty(ev.StayFunctionName))
                {
                    float progress = Math.Clamp((curr - start) / span, 0f, 1f);
                    InvokeBoundStay(index, ev, progress);
                }
            }
            else if (crossedWholeRange)
            {
                // A single frame step jumped completely across [start, end]: fire Enter, Stay(1), and Exit in sequence
                if (!string.IsNullOrEmpty(ev.FunctionName))
                    InvokeBoundEnter(index, ev);
                if (!string.IsNullOrEmpty(ev.StayFunctionName))
                    InvokeBoundStay(index, ev, forward ? 1f : 0f);
                if (!string.IsNullOrEmpty(ev.ExitFunctionName))
                    InvokeBoundExit(index, ev);
            }
        }
        else
        {
            if (isInsideAtCurr)
            {
                // Remaining inside the range: fire Stay
                if (!string.IsNullOrEmpty(ev.StayFunctionName))
                {
                    float progress = Math.Clamp((curr - start) / span, 0f, 1f);
                    InvokeBoundStay(index, ev, progress);
                }
            }
            else
            {
                // Left the range this step: fire Exit
                _activeRangedEvents.Remove(index);
                if (!string.IsNullOrEmpty(ev.ExitFunctionName))
                    InvokeBoundExit(index, ev);
            }
        }
    }

    private void InvokeBoundEnter(int index, AnimationClipEvent ev)
    {
        if ((uint)index < (uint)_boundEvents.Length && _boundEvents[index].TryInvokeEnter(ev))
            return;
        InvokeEvent(_root, ev);
    }

    private void InvokeBoundStay(int index, AnimationClipEvent ev, float progress)
    {
        if ((uint)index < (uint)_boundEvents.Length && _boundEvents[index].TryInvokeStay(progress))
            return;
        InvokeNamedEventMethod(_root, ev.RelativePath, ev.StayFunctionName, ProwlActionArgType.Float, progress);
    }

    private void InvokeBoundExit(int index, AnimationClipEvent ev)
    {
        if ((uint)index < (uint)_boundEvents.Length && _boundEvents[index].TryInvokeExit(ev))
            return;
        InvokeNamedEventMethod(_root, ev.RelativePath, ev.ExitFunctionName, ev.ArgType, GetEventArg(ev));
    }

    private static bool RangeCrossed(float start, float end, float prev, float curr, float duration, bool forward, bool looped)
    {
        if (forward)
        {
            if (!looped)
                return prev < start && curr > end;
            return (prev < start && duration + 1e-5f > end) || (0f <= start && curr > end);
        }
        else
        {
            if (!looped)
                return prev > end && curr < start;
            return (prev > end && -1e-5f < start) || (duration >= end && curr < start);
        }
    }

    private static bool ShouldFire(float eventTime, float prev, float curr, float duration, bool forward, bool looped)
    {
        if (forward)
        {
            if (!looped)
                return eventTime > prev && eventTime <= curr;
            return (eventTime > prev && eventTime <= duration + 1e-5f) || (eventTime >= 0f && eventTime <= curr);
        }
        else
        {
            if (!looped)
                return eventTime < prev && eventTime >= curr;
            return (eventTime < prev && eventTime >= -1e-5f) || (eventTime <= duration && eventTime >= curr);
        }
    }

    private static object? GetEventArg(AnimationClipEvent ev) => ev.ArgType switch
    {
        ProwlActionArgType.Bool => ev.BoolParam,
        ProwlActionArgType.Int => ev.IntParam,
        ProwlActionArgType.Float => ev.FloatParam,
        ProwlActionArgType.String => ev.StringParam,
        ProwlActionArgType.Object => ev.ObjectParam,
        _ => null,
    };

    public static void InvokeEvent(GameObject root, AnimationClipEvent ev)
    {
        if (root.IsNotValid() || string.IsNullOrEmpty(ev.FunctionName)) return;
        InvokeNamedEventMethod(root, ev.RelativePath, ev.FunctionName, ev.ArgType, GetEventArg(ev));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Animation events invoke component methods preserved by user scripts.")]
    public static void InvokeNamedEventMethod(GameObject root, string relativePath, string functionName, ProwlActionArgType argType, object? arg)
    {
        if (root.IsNotValid() || string.IsNullOrEmpty(functionName)) return;
        GameObject? targetGo = ResolveGameObject(root, relativePath);
        if (targetGo.IsNotValid()) return;

        for (int i = 0; i < targetGo._components.Count; i++)
        {
            Component comp = targetGo._components[i];
            if (comp.IsNotValid() || !comp.EnabledInHierarchy) continue;

            (MethodInfo? method, int paramCount) = GetCachedEventMethod(comp.GetType(), functionName, argType);
            if (method == null) continue;

            try
            {
                method.Invoke(comp, paramCount == 0 ? null : [arg]);
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException : ex;
                Debug.LogException($"[Animator] Animation event '{functionName}' on '{targetGo.Name}/{comp.GetType().Name}' threw", inner);
            }
        }
    }

    private static (MethodInfo? Method, int ParamCount) GetCachedEventMethod(Type type, string name, ProwlActionArgType argType)
    {
        var key = (type, name, argType);
        if (s_eventMethodCache.TryGetValue(key, out (MethodInfo? Method, int ParamCount) cached))
            return cached;

        (MethodInfo? Method, int ParamCount) resolved = ResolveEventMethod(type, name, argType);
        s_eventMethodCache[key] = resolved;
        return resolved;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070:DynamicallyAccessedMembers",
        Justification = "Animation events invoke component methods preserved by user scripts.")]
    private static (MethodInfo? Method, int ParamCount) ResolveEventMethod(Type type, string name, ProwlActionArgType argType)
    {
        MethodInfo? parameterlessFallback = null;
        foreach (MethodInfo m in type.GetMethods(MemberFlags))
        {
            if (m.Name != name || m.IsSpecialName) continue;
            ParameterInfo[] ps = m.GetParameters();
            if (argType == ProwlActionArgType.None)
            {
                if (ps.Length == 0) return (m, 0);
            }
            else if (ps.Length == 1 && ProwlActionArg.Matches(ps[0].ParameterType, argType))
            {
                return (m, 1);
            }
            else if (ps.Length == 0)
            {
                parameterlessFallback = m;
            }
        }
        return (parameterlessFallback, 0);
    }

    private static readonly HashSet<string> s_excludedLifecycleMethods = new(StringComparer.Ordinal)
    {
        "Awake", "Start", "Update", "FixedUpdate", "LateUpdate",
        "OnEnable", "OnDisable", "OnDestroy", "OnValidate", "Reset",
        "OnDrawGizmos", "OnDrawGizmosSelected",
        "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
        "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit",
        "AssertOwner", "Rebind", "RecompileGraph",
    };

    /// <summary>
    /// Discovers all public instance <c>void</c> methods taking 0 or 1 supported argument on components attached to <paramref name="go"/>,
    /// sorted with user script methods first followed by built-in engine components.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Editor animation event picker reflects over attached component methods.")]
    public static List<AnimatableEventMethodDescriptor> GetCallableEventMethods(GameObject go)
    {
        var userMethods = new List<AnimatableEventMethodDescriptor>();
        var builtinMethods = new List<AnimatableEventMethodDescriptor>();
        if (go.IsNotValid()) return userMethods;

        for (int i = 0; i < go._components.Count; i++)
        {
            Component comp = go._components[i];
            if (comp.IsNotValid() || comp.HideFlags.HasFlag(HideFlags.Hide)) continue;

            Type compType = comp.GetType();
            string compName = compType.Name;
            bool isUserScript = compType.Assembly != typeof(Component).Assembly;
            List<AnimatableEventMethodDescriptor> bucket = isUserScript ? userMethods : builtinMethods;

            foreach (MethodInfo m in compType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.IsSpecialName || m.IsGenericMethodDefinition || m.ContainsGenericParameters) continue;
                if (m.ReturnType != typeof(void)) continue;
                if (s_excludedLifecycleMethods.Contains(m.Name)) continue;
                if (m.GetCustomAttribute<ObsoleteAttribute>() != null) continue;

                Type? origin = m.GetBaseDefinition().DeclaringType;
                if (origin != null && (origin.IsAssignableFrom(typeof(Component)) || origin == typeof(Component)))
                    continue;

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0)
                {
                    bucket.Add(new AnimatableEventMethodDescriptor
                    {
                        ComponentType = compName,
                        MethodName = m.Name,
                        DisplayName = $"{compName} / {m.Name} ()",
                        ArgType = ProwlActionArgType.None,
                        ParameterName = string.Empty,
                        ParameterType = null,
                        IsUserScript = isUserScript,
                    });
                }
                else if (ps.Length == 1 && ProwlActionArg.TryFromType(ps[0].ParameterType, out ProwlActionArgType at))
                {
                    Type paramType = ps[0].ParameterType;
                    string typeLabel = at == ProwlActionArgType.Object ? paramType.Name : ProwlActionArg.Label(at);
                    string paramName = ps[0].Name ?? "value";
                    bucket.Add(new AnimatableEventMethodDescriptor
                    {
                        ComponentType = compName,
                        MethodName = m.Name,
                        DisplayName = $"{compName} / {m.Name} ({typeLabel} {paramName})",
                        ArgType = at,
                        ParameterName = paramName,
                        ParameterType = paramType,
                        IsUserScript = isUserScript,
                    });
                }
            }
        }

        userMethods.AddRange(builtinMethods);
        return userMethods;
    }

    /// <summary>
    /// Checks whether <paramref name="functionName"/> resolves to a callable method on <paramref name="root"/> (at <paramref name="relativePath"/>),
    /// returning its human-readable signature and expected parameter type for the Editor UI.
    /// </summary>
    public static bool TryValidateEventMethod(GameObject root, string relativePath, string functionName, ProwlActionArgType argType,
        out string resolvedSignature, out string parameterName, out Type? parameterType)
    {
        resolvedSignature = string.Empty;
        parameterName = "Value";
        parameterType = null;

        if (root.IsNotValid() || string.IsNullOrEmpty(functionName)) return false;
        GameObject? targetGo = ResolveGameObject(root, relativePath);
        if (targetGo.IsNotValid()) return false;

        for (int i = 0; i < targetGo!._components.Count; i++)
        {
            Component comp = targetGo._components[i];
            if (comp.IsNotValid()) continue;

            (MethodInfo? method, int paramCount) = GetCachedEventMethod(comp.GetType(), functionName, argType);
            if (method == null) continue;

            if (paramCount == 1)
            {
                ParameterInfo p = method.GetParameters()[0];
                parameterType = p.ParameterType;
                parameterName = !string.IsNullOrEmpty(p.Name) ? p.Name : "Value";
                resolvedSignature = $"{comp.GetType().Name}.{method.Name}({p.ParameterType.Name})";
            }
            else
            {
                resolvedSignature = $"{comp.GetType().Name}.{method.Name}()";
            }
            return true;
        }
        return false;
    }

    // ================================================================
    //  Target & Member Resolution
    // ================================================================

    /// <summary>
    /// Resolves a relative hierarchy path from <paramref name="root"/> to a target <see cref="GameObject"/>.
    /// Paths starting with <c>'@'</c> (e.g. <c>"@MainCamera"</c> or <c>"@Boss/RightHand"</c>) resolve against
    /// the <see cref="Animator.ObjectBindings"/> slot table on <paramref name="root"/>.
    /// </summary>
    public static GameObject? ResolveGameObject(GameObject root, string relativePath)
    {
        if (root.IsNotValid()) return null;
        if (string.IsNullOrEmpty(relativePath)) return root;

        if (relativePath[0] == '@')
        {
            int slashIdx = relativePath.IndexOf('/');
            string slotName = slashIdx > 0 ? relativePath[1..slashIdx] : relativePath[1..];
            string subPath = slashIdx > 0 ? relativePath[(slashIdx + 1)..] : string.Empty;

            Animator? animator = root.GetComponent<Animator>();
            if (animator.IsNotValid()) return null;

            GameObject? boundGo = animator!.GetObjectBinding(slotName);
            if (boundGo.IsNotValid()) return null;
            if (string.IsNullOrEmpty(subPath)) return boundGo;

            Transform? subFound = boundGo!.Transform.Find(subPath);
            return subFound != null && subFound.GameObject.IsValid() ? subFound.GameObject : null;
        }

        Transform? found = root.Transform.Find(relativePath);
        return found != null && found.GameObject.IsValid() ? found.GameObject : null;
    }

    /// <summary>
    /// Returns true if the target <see cref="GameObject"/>, <paramref name="componentType"/>, and <paramref name="propertyPath"/>
    /// all currently resolve on <paramref name="root"/> (used by the Editor to detect broken/missing track bindings).
    /// </summary>
    public static bool IsTrackTargetValid(GameObject root, string relativePath, string componentType, string propertyPath)
    {
        if (root.IsNotValid()) return false;
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;
        if (string.IsNullOrEmpty(propertyPath)) return true;
        if (TryParseMaterialUniformPath(target, propertyPath, out _, out _, out _, out _))
            return true;
        return TryResolveAccessChain(target, propertyPath, out _);
    }

    /// <summary>Resolves the target object (<see cref="GameObject"/>, <see cref="Transform"/>, or <see cref="Component"/>) on <paramref name="go"/>.</summary>
    public static object? ResolveTargetObject(GameObject go, string componentType)
    {
        if (go.IsNotValid() || string.IsNullOrEmpty(componentType)) return null;

        if (componentType.Equals("GameObject", StringComparison.OrdinalIgnoreCase) || componentType == "$")
            return go;

        if (componentType.Equals("Transform", StringComparison.OrdinalIgnoreCase))
            return go.Transform;

        // Check exact or short name match on attached components
        for (int i = 0; i < go._components.Count; i++)
        {
            Component c = go._components[i];
            if (c.IsNotValid()) continue;
            Type t = c.GetType();
            if (t.Name.Equals(componentType, StringComparison.OrdinalIgnoreCase) ||
                (t.FullName != null && t.FullName.Equals(componentType, StringComparison.OrdinalIgnoreCase)))
                return c;
        }
        return null;
    }

    /// <summary>Returns the canonical component type identifier used in tracks for a given target object.</summary>
    public static string GetComponentTypeName(object target) => target switch
    {
        GameObject => "GameObject",
        Transform => "Transform",
        Component c => c.GetType().Name,
        _ => target.GetType().Name,
    };

    /// <summary>Reads the current scalar value of a curve property path from the hierarchy under <paramref name="root"/>.</summary>
    public static bool TryReadScalar(GameObject root, string relativePath, string componentType, string propertyPath, out float value)
    {
        value = 0f;
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;

        return TryReadScalarOnTarget(target, propertyPath, out value);
    }

    /// <summary>Reads the current scalar value of a property path directly on <paramref name="target"/>.</summary>
    public static bool TryReadScalarOnTarget(object target, string propertyPath, out float value)
    {
        value = 0f;
        if (target is Transform tf && TryFastReadTransform(tf, propertyPath, out value))
            return true;
        if (target is GameObject go && propertyPath.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            value = go.Enabled ? 1f : 0f;
            return true;
        }
        if (TryParseMaterialUniformPath(target, propertyPath, out Material? mat, out string uniformName, out MaterialPropertyType propType, out SubChannelKind sub) && mat.IsValid())
        {
            value = ReadMaterialUniformChannel(mat!, uniformName, propType, sub);
            return true;
        }

        if (!TryResolveAccessChain(target, propertyPath, out MemberAccessChain chain))
            return false;

        object? leaf = chain.GetLeafValue(target);
        return TryConvertToFloat(leaf, chain.SubChannel, out value);
    }

    /// <summary>Reads the current <see cref="EngineObject"/> reference of a property path from the hierarchy under <paramref name="root"/>.</summary>
    public static bool TryReadObject(GameObject root, string relativePath, string componentType, string propertyPath, out EngineObject? value)
    {
        value = null;
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;

        if (!TryResolveAccessChain(target, propertyPath, out MemberAccessChain chain) || chain.SubChannel != SubChannelKind.None)
            return false;

        if (!typeof(EngineObject).IsAssignableFrom(chain.LeafType))
            return false;

        value = chain.GetLeafValue(target) as EngineObject;
        return true;
    }

    /// <summary>Reads the current <see cref="string"/> value of a property path from the hierarchy under <paramref name="root"/>.</summary>
    public static bool TryReadString(GameObject root, string relativePath, string componentType, string propertyPath, out string value)
    {
        value = string.Empty;
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;

        if (!TryResolveAccessChain(target, propertyPath, out MemberAccessChain chain) || chain.SubChannel != SubChannelKind.None)
            return false;

        if (chain.LeafType != typeof(string))
            return false;

        value = (chain.GetLeafValue(target) as string) ?? string.Empty;
        return true;
    }

    /// <summary>Writes a single scalar property path on <paramref name="root"/> immediately.</summary>
    public static bool TryWriteScalar(GameObject root, string relativePath, string componentType, string propertyPath, float value)
    {
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;

        if (TryParseMaterialUniformPath(target, propertyPath, out Material? mat, out string uniformName, out MaterialPropertyType propType, out SubChannelKind matSub) && mat.IsValid())
        {
            WriteMaterialUniformChannel(mat!, uniformName, propType, matSub, value);
            NotifyTargetChanged(target);
            return true;
        }

        if (!TryResolveAccessChain(target, propertyPath, out MemberAccessChain chain))
            return false;

        object? current = chain.GetLeafValue(target);
        object? updated = ApplySubChannel(current, chain.LeafType, chain.SubChannel, value);
        if (updated == null) return false;

        chain.SetLeafValue(target, updated);
        NotifyTargetChanged(target);
        return true;
    }

    /// <summary>Reads the raw value of a property path (without sub-channel splitting) on a target in the hierarchy.</summary>
    public static bool TryReadRawProperty(GameObject root, string relativePath, string componentType, string basePropertyPath, out object? value, out Type? memberType)
    {
        value = null;
        memberType = null;
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;

        if (!TryResolveAccessChain(target, basePropertyPath, out MemberAccessChain chain))
            return false;

        memberType = chain.LeafType;
        value = chain.GetLeafValue(target);
        return true;
    }

    /// <summary>Writes the raw value of a property path (without sub-channel splitting) on a target in the hierarchy.</summary>
    public static bool TryWriteRawProperty(GameObject root, string relativePath, string componentType, string basePropertyPath, object? value)
    {
        GameObject? go = ResolveGameObject(root, relativePath);
        if (go.IsNotValid()) return false;
        object? target = ResolveTargetObject(go!, componentType);
        if (target == null) return false;

        if (!TryResolveAccessChain(target, basePropertyPath, out MemberAccessChain chain))
            return false;

        chain.SetLeafValue(target, value);
        NotifyTargetChanged(target);
        return true;
    }

    // ================================================================
    //  Discovery of Animatable Properties (for Editor Add Property UI)
    // ================================================================

    /// <summary>Enumerates all animatable properties on a GameObject, its Transform, and its attached Components.</summary>
    public static List<AnimatablePropertyDescriptor> GetAnimatableProperties(GameObject go)
    {
        var list = new List<AnimatablePropertyDescriptor>();
        if (go.IsNotValid()) return list;

        // 1. GameObject properties
        list.Add(new AnimatablePropertyDescriptor
        {
            ComponentType = "GameObject",
            PropertyPath = "Enabled",
            DisplayName = "GameObject.Enabled",
            ValueType = typeof(bool),
            SubChannels = [],
        });

        // 2. Transform properties
        list.Add(new AnimatablePropertyDescriptor
        {
            ComponentType = "Transform",
            PropertyPath = "LocalPosition",
            DisplayName = "Transform.LocalPosition",
            ValueType = typeof(Float3),
            SubChannels = ["X", "Y", "Z"],
        });
        list.Add(new AnimatablePropertyDescriptor
        {
            ComponentType = "Transform",
            PropertyPath = "LocalEulerAngles",
            DisplayName = "Transform.LocalEulerAngles",
            ValueType = typeof(Float3),
            SubChannels = ["X", "Y", "Z"],
        });
        list.Add(new AnimatablePropertyDescriptor
        {
            ComponentType = "Transform",
            PropertyPath = "LocalScale",
            DisplayName = "Transform.LocalScale",
            ValueType = typeof(Float3),
            SubChannels = ["X", "Y", "Z"],
        });

        // 3. Attached Components
        for (int i = 0; i < go._components.Count; i++)
        {
            Component comp = go._components[i];
            if (comp.IsNotValid() || comp.HideFlags.HasFlag(HideFlags.Hide)) continue;

            Type compType = comp.GetType();
            string compName = compType.Name;

            // Component.Enabled is always animatable
            list.Add(new AnimatablePropertyDescriptor
            {
                ComponentType = compName,
                PropertyPath = "Enabled",
                DisplayName = $"{compName}.Enabled",
                ValueType = typeof(bool),
                SubChannels = [],
            });

            CollectAnimatableMembers(compType, compName, "", list, depth: 0);

            // Expose shader uniform properties on Material renderers (Unity-style Material.<Uniform> animation)
            if (comp is IMaterialRenderer matRenderer && matRenderer.MaterialSlotCount > 0)
            {
                Material? mat = matRenderer.GetMaterial(0);
                if (mat.IsValid() && mat!.Shader.IsValid())
                {
                    Shader shader = mat.Shader!;
                    if (shader.State is not (AssetState.Missing or AssetState.Failed))
                        shader.Load();
                    if (shader.IsLoaded)
                    {
                        foreach (MaterialProperty mp in shader.Properties)
                        {
                            string matPath = $"Material.{mp.Name}";
                            switch (mp.Type)
                            {
                                case MaterialPropertyType.Float:
                                    list.Add(new AnimatablePropertyDescriptor { ComponentType = compName, PropertyPath = matPath, DisplayName = $"{compName}.{matPath}", ValueType = typeof(float), SubChannels = [] });
                                    break;
                                case MaterialPropertyType.Int:
                                    list.Add(new AnimatablePropertyDescriptor { ComponentType = compName, PropertyPath = matPath, DisplayName = $"{compName}.{matPath}", ValueType = typeof(int), SubChannels = [] });
                                    break;
                                case MaterialPropertyType.Color:
                                    list.Add(new AnimatablePropertyDescriptor { ComponentType = compName, PropertyPath = matPath, DisplayName = $"{compName}.{matPath}", ValueType = typeof(Color), SubChannels = ["R", "G", "B", "A"] });
                                    break;
                                case MaterialPropertyType.Float2:
                                    list.Add(new AnimatablePropertyDescriptor { ComponentType = compName, PropertyPath = matPath, DisplayName = $"{compName}.{matPath}", ValueType = typeof(Float2), SubChannels = ["X", "Y"] });
                                    break;
                                case MaterialPropertyType.Float3:
                                    list.Add(new AnimatablePropertyDescriptor { ComponentType = compName, PropertyPath = matPath, DisplayName = $"{compName}.{matPath}", ValueType = typeof(Float3), SubChannels = ["X", "Y", "Z"] });
                                    break;
                                case MaterialPropertyType.Float4:
                                    list.Add(new AnimatablePropertyDescriptor { ComponentType = compName, PropertyPath = matPath, DisplayName = $"{compName}.{matPath}", ValueType = typeof(Float4), SubChannels = ["X", "Y", "Z", "W"] });
                                    break;
                            }
                        }
                    }
                }
            }
        }

        return list;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070:DynamicallyAccessedMembers",
        Justification = "Editor property discovery reflects over component types.")]
    private static void CollectAnimatableMembers(Type type, string componentName, string prefix, List<AnimatablePropertyDescriptor> into, int depth)
    {
        if (depth > 2) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Properties first (public instance with getter + setter)
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
            if (prop.Name is "Name" or "HideFlags" or "Enabled" or "EnabledInHierarchy" or "Tag" or "Layer" or "Persistent" or "SiblingIndex") continue;
            if (prop.GetCustomAttribute<ObsoleteAttribute>() != null) continue;

            string path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            if (TryDescribeType(componentName, path, prop.PropertyType, out AnimatablePropertyDescriptor desc))
            {
                seen.Add(prop.Name);
                into.Add(desc);
            }
        }

        // Serialized / public fields
        foreach (FieldInfo field in type.GetFields(MemberFlags))
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral) continue;
            if (seen.Contains(field.Name)) continue;
            if (field.GetCustomAttribute<SerializeIgnoreAttribute>() != null || field.GetCustomAttribute<NonSerializedAttribute>() != null) continue;
            bool isPublic = field.IsPublic;
            bool hasSerializeField = field.GetCustomAttribute<SerializeFieldAttribute>() != null;
            if (!isPublic && !hasSerializeField) continue;
            if (field.Name.StartsWith('_')) continue;

            string path = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
            if (TryDescribeType(componentName, path, field.FieldType, out AnimatablePropertyDescriptor desc))
            {
                seen.Add(field.Name);
                into.Add(desc);
            }
            else if (depth < 1 && IsNestedSerializableStructOrClass(field.FieldType))
            {
                CollectAnimatableMembers(field.FieldType, componentName, path, into, depth + 1);
            }
        }
    }

    private static bool IsNestedSerializableStructOrClass(Type t)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) return false;
        if (typeof(EngineObject).IsAssignableFrom(t) || typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return false;
        if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal)) return false;
        return t.IsValueType || t.GetCustomAttribute<SerializableAttribute>() != null;
    }

    public static bool TryDescribeType(string componentName, string propertyPath, Type memberType, out AnimatablePropertyDescriptor descriptor)
    {
        descriptor = default;
        string display = $"{componentName}.{propertyPath}";

        if (memberType == typeof(float) || memberType == typeof(double) ||
            memberType == typeof(int) || memberType == typeof(bool) || memberType.IsEnum)
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                SubChannels = [],
            };
            return true;
        }

        if (memberType == typeof(Float2) || memberType == typeof(Double2) || memberType == typeof(Int2))
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                SubChannels = ["X", "Y"],
            };
            return true;
        }

        if (memberType == typeof(Float3) || memberType == typeof(Double3) || memberType == typeof(Int3))
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                SubChannels = ["X", "Y", "Z"],
            };
            return true;
        }

        if (memberType == typeof(Float4) || memberType == typeof(Double4) || memberType == typeof(Int4) || memberType == typeof(Quaternion))
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                SubChannels = ["X", "Y", "Z", "W"],
            };
            return true;
        }

        if (memberType == typeof(Color) || memberType == typeof(Color32))
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                SubChannels = ["R", "G", "B", "A"],
            };
            return true;
        }

        if (typeof(EngineObject).IsAssignableFrom(memberType))
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                IsObjectReference = true,
                SubChannels = [],
            };
            return true;
        }

        if (memberType == typeof(string))
        {
            descriptor = new AnimatablePropertyDescriptor
            {
                ComponentType = componentName,
                PropertyPath = propertyPath,
                DisplayName = display,
                ValueType = memberType,
                IsString = true,
                SubChannels = [],
            };
            return true;
        }

        return false;
    }

    // ================================================================
    //  Internal Binding Construction
    // ================================================================

    private void BuildBindings()
    {
        // Group curve tracks by (Target Object, Base Member Chain) so vector/color sub-channels update together.
        var groupMap = new Dictionary<(object Target, string BasePath), BoundPropertyGroup>();
        var matGroupMap = new Dictionary<(IMaterialRenderer Renderer, string UniformName), BoundMaterialPropertyGroup>();

        for (int i = 0; i < _clip.CurveTracks.Count; i++)
        {
            AnimationCurveTrack track = _clip.CurveTracks[i];
            if (track.Curve.Count == 0) continue;

            GameObject? go = ResolveGameObject(_root, track.RelativePath);
            if (go.IsNotValid()) continue;

            object? target = ResolveTargetObject(go!, track.ComponentType);
            if (target == null) continue;

            if (target is IMaterialRenderer matRenderer &&
                TryParseMaterialUniformPath(target, track.PropertyPath, out _, out string uniformName, out MaterialPropertyType propType, out SubChannelKind matSub))
            {
                var matKey = (matRenderer, uniformName);
                if (!matGroupMap.TryGetValue(matKey, out BoundMaterialPropertyGroup? matGroup))
                {
                    matGroup = new BoundMaterialPropertyGroup(matRenderer, uniformName, propType);
                    matGroupMap[matKey] = matGroup;
                    _materialGroups.Add(matGroup);
                }
                matGroup.AddChannel(matSub, track);
                continue;
            }

            if (!TryResolveAccessChain(target, track.PropertyPath, out MemberAccessChain chain))
                continue;

            (object target, string BasePath) key = (target, chain.BasePath);
            if (!groupMap.TryGetValue(key, out BoundPropertyGroup? group))
            {
                group = new BoundPropertyGroup(target, chain);
                groupMap[key] = group;
                _curveGroups.Add(group);
            }
            group.AddChannel(chain.SubChannel, track);
        }

        for (int i = 0; i < _clip.ObjectTracks.Count; i++)
        {
            AnimationObjectTrack track = _clip.ObjectTracks[i];
            if (track.Keyframes.Count == 0) continue;

            GameObject? go = ResolveGameObject(_root, track.RelativePath);
            if (go.IsNotValid()) continue;

            object? target = ResolveTargetObject(go!, track.ComponentType);
            if (target == null) continue;

            if (!TryResolveAccessChain(target, track.PropertyPath, out MemberAccessChain chain))
                continue;

            if (typeof(EngineObject).IsAssignableFrom(chain.LeafType))
                _objectTracks.Add(new BoundObjectTrack(target, chain, track));
        }

        for (int i = 0; i < _clip.StringTracks.Count; i++)
        {
            AnimationStringTrack track = _clip.StringTracks[i];
            if (track.Keyframes.Count == 0) continue;

            GameObject? go = ResolveGameObject(_root, track.RelativePath);
            if (go.IsNotValid()) continue;

            object? target = ResolveTargetObject(go!, track.ComponentType);
            if (target == null) continue;

            if (!TryResolveAccessChain(target, track.PropertyPath, out MemberAccessChain chain))
                continue;

            if (chain.LeafType == typeof(string))
                _stringTracks.Add(new BoundStringTrack(target, chain, track));
        }

        if (_clip.PropertyEvents.Count > 0)
        {
            _boundEvents = new BoundEvent[_clip.PropertyEvents.Count];
            for (int i = 0; i < _clip.PropertyEvents.Count; i++)
                _boundEvents[i] = BoundEvent.Create(_root, _clip.PropertyEvents[i]);
        }
    }

    // ================================================================
    //  Fast Transform & Reflection Access Chains
    // ================================================================

    internal enum SubChannelKind : byte
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 3,
        W = 4,
        R = 5,
        G = 6,
        B = 7,
        A = 8,
    }

    internal readonly struct MemberStep
    {
        public readonly FieldInfo? Field;
        public readonly PropertyInfo? Property;

        public MemberStep(FieldInfo field) { Field = field; Property = null; }
        public MemberStep(PropertyInfo property) { Field = null; Property = property; }

        public bool IsValid => Field != null || Property != null;
        public Type MemberType => Field != null ? Field.FieldType : Property!.PropertyType;

        public object? Get(object instance) => Field != null ? Field.GetValue(instance) : Property!.GetValue(instance);

        public void Set(object instance, object? value)
        {
            if (Field != null) Field.SetValue(instance, value);
            else if (Property is { CanWrite: true }) Property.SetValue(instance, value);
        }
    }

    internal readonly struct MemberAccessChain
    {
        public readonly string BasePath;
        public readonly MemberStep[] Steps;
        public readonly SubChannelKind SubChannel;

        public MemberAccessChain(string basePath, MemberStep[] steps, SubChannelKind subChannel)
        {
            BasePath = basePath;
            Steps = steps;
            SubChannel = subChannel;
        }

        public bool IsValid => Steps is { Length: > 0 };
        public Type LeafType => Steps[^1].MemberType;

        public object? GetLeafValue(object rootTarget)
        {
            object? current = rootTarget;
            for (int i = 0; i < Steps.Length; i++)
            {
                if (current == null) return null;
                current = Steps[i].Get(current);
            }
            return current;
        }

        public void SetLeafValue(object rootTarget, object? newLeafValue, ref object?[]? scratchParents)
        {
            if (Steps.Length == 1)
            {
                Steps[0].Set(rootTarget, newLeafValue);
                return;
            }

            // To support nested value types (structs), walk down using a reusable scratch buffer, mutate, and write back up.
            if (scratchParents == null || scratchParents.Length < Steps.Length)
                scratchParents = new object?[Steps.Length];
            scratchParents[0] = rootTarget;
            for (int i = 0; i < Steps.Length - 1; i++)
            {
                scratchParents[i + 1] = Steps[i].Get(scratchParents[i]!);
                if (scratchParents[i + 1] == null) return;
            }

            Steps[^1].Set(scratchParents[^1]!, newLeafValue);

            for (int i = Steps.Length - 2; i >= 0; i--)
            {
                if (Steps[i].MemberType.IsValueType)
                    Steps[i].Set(scratchParents[i]!, scratchParents[i + 1]);
            }
        }

        public void SetLeafValue(object rootTarget, object? newLeafValue)
        {
            object?[]? tmp = null;
            SetLeafValue(rootTarget, newLeafValue, ref tmp);
        }
    }

    // ================================================================
    //  Compiled Zero-Allocation 1-Step Member & Event Delegates
    // ================================================================

    private static Float4 Float2ToVec4(Float2 v) => new(v.X, v.Y, 0f, 0f);
    private static Float2 Vec4ToFloat2(Float4 v) => new(v.X, v.Y);
    private static Float4 Float3ToVec4(Float3 v) => new(v.X, v.Y, v.Z, 0f);
    private static Float3 Vec4ToFloat3(Float4 v) => new(v.X, v.Y, v.Z);
    private static Float4 ColorToVec4(Color c) => new(c.R, c.G, c.B, c.A);
    private static Color Vec4ToColor(Float4 v) => new(v.X, v.Y, v.Z, v.W);
    private static float RoundToIntFloat(float v) => MathF.Round(v);

    private static (Func<object, float>? Get, Action<object, float>? Set) GetOrCompileScalarAccessor(Type targetType, MemberAccessChain chain)
    {
        if (targetType.IsValueType || chain.Steps.Length != 1) return (null, null);
        Type leaf = chain.LeafType;
        if (leaf != typeof(float) && leaf != typeof(double) && leaf != typeof(int) && leaf != typeof(bool))
            return (null, null);

        return s_scalarAccessorCache.GetOrAdd((targetType, chain.BasePath), key =>
        {
            MemberStep step = chain.Steps[0];
            if (step.Property != null && (!step.Property.CanRead || !step.Property.CanWrite))
                return (null, null);

            ParameterExpression objParam = Expression.Parameter(typeof(object), "o");
            UnaryExpression inst = Expression.Convert(objParam, key.Type);
            MemberExpression member = step.Field != null ? Expression.Field(inst, step.Field) : Expression.Property(inst, step.Property!);

            Expression readFloat = leaf switch
            {
                Type t when t == typeof(float) => member,
                Type t when t == typeof(double) => Expression.Convert(member, typeof(float)),
                Type t when t == typeof(int) => Expression.Convert(member, typeof(float)),
                _ => Expression.Condition(member, Expression.Constant(1f), Expression.Constant(0f)),
            };
            Func<object, float> getter = Expression.Lambda<Func<object, float>>(readFloat, objParam).Compile();

            ParameterExpression valParam = Expression.Parameter(typeof(float), "v");
            MethodInfo roundMethod = ((Func<float, float>)RoundToIntFloat).Method;
            Expression writeVal = leaf switch
            {
                Type t when t == typeof(float) => valParam,
                Type t when t == typeof(double) => Expression.Convert(valParam, typeof(double)),
                Type t when t == typeof(int) => Expression.Convert(Expression.Call(roundMethod, valParam), typeof(int)),
                _ => Expression.GreaterThanOrEqual(valParam, Expression.Constant(0.5f)),
            };
            Action<object, float> setter = Expression.Lambda<Action<object, float>>(Expression.Assign(member, writeVal), objParam, valParam).Compile();
            return (getter, setter);
        });
    }

    private static (Func<object, Float4>? Get, Action<object, Float4>? Set) GetOrCompileVec4Accessor(Type targetType, MemberAccessChain chain)
    {
        if (targetType.IsValueType || chain.Steps.Length != 1) return (null, null);
        Type leaf = chain.LeafType;
        if (leaf != typeof(Float2) && leaf != typeof(Float3) && leaf != typeof(Float4) && leaf != typeof(Color))
            return (null, null);

        return s_vec4AccessorCache.GetOrAdd((targetType, chain.BasePath), key =>
        {
            MemberStep step = chain.Steps[0];
            if (step.Property != null && (!step.Property.CanRead || !step.Property.CanWrite))
                return (null, null);

            ParameterExpression objParam = Expression.Parameter(typeof(object), "o");
            UnaryExpression inst = Expression.Convert(objParam, key.Type);
            MemberExpression member = step.Field != null ? Expression.Field(inst, step.Field) : Expression.Property(inst, step.Property!);

            Expression toFloat4 = leaf switch
            {
                Type t when t == typeof(Float4) => member,
                Type t when t == typeof(Float2) => Expression.Call(((Func<Float2, Float4>)Float2ToVec4).Method, member),
                Type t when t == typeof(Float3) => Expression.Call(((Func<Float3, Float4>)Float3ToVec4).Method, member),
                _ => Expression.Call(((Func<Color, Float4>)ColorToVec4).Method, member),
            };
            Func<object, Float4> getter = Expression.Lambda<Func<object, Float4>>(toFloat4, objParam).Compile();

            ParameterExpression valParam = Expression.Parameter(typeof(Float4), "v");
            Expression fromFloat4 = leaf switch
            {
                Type t when t == typeof(Float4) => valParam,
                Type t when t == typeof(Float2) => Expression.Call(((Func<Float4, Float2>)Vec4ToFloat2).Method, valParam),
                Type t when t == typeof(Float3) => Expression.Call(((Func<Float4, Float3>)Vec4ToFloat3).Method, valParam),
                _ => Expression.Call(((Func<Float4, Color>)Vec4ToColor).Method, valParam),
            };
            Action<object, Float4> setter = Expression.Lambda<Action<object, Float4>>(Expression.Assign(member, fromFloat4), objParam, valParam).Compile();
            return (getter, setter);
        });
    }

    private static (Func<object, EngineObject?>? Get, Action<object, EngineObject?>? Set) GetOrCompileObjectAccessor(Type targetType, MemberAccessChain chain)
    {
        if (targetType.IsValueType || chain.Steps.Length != 1 || !typeof(EngineObject).IsAssignableFrom(chain.LeafType))
            return (null, null);

        return s_objectAccessorCache.GetOrAdd((targetType, chain.BasePath), key =>
        {
            MemberStep step = chain.Steps[0];
            if (step.Property != null && (!step.Property.CanRead || !step.Property.CanWrite))
                return (null, null);

            ParameterExpression objParam = Expression.Parameter(typeof(object), "o");
            UnaryExpression inst = Expression.Convert(objParam, key.Type);
            MemberExpression member = step.Field != null ? Expression.Field(inst, step.Field) : Expression.Property(inst, step.Property!);

            Func<object, EngineObject?> getter = Expression.Lambda<Func<object, EngineObject?>>(
                Expression.Convert(member, typeof(EngineObject)), objParam).Compile();

            ParameterExpression valParam = Expression.Parameter(typeof(EngineObject), "v");
            Action<object, EngineObject?> setter = Expression.Lambda<Action<object, EngineObject?>>(
                Expression.Assign(member, Expression.Convert(valParam, chain.LeafType)), objParam, valParam).Compile();
            return (getter, setter);
        });
    }

    private static (Func<object, string?>? Get, Action<object, string>? Set) GetOrCompileStringAccessor(Type targetType, MemberAccessChain chain)
    {
        if (targetType.IsValueType || chain.Steps.Length != 1 || chain.LeafType != typeof(string))
            return (null, null);

        return s_stringAccessorCache.GetOrAdd((targetType, chain.BasePath), key =>
        {
            MemberStep step = chain.Steps[0];
            if (step.Property != null && (!step.Property.CanRead || !step.Property.CanWrite))
                return (null, null);

            ParameterExpression objParam = Expression.Parameter(typeof(object), "o");
            UnaryExpression inst = Expression.Convert(objParam, key.Type);
            MemberExpression member = step.Field != null ? Expression.Field(inst, step.Field) : Expression.Property(inst, step.Property!);

            Func<object, string?> getter = Expression.Lambda<Func<object, string?>>(member, objParam).Compile();

            ParameterExpression valParam = Expression.Parameter(typeof(string), "v");
            Action<object, string> setter = Expression.Lambda<Action<object, string>>(
                Expression.Assign(member, valParam), objParam, valParam).Compile();
            return (getter, setter);
        });
    }

    private readonly struct BoundEventCall
    {
        public readonly Component Component;
        public readonly string MethodName;
        public readonly Action? VoidCall;
        public readonly Action<float>? FloatCall;
        public readonly Action<int>? IntCall;
        public readonly Action<bool>? BoolCall;
        public readonly Action<string>? StringCall;
        public readonly Action<Component, EngineObject?>? ObjectCall;

        public BoundEventCall(
            Component component,
            string methodName,
            Action? voidCall = null,
            Action<float>? floatCall = null,
            Action<int>? intCall = null,
            Action<bool>? boolCall = null,
            Action<string>? stringCall = null,
            Action<Component, EngineObject?>? objectCall = null)
        {
            Component = component;
            MethodName = methodName;
            VoidCall = voidCall;
            FloatCall = floatCall;
            IntCall = intCall;
            BoolCall = boolCall;
            StringCall = stringCall;
            ObjectCall = objectCall;
        }

        public void InvokeWithEventArg(AnimationClipEvent ev)
        {
            if (Component.IsNotValid() || !Component.EnabledInHierarchy) return;
            try
            {
                if (VoidCall != null) VoidCall();
                else if (FloatCall != null) FloatCall(ev.FloatParam);
                else if (IntCall != null) IntCall(ev.IntParam);
                else if (BoolCall != null) BoolCall(ev.BoolParam);
                else if (StringCall != null) StringCall(ev.StringParam);
                else if (ObjectCall != null) ObjectCall(Component, ev.ObjectParam);
            }
            catch (Exception ex)
            {
                Debug.LogException($"[Animator] Animation event '{MethodName}' on '{Component.GameObject.Name}/{Component.GetType().Name}' threw", ex);
            }
        }

        public void InvokeFloat(float value)
        {
            if (Component.IsNotValid() || !Component.EnabledInHierarchy) return;
            try
            {
                if (FloatCall != null) FloatCall(value);
                else VoidCall?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException($"[Animator] Animation event '{MethodName}' on '{Component.GameObject.Name}/{Component.GetType().Name}' threw", ex);
            }
        }
    }

    private readonly struct BoundEvent
    {
        private readonly GameObject? _targetGo;
        private readonly int _boundComponentCount;
        private readonly BoundEventCall[]? _enterCalls;
        private readonly BoundEventCall[]? _stayCalls;
        private readonly BoundEventCall[]? _exitCalls;

        private BoundEvent(GameObject? targetGo, int boundComponentCount, BoundEventCall[]? enter, BoundEventCall[]? stay, BoundEventCall[]? exit)
        {
            _targetGo = targetGo;
            _boundComponentCount = boundComponentCount;
            _enterCalls = enter;
            _stayCalls = stay;
            _exitCalls = exit;
        }

        public static BoundEvent Create(GameObject root, AnimationClipEvent ev)
        {
            GameObject? targetGo = ResolveGameObject(root, ev.RelativePath);
            if (targetGo.IsNotValid()) return default;

            int compCount = targetGo!._components.Count;
            BoundEventCall[]? enter = !string.IsNullOrEmpty(ev.FunctionName) ? BuildCalls(targetGo, ev.FunctionName, ev.ArgType) : null;
            BoundEventCall[]? stay = ev.IsRange && !string.IsNullOrEmpty(ev.StayFunctionName) ? BuildCalls(targetGo, ev.StayFunctionName, ProwlActionArgType.Float) : null;
            BoundEventCall[]? exit = ev.IsRange && !string.IsNullOrEmpty(ev.ExitFunctionName) ? BuildCalls(targetGo, ev.ExitFunctionName, ev.ArgType) : null;
            return new BoundEvent(targetGo, compCount, enter, stay, exit);
        }

        private static BoundEventCall[]? BuildCalls(GameObject targetGo, string methodName, ProwlActionArgType argType)
        {
            List<BoundEventCall>? list = null;
            for (int i = 0; i < targetGo._components.Count; i++)
            {
                Component comp = targetGo._components[i];
                if (comp.IsNotValid()) continue;

                (MethodInfo? method, int paramCount) = GetCachedEventMethod(comp.GetType(), methodName, argType);
                if (method == null) continue;

                BoundEventCall call;
                if (paramCount == 0)
                {
                    call = new BoundEventCall(comp, methodName, voidCall: method.CreateDelegate<Action>(comp));
                }
                else
                {
                    switch (argType)
                    {
                        case ProwlActionArgType.Float:
                            call = new BoundEventCall(comp, methodName, floatCall: method.CreateDelegate<Action<float>>(comp));
                            break;
                        case ProwlActionArgType.Int:
                            call = new BoundEventCall(comp, methodName, intCall: method.CreateDelegate<Action<int>>(comp));
                            break;
                        case ProwlActionArgType.Bool:
                            call = new BoundEventCall(comp, methodName, boolCall: method.CreateDelegate<Action<bool>>(comp));
                            break;
                        case ProwlActionArgType.String:
                            call = new BoundEventCall(comp, methodName, stringCall: method.CreateDelegate<Action<string>>(comp));
                            break;
                        case ProwlActionArgType.Object:
                        {
                            Action<Component, EngineObject?> objCaller = s_objectEventCallers.GetOrAdd(method, static m =>
                            {
                                Type compType = m.DeclaringType!;
                                Type paramType = m.GetParameters()[0].ParameterType;
                                ParameterExpression cParam = Expression.Parameter(typeof(Component), "c");
                                ParameterExpression oParam = Expression.Parameter(typeof(EngineObject), "o");
                                TypeBinaryExpression typeOk = Expression.TypeIs(oParam, paramType);
                                Expression convertedArg = Expression.Condition(
                                    typeOk,
                                    Expression.Convert(oParam, paramType),
                                    Expression.Constant(null, paramType));
                                MethodCallExpression invoke = Expression.Call(Expression.Convert(cParam, compType), m, convertedArg);
                                return Expression.Lambda<Action<Component, EngineObject?>>(invoke, cParam, oParam).Compile();
                            });
                            call = new BoundEventCall(comp, methodName, objectCall: objCaller);
                            break;
                        }
                        default:
                            continue;
                    }
                }

                list ??= [];
                list.Add(call);
            }
            return list?.ToArray();
        }

        private bool IsTargetUnchanged => _targetGo.IsValid() && _targetGo!._components.Count == _boundComponentCount;

        public bool TryInvokeEnter(AnimationClipEvent ev)
        {
            if (!IsTargetUnchanged) return false;
            if (_enterCalls != null)
                for (int i = 0; i < _enterCalls.Length; i++)
                    _enterCalls[i].InvokeWithEventArg(ev);
            return true;
        }

        public bool TryInvokeStay(float progress)
        {
            if (!IsTargetUnchanged) return false;
            if (_stayCalls != null)
                for (int i = 0; i < _stayCalls.Length; i++)
                    _stayCalls[i].InvokeFloat(progress);
            return true;
        }

        public bool TryInvokeExit(AnimationClipEvent ev)
        {
            if (!IsTargetUnchanged) return false;
            if (_exitCalls != null)
                for (int i = 0; i < _exitCalls.Length; i++)
                    _exitCalls[i].InvokeWithEventArg(ev);
            return true;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Property animation resolves fields and properties by name.")]
    internal static bool TryResolveAccessChain(object target, string propertyPath, out MemberAccessChain chain)
    {
        chain = default;
        if (target == null || string.IsNullOrEmpty(propertyPath)) return false;

        Type rootType = target.GetType();
        var cacheKey = (rootType, propertyPath);
        if (s_accessChainCache.TryGetValue(cacheKey, out MemberAccessChain cached))
        {
            chain = cached;
            return cached.IsValid;
        }

        string[] parts = propertyPath.Split('.');
        var steps = new List<MemberStep>(parts.Length);
        Type currentType = rootType;
        SubChannelKind subChannel = SubChannelKind.None;
        int consumedParts = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (i == parts.Length - 1 && IsCompositeChannelType(currentType) && TryParseSubChannel(part, out SubChannelKind parsedSub))
            {
                subChannel = parsedSub;
                break;
            }

            MemberStep step = FindMemberStep(currentType, part);
            if (!step.IsValid)
            {
                s_accessChainCache[cacheKey] = default;
                return false;
            }

            // If this is the final member and there is no sub-channel, it must be writable
            if (i == parts.Length - 1 && step.Property != null && !step.Property.CanWrite)
            {
                s_accessChainCache[cacheKey] = default;
                return false;
            }

            steps.Add(step);
            currentType = step.MemberType;
            consumedParts++;
        }

        if (steps.Count == 0 || (steps[^1].Property != null && !steps[^1].Property!.CanWrite))
        {
            s_accessChainCache[cacheKey] = default;
            return false;
        }

        string basePath = consumedParts == parts.Length ? propertyPath : string.Join('.', parts, 0, consumedParts);
        chain = new MemberAccessChain(basePath, [.. steps], subChannel);
        s_accessChainCache[cacheKey] = chain;
        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070:DynamicallyAccessedMembers",
        Justification = "Property animation resolves fields and properties by name.")]
    private static MemberStep FindMemberStep(Type type, string name)
    {
        // Prefer exact property, then exact field, then case-insensitive fallback
        PropertyInfo? prop = type.GetProperty(name, MemberFlags);
        if (prop != null && prop.GetIndexParameters().Length == 0) return new MemberStep(prop);

        FieldInfo? field = type.GetField(name, MemberFlags);
        if (field != null) return new MemberStep(field);

        // Case-insensitive or underscore-prefixed field fallback (e.g. "localPosition" -> "LocalPosition" or "_localPosition")
        foreach (PropertyInfo p in type.GetProperties(MemberFlags))
            if (p.GetIndexParameters().Length == 0 && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return new MemberStep(p);

        foreach (FieldInfo f in type.GetFields(MemberFlags))
            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || f.Name.Equals("_" + name, StringComparison.OrdinalIgnoreCase))
                return new MemberStep(f);

        return default;
    }

    private static bool IsCompositeChannelType(Type t)
        => t == typeof(Float2) || t == typeof(Float3) || t == typeof(Float4) ||
           t == typeof(Double2) || t == typeof(Double3) || t == typeof(Double4) ||
           t == typeof(Int2) || t == typeof(Int3) || t == typeof(Int4) ||
           t == typeof(Quaternion) || t == typeof(Color) || t == typeof(Color32);

    private static bool TryParseSubChannel(string part, out SubChannelKind channel)
    {
        channel = part switch
        {
            "X" or "x" => SubChannelKind.X,
            "Y" or "y" => SubChannelKind.Y,
            "Z" or "z" => SubChannelKind.Z,
            "W" or "w" => SubChannelKind.W,
            "R" or "r" => SubChannelKind.R,
            "G" or "g" => SubChannelKind.G,
            "B" or "b" => SubChannelKind.B,
            "A" or "a" => SubChannelKind.A,
            _ => SubChannelKind.None,
        };
        return channel != SubChannelKind.None;
    }

    private static bool TryFastReadTransform(Transform tf, string path, out float value)
    {
        switch (path)
        {
            case "LocalPosition.X": value = tf.LocalPosition.X; return true;
            case "LocalPosition.Y": value = tf.LocalPosition.Y; return true;
            case "LocalPosition.Z": value = tf.LocalPosition.Z; return true;
            case "LocalEulerAngles.X": value = tf.LocalEulerAngles.X; return true;
            case "LocalEulerAngles.Y": value = tf.LocalEulerAngles.Y; return true;
            case "LocalEulerAngles.Z": value = tf.LocalEulerAngles.Z; return true;
            case "LocalScale.X": value = tf.LocalScale.X; return true;
            case "LocalScale.Y": value = tf.LocalScale.Y; return true;
            case "LocalScale.Z": value = tf.LocalScale.Z; return true;
            case "LocalRotation.X": value = tf.LocalRotation.X; return true;
            case "LocalRotation.Y": value = tf.LocalRotation.Y; return true;
            case "LocalRotation.Z": value = tf.LocalRotation.Z; return true;
            case "LocalRotation.W": value = tf.LocalRotation.W; return true;
            default:
                value = 0f;
                return false;
        }
    }

    private static bool TryConvertToFloat(object? obj, SubChannelKind sub, out float value)
    {
        value = 0f;
        if (obj == null) return false;

        switch (obj)
        {
            case float f: value = f; return true;
            case double d: value = (float)d; return true;
            case int i: value = i; return true;
            case uint u: value = u; return true;
            case long l: value = l; return true;
            case short s: value = s; return true;
            case byte b: value = b; return true;
            case bool bo: value = bo ? 1f : 0f; return true;
            case Enum e: value = Convert.ToInt32(e); return true;

            case Float2 f2:
                value = sub switch { SubChannelKind.Y or SubChannelKind.G => f2.Y, _ => f2.X };
                return true;
            case Float3 f3:
                value = sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => f3.Y,
                    SubChannelKind.Z or SubChannelKind.B => f3.Z,
                    _ => f3.X,
                };
                return true;
            case Float4 f4:
                value = sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => f4.Y,
                    SubChannelKind.Z or SubChannelKind.B => f4.Z,
                    SubChannelKind.W or SubChannelKind.A => f4.W,
                    _ => f4.X,
                };
                return true;
            case Quaternion q:
                value = sub switch
                {
                    SubChannelKind.Y => q.Y,
                    SubChannelKind.Z => q.Z,
                    SubChannelKind.W => q.W,
                    _ => q.X,
                };
                return true;
            case Color c:
                value = sub switch
                {
                    SubChannelKind.G or SubChannelKind.Y => c.G,
                    SubChannelKind.B or SubChannelKind.Z => c.B,
                    SubChannelKind.A or SubChannelKind.W => c.A,
                    _ => c.R,
                };
                return true;
            case Color32 c32:
                value = sub switch
                {
                    SubChannelKind.G or SubChannelKind.Y => c32.G / 255f,
                    SubChannelKind.B or SubChannelKind.Z => c32.B / 255f,
                    SubChannelKind.A or SubChannelKind.W => c32.A / 255f,
                    _ => c32.R / 255f,
                };
                return true;
            case Double2 d2:
                value = (float)(sub switch { SubChannelKind.Y or SubChannelKind.G => d2.Y, _ => d2.X });
                return true;
            case Double3 d3:
                value = (float)(sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => d3.Y,
                    SubChannelKind.Z or SubChannelKind.B => d3.Z,
                    _ => d3.X,
                });
                return true;
            case Double4 d4:
                value = (float)(sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => d4.Y,
                    SubChannelKind.Z or SubChannelKind.B => d4.Z,
                    SubChannelKind.W or SubChannelKind.A => d4.W,
                    _ => d4.X,
                });
                return true;
            case Int2 i2:
                value = sub switch { SubChannelKind.Y or SubChannelKind.G => i2.Y, _ => i2.X };
                return true;
            case Int3 i3:
                value = sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => i3.Y,
                    SubChannelKind.Z or SubChannelKind.B => i3.Z,
                    _ => i3.X,
                };
                return true;
            case Int4 i4:
                value = sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => i4.Y,
                    SubChannelKind.Z or SubChannelKind.B => i4.Z,
                    SubChannelKind.W or SubChannelKind.A => i4.W,
                    _ => i4.X,
                };
                return true;
            default:
                return false;
        }
    }

    private static object? ApplySubChannel(object? current, Type leafType, SubChannelKind sub, float val)
    {
        if (leafType == typeof(float)) return val;
        if (leafType == typeof(double)) return (double)val;
        if (leafType == typeof(int)) return (int)MathF.Round(val);
        if (leafType == typeof(uint)) return (uint)MathF.Max(0f, MathF.Round(val));
        if (leafType == typeof(long)) return (long)MathF.Round(val);
        if (leafType == typeof(short)) return (short)MathF.Round(val);
        if (leafType == typeof(byte)) return (byte)Math.Clamp((int)MathF.Round(val), 0, 255);
        if (leafType == typeof(bool)) return val >= 0.5f;
        if (leafType.IsEnum) return Enum.ToObject(leafType, (int)MathF.Round(val));

        if (leafType == typeof(Float2))
        {
            Float2 v = current is Float2 cur ? cur : default;
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = val;
            else v.X = val;
            return v;
        }
        if (leafType == typeof(Float3))
        {
            Float3 v = current is Float3 cur ? cur : default;
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = val;
            else if (sub is SubChannelKind.Z or SubChannelKind.B) v.Z = val;
            else v.X = val;
            return v;
        }
        if (leafType == typeof(Float4))
        {
            Float4 v = current is Float4 cur ? cur : default;
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = val;
            else if (sub is SubChannelKind.Z or SubChannelKind.B) v.Z = val;
            else if (sub is SubChannelKind.W or SubChannelKind.A) v.W = val;
            else v.X = val;
            return v;
        }
        if (leafType == typeof(Quaternion))
        {
            Quaternion v = current is Quaternion cur ? cur : Quaternion.Identity;
            if (sub == SubChannelKind.Y) v.Y = val;
            else if (sub == SubChannelKind.Z) v.Z = val;
            else if (sub == SubChannelKind.W) v.W = val;
            else v.X = val;
            return Quaternion.NormalizeSafe(v);
        }
        if (leafType == typeof(Color))
        {
            Color v = current is Color cur ? cur : Color.White;
            if (sub is SubChannelKind.G or SubChannelKind.Y) v.G = val;
            else if (sub is SubChannelKind.B or SubChannelKind.Z) v.B = val;
            else if (sub is SubChannelKind.A or SubChannelKind.W) v.A = val;
            else v.R = val;
            return v;
        }
        if (leafType == typeof(Color32))
        {
            Color32 v = current is Color32 cur ? cur : new Color32(255, 255, 255, 255);
            byte bVal = (byte)Math.Clamp((int)MathF.Round(val * 255f), 0, 255);
            if (sub is SubChannelKind.G or SubChannelKind.Y) return Color32.FromArgb(v.A, v.R, bVal, v.B);
            if (sub is SubChannelKind.B or SubChannelKind.Z) return Color32.FromArgb(v.A, v.R, v.G, bVal);
            if (sub is SubChannelKind.A or SubChannelKind.W) return Color32.FromArgb(bVal, v.R, v.G, v.B);
            return Color32.FromArgb(v.A, bVal, v.G, v.B);
        }
        if (leafType == typeof(Double2))
        {
            Double2 v = current is Double2 cur ? cur : default;
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = val;
            else v.X = val;
            return v;
        }
        if (leafType == typeof(Double3))
        {
            Double3 v = current is Double3 cur ? cur : default;
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = val;
            else if (sub is SubChannelKind.Z or SubChannelKind.B) v.Z = val;
            else v.X = val;
            return v;
        }
        if (leafType == typeof(Double4))
        {
            Double4 v = current is Double4 cur ? cur : default;
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = val;
            else if (sub is SubChannelKind.Z or SubChannelKind.B) v.Z = val;
            else if (sub is SubChannelKind.W or SubChannelKind.A) v.W = val;
            else v.X = val;
            return v;
        }
        if (leafType == typeof(Int2))
        {
            Int2 v = current is Int2 cur ? cur : default;
            int iv = (int)MathF.Round(val);
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = iv;
            else v.X = iv;
            return v;
        }
        if (leafType == typeof(Int3))
        {
            Int3 v = current is Int3 cur ? cur : default;
            int iv = (int)MathF.Round(val);
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = iv;
            else if (sub is SubChannelKind.Z or SubChannelKind.B) v.Z = iv;
            else v.X = iv;
            return v;
        }
        if (leafType == typeof(Int4))
        {
            Int4 v = current is Int4 cur ? cur : default;
            int iv = (int)MathF.Round(val);
            if (sub is SubChannelKind.Y or SubChannelKind.G) v.Y = iv;
            else if (sub is SubChannelKind.Z or SubChannelKind.B) v.Z = iv;
            else if (sub is SubChannelKind.W or SubChannelKind.A) v.W = iv;
            else v.X = iv;
            return v;
        }

        return null;
    }

    internal static void NotifyTargetChanged(object target)
    {
        if (target is RectTransform rt)
        {
            rt.MarkLayoutDirty();
        }
        else if (target is UIBehaviour ui && ui.GameObject.IsValid())
        {
            GameCanvas? canvas = ui.GameObject.GetComponentInParent<GameCanvas>();
            if (canvas.IsValid()) canvas!.MarkDirty(UIDirtyFlags.Vertices);
        }
        else if (target is Rendering.IRenderSync renderSync && target is Component comp && comp.GameObject.IsValid() && comp.GameObject.Scene.IsValid())
        {
            comp.GameObject.Scene!.Rendering.Refresh(renderSync);
        }
    }

    private struct TargetDirtyNotifier
    {
        private readonly RectTransform? _rectTransform;
        private readonly UIBehaviour? _uiBehaviour;
        private readonly Rendering.IRenderSync? _renderSync;
        private readonly Component? _renderComp;
        private GameCanvas? _cachedCanvas;

        public TargetDirtyNotifier(object target)
        {
            if (target is RectTransform rt)
            {
                _rectTransform = rt;
            }
            else if (target is UIBehaviour ui)
            {
                _uiBehaviour = ui;
            }
            else if (target is Rendering.IRenderSync rs && target is Component c)
            {
                _renderSync = rs;
                _renderComp = c;
            }
        }

        public void Notify()
        {
            if (!ReferenceEquals(_rectTransform, null))
            {
                if (_rectTransform.IsValid())
                    _rectTransform.MarkLayoutDirty();
            }
            else if (!ReferenceEquals(_uiBehaviour, null))
            {
                if (_uiBehaviour.IsValid() && _uiBehaviour.GameObject.IsValid())
                {
                    if (_cachedCanvas.IsNotValid())
                        _cachedCanvas = _uiBehaviour.GameObject.GetComponentInParent<GameCanvas>();
                    if (_cachedCanvas.IsValid())
                        _cachedCanvas!.MarkDirty(UIDirtyFlags.Vertices);
                }
            }
            else if (_renderSync != null && !ReferenceEquals(_renderComp, null))
            {
                if (_renderComp.IsValid() && _renderComp.GameObject.IsValid() && _renderComp.GameObject.Scene.IsValid())
                    _renderComp.GameObject.Scene!.Rendering.Refresh(_renderSync);
            }
        }
    }

    // ================================================================
    //  Bound Track Runners
    // ================================================================

    private sealed class BoundPropertyGroup
    {
        private readonly object _target;
        private readonly MemberAccessChain _chain;
        private readonly List<(SubChannelKind Sub, AnimationCurveTrack Track)> _channels = [];
        private readonly Func<object, float>? _scalarGet;
        private readonly Action<object, float>? _scalarSet;
        private readonly Func<object, Float4>? _vec4Get;
        private readonly Action<object, Float4>? _vec4Set;
        private TargetDirtyNotifier _notifier;
        private object?[]? _scratchParents;

        private object? _baselineValue;
        private float _baselineScalar;
        private Float4 _baselineVec4;
        private Float3 _baselineFloat3;
        private Quaternion _baselineQuat = Quaternion.Identity;

        // Fast-path enum for Transform properties to avoid boxing on every frame
        private enum FastTransformKind : byte
        {
            None = 0,
            LocalPosition,
            LocalEulerAngles,
            LocalScale,
            LocalRotation,
        }

        private readonly FastTransformKind _fastTransform;

        public BoundPropertyGroup(object target, MemberAccessChain chain)
        {
            _target = target;
            _chain = chain;
            _notifier = new TargetDirtyNotifier(target);

            if (target is Transform)
            {
                _fastTransform = chain.BasePath switch
                {
                    "LocalPosition" => FastTransformKind.LocalPosition,
                    "LocalEulerAngles" => FastTransformKind.LocalEulerAngles,
                    "LocalScale" => FastTransformKind.LocalScale,
                    "LocalRotation" => FastTransformKind.LocalRotation,
                    _ => FastTransformKind.None,
                };
            }

            if (_fastTransform == FastTransformKind.None)
            {
                Type targetType = target.GetType();
                (_scalarGet, _scalarSet) = GetOrCompileScalarAccessor(targetType, chain);
                if (_scalarGet == null)
                    (_vec4Get, _vec4Set) = GetOrCompileVec4Accessor(targetType, chain);
            }

            CaptureBaseline();
        }

        public void CaptureBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            if (_target is Transform tf)
            {
                if (tf.GameObject.IsNotValid()) return;
                switch (_fastTransform)
                {
                    case FastTransformKind.LocalPosition: _baselineFloat3 = tf.LocalPosition; return;
                    case FastTransformKind.LocalEulerAngles: _baselineFloat3 = tf.LocalEulerAngles; return;
                    case FastTransformKind.LocalScale: _baselineFloat3 = tf.LocalScale; return;
                    case FastTransformKind.LocalRotation: _baselineQuat = tf.LocalRotation; return;
                }
            }
            if (_scalarGet != null) { _baselineScalar = _scalarGet(_target); return; }
            if (_vec4Get != null) { _baselineVec4 = _vec4Get(_target); return; }
            _baselineValue = _chain.GetLeafValue(_target);
        }

        public void RestoreBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            if (_target is Transform tf)
            {
                if (tf.GameObject.IsNotValid()) return;
                switch (_fastTransform)
                {
                    case FastTransformKind.LocalPosition: tf.LocalPosition = _baselineFloat3; return;
                    case FastTransformKind.LocalEulerAngles: tf.LocalEulerAngles = _baselineFloat3; return;
                    case FastTransformKind.LocalScale: tf.LocalScale = _baselineFloat3; return;
                    case FastTransformKind.LocalRotation: tf.LocalRotation = _baselineQuat; return;
                }
            }
            if (_scalarSet != null) { _scalarSet(_target, _baselineScalar); _notifier.Notify(); return; }
            if (_vec4Set != null) { _vec4Set(_target, _baselineVec4); _notifier.Notify(); return; }
            _chain.SetLeafValue(_target, _baselineValue, ref _scratchParents);
            _notifier.Notify();
        }

        public void AddChannel(SubChannelKind sub, AnimationCurveTrack track)
        {
            for (int i = 0; i < _channels.Count; i++)
            {
                if (_channels[i].Sub == sub)
                {
                    _channels[i] = (sub, track);
                    return;
                }
            }
            _channels.Add((sub, track));
        }

        private static bool IsTrackActive(AnimationCurveTrack track, bool hasSolo)
            => !track.Muted && (!hasSolo || track.Solo);

        public void Apply(float time, float weight = 1f, bool hasSolo = false)
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;

            bool anyActive = false;
            for (int i = 0; i < _channels.Count; i++)
            {
                if (IsTrackActive(_channels[i].Track, hasSolo))
                {
                    anyActive = true;
                    break;
                }
            }
            if (!anyActive) return;

            if (_target is Transform tf)
            {
                if (tf.GameObject.IsNotValid()) return;
                if (_fastTransform != FastTransformKind.None)
                {
                    ApplyFastTransform(tf, time, weight, hasSolo);
                    return;
                }
            }

            // Fast Path 1: Compiled scalar field/property (float, double, int, bool — 0 boxing)
            if (_scalarGet != null && _scalarSet != null)
            {
                AnimationCurveTrack track = _channels[0].Track;
                float sampled = track.Curve.Evaluate(time);
                if (track.BlendMode == AnimationTrackBlendMode.Additive)
                {
                    sampled = _baselineScalar + sampled * weight;
                }
                else if (weight < 0.9999f && (_chain.LeafType == typeof(float) || _chain.LeafType == typeof(double)))
                {
                    float cur = _scalarGet(_target);
                    sampled = cur + (sampled - cur) * weight;
                }
                _scalarSet(_target, sampled);
                _notifier.Notify();
                return;
            }

            // Fast Path 2: Compiled vector/color field/property (Float2, Float3, Float4, Color — 0 boxing)
            if (_vec4Get != null && _vec4Set != null)
            {
                Float4 v = _vec4Get(_target);
                for (int i = 0; i < _channels.Count; i++)
                {
                    (SubChannelKind sub, AnimationCurveTrack track) = _channels[i];
                    if (!IsTrackActive(track, hasSolo)) continue;
                    float s = track.Curve.Evaluate(time);
                    bool add = track.BlendMode == AnimationTrackBlendMode.Additive;
                    switch (sub)
                    {
                        case SubChannelKind.Y or SubChannelKind.G:
                            v.Y = add ? _baselineVec4.Y + s * weight : (weight >= 0.9999f ? s : v.Y + (s - v.Y) * weight);
                            break;
                        case SubChannelKind.Z or SubChannelKind.B:
                            v.Z = add ? _baselineVec4.Z + s * weight : (weight >= 0.9999f ? s : v.Z + (s - v.Z) * weight);
                            break;
                        case SubChannelKind.W or SubChannelKind.A:
                            v.W = add ? _baselineVec4.W + s * weight : (weight >= 0.9999f ? s : v.W + (s - v.W) * weight);
                            break;
                        default:
                            v.X = add ? _baselineVec4.X + s * weight : (weight >= 0.9999f ? s : v.X + (s - v.X) * weight);
                            break;
                    }
                }
                _vec4Set(_target, v);
                _notifier.Notify();
                return;
            }

            // Fallback: Nested value-type structs, enums, Color32, Double2/3/4, Int2/3/4, custom Quaternion fields
            object? val = _chain.GetLeafValue(_target);
            Type leafType = _chain.LeafType;

            for (int i = 0; i < _channels.Count; i++)
            {
                (SubChannelKind sub, AnimationCurveTrack track) = _channels[i];
                if (!IsTrackActive(track, hasSolo)) continue;

                float sampled = track.Curve.Evaluate(time);
                if (track.BlendMode == AnimationTrackBlendMode.Additive)
                {
                    TryConvertToFloat(_baselineValue, sub, out float baseF);
                    sampled = baseF + sampled * weight;
                }
                else if (weight < 0.9999f && TryConvertToFloat(val, sub, out float curF) && (leafType == typeof(float) || leafType == typeof(double) || IsCompositeChannelType(leafType)))
                {
                    sampled = curF + (sampled - curF) * weight;
                }
                val = ApplySubChannel(val, leafType, sub, sampled);
            }

            if (val != null)
            {
                _chain.SetLeafValue(_target, val, ref _scratchParents);
                _notifier.Notify();
            }
        }

        private void ApplyFastTransform(Transform tf, float time, float weight, bool hasSolo)
        {
            switch (_fastTransform)
            {
                case FastTransformKind.LocalPosition:
                {
                    Float3 v = tf.LocalPosition;
                    for (int i = 0; i < _channels.Count; i++)
                    {
                        (SubChannelKind sub, AnimationCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        float s = track.Curve.Evaluate(time);
                        bool additive = track.BlendMode == AnimationTrackBlendMode.Additive;
                        switch (sub)
                        {
                            case SubChannelKind.Y:
                                v.Y = additive ? _baselineFloat3.Y + s * weight : (weight >= 0.9999f ? s : v.Y + (s - v.Y) * weight);
                                break;
                            case SubChannelKind.Z:
                                v.Z = additive ? _baselineFloat3.Z + s * weight : (weight >= 0.9999f ? s : v.Z + (s - v.Z) * weight);
                                break;
                            default:
                                v.X = additive ? _baselineFloat3.X + s * weight : (weight >= 0.9999f ? s : v.X + (s - v.X) * weight);
                                break;
                        }
                    }
                    tf.LocalPosition = v;
                    break;
                }
                case FastTransformKind.LocalEulerAngles:
                {
                    Float3 v = tf.LocalEulerAngles;
                    for (int i = 0; i < _channels.Count; i++)
                    {
                        (SubChannelKind sub, AnimationCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        float s = track.Curve.Evaluate(time);
                        bool additive = track.BlendMode == AnimationTrackBlendMode.Additive;
                        switch (sub)
                        {
                            case SubChannelKind.Y:
                                v.Y = additive ? _baselineFloat3.Y + s * weight : (weight >= 0.9999f ? s : v.Y + (s - v.Y) * weight);
                                break;
                            case SubChannelKind.Z:
                                v.Z = additive ? _baselineFloat3.Z + s * weight : (weight >= 0.9999f ? s : v.Z + (s - v.Z) * weight);
                                break;
                            default:
                                v.X = additive ? _baselineFloat3.X + s * weight : (weight >= 0.9999f ? s : v.X + (s - v.X) * weight);
                                break;
                        }
                    }
                    tf.LocalEulerAngles = v;
                    break;
                }
                case FastTransformKind.LocalScale:
                {
                    Float3 v = tf.LocalScale;
                    for (int i = 0; i < _channels.Count; i++)
                    {
                        (SubChannelKind sub, AnimationCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        float s = track.Curve.Evaluate(time);
                        bool additive = track.BlendMode == AnimationTrackBlendMode.Additive;
                        switch (sub)
                        {
                            case SubChannelKind.Y:
                                v.Y = additive ? _baselineFloat3.Y + s * weight : (weight >= 0.9999f ? s : v.Y + (s - v.Y) * weight);
                                break;
                            case SubChannelKind.Z:
                                v.Z = additive ? _baselineFloat3.Z + s * weight : (weight >= 0.9999f ? s : v.Z + (s - v.Z) * weight);
                                break;
                            default:
                                v.X = additive ? _baselineFloat3.X + s * weight : (weight >= 0.9999f ? s : v.X + (s - v.X) * weight);
                                break;
                        }
                    }
                    tf.LocalScale = v;
                    break;
                }
                case FastTransformKind.LocalRotation:
                {
                    Quaternion curQ = tf.LocalRotation;
                    Quaternion q = curQ;
                    bool anyAdditive = false;
                    for (int i = 0; i < _channels.Count; i++)
                    {
                        (SubChannelKind sub, AnimationCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        if (track.BlendMode == AnimationTrackBlendMode.Additive) anyAdditive = true;
                        float s = track.Curve.Evaluate(time);
                        switch (sub)
                        {
                            case SubChannelKind.Y: q.Y = s; break;
                            case SubChannelKind.Z: q.Z = s; break;
                            case SubChannelKind.W: q.W = s; break;
                            default: q.X = s; break;
                        }
                    }
                    q = Quaternion.NormalizeSafe(q);
                    if (anyAdditive)
                        q = Quaternion.NormalizeSafe(_baselineQuat * q);
                    tf.LocalRotation = weight >= 0.9999f ? q : Quaternion.Slerp(curQ, q, weight);
                    break;
                }
            }
        }
    }

    private sealed class BoundObjectTrack
    {
        private readonly object _target;
        private readonly MemberAccessChain _chain;
        private readonly AnimationObjectTrack _track;
        private readonly Func<object, EngineObject?>? _fastGet;
        private readonly Action<object, EngineObject?>? _fastSet;
        private TargetDirtyNotifier _notifier;
        private object?[]? _scratchParents;
        private EngineObject? _baselineObj;
        private object? _baselineValue;

        public BoundObjectTrack(object target, MemberAccessChain chain, AnimationObjectTrack track)
        {
            _target = target;
            _chain = chain;
            _track = track;
            _notifier = new TargetDirtyNotifier(target);
            (_fastGet, _fastSet) = GetOrCompileObjectAccessor(target.GetType(), chain);
            CaptureBaseline();
        }

        public void CaptureBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            if (_fastGet != null) { _baselineObj = _fastGet(_target); return; }
            _baselineValue = _chain.GetLeafValue(_target);
        }

        public void RestoreBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            if (_fastSet != null) { _fastSet(_target, _baselineObj); _notifier.Notify(); return; }
            _chain.SetLeafValue(_target, _baselineValue, ref _scratchParents);
            _notifier.Notify();
        }

        public void Apply(float time, bool hasSolo = false)
        {
            if (_track.Muted || (hasSolo && !_track.Solo)) return;
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            EngineObject? sampled = _track.Evaluate(time);
            EngineObject? resolved = sampled.IsValid() ? sampled : null;
            if (!ReferenceEquals(resolved, null) && !_chain.LeafType.IsInstanceOfType(resolved))
                return;

            if (_fastSet != null)
                _fastSet(_target, resolved);
            else
                _chain.SetLeafValue(_target, resolved, ref _scratchParents);
            _notifier.Notify();
        }
    }

    private sealed class BoundStringTrack
    {
        private readonly object _target;
        private readonly MemberAccessChain _chain;
        private readonly AnimationStringTrack _track;
        private readonly Func<object, string?>? _fastGet;
        private readonly Action<object, string>? _fastSet;
        private TargetDirtyNotifier _notifier;
        private object?[]? _scratchParents;
        private string? _baselineString;
        private object? _baselineValue;

        public BoundStringTrack(object target, MemberAccessChain chain, AnimationStringTrack track)
        {
            _target = target;
            _chain = chain;
            _track = track;
            _notifier = new TargetDirtyNotifier(target);
            (_fastGet, _fastSet) = GetOrCompileStringAccessor(target.GetType(), chain);
            CaptureBaseline();
        }

        public void CaptureBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            if (_fastGet != null) { _baselineString = _fastGet(_target); return; }
            _baselineValue = _chain.GetLeafValue(_target);
        }

        public void RestoreBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            if (_fastSet != null) { _fastSet(_target, _baselineString ?? string.Empty); _notifier.Notify(); return; }
            _chain.SetLeafValue(_target, _baselineValue, ref _scratchParents);
            _notifier.Notify();
        }

        public void Apply(float time, bool hasSolo = false)
        {
            if (_track.Muted || (hasSolo && !_track.Solo)) return;
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            string sampled = _track.Evaluate(time);
            if (_fastSet != null)
                _fastSet(_target, sampled);
            else
                _chain.SetLeafValue(_target, sampled, ref _scratchParents);
            _notifier.Notify();
        }
    }

    // ================================================================
    //  Material Shader Uniform Tracks (Material.<Uniform>[.<Channel>])
    // ================================================================

    private static bool TryParseMaterialUniformPath(object target, string propertyPath, out Material? material, out string uniformName, out MaterialPropertyType propType, out SubChannelKind sub)
    {
        material = null;
        uniformName = string.Empty;
        propType = MaterialPropertyType.Float;
        sub = SubChannelKind.None;

        if (target is not IMaterialRenderer renderer || !propertyPath.StartsWith("Material.", StringComparison.OrdinalIgnoreCase))
            return false;
        if (renderer.MaterialSlotCount <= 0) return false;

        material = renderer.GetMaterial(0);
        if (material.IsNotValid()) return false;

        string rest = propertyPath["Material.".Length..];
        if (string.IsNullOrEmpty(rest)) return false;

        int dot = rest.LastIndexOf('.');
        if (dot > 0 && TryParseSubChannel(rest[(dot + 1)..], out SubChannelKind parsedSub))
        {
            uniformName = rest[..dot];
            sub = parsedSub;
        }
        else
        {
            uniformName = rest;
        }

        Shader? shader = material!.Shader;
        if (shader.IsValid())
        {
            if (shader!.State is not (AssetState.Missing or AssetState.Failed))
                shader.Load();
            if (shader.IsLoaded && shader.FindProperty(uniformName) is { } mp)
            {
                propType = mp.Type;
                return propType is MaterialPropertyType.Float or MaterialPropertyType.Int or MaterialPropertyType.Color
                    or MaterialPropertyType.Float2 or MaterialPropertyType.Float3 or MaterialPropertyType.Float4;
            }
        }

        // Allow unbound/custom shader uniform names by inferring type from sub-channel
        propType = sub switch
        {
            SubChannelKind.R or SubChannelKind.G or SubChannelKind.B or SubChannelKind.A => MaterialPropertyType.Color,
            SubChannelKind.X or SubChannelKind.Y or SubChannelKind.Z or SubChannelKind.W => MaterialPropertyType.Float4,
            _ => MaterialPropertyType.Float,
        };
        return true;
    }

    private static float ReadMaterialUniformChannel(Material mat, string uniformName, MaterialPropertyType propType, SubChannelKind sub)
    {
        switch (propType)
        {
            case MaterialPropertyType.Float: return mat.GetFloat(uniformName);
            case MaterialPropertyType.Int: return mat.GetInt(uniformName);
            case MaterialPropertyType.Color:
            {
                Color c = mat.GetColor(uniformName);
                return sub switch
                {
                    SubChannelKind.G or SubChannelKind.Y => c.G,
                    SubChannelKind.B or SubChannelKind.Z => c.B,
                    SubChannelKind.A or SubChannelKind.W => c.A,
                    _ => c.R,
                };
            }
            case MaterialPropertyType.Float2:
            case MaterialPropertyType.Float3:
            case MaterialPropertyType.Float4:
            {
                Float4 v = mat.GetVector(uniformName);
                return sub switch
                {
                    SubChannelKind.Y or SubChannelKind.G => v.Y,
                    SubChannelKind.Z or SubChannelKind.B => v.Z,
                    SubChannelKind.W or SubChannelKind.A => v.W,
                    _ => v.X,
                };
            }
            default: return 0f;
        }
    }

    private static void WriteMaterialUniformChannel(Material mat, string uniformName, MaterialPropertyType propType, SubChannelKind sub, float val)
    {
        switch (propType)
        {
            case MaterialPropertyType.Float:
                mat.SetFloat(uniformName, val);
                break;
            case MaterialPropertyType.Int:
                mat.SetInt(uniformName, (int)MathF.Round(val));
                break;
            case MaterialPropertyType.Color:
            {
                Color c = mat.GetColor(uniformName);
                switch (sub)
                {
                    case SubChannelKind.G or SubChannelKind.Y: c.G = val; break;
                    case SubChannelKind.B or SubChannelKind.Z: c.B = val; break;
                    case SubChannelKind.A or SubChannelKind.W: c.A = val; break;
                    default: c.R = val; break;
                }
                mat.SetColor(uniformName, c);
                break;
            }
            default:
            {
                Float4 v = mat.GetVector(uniformName);
                switch (sub)
                {
                    case SubChannelKind.Y or SubChannelKind.G: v.Y = val; break;
                    case SubChannelKind.Z or SubChannelKind.B: v.Z = val; break;
                    case SubChannelKind.W or SubChannelKind.A: v.W = val; break;
                    default: v.X = val; break;
                }
                if (propType == MaterialPropertyType.Float2) mat.SetVector(uniformName, new Float2(v.X, v.Y));
                else if (propType == MaterialPropertyType.Float3) mat.SetVector(uniformName, new Float3(v.X, v.Y, v.Z));
                else mat.SetVector(uniformName, v);
                break;
            }
        }
    }

    private sealed class BoundMaterialPropertyGroup
    {
        private readonly IMaterialRenderer _renderer;
        private readonly string _uniformName;
        private readonly MaterialPropertyType _propType;
        private readonly List<(SubChannelKind Sub, AnimationCurveTrack Track)> _channels = [];
        private TargetDirtyNotifier _notifier;
        private float _baseFloat;
        private int _baseInt;
        private Color _baseColor = Color.White;
        private Float4 _baseVec = Float4.Zero;

        public BoundMaterialPropertyGroup(IMaterialRenderer renderer, string uniformName, MaterialPropertyType propType)
        {
            _renderer = renderer;
            _uniformName = uniformName;
            _propType = propType;
            _notifier = new TargetDirtyNotifier(renderer);
            CaptureBaseline();
        }

        public void AddChannel(SubChannelKind sub, AnimationCurveTrack track)
        {
            for (int i = 0; i < _channels.Count; i++)
            {
                if (_channels[i].Sub == sub) { _channels[i] = (sub, track); return; }
            }
            _channels.Add((sub, track));
        }

        private Material? GetMaterial()
        {
            if (_renderer is EngineObject eo && eo.IsNotValid()) return null;
            if (_renderer.MaterialSlotCount <= 0) return null;
            Material? m = _renderer.GetMaterial(0);
            return m.IsValid() ? m : null;
        }

        // Whether the material itself set this uniform before animation, so restoring can revert to the
        // shader default / parent value instead of leaving a permanent override on the shared material asset.
        private bool _hadOverride;

        public void CaptureBaseline()
        {
            Material? mat = GetMaterial();
            if (mat.IsNotValid()) return;
            _hadOverride = mat!._overrides.Contains(_uniformName);
            switch (_propType)
            {
                case MaterialPropertyType.Float: _baseFloat = mat!.GetFloat(_uniformName); break;
                case MaterialPropertyType.Int: _baseInt = mat!.GetInt(_uniformName); break;
                case MaterialPropertyType.Color: _baseColor = mat!.GetColor(_uniformName); break;
                default: _baseVec = mat!.GetVector(_uniformName); break;
            }
        }

        public void RestoreBaseline()
        {
            Material? mat = GetMaterial();
            if (mat.IsNotValid()) return;
            if (!_hadOverride)
            {
                mat!.RevertProperty(_uniformName);
                _notifier.Notify();
                return;
            }
            switch (_propType)
            {
                case MaterialPropertyType.Float: mat!.SetFloat(_uniformName, _baseFloat); break;
                case MaterialPropertyType.Int: mat!.SetInt(_uniformName, _baseInt); break;
                case MaterialPropertyType.Color: mat!.SetColor(_uniformName, _baseColor); break;
                case MaterialPropertyType.Float2: mat!.SetVector(_uniformName, new Float2(_baseVec.X, _baseVec.Y)); break;
                case MaterialPropertyType.Float3: mat!.SetVector(_uniformName, new Float3(_baseVec.X, _baseVec.Y, _baseVec.Z)); break;
                default: mat!.SetVector(_uniformName, _baseVec); break;
            }
            _notifier.Notify();
        }

        public void Apply(float time, float weight, bool hasSolo)
        {
            Material? mat = GetMaterial();
            if (mat.IsNotValid()) return;

            bool anyActive = false;
            for (int i = 0; i < _channels.Count; i++)
                if (!_channels[i].Track.Muted && (!hasSolo || _channels[i].Track.Solo)) { anyActive = true; break; }
            if (!anyActive) return;

            switch (_propType)
            {
                case MaterialPropertyType.Float:
                {
                    AnimationCurveTrack t = _channels[0].Track;
                    float s = t.Curve.Evaluate(time);
                    float cur = mat!.GetFloat(_uniformName);
                    float val = t.BlendMode == AnimationTrackBlendMode.Additive ? _baseFloat + s * weight : (weight >= 0.9999f ? s : cur + (s - cur) * weight);
                    mat.SetFloat(_uniformName, val);
                    break;
                }
                case MaterialPropertyType.Int:
                {
                    AnimationCurveTrack t = _channels[0].Track;
                    float s = t.Curve.Evaluate(time);
                    int val = t.BlendMode == AnimationTrackBlendMode.Additive ? _baseInt + (int)MathF.Round(s * weight) : (int)MathF.Round(s);
                    mat!.SetInt(_uniformName, val);
                    break;
                }
                case MaterialPropertyType.Color:
                {
                    Color c = mat!.GetColor(_uniformName);
                    for (int i = 0; i < _channels.Count; i++)
                    {
                        (SubChannelKind sub, AnimationCurveTrack t) = _channels[i];
                        if (t.Muted || (hasSolo && !t.Solo)) continue;
                        float s = t.Curve.Evaluate(time);
                        bool add = t.BlendMode == AnimationTrackBlendMode.Additive;
                        switch (sub)
                        {
                            case SubChannelKind.G or SubChannelKind.Y: c.G = add ? _baseColor.G + s * weight : (weight >= 0.9999f ? s : c.G + (s - c.G) * weight); break;
                            case SubChannelKind.B or SubChannelKind.Z: c.B = add ? _baseColor.B + s * weight : (weight >= 0.9999f ? s : c.B + (s - c.B) * weight); break;
                            case SubChannelKind.A or SubChannelKind.W: c.A = add ? _baseColor.A + s * weight : (weight >= 0.9999f ? s : c.A + (s - c.A) * weight); break;
                            default: c.R = add ? _baseColor.R + s * weight : (weight >= 0.9999f ? s : c.R + (s - c.R) * weight); break;
                        }
                    }
                    mat.SetColor(_uniformName, c);
                    break;
                }
                default:
                {
                    Float4 v = mat!.GetVector(_uniformName);
                    for (int i = 0; i < _channels.Count; i++)
                    {
                        (SubChannelKind sub, AnimationCurveTrack t) = _channels[i];
                        if (t.Muted || (hasSolo && !t.Solo)) continue;
                        float s = t.Curve.Evaluate(time);
                        bool add = t.BlendMode == AnimationTrackBlendMode.Additive;
                        switch (sub)
                        {
                            case SubChannelKind.Y or SubChannelKind.G: v.Y = add ? _baseVec.Y + s * weight : (weight >= 0.9999f ? s : v.Y + (s - v.Y) * weight); break;
                            case SubChannelKind.Z or SubChannelKind.B: v.Z = add ? _baseVec.Z + s * weight : (weight >= 0.9999f ? s : v.Z + (s - v.Z) * weight); break;
                            case SubChannelKind.W or SubChannelKind.A: v.W = add ? _baseVec.W + s * weight : (weight >= 0.9999f ? s : v.W + (s - v.W) * weight); break;
                            default: v.X = add ? _baseVec.X + s * weight : (weight >= 0.9999f ? s : v.X + (s - v.X) * weight); break;
                        }
                    }
                    if (_propType == MaterialPropertyType.Float2) mat.SetVector(_uniformName, new Float2(v.X, v.Y));
                    else if (_propType == MaterialPropertyType.Float3) mat.SetVector(_uniformName, new Float3(v.X, v.Y, v.Z));
                    else mat.SetVector(_uniformName, v);
                    break;
                }
            }

            _notifier.Notify();
        }
    }
}
