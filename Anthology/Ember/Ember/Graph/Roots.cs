using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Ember;

/// <summary>
/// Where a walk starts. Either a pair of slots, read from one and written to the other, or a bare instance
/// whose replacement is reported rather than written anywhere.
/// </summary>
public readonly struct Root
{
    private Root(ValueSlot source, ValueSlot destination, object? instance)
    {
        Source = source;
        Destination = destination;
        Instance = instance;
    }

    public ValueSlot Source { get; }

    /// <summary>
    /// Where the migrated value goes. Different from <see cref="Source"/> whenever the storage itself moved,
    /// which is the normal case for a static field of a type this reload replaces.
    /// </summary>
    public ValueSlot Destination { get; }

    public object? Instance { get; }

    public bool HasSlot => !Source.IsEmpty;

    public static Root At(ValueSlot slot) => new(slot, slot, null);
    public static Root At(ValueSlot source, ValueSlot destination) => new(source, destination, null);
    public static Root Value(object instance) => new(default, default, instance ?? throw new ArgumentNullException(nameof(instance)));
}

/// <summary>What a root provider gets to work out where the walk should start.</summary>
public sealed class RootContext
{
    private readonly ReportBuilder _report;

    internal RootContext(AssemblyMap assemblies, TypeMap types, MemberMap members, ScopeRules scope, ReportBuilder report)
    {
        Assemblies = assemblies;
        Types = types;
        Members = members;
        Scope = scope;
        _report = report;
    }

    public AssemblyMap Assemblies { get; }
    public TypeMap Types { get; }
    public MemberMap Members { get; }
    public ScopeRules Scope { get; }

    public void Report(ReloadCode code, ReloadSeverity severity, string message, string? subject = null)
        => _report.Report(code, severity, message, subject);
}

public interface IRootProvider
{
    IEnumerable<Root> Enumerate(RootContext context);
}

public sealed class RootProviderCollection : ICollection<IRootProvider>
{
    private readonly List<IRootProvider> _providers = new();

    public static RootProviderCollection CreateDefault()
    {
        var providers = new RootProviderCollection();
        providers.Add(new StaticFieldRoots());
        return providers;
    }

    public int Count => _providers.Count;
    public bool IsReadOnly => false;

    public void Add(IRootProvider provider) => _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
    public bool Remove(IRootProvider provider) => _providers.Remove(provider);
    public void Clear() => _providers.Clear();
    public bool Contains(IRootProvider provider) => _providers.Contains(provider);
    public void CopyTo(IRootProvider[] array, int index) => _providers.CopyTo(array, index);

    public IEnumerator<IRootProvider> GetEnumerator() => _providers.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Instances the host registered as roots, such as a scene's components.</summary>
public sealed class ExplicitRoots : IRootProvider
{
    private readonly HashSet<object> _instances = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyCollection<object> Instances => _instances;

    public void Add(object instance) => _instances.Add(instance ?? throw new ArgumentNullException(nameof(instance)));
    public bool Remove(object instance) => _instances.Remove(instance);
    public void Clear() => _instances.Clear();

    public IEnumerable<Root> Enumerate(RootContext context)
    {
        foreach (var instance in _instances)
            yield return Root.Value(instance);
    }
}
