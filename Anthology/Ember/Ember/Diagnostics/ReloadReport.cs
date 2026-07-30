using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Ember;

/// <summary>Counts and timings for one reload. Zeroed when <see cref="ReloadOptions.CollectStatistics"/> is off.</summary>
public readonly record struct ReloadStatistics(
    int TypesPlanned,
    int TypesInert,
    int ObjectsVisited,
    int ObjectsReplaced,
    int ObjectsPreserved,
    int ObjectsDropped,
    int DelegatesRebuilt,
    int DelegatesBroken,
    TimeSpan PlanTime,
    TimeSpan MapTime,
    TimeSpan FillTime,
    TimeSpan RebuildTime,
    TimeSpan NotifyTime);

/// <summary>The result of one reload.</summary>
public sealed class ReloadReport
{
    private readonly List<ReloadDiagnostic> _diagnostics;

    internal ReloadReport(
        List<ReloadDiagnostic> diagnostics,
        IReadOnlyDictionary<object, object> replaced,
        IReadOnlyList<object> dropped,
        ReloadStatistics statistics)
    {
        _diagnostics = diagnostics;
        Replaced = replaced;
        Dropped = dropped;
        Statistics = statistics;
    }

    /// <summary>False when any diagnostic was reported at <see cref="ReloadSeverity.Error"/>.</summary>
    public bool Succeeded => !_diagnostics.Any(d => d.Severity == ReloadSeverity.Error);

    public IReadOnlyList<ReloadDiagnostic> Diagnostics => _diagnostics;
    public IEnumerable<ReloadDiagnostic> Errors => _diagnostics.Where(d => d.Severity == ReloadSeverity.Error);
    public IEnumerable<ReloadDiagnostic> Warnings => _diagnostics.Where(d => d.Severity == ReloadSeverity.Warning);

    /// <summary>
    /// Every visited object whose replacement is a different instance, keyed by reference identity. Not only
    /// the roots.
    /// </summary>
    public IReadOnlyDictionary<object, object> Replaced { get; }

    /// <summary>Visited objects whose type was removed, so every reference to them became null.</summary>
    public IReadOnlyList<object> Dropped { get; }

    public ReloadStatistics Statistics { get; }

    public bool TryGetReplacement<T>(T previous, out T current) where T : class
    {
        if (previous != null && Replaced.TryGetValue(previous, out var found) && found is T typed)
        {
            current = typed;
            return true;
        }
        current = null!;
        return false;
    }
}
