using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Prowl.Analyzers;

/// <summary>
/// Flags null-conditional (<c>?.</c>, <c>?[]</c>) and null-coalescing (<c>??</c>, <c>??=</c>) operators used on an
/// <c>EngineObject</c>. Those operators only test reference-null, but an EngineObject can be destroyed/disposed while
/// still being a live reference, so <c>obj?.M()</c> runs on a dead object and <c>obj ?? fallback</c> returns the dead
/// object instead of the fallback. Use <c>IsValid()</c>/<c>IsNotValid()</c> for a validity check, or
/// <c>object.ReferenceEquals</c> when a true reference-null test is genuinely intended.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EngineObjectNullAnalyzer : DiagnosticAnalyzer
{
    public const string NullConditionalId = "PROWLEO001";
    public const string NullCoalescingId = "PROWLEO002";

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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(s_nullConditional, s_nullCoalescing);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var engineObject = start.Compilation.GetTypeByMetadataName(EngineObjectMetadataName);
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
        });
    }

    private static void Report(OperationAnalysisContext ctx, DiagnosticDescriptor rule, IOperation operand, INamedTypeSymbol engineObject)
    {
        if (!DerivesFromEngineObject(operand.Type, engineObject)) return;
        ctx.ReportDiagnostic(Diagnostic.Create(rule, operand.Syntax.GetLocation(), operand.Type!.Name));
    }

    private static bool DerivesFromEngineObject(ITypeSymbol? type, INamedTypeSymbol engineObject)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, engineObject))
                return true;
        return false;
    }
}
