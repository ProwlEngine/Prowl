// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Echo.Cloning;

/// <summary>
/// Finds the clone handling for a type: a format registered here, or failing that the type's
/// serialization format when it also implements <see cref="ICloneFormat"/>.
/// </summary>
public static class CloneFormats
{
    private static readonly List<ICloneFormat> _standalone = [new DelegateCloneFormat()];
    private static readonly ConcurrentDictionary<Type, ICloneFormat?> _cache = new();

    public static void Register(ICloneFormat format)
    {
        _standalone.Insert(0, format);
        _cache.Clear();
    }

    public static void Unregister(ICloneFormat format)
    {
        _standalone.RemoveAll(f => ReferenceEquals(f, format));
        _cache.Clear();
    }

    internal static void ClearCache() => _cache.Clear();

    [RequiresUnreferencedCode("Cloning reflects over every instance field and cannot be statically analyzed.")]
    internal static ICloneFormat? For(Type type) => _cache.GetOrAdd(type, static t =>
    {
        foreach (ICloneFormat format in _standalone)
            if (format.CanClone(t))
                return format;

        return Serializer.GetFormatForType(t) is ICloneFormat shared && shared.CanClone(t) ? shared : null;
    });
}

/// <summary>
/// Rebuilds a delegate against the copy. Entries bound to an object inside the copy are rebound to
/// that object's counterpart, entries bound anywhere else are dropped, and a target's own entries
/// bound outside the copy are kept.
/// </summary>
internal sealed class DelegateCloneFormat : ICloneLateFormat
{
    public bool CanClone(Type type) => typeof(Delegate).IsAssignableFrom(type);

    public bool RequiresMerge => true;

    public object CreateCloneTarget(object source, object? existingTarget)
        => throw new NotSupportedException("Delegate targets are produced in the late setup step.");

    public void SetupCloneTargets(object source, object target, ICloneSetup setup) { }

    public void CopyCloneTo(object source, object target, ICloneOperation operation) { }

    public object? CreateTargetLate(object? source, object? target, ICloneOperation operation)
    {
        Delegate[]? sourceList = (source as Delegate)?.GetInvocationList();
        Delegate[]? targetList = (target as Delegate)?.GetInvocationList();

        var merged = new List<Delegate>();

        if (sourceList != null)
        {
            foreach (Delegate entry in sourceList)
            {
                if (entry.Target == null) continue;

                object? boundTo = operation.GetMappedTarget(entry.Target);
                if (boundTo == null) continue;

                merged.Add(entry.Method.CreateDelegate(entry.GetType(), boundTo));
            }
        }

        if (targetList != null)
        {
            foreach (Delegate entry in targetList)
            {
                if (entry.Target == null) continue;
                if (operation.IsTarget(entry.Target)) continue;

                merged.Add(entry.Method.CreateDelegate(entry.GetType(), entry.Target));
            }
        }

        return merged.Count == 0 ? null : Delegate.Combine(merged.ToArray());
    }
}
