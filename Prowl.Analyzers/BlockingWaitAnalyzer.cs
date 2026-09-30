using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Prowl.Analyzers;

/// <summary>
/// Flags blocking waits on a task inside a <c>MonoBehaviour</c>: <c>.Result</c>, <c>.Wait()</c> and
/// <c>GetAwaiter().GetResult()</c>. Component code runs on the main thread, and the engine finishes
/// asset loads and frame waits from that same thread, so blocking there on one of them never returns.
/// Code inside a lambda or a static method is left alone, since it usually runs on some other thread, and so
/// is a read that cannot block: <c>.Result</c> behind an <c>IsCompleted</c> check, or a <c>Wait(0)</c> poll.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlockingWaitAnalyzer : DiagnosticAnalyzer
{
    public const string BlockingWaitId = "PROWLTH001";

    private const string MonoBehaviourMetadataName = "Prowl.Runtime.MonoBehaviour";

    public static readonly DiagnosticDescriptor BlockingWait = new(
        BlockingWaitId,
        title: "Blocking wait on a task in a MonoBehaviour",
        messageFormat: "'{0}' blocks the main thread until the task finishes. Asset loads and GameTask waits finish on the main thread, so this never returns for them. Use await, or the blocking Load() for assets.",
        category: "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Components run on the main thread, which is also the thread that publishes finished asset loads and completes frame waits. Blocking it on such a task waits for work that can only happen once the wait is over.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(BlockingWait);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var monoBehaviour = start.Compilation.GetTypeByMetadataName(MonoBehaviourMetadataName);
            if (monoBehaviour is null) return;

            var tasks = ImmutableArray.Create(
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"));

            start.RegisterOperationAction(ctx => AnalyzeResult(ctx, monoBehaviour, tasks), OperationKind.PropertyReference);
            start.RegisterOperationAction(ctx => AnalyzeCall(ctx, monoBehaviour, tasks), OperationKind.Invocation);
        });
    }

    private static void AnalyzeResult(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour, ImmutableArray<INamedTypeSymbol?> tasks)
    {
        var reference = (IPropertyReferenceOperation)ctx.Operation;
        if (reference.Property.Name != "Result" || !IsTask(reference.Property.ContainingType, tasks)) return;
        if (IsGuardedByCompletion(reference)) return;
        Report(ctx, monoBehaviour, ".Result");
    }

    private static void AnalyzeCall(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour, ImmutableArray<INamedTypeSymbol?> tasks)
    {
        var invocation = (IInvocationOperation)ctx.Operation;
        IMethodSymbol method = invocation.TargetMethod;

        if (method.Name is "Wait" or "WaitAll" or "WaitAny" && IsTask(method.ContainingType, tasks) && !IsZeroTimeout(invocation))
            Report(ctx, monoBehaviour, "." + method.Name + "()");
        else if (method.Name == "GetResult" && IsAwaiter(method.ContainingType))
            Report(ctx, monoBehaviour, ".GetAwaiter().GetResult()");
    }

    private static void Report(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour, string what)
    {
        ISymbol? member = ctx.ContainingSymbol;
        while (member is IMethodSymbol { MethodKind: MethodKind.LocalFunction }) member = member.ContainingSymbol;
        if (member is null || member.IsStatic || !DerivesFrom(member.ContainingType, monoBehaviour)) return;

        for (IOperation? op = ctx.Operation.Parent; op is not null; op = op.Parent)
            if (op is IAnonymousFunctionOperation) return;

        ctx.ReportDiagnostic(Diagnostic.Create(BlockingWait, ctx.Operation.Syntax.GetLocation(), what));
    }

    // Inside the true branch of an if or a conditional that checked IsCompleted or IsCompletedSuccessfully on the same task.
    private static bool IsGuardedByCompletion(IPropertyReferenceOperation result)
    {
        string? task = result.Instance?.Syntax.ToString();
        if (task is null) return false;

        IOperation child = result;
        for (IOperation? op = result.Parent; op is not null; child = op, op = op.Parent)
        {
            if (op is not IConditionalOperation conditional || !ReferenceEquals(conditional.WhenTrue, child)) continue;

            foreach (IOperation check in conditional.Condition.DescendantsAndSelf())
                if (check is IPropertyReferenceOperation { Property.Name: "IsCompleted" or "IsCompletedSuccessfully" } done
                    && done.Instance?.Syntax.ToString() == task)
                    return true;
        }
        return false;
    }

    // Wait(0) and Wait(TimeSpan.Zero) only poll.
    private static bool IsZeroTimeout(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "Wait" || invocation.Arguments.Length == 0) return false;

        IOperation value = invocation.Arguments[0].Value;
        if (value.ConstantValue is { HasValue: true, Value: 0 }) return true;
        return value is IFieldReferenceOperation { Field.Name: "Zero" } field
               && field.Field.ContainingType?.ToDisplayString() == "System.TimeSpan";
    }

    private static bool IsTask(INamedTypeSymbol? type, ImmutableArray<INamedTypeSymbol?> tasks)
    {
        INamedTypeSymbol? definition = type?.OriginalDefinition;
        foreach (INamedTypeSymbol? task in tasks)
            if (task is not null && SymbolEqualityComparer.Default.Equals(definition, task))
                return true;
        return false;
    }

    // TaskAwaiter, ValueTaskAwaiter and their configured forms, all in System.Runtime.CompilerServices.
    private static bool IsAwaiter(INamedTypeSymbol? type)
        => type is not null
           && type.Name.EndsWith("Awaiter", System.StringComparison.Ordinal)
           && type.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices";

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol monoBehaviour)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, monoBehaviour))
                return true;
        return false;
    }
}
