using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Which assemblies participate in a reload. Exclusion always beats inclusion. Ember's own assemblies exclude
/// themselves.
/// </summary>
public sealed class ScopeRules
{
    private readonly Dictionary<Assembly, Func<Type, bool>?> _included = new();
    private readonly HashSet<Assembly> _excluded = new();
    private readonly HashSet<string> _excludedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _excludedPrefixes = new();

    public ScopeRules()
    {
        // The engine walking its own live state mid reload would be chaos, so it excludes itself. The contracts
        // assembly is deliberately not excluded: it holds no engine state, and the types in it that user code
        // actually instantiates, such as a self-clearing cache, have to be reachable for their hooks to fire.
        Exclude(typeof(ScopeRules).Assembly);
    }

    public IReadOnlyCollection<Assembly> Included => _included.Keys;

    /// <summary>
    /// Walk this assembly's statics on reload. The optional filter narrows which of its types are considered.
    /// </summary>
    public ScopeRules Include(Assembly assembly, Func<Type, bool>? typeFilter = null)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        _included[assembly] = typeFilter;
        return this;
    }

    public ScopeRules Exclude(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        _excluded.Add(assembly);
        return this;
    }

    public ScopeRules Exclude(string simpleName)
    {
        _excludedNames.Add(simpleName ?? throw new ArgumentNullException(nameof(simpleName)));
        return this;
    }

    /// <summary>Exclude every assembly whose simple name starts with the prefix, matched case insensitively.</summary>
    public ScopeRules ExcludePrefix(string simpleNamePrefix)
    {
        _excludedPrefixes.Add(simpleNamePrefix ?? throw new ArgumentNullException(nameof(simpleNamePrefix)));
        return this;
    }

    public bool IsIncluded(Assembly assembly) => _included.ContainsKey(assembly) && !IsExcluded(assembly);

    public bool IsExcluded(Assembly assembly)
    {
        if (_excluded.Contains(assembly)) return true;

        var name = assembly.GetName().Name ?? string.Empty;
        if (_excludedNames.Contains(name)) return true;

        foreach (var prefix in _excludedPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// Whether this type may be walked: its assembly is not excluded, and the type filter given to
    /// <see cref="Include"/> accepts it. An assembly that was never included, which is how a replaced assembly
    /// arrives, has no filter and is accepted.
    /// </summary>
    public bool Accepts(Type type)
    {
        if (IsExcluded(type.Assembly)) return false;
        return !_included.TryGetValue(type.Assembly, out var filter) || filter == null || filter(type);
    }

    /// <summary>
    /// Rotate the include set onto the current side after a reload. Without this the next reload would walk
    /// assemblies that no longer exist. This is the only state a reload carries over.
    /// </summary>
    internal void ApplyChanges(AssemblyMap map)
    {
        foreach (var previous in map.Previous)
        {
            if (!_included.TryGetValue(previous, out var filter)) continue;

            _included.Remove(previous);

            var resolution = map.Resolve(previous);
            if (resolution.Kind == AssemblyResolutionKind.Substituted && resolution.Target != null)
                _included[resolution.Target] = filter;
        }

        foreach (var added in map.Added)
            if (!_included.ContainsKey(added))
                _included[added] = null;
    }
}
