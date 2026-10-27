using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Prowl.Analyzers;

/// <summary>
/// Flags null-conditional (<c>?.</c>, <c>?[]</c>) and null-coalescing (<c>??</c>, <c>??=</c>) operators and comparisons
/// with null (<c>== null</c>, <c>!= null</c>, <c>is null</c>, <c>is not null</c>) used on an <c>EngineObject</c>. All of
/// them only test reference-null, but an EngineObject can be destroyed/disposed while still being a live reference, so
/// <c>obj?.M()</c> runs on a dead object, <c>obj ?? fallback</c> returns the dead object instead of the fallback and
/// <c>obj != null</c> holds for a dead object. Use <c>IsValid()</c>/<c>IsNotValid()</c> for a validity check, or
/// <c>object.ReferenceEquals</c> when a true reference-null test is genuinely intended.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EngineObjectNullAnalyzer : DiagnosticAnalyzer
{
    public const string NullConditionalId = "PROWLEO001";
    public const string NullCoalescingId = "PROWLEO002";
    public const string NullComparisonId = "PROWLEO003";

    private const string EngineObjectMetadataName = "Prowl.Runtime.EngineObject";

    private static readonly DiagnosticDescriptor s_nullConditional = new(
        NullConditionalId,
        title: "Null-conditional operator used on an EngineObject",
        messageFormat: "'{0}' is an EngineObject; '?.' only tests reference-null and ignores destruction, so it runs on a destroyed object. Guard with IsValid()/IsNotValid() instead (or object.ReferenceEquals for a true-null check).",
        category: "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Null-conditional operators bypass EngineObject validity: a destroyed object is still a live reference, so '?.' invokes members on it instead of short-circuiting.");

    private static readonly DiagnosticDescriptor s_nullCoalescing = new(
        NullCoalescingId,
        title: "Null-coalescing operator used on an EngineObject",
        messageFormat: "'{0}' is an EngineObject; '??'/'??=' only test reference-null and ignore destruction, so a destroyed object is used instead of the fallback. Guard with an explicit IsValid() check instead.",
        category: "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Null-coalescing operators bypass EngineObject validity: a destroyed object is still a live reference, so it is returned instead of the fallback operand.");

    private static readonly DiagnosticDescriptor s_nullComparison = new(
        NullComparisonId,
        title: "Null comparison used on an EngineObject",
        messageFormat: "'{0}' is an EngineObject; comparing it with null only tests the reference and misses a destroyed object. Use IsValid()/IsNotValid() instead (or object.ReferenceEquals for a true-null check).",
        category: "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "'== null', '!= null', 'is null' and 'is not null' bypass EngineObject validity: a destroyed object is still a live reference, so it compares as not null.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(s_nullConditional, s_nullCoalescing, s_nullComparison);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? engineObject = start.Compilation.GetTypeByMetadataName(EngineObjectMetadataName);
            if (engineObject is null) return; // no reference to Prowl.Runtime in this compilation

            start.RegisterOperationAction(ctx =>
            {
                var op = (IConditionalAccessOperation)ctx.Operation;
                Report(ctx, s_nullConditional, op.Operation, engineObject);
            }, OperationKind.ConditionalAccess);

            start.RegisterOperationAction(ctx =>
            {
                var op = (ICoalesceOperation)ctx.Operation;
                Report(ctx, s_nullCoalescing, op.Value, engineObject);
            }, OperationKind.Coalesce);

            start.RegisterOperationAction(ctx =>
            {
                var op = (ICoalesceAssignmentOperation)ctx.Operation;
                Report(ctx, s_nullCoalescing, op.Target, engineObject);
            }, OperationKind.CoalesceAssignment);

            start.RegisterOperationAction(ctx =>
            {
                var op = (IBinaryOperation)ctx.Operation;
                if (op.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)) return;
                if (IsNull(op.RightOperand)) Report(ctx, s_nullComparison, Unwrap(op.LeftOperand), engineObject);
                else if (IsNull(op.LeftOperand)) Report(ctx, s_nullComparison, Unwrap(op.RightOperand), engineObject);
            }, OperationKind.Binary);

            start.RegisterOperationAction(ctx =>
            {
                var op = (IIsPatternOperation)ctx.Operation;
                IPatternOperation pattern = op.Pattern is INegatedPatternOperation negated ? negated.Pattern : op.Pattern;
                if (pattern is IConstantPatternOperation constant && IsNull(constant.Value))
                    Report(ctx, s_nullComparison, Unwrap(op.Value), engineObject);
            }, OperationKind.IsPattern);
        });
    }

    private static bool IsNull(IOperation operand) => operand.ConstantValue is { HasValue: true, Value: null };

    // The operand as written, before the implicit conversions to a comparison's parameter types. An explicit cast such
    // as (object)obj stays, so it opts out.
    private static IOperation Unwrap(IOperation operand)
    {
        while (operand is IConversionOperation { IsImplicit: true } conversion)
            operand = conversion.Operand;
        return operand;
    }

    private static void Report(OperationAnalysisContext ctx, DiagnosticDescriptor rule, IOperation operand, INamedTypeSymbol engineObject)
    {
        if (!DerivesFromEngineObject(operand.Type, engineObject)) return;
        ctx.ReportDiagnostic(Diagnostic.Create(rule, operand.Syntax.GetLocation(), operand.Type!.Name));
    }

    private static bool DerivesFromEngineObject(ITypeSymbol? type, INamedTypeSymbol engineObject, int depth = 0)
    {
        // A generic parameter is an EngineObject when one of its constraints is, which may itself be another parameter.
        if (type is ITypeParameterSymbol parameter)
        {
            if (depth > 8) return false;
            foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                if (DerivesFromEngineObject(constraint, engineObject, depth + 1))
                    return true;
            return false;
        }

        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, engineObject))
                return true;
        return false;
    }
}
