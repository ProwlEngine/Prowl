// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Echo.Cloning;

/// <summary>
/// Copies object graphs in memory.
/// <para/>
/// A reference to something inside the copy is rewritten to that thing's counterpart, and a reference
/// to anything outside it is shared with the original. The two are told apart by a map built in a
/// first pass that completes before any value is written, so the result does not depend on the order
/// the graph is walked in.
/// </summary>
[RequiresUnreferencedCode("Cloning reflects over every instance field and cannot be statically analyzed.")]
public static class Cloner
{
    /// <summary>Creates a copy of an object graph.</summary>
    public static T Clone<T>(T source, CloneContext? context = null)
    {
        if (source is null) return source;
        var provider = new CloneProvider(context ?? new CloneContext());
        return (T)provider.CloneRoot(source)!;
    }

    /// <summary>
    /// Copies an object graph onto an existing one, reusing the target's objects wherever they
    /// correspond so that existing references to them stay valid.
    /// </summary>
    public static void CopyTo<T>(T source, T target, CloneContext? context = null)
    {
        if (source is null || target is null) return;
        var provider = new CloneProvider(context ?? new CloneContext());
        provider.CopyRoot(source, target);
    }

    /// <summary>
    /// Clones several roots as one operation, so a reference from one root to another lands on that
    /// root's copy rather than on the original.
    /// </summary>
    public static List<T> CloneAll<T>(IEnumerable<T> sources, CloneContext? context = null)
    {
        var provider = new CloneProvider(context ?? new CloneContext());
        return provider.CloneRoots(sources);
    }
}

[RequiresUnreferencedCode("Cloning reflects over every instance field and cannot be statically analyzed.")]
public static class CloneExtensions
{
    public static T DeepClone<T>(this T source, CloneContext? context = null) => Cloner.Clone(source, context);

    public static void DeepCopyTo<T>(this T source, T target, CloneContext? context = null) => Cloner.CopyTo(source, target, context);
}

[RequiresUnreferencedCode("Cloning reflects over every instance field and cannot be statically analyzed.")]
internal sealed class CloneProvider : ICloneSetup, ICloneOperation
{
    private sealed class LocalBehavior
    {
        public Type? TargetType;
        public CloneBehavior Behavior;
        public bool Locked;
    }

    private readonly struct LateSetupEntry(object? source, object? target)
    {
        public readonly object? Source = source;
        public readonly object? Target = target;
    }

    private readonly CloneContext _context;
    private readonly HashSet<object> _handled = new(ReferenceEqualityComparer.Instance);
    private readonly List<LateSetupEntry> _lateSetup = [];
    private readonly List<LocalBehavior> _localBehavior = [];

    private object? _currentObject;
    private CloneTypeInfo? _currentType;

    public CloneContext Context => _context;

    public CloneProvider(CloneContext context) => _context = context;

    public object? CloneRoot(object source)
    {
        RejectClonedSource(source);
        Setup(source, null, null, CloneBehavior.ChildObject);
        RunLateSetup();

        object? target = GetTargetOf(source);
        Copy(source, target);
        return target;
    }

    public void CopyRoot(object source, object target)
    {
        RejectClonedSource(source);
        Setup(source, target, null, CloneBehavior.ChildObject);
        RunLateSetup();

        object? resolved = GetTargetOf(source);
        if (!ReferenceEquals(resolved, target))
            throw new ArgumentException(
                $"Echo: cannot copy a {source.GetType()} onto a {target.GetType()}.", nameof(target));

        Copy(source, resolved);
    }

    public List<T> CloneRoots<T>(IEnumerable<T> sources)
    {
        var roots = new List<T>();
        foreach (T source in sources)
            if (source is not null)
            {
                RejectClonedSource(source);
                roots.Add(source);
            }

        foreach (T source in roots)
            Setup(source, null, null, CloneBehavior.ChildObject);

        RunLateSetup();

        var results = new List<T>(roots.Count);
        foreach (T source in roots)
        {
            object? target = GetTargetOf(source);
            Copy(source, target);
            results.Add((T)target!);
        }

        return results;
    }

