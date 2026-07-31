using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>What a migrator gets while building a plan. Everything here is per reload.</summary>
public sealed class PlanContext
{
    private readonly TypeAnalyzer _analyzer;
    private readonly MetadataCache _metadata;
    private readonly ReportBuilder _report;

    private Planner _planner = null!;

    internal PlanContext(AssemblyMap assemblies, TypeMap types, MemberMap members, ReloadOptions options,
        TypeAnalyzer analyzer, MetadataCache metadata, ReportBuilder report)
    {
        Assemblies = assemblies;
        Types = types;
        Members = members;
        Options = options;
        _analyzer = analyzer;
        _metadata = metadata;
        _report = report;
    }

    internal void UsePlanner(Planner planner) => _planner = planner;

    public AssemblyMap Assemblies { get; }
    public TypeMap Types { get; }
    public MemberMap Members { get; }
    public ReloadOptions Options { get; }

    public TypeFacts Facts(Type type) => _analyzer.For(type);

    /// <summary>
    /// Whether a slot declared as this type can be carried across without migrating what it holds. Stricter
    /// than <see cref="TypeFacts.IsInert"/>, which describes what a type itself stores rather than what a slot
    /// of that type can be holding at runtime.
    /// </summary>
    public bool IsInertSlot(Type declaredType) => _analyzer.IsInertSlot(declaredType);

    /// <summary>The plan for another type, so a container can ask about its element type.</summary>
    public MigrationPlan PlanFor(Type type) => _planner.For(type);

    /// <summary>
    /// A factory for the value a field's initializer would have produced, evaluated once per instance so that
    /// <c>= new List<T>()</c> yields a fresh list each time. False when there is no independent
    /// initializer, or its expression could not be translated.
    /// </summary>
    public bool TryGetFieldDefault(FieldInfo field, out Func<object?> factory)
    {
        factory = null!;

        if (Options.NewFields != NewFieldPolicy.DeclaredInitializer) return false;
        if (field.DeclaringType is not { } declaring) return false;

        var metadata = _metadata.For(declaring.Assembly);
        return metadata != null && metadata.TryGetFieldInitializer(field, out factory);
    }

    public void Report(ReloadCode code, ReloadSeverity severity, string message, string? subject = null)
        => _report.Report(code, severity, message, subject);
}
