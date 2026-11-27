using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>Why a type got the plan it got. Returned by <see cref="ReloadEngine.Explain"/>.</summary>
public sealed class PlanExplanation
{
    internal PlanExplanation(Type type, TypeFacts facts, MigrationPlan plan, IValueMigrator? migrator, string reason)
    {
        Type = type;
        Facts = facts;
        Plan = plan;
        Migrator = migrator;
        Reason = reason;
    }

    public Type Type { get; }
    public TypeFacts Facts { get; }
    public MigrationPlan Plan { get; }
    public IValueMigrator? Migrator { get; }
    public string Reason { get; }

    public override string ToString() => $"{Type.FullName}: {Reason}";
}

/// <summary>
/// Chooses the plan for a type, once. Five ordered branches, cached, and printable through
/// <see cref="Explain"/>, so "why did my field not carry over" is answerable without a debugger.
/// </summary>
internal sealed class Planner
{
    private readonly PlanContext _context;
    private readonly TypeMap _types;
    private readonly TypeAnalyzer _analyzer;
    private readonly ScopeRules _scope;
    private readonly MigratorRegistry _migrators;
    private readonly ReportBuilder _report;

    private readonly Dictionary<Type, MigrationPlan> _plans = new();
    private readonly HashSet<Type> _planning = new();
    private readonly Dictionary<Type, (IValueMigrator? Migrator, string Reason)> _reasons = new();

    public Planner(PlanContext context, TypeMap types, TypeAnalyzer analyzer, ScopeRules scope,
        MigratorRegistry migrators, ReportBuilder report)
    {
        _context = context;
        _types = types;
        _analyzer = analyzer;
        _scope = scope;
        _migrators = migrators;
        _report = report;
    }

    public int PlannedCount => _plans.Count;

    public MigrationPlan For(Type type)
    {
        if (_plans.TryGetValue(type, out var cached)) return cached;

        // A plan that asks for the plan of its own type. Preserving is the only safe answer, but it is a bug in
        // whichever migrator asked, so it is reported rather than absorbed.
        if (!_planning.Add(type))
        {
            _report.Report(ReloadCode.NoPlanForType, ReloadSeverity.Error,
                "A migrator asked for the plan of the type it was being asked to plan, so instances are preserved untouched.",
                type.FullName ?? type.Name);
            return PreservePlan.Instance;
        }

        try
        {
            var plan = Choose(type, out var migrator, out var reason);

            _plans[type] = plan;
            _reasons[type] = (migrator, reason);
            return plan;
        }
        finally
        {
            _planning.Remove(type);
        }
    }

    public PlanExplanation Explain(Type type)
    {
        var plan = For(type);
        var reason = _reasons.TryGetValue(type, out var found) ? found : (null, "unknown");
        return new PlanExplanation(type, _analyzer.For(type), plan, reason.Item1, reason.Item2);
    }

    private MigrationPlan Choose(Type type, out IValueMigrator? migrator, out string reason)
    {
        migrator = null;

        var resolution = _types.Resolve(type);

        if (resolution.IsRemoved)
        {
            reason = "the type no longer exists";
            return DropPlan.Instance;
        }

        // Opting out only means "leave it alone". A type this reload replaces is migrated regardless, since
        // leaving an instance of the previous type behind is never what was asked for.
        if (!resolution.IsSubstituted)
        {
            if (IgnoreRules.Applies(type))
            {
                reason = "marked [ReloadIgnore]";
                return PreservePlan.Instance;
            }

            if (_scope.IsExcluded(type.Assembly))
            {
                reason = $"assembly {type.Assembly.GetName().Name} is excluded from the reload";
                return PreservePlan.Instance;
            }
        }

        if (_analyzer.IsInert(type))
        {
            reason = "inert, its storage cannot reach anything this reload changes";
            return PreservePlan.Instance;
        }

        foreach (var candidate in _migrators)
        {
            if (!candidate.Handles(type)) continue;

            migrator = candidate;
            reason = $"claimed by {candidate.GetType().Name}";

            try
            {
                return candidate.Plan(type, _context);
            }
            catch (Exception e)
            {
                _report.Report(ReloadCode.MigratorThrew, e, $"{candidate.GetType().Name} planning {type.FullName}");
                reason += ", which threw while planning, so the instance is preserved";
                return PreservePlan.Instance;
            }
        }

        reason = resolution.IsSubstituted ? "field copy onto the replacement type" : "field copy in place";
        return new ObjectPlan(type, resolution.Target!, _context);
    }
}