    private void RejectClonedSource(object source)
    {
        if (_context.TargetSet.Contains(source))
            throw new InvalidOperationException(
                "Echo: cannot clone an object that is already the target of this context's map. " +
                "Use a fresh CloneContext, or call ClearTargets first.");
    }

    #region Setup pass

    private void Setup(object? source, object? target, CloneTypeInfo? typeInfo, CloneBehavior behavior = CloneBehavior.Default)
    {
        if (source is null)
        {
            if (target is null) return;
            typeInfo ??= CloneTypeInfo.Get(target.GetType());
            if (!typeInfo.RequiresMerge) return;
        }

        typeInfo ??= CloneTypeInfo.Get(source!.GetType());
        if (typeInfo.CopyByAssignment) return;
        if (typeInfo.Type.IsValueType && !typeInfo.InvestigateOwnership) return;

        object? behaviorLock = null;
        if (!typeInfo.Type.IsValueType && source is not null)
        {
            // Already mapped, which is also what stops cycles.
            if (_context.Targets.ContainsKey(source)) return;

            if (behavior == CloneBehavior.Default)
                behavior = ResolveBehavior(typeInfo, out behaviorLock);

            // Not ours. Leaving it unmapped is what makes the copy share it.
            if (behavior != CloneBehavior.ChildObject)
            {
                Unlock(behaviorLock);
                return;
            }

            if (target != null && target.GetType() != typeInfo.Type)
                target = null;
        }

        object? lastObject = _currentObject;
        CloneTypeInfo? lastType = _currentType;
        _currentObject = source;
        _currentType = typeInfo;

        if (typeInfo.Type.IsValueType)
        {
            target ??= CreateInstance(typeInfo.Type);
            SetupChildren(source!, target, typeInfo);
        }
        else if (typeInfo.Format is ICloneLateFormat)
        {
            _lateSetup.Add(new LateSetupEntry(source, target));
        }
        else
        {
            Array? replacedTargetArray = null;

            if (typeInfo.IsArray)
            {
                // An array's length is fixed, so the target is always a new one sized to the source.
                replacedTargetArray = target as Array;
                target = Array.CreateInstance(typeInfo.ElementType!.Type, ((Array)source!).Length);
            }
            else if (typeInfo.Format != null)
            {
                target = typeInfo.Format.CreateCloneTarget(source!, target);
            }
            else
            {
                target ??= CreateInstance(typeInfo.Type);
            }

            _context.SetTarget(source!, target);

            // The array being replaced still holds the reuse candidates for the new one's elements.
            if (replacedTargetArray != null) target = replacedTargetArray;

            if (typeInfo.Format != null)
                typeInfo.Format.SetupCloneTargets(source!, target, this);
            else if (source is ICloneExplicit explicitSource)
                explicitSource.SetupCloneTargets(target, this);
            else
                SetupChildren(source!, target, typeInfo);
        }

        _currentObject = lastObject;
        _currentType = lastType;
        Unlock(behaviorLock);
    }

    private void SetupChildren(object source, object? target, CloneTypeInfo typeInfo)
    {
        if (!typeInfo.InvestigateOwnership) return;

        if (typeInfo.IsArray)
        {
            var sourceArray = (Array)source;
            var targetArray = target as Array;

            for (int i = 0; i < sourceArray.Length; i++)
            {
                object? targetElement = targetArray != null && targetArray.Length > i ? targetArray.GetValue(i) : null;
                Setup(sourceArray.GetValue(i), targetElement, null);
            }
            return;
        }

        foreach (CloneFieldInfo field in typeInfo.Fields)
        {
            object? sourceValue = field.Field.GetValue(source);
            object? targetValue = target != null ? field.Field.GetValue(target) : null;

            if (field.BehaviorTarget != null)
            {
                _localBehavior.Add(new LocalBehavior { TargetType = field.BehaviorTarget, Behavior = field.Behavior });
                Setup(sourceValue, targetValue, null);
                _localBehavior.RemoveAt(_localBehavior.Count - 1);
            }
            else
            {
                Setup(sourceValue, targetValue, null, field.Behavior);
            }
        }
    }

