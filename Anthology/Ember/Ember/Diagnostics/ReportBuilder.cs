using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Ember;

/// <summary>Accumulates everything a reload produces, and mints the <see cref="ReloadReport"/> at the end.</summary>
internal sealed class ReportBuilder
{
    private readonly List<ReloadDiagnostic> _diagnostics = new();
    private readonly IDiagnosticSink? _sink;
    private readonly bool _collectStatistics;

    private readonly Dictionary<object, object> _replaced = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _dropped = new();

    private long _planTicks, _mapTicks, _fillTicks, _rebuildTicks, _notifyTicks;

    public ReportBuilder(IDiagnosticSink? sink, bool collectStatistics)
    {
        _sink = sink;
        _collectStatistics = collectStatistics;
    }

    public int TypesPlanned;
    public int TypesInert;
    public int ObjectsVisited;
    public int ObjectsPreserved;
    public int DelegatesRebuilt;
    public int DelegatesBroken;

    public void Report(ReloadCode code, ReloadSeverity severity, string message, string? subject = null)
    {
        var diagnostic = new ReloadDiagnostic(code, severity, message, subject);
        _diagnostics.Add(diagnostic);
        _sink?.Report(diagnostic);
    }

    public void Report(ReloadCode code, Exception exception, string? subject = null)
        => Report(code, ReloadSeverity.Error, $"{exception.GetType().Name}: {exception.Message}", subject);

    public void RecordReplacement(object previous, object current) => _replaced[previous] = current;
    public void RecordDrop(object previous) => _dropped.Add(previous);

    public IReadOnlyList<object> Dropped => _dropped;

    /// <summary>Times a phase into the statistics. A no-op stopwatch when statistics are off.</summary>
    public PhaseTimer Time(ReloadPhase phase) => new(this, phase, _collectStatistics);

    private void AddPhaseTime(ReloadPhase phase, long ticks)
    {
        switch (phase)
        {
            case ReloadPhase.Plan: _planTicks += ticks; break;
            case ReloadPhase.Map: _mapTicks += ticks; break;
            case ReloadPhase.Fill: _fillTicks += ticks; break;
            case ReloadPhase.Rebuild: _rebuildTicks += ticks; break;
            case ReloadPhase.Notify: _notifyTicks += ticks; break;
        }
    }

    public ReloadReport Build() => new(
        _diagnostics,
        _replaced,
        _dropped,
        new ReloadStatistics(
            TypesPlanned, TypesInert,
            ObjectsVisited, _replaced.Count, ObjectsPreserved, _dropped.Count,
            DelegatesRebuilt, DelegatesBroken,
            TimeSpan.FromTicks(_planTicks), TimeSpan.FromTicks(_mapTicks), TimeSpan.FromTicks(_fillTicks),
            TimeSpan.FromTicks(_rebuildTicks), TimeSpan.FromTicks(_notifyTicks)));

    internal readonly struct PhaseTimer : IDisposable
    {
        private readonly ReportBuilder? _owner;
        private readonly ReloadPhase _phase;
        private readonly long _start;

        public PhaseTimer(ReportBuilder owner, ReloadPhase phase, bool enabled)
        {
            _owner = enabled ? owner : null;
            _phase = phase;
            _start = enabled ? Stopwatch.GetTimestamp() : 0;
        }

        public void Dispose()
            => _owner?.AddPhaseTime(_phase, Stopwatch.GetElapsedTime(_start).Ticks);
    }
}
