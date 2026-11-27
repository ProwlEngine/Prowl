using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Prowl.Ember;

public enum AssemblyResolutionKind
{
    /// <summary>Not part of this reload.</summary>
    Unchanged,

    /// <summary>Replaced. <c>Target</c> is the replacement.</summary>
    Substituted,

    /// <summary>Unloaded with no replacement. <c>Target</c> is null.</summary>
    Removed,
}

public readonly record struct AssemblyResolution(AssemblyResolutionKind Kind, Assembly? Target);

/// <summary>
/// Which assemblies were replaced by which, for one reload. Swap chains are collapsed at construction, so a
/// previous assembly always resolves directly to its final replacement.
/// </summary>
public sealed class AssemblyMap
{
    private readonly Dictionary<Assembly, Assembly?> _substitutions = new();
    private readonly HashSet<Assembly> _added = new();

    internal AssemblyMap(IReadOnlyList<AssemblyChange> changes, ReportBuilder report)
    {
        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case AssemblyChangeKind.Added:
                    _added.Add(change.Current!);
                    break;
                case AssemblyChangeKind.Removed:
                    _substitutions[change.Previous!] = null;
                    break;
                case AssemblyChangeKind.Replaced:
                    _substitutions[change.Previous!] = change.Current;
                    break;
            }
        }

        Collapse(report);
    }

    /// <summary>Assemblies replaced or removed by this reload.</summary>
    public IReadOnlyCollection<Assembly> Previous => _substitutions.Keys;

    /// <summary>Replacements introduced by this reload, excluding assemblies added outright.</summary>
    public IReadOnlyCollection<Assembly> Current
        => _substitutions.Values.Where(x => x != null).Distinct().ToArray()!;

    /// <summary>Assemblies introduced without replacing anything.</summary>
    public IReadOnlyCollection<Assembly> Added => _added;

    public bool IsSubstituted(Assembly assembly) => _substitutions.ContainsKey(assembly);

    public AssemblyResolution Resolve(Assembly assembly)
    {
        if (!_substitutions.TryGetValue(assembly, out var target))
            return new AssemblyResolution(AssemblyResolutionKind.Unchanged, assembly);

        return target == null
            ? new AssemblyResolution(AssemblyResolutionKind.Removed, null)
            : new AssemblyResolution(AssemblyResolutionKind.Substituted, target);
    }

    /// <summary>Whether anything at all changed. A reload with nothing to do returns early.</summary>
    public bool IsEmpty => _substitutions.Count == 0 && _added.Count == 0;

    // If A becomes B and B becomes C in the same request, A resolves straight to C.
    private void Collapse(ReportBuilder report)
    {
        foreach (var previous in _substitutions.Keys.ToArray())
        {
            var target = _substitutions[previous];
            if (target == null) continue;

            var visited = new List<Assembly> { previous };

            while (target != null && _substitutions.TryGetValue(target, out var next))
            {
                if (visited.Contains(target))
                {
                    report.Report(ReloadCode.AssemblySwapCycle, ReloadSeverity.Error,
                        $"Assembly swap cycle: {DescribeCycle(visited, target)}. The chain is left at its first hop.",
                        previous.GetName().Name);
                    target = _substitutions[previous];
                    break;
                }

                visited.Add(target);
                target = next;
            }

            _substitutions[previous] = target;
        }
    }

    private static string DescribeCycle(List<Assembly> visited, Assembly repeated)
    {
        var text = new StringBuilder();
        foreach (var assembly in visited)
            text.Append(assembly.GetName().Name).Append(" to ");
        return text.Append(repeated.GetName().Name).ToString();
    }

    /// <summary>
    /// Whether an assembly outside this reload references one inside it. Its code was compiled against the
    /// previous types, so walking its statics is not safe.
    /// </summary>
    internal bool ReferencesSubstituted(Assembly assembly, out AssemblyName reference)
    {
        var references = assembly.GetReferencedAssemblies();

        foreach (var candidate in references)
        {
            var substituted = _substitutions.Keys.FirstOrDefault(x => SameIdentity(x.GetName(), candidate));
            if (substituted == null) continue;

            // Referencing both sides is deliberate, not a mistake, so it does not disqualify the assembly.
            var replacement = _substitutions[substituted];
            if (replacement != null && references.Any(x => SameIdentity(x, replacement.GetName()))) continue;

            reference = candidate;
            return true;
        }

        reference = null!;
        return false;
    }

    private static bool SameIdentity(AssemblyName a, AssemblyName b)
        => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && (a.Version?.Equals(b.Version) ?? false);
}