    private CloneBehavior ResolveBehavior(CloneTypeInfo typeInfo, out object? acquiredLock)
    {
        acquiredLock = null;

        for (int i = _localBehavior.Count - 1; i >= 0; i--)
        {
            LocalBehavior local = _localBehavior[i];
            if (local.Locked) continue;
            if (local.TargetType != null && !local.TargetType.IsAssignableFrom(typeInfo.Type)) continue;

            acquiredLock = local;
            local.Locked = true;
            return local.Behavior != CloneBehavior.Default ? local.Behavior : typeInfo.Behavior;
        }

        return typeInfo.Behavior;
    }

    private void Unlock(object? behaviorLock)
    {
        if (behaviorLock is LocalBehavior local)
            local.Locked = false;
    }

    private void RunLateSetup()
    {
        if (_lateSetup.Count == 0) return;

        foreach (LateSetupEntry entry in _lateSetup)
        {
            object? subject = entry.Source ?? entry.Target;
            if (subject == null) continue;

            if (CloneTypeInfo.Get(subject.GetType()).Format is not ICloneLateFormat late) continue;

            object? target = late.CreateTargetLate(entry.Source, entry.Target, this);
            _context.SetTarget(subject, target);
        }

        _lateSetup.Clear();
    }

    #endregion

    #region Copy pass

    private void Copy(object? source, object? target, CloneTypeInfo? typeInfo = null)
    {
        // Same instance means it was never ours to copy, which is how a shared reference stays shared.
        if (ReferenceEquals(source, target)) return;

        if (source is null)
        {
            if (target is null) return;
            typeInfo ??= CloneTypeInfo.Get(target.GetType());
            if (!typeInfo.RequiresMerge) return;
        }

        typeInfo ??= CloneTypeInfo.Get(source!.GetType());
        if (typeInfo.CopyByAssignment) return;
        if (target is null) return;
        if (!Push(source, typeInfo)) return;

        object? lastObject = _currentObject;
        CloneTypeInfo? lastType = _currentType;
        _currentObject = source;
        _currentType = typeInfo;

        if (source is ICloneCallbackReceiver beforeSource)
            beforeSource.OnBeforeClone(_context);

        if (typeInfo.Format != null)
            typeInfo.Format.CopyCloneTo(source!, target, this);
        else if (source is ICloneExplicit explicitSource)
            explicitSource.CopyCloneTo(target, this);
        else
            CopyChildren(source!, target, typeInfo);

        if (target is ICloneCallbackReceiver afterTarget)
            afterTarget.OnAfterClone(_context);

        _currentObject = lastObject;
        _currentType = lastType;
    }

    private void CopyChildren(object source, object target, CloneTypeInfo typeInfo)
    {
        if (typeInfo.IsArray)
        {
            CopyArray((Array)source, (Array)target, typeInfo.ElementType!);
            return;
        }

        foreach (CloneFieldInfo field in typeInfo.Fields)
        {
            if (_context.PreserveIdentity && (field.Flags & CloneFieldFlags.IdentityRelevant) != 0)
                continue;

            CopyField(source, target, field);
        }
    }

