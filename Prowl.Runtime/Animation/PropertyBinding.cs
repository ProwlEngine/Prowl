// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Prowl.Echo;
using Prowl.Runtime.UI;
using Prowl.Vector;

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

/// <summary>
/// Binds an <see cref="AnimationClip"/>'s property tracks to a live <see cref="GameObject"/> hierarchy
/// and evaluates or records properties at any time.
/// Groups sub-channel tracks (such as <c>LocalPosition.X</c>, <c>.Y</c>, <c>.Z</c> or <c>Color.R</c>, <c>.G</c>, <c>.B</c>, <c>.A</c>)
/// into a single property read-modify-write per frame so setters and dirty notifications run cleanly.
/// </summary>
public sealed class SceneClipBinding
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly GameObject _root;
    private readonly AnimationClip _clip;
    private readonly List<BoundPropertyGroup> _curveGroups = [];
    private readonly List<BoundObjectTrack> _objectTracks = [];
    private readonly List<BoundStringTrack> _stringTracks = [];
    private readonly HashSet<int> _activeRangedEvents = [];

    private static readonly ConcurrentDictionary<(Type Type, string Name, ProwlActionArgType ArgType), (MethodInfo? Method, int ParamCount)> s_eventMethodCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Path), MemberAccessChain> s_accessChainCache = new();

    public GameObject Root => _root;
    public AnimationClip Clip => _clip;

    public int BoundCurveGroupCount => _curveGroups.Count;
    public int BoundObjectTrackCount => _objectTracks.Count;
    public int BoundStringTrackCount => _stringTracks.Count;
    public bool HasActiveRangedEvents => _activeRangedEvents.Count > 0;

    public SceneClipBinding(GameObject root, AnimationClip clip)
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

        if (w >= 0.5f)
        {
            for (int i = 0; i < _objectTracks.Count; i++)
                _objectTracks[i].Apply(time, hasSolo);

            for (int i = 0; i < _stringTracks.Count; i++)
                _stringTracks[i].Apply(time, hasSolo);
        }
    }

    /// <summary>Captures the current property values on the target hierarchy as the pre-animated baseline for Additive tracks and <see cref="SceneClipCompletionMode.RestoreState"/>.</summary>
    public void CaptureBaseline()
    {
        if (_root.IsNotValid()) return;
        for (int i = 0; i < _curveGroups.Count; i++)
            _curveGroups[i].CaptureBaseline();
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
        for (int i = 0; i < _objectTracks.Count; i++)
            _objectTracks[i].RestoreBaseline();
        for (int i = 0; i < _stringTracks.Count; i++)
            _stringTracks[i].RestoreBaseline();
    }

    /// <summary>
    /// Fires any instant <see cref="SceneAnimationEvent"/> whose timestamp falls between <paramref name="previousTime"/> and <paramref name="currentTime"/>,
    /// and evaluates Enter, Stay (passing normalized 0..1 progress), and Exit callbacks for ranged events (<see cref="SceneAnimationEvent.IsRange"/>).
    /// Handles loop wrap-around when <paramref name="looped"/> is true.
    /// </summary>
    public void FireEvents(float previousTime, float currentTime, bool forward, bool looped)
    {
        if (_root.IsNotValid() || _clip.PropertyEvents.Count == 0) return;

        float duration = _clip.Duration;
        for (int i = 0; i < _clip.PropertyEvents.Count; i++)
        {
            SceneAnimationEvent ev = _clip.PropertyEvents[i];
            if (!ev.IsRange)
            {
                if (ShouldFire(ev.Time, previousTime, currentTime, duration, forward, looped))
                    InvokeEvent(_root, ev);
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
                SceneAnimationEvent ev = _clip.PropertyEvents[idx];
                if (ev.IsRange && !string.IsNullOrEmpty(ev.ExitFunctionName))
                    InvokeNamedEventMethod(_root, ev.RelativePath, ev.ExitFunctionName, ev.ArgType, GetEventArg(ev));
            }
        }
    }

    private void EvaluateRangedEvent(int index, SceneAnimationEvent ev, float prev, float curr, float duration, bool forward, bool looped)
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
                InvokeNamedEventMethod(_root, ev.RelativePath, ev.ExitFunctionName, ev.ArgType, GetEventArg(ev));
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
                    InvokeEvent(_root, ev);
                if (!string.IsNullOrEmpty(ev.StayFunctionName))
                {
                    float progress = Math.Clamp((curr - start) / span, 0f, 1f);
                    InvokeNamedEventMethod(_root, ev.RelativePath, ev.StayFunctionName, ProwlActionArgType.Float, progress);
                }
            }
            else if (crossedWholeRange)
            {
                // A single frame step jumped completely across [start, end]: fire Enter, Stay(1), and Exit in sequence
                if (!string.IsNullOrEmpty(ev.FunctionName))
                    InvokeEvent(_root, ev);
                if (!string.IsNullOrEmpty(ev.StayFunctionName))
                    InvokeNamedEventMethod(_root, ev.RelativePath, ev.StayFunctionName, ProwlActionArgType.Float, forward ? 1f : 0f);
                if (!string.IsNullOrEmpty(ev.ExitFunctionName))
                    InvokeNamedEventMethod(_root, ev.RelativePath, ev.ExitFunctionName, ev.ArgType, GetEventArg(ev));
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
                    InvokeNamedEventMethod(_root, ev.RelativePath, ev.StayFunctionName, ProwlActionArgType.Float, progress);
                }
            }
            else
            {
                // Left the range this step: fire Exit
                _activeRangedEvents.Remove(index);
                if (!string.IsNullOrEmpty(ev.ExitFunctionName))
                    InvokeNamedEventMethod(_root, ev.RelativePath, ev.ExitFunctionName, ev.ArgType, GetEventArg(ev));
            }
        }
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

    private static object? GetEventArg(SceneAnimationEvent ev) => ev.ArgType switch
    {
        ProwlActionArgType.Bool => ev.BoolParam,
        ProwlActionArgType.Int => ev.IntParam,
        ProwlActionArgType.Float => ev.FloatParam,
        ProwlActionArgType.String => ev.StringParam,
        ProwlActionArgType.Object => ev.ObjectParam,
        _ => null,
    };

    public static void InvokeEvent(GameObject root, SceneAnimationEvent ev)
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

    // ================================================================
    //  Target & Member Resolution
    // ================================================================

    /// <summary>Resolves a relative hierarchy path from <paramref name="root"/> to a child <see cref="GameObject"/>.</summary>
    public static GameObject? ResolveGameObject(GameObject root, string relativePath)
    {
        if (root.IsNotValid()) return null;
        if (string.IsNullOrEmpty(relativePath)) return root;

        Transform? found = root.Transform.Find(relativePath);
        return found != null && found.GameObject.IsValid() ? found.GameObject : null;
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

        for (int i = 0; i < _clip.CurveTracks.Count; i++)
        {
            SceneCurveTrack track = _clip.CurveTracks[i];
            if (track.Curve.Count == 0) continue;

            GameObject? go = ResolveGameObject(_root, track.RelativePath);
            if (go.IsNotValid()) continue;

            object? target = ResolveTargetObject(go!, track.ComponentType);
            if (target == null) continue;

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
            SceneObjectTrack track = _clip.ObjectTracks[i];
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
            SceneStringTrack track = _clip.StringTracks[i];
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

        public void SetLeafValue(object rootTarget, object? newLeafValue)
        {
            if (Steps.Length == 1)
            {
                Steps[0].Set(rootTarget, newLeafValue);
                return;
            }

            // To support nested value types (structs), we box-walk down, mutate, and write back up the chain.
            var parents = new object?[Steps.Length];
            parents[0] = rootTarget;
            for (int i = 0; i < Steps.Length - 1; i++)
            {
                parents[i + 1] = Steps[i].Get(parents[i]!);
                if (parents[i + 1] == null) return;
            }

            Steps[^1].Set(parents[^1]!, newLeafValue);

            for (int i = Steps.Length - 2; i >= 0; i--)
            {
                if (Steps[i].MemberType.IsValueType)
                    Steps[i].Set(parents[i]!, parents[i + 1]);
            }
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

    // ================================================================
    //  Bound Track Runners
    // ================================================================

    private sealed class BoundPropertyGroup
    {
        private readonly object _target;
        private readonly MemberAccessChain _chain;
        private readonly List<(SubChannelKind Sub, SceneCurveTrack Track)> _channels = [];
        private object? _baselineValue;
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
            _chain.SetLeafValue(_target, _baselineValue);
            NotifyTargetChanged(_target);
        }

        public void AddChannel(SubChannelKind sub, SceneCurveTrack track)
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

        private static bool IsTrackActive(SceneCurveTrack track, bool hasSolo)
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

            object? val = _chain.GetLeafValue(_target);
            Type leafType = _chain.LeafType;

            for (int i = 0; i < _channels.Count; i++)
            {
                (SubChannelKind sub, SceneCurveTrack track) = _channels[i];
                if (!IsTrackActive(track, hasSolo)) continue;

                float sampled = track.Curve.Evaluate(time);
                if (track.BlendMode == SceneTrackBlendMode.Additive)
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
                _chain.SetLeafValue(_target, val);
                NotifyTargetChanged(_target);
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
                        (SubChannelKind sub, SceneCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        float s = track.Curve.Evaluate(time);
                        bool additive = track.BlendMode == SceneTrackBlendMode.Additive;
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
                        (SubChannelKind sub, SceneCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        float s = track.Curve.Evaluate(time);
                        bool additive = track.BlendMode == SceneTrackBlendMode.Additive;
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
                        (SubChannelKind sub, SceneCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        float s = track.Curve.Evaluate(time);
                        bool additive = track.BlendMode == SceneTrackBlendMode.Additive;
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
                        (SubChannelKind sub, SceneCurveTrack track) = _channels[i];
                        if (!IsTrackActive(track, hasSolo)) continue;
                        if (track.BlendMode == SceneTrackBlendMode.Additive) anyAdditive = true;
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
        private readonly SceneObjectTrack _track;
        private object? _baselineValue;

        public BoundObjectTrack(object target, MemberAccessChain chain, SceneObjectTrack track)
        {
            _target = target;
            _chain = chain;
            _track = track;
            CaptureBaseline();
        }

        public void CaptureBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            _baselineValue = _chain.GetLeafValue(_target);
        }

        public void RestoreBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            _chain.SetLeafValue(_target, _baselineValue);
            NotifyTargetChanged(_target);
        }

        public void Apply(float time, bool hasSolo = false)
        {
            if (_track.Muted || (hasSolo && !_track.Solo)) return;
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            EngineObject? sampled = _track.Evaluate(time);
            if (sampled.IsValid() && !_chain.LeafType.IsInstanceOfType(sampled))
                return;

            _chain.SetLeafValue(_target, sampled.IsValid() ? sampled : null);
            NotifyTargetChanged(_target);
        }
    }

    private sealed class BoundStringTrack
    {
        private readonly object _target;
        private readonly MemberAccessChain _chain;
        private readonly SceneStringTrack _track;
        private object? _baselineValue;

        public BoundStringTrack(object target, MemberAccessChain chain, SceneStringTrack track)
        {
            _target = target;
            _chain = chain;
            _track = track;
            CaptureBaseline();
        }

        public void CaptureBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            _baselineValue = _chain.GetLeafValue(_target);
        }

        public void RestoreBaseline()
        {
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            _chain.SetLeafValue(_target, _baselineValue);
            NotifyTargetChanged(_target);
        }

        public void Apply(float time, bool hasSolo = false)
        {
            if (_track.Muted || (hasSolo && !_track.Solo)) return;
            if (_target is EngineObject eo && eo.IsNotValid()) return;
            string sampled = _track.Evaluate(time);
            _chain.SetLeafValue(_target, sampled);
            NotifyTargetChanged(_target);
        }
    }
}
