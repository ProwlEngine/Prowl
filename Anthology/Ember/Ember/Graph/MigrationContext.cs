using System;

namespace Prowl.Ember;

/// <summary>What a plan gets while migrating an instance.</summary>
public sealed class MigrationContext
{
    private readonly GraphRewriter _rewriter;
    private readonly ReportBuilder _report;

    internal MigrationContext(GraphRewriter rewriter, TypeMap types, MemberMap members, ReportBuilder report)
    {
        _rewriter = rewriter;
        Types = types;
        Members = members;
        _report = report;
    }

    public TypeMap Types { get; }
    public MemberMap Members { get; }
    public ReloadPhase Phase { get; internal set; }

    /// <summary>
    /// The migrated counterpart of a value, allocating it on first sight. Never call this from
    /// <see cref="MigrationPlan.Allocate"/>: allocation has to stay shallow for the walk to terminate.
    /// </summary>
    public object? Map(object? value) => _rewriter.Map(value);

    /// <summary>
    /// Binds a source to a target that already exists, and queues the fill. This is what makes a readonly
    /// static work: the destination cannot be assigned, so whatever is already in it becomes the target.
    /// </summary>
    public void MapInto(object source, object existingTarget) => _rewriter.MapInto(source, existingTarget);

    /// <summary>Defers work until every fill has drained, for content that cannot be written until its keys are complete.</summary>
    public void ScheduleRebuild(object source, object target) => _rewriter.ScheduleRebuild(source, target);

    internal void RecordDrop(object source) => _rewriter.RecordDrop(source);

    internal void CountDelegate(bool rebuilt)
    {
        if (rebuilt) _report.DelegatesRebuilt++;
        else _report.DelegatesBroken++;
    }

    public void Report(ReloadCode code, ReloadSeverity severity, string message, string? subject = null)
        => _report.Report(code, severity, message, subject);
}