    private void CopyArray(Array source, Array target, CloneTypeInfo elementType)
    {
        int length = Math.Min(source.Length, target.Length);

        if (elementType.CopyByAssignment)
        {
            Array.Copy(source, target, length);
            return;
        }

        if (elementType.Type.IsValueType)
        {
            for (int i = 0; i < length; i++)
            {
                object? targetElement = target.GetValue(i);
                Copy(source.GetValue(i), targetElement, elementType);
                target.SetValue(targetElement, i);
            }
            return;
        }

        for (int i = 0; i < length; i++)
        {
            object? sourceElement = source.GetValue(i);
            object? targetElement = GetTargetOf(sourceElement);
            Copy(sourceElement, targetElement, null);
            target.SetValue(targetElement, i);
        }
    }

    private void CopyField(object source, object target, in CloneFieldInfo field)
    {
        FieldInfo info = field.Field;
        CloneTypeInfo fieldType = CloneTypeInfo.Get(info.FieldType);

        if (fieldType.CopyByAssignment)
        {
            info.SetValue(target, info.GetValue(source));
            return;
        }

        if (info.FieldType.IsValueType)
        {
            object? targetValue = info.GetValue(target);
            Copy(info.GetValue(source), targetValue, fieldType);
            info.SetValue(target, targetValue);
            return;
        }

        object? sourceValue = info.GetValue(source);
        bool nullMerge = false;
        CloneTypeInfo? actualType = null;

        // A null source over a live target is normally just a clear, unless the target's type asked
        // to be consulted so it can merge instead.
        if (sourceValue is null)
        {
            sourceValue = info.GetValue(target);
            if (sourceValue != null)
            {
                actualType = CloneTypeInfo.Get(sourceValue.GetType());
                if (actualType.RequiresMerge)
                    nullMerge = true;
                else
                    sourceValue = null;
            }
        }

        object? mapped = GetTargetOf(sourceValue);
        Copy(nullMerge ? null : sourceValue, mapped, actualType);
        info.SetValue(target, mapped);
    }

    // Marks an object as copied and reports whether this call is the one doing it. Never unmarked:
    // that stops cycles, and stops a shared object being copied once per reference to it.
    private bool Push(object? source, CloneTypeInfo typeInfo)
        => typeInfo.Type.IsValueType || source is null || _handled.Add(source);

    #endregion

    private object? GetTargetOf(object? source)
        => source != null && _context.Targets.TryGetValue(source, out object? target) ? target : source;

    private static object CreateInstance(Type type)
    {
        try
        {
            object? created = Activator.CreateInstance(type, nonPublic: true);
            if (created != null) return created;
        }
        catch (MissingMethodException) { }
        catch (MemberAccessException) { }

        // Every field is written afterwards, so a blank instance is enough.
#if NETSTANDARD2_1
        return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
#else
        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
#endif
    }

    #region ICloneSetup

    void ICloneSetup.AddTarget(object source, object target) => _context.AddTarget(source, target);

    void ICloneSetup.HandleObject(object? source, object? target, CloneBehavior behavior, Type? behaviorTarget)
    {
        // A handler passing the object it is currently handling is asking for the default walk.
        bool fromHandler = _currentObject is ICloneExplicit || _currentType?.Format != null;
        if (fromHandler && ReferenceEquals(source, _currentObject))
        {
            SetupChildren(_currentObject!, target, _currentType!);
            return;
        }

        if (behaviorTarget != null)
        {
            _localBehavior.Add(new LocalBehavior { TargetType = behaviorTarget, Behavior = behavior });
            Setup(source, target, null);
            _localBehavior.RemoveAt(_localBehavior.Count - 1);
            return;
        }

        if (behavior == CloneBehavior.Reference) return;

        Setup(source, target, null, behavior);
    }

    #endregion

    #region ICloneOperation

    bool ICloneOperation.IsTarget(object? target) => target != null && _context.TargetSet.Contains(target);

    object? ICloneOperation.GetTarget(object? source) => GetTargetOf(source);

    object? ICloneOperation.GetMappedTarget(object? source)
        => source != null && _context.Targets.TryGetValue(source, out object? target) ? target : null;

    void ICloneOperation.HandleObject(object? source, object? target) => Copy(source, target);

    #endregion
}
