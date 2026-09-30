using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Prowl.Analyzers;

/// <summary>
/// Flags blocking waits on a task inside a <c>MonoBehaviour</c>: <c>.Result</c>, <c>.Wait()</c> and
/// <c>GetAwaiter().GetResult()</c>. Component code runs on the main thread, and the engine finishes
/// asset loads and frame waits from that same thread, so blocking there on one of them never returns.
/// Code inside a lambda, a static method or a static local function is left alone, since it usually runs on some
/// other thread, and so is a read that cannot block: behind an <c>IsCompleted</c> check on the same variable, or a
/// zero timeout poll.
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
        if (IsGuardedByCompletion(reference, reference.Instance)) return;
        Report(ctx, monoBehaviour, ".Result");
    }

    private static void AnalyzeCall(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour, ImmutableArray<INamedTypeSymbol?> tasks)
    {
        var invocation = (IInvocationOperation)ctx.Operation;
        IMethodSymbol method = invocation.TargetMethod;

        if (method.Name is "Wait" or "WaitAll" or "WaitAny" && IsTask(method.ContainingType, tasks))
        {
            if (IsZeroTimeout(invocation) || IsGuardedByCompletion(invocation, invocation.Instance)) return;
            Report(ctx, monoBehaviour, "." + method.Name + "()");
        }
        else if (method.Name == "GetResult" && IsAwaiter(method.ContainingType))
        {
            if (IsGuardedByCompletion(invocation, TaskBehindAwaiter(invocation.Instance))) return;
            Report(ctx, monoBehaviour, ".GetAwaiter().GetResult()");
        }
    }

    private static void Report(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour, string what)
    {
        ISymbol? member = ctx.ContainingSymbol;
        if (member is null || member.IsStatic || !DerivesFrom(member.ContainingType, monoBehaviour)) return;

        for (IOperation? op = ctx.Operation.Parent; op is not null; op = op.Parent)
            if (op is IAnonymousFunctionOperation or ILocalFunctionOperation { Symbol.IsStatic: true }) return;

        ctx.ReportDiagnostic(Diagnostic.Create(BlockingWait, ctx.Operation.Syntax.GetLocation(), what));
    }

    // t.GetAwaiter() and t.ConfigureAwait(x).GetAwaiter() both come back to t.
    private static IOperation? TaskBehindAwaiter(IOperation? awaiter)
    {
        while (awaiter is IInvocationOperation { TargetMethod.Name: "GetAwaiter" or "ConfigureAwait" } call)
            awaiter = call.Instance;
        return awaiter;
    }

    /// <summary>
    /// Whether the task is already known to be complete where it is read: behind a check of IsCompleted or
    /// IsCompletedSuccessfully that must have held, on the same local, parameter or field, with nothing assigning
    /// it in between. Anything the analyzer cannot follow, such as a call that may return a new task each time,
    /// counts as unguarded.
    /// </summary>
    private static bool IsGuardedByCompletion(IOperation use, IOperation? task)
    {
        ISymbol? symbol = StableSymbol(task);
        if (symbol is null) return false;

        IOperation child = use;
        for (IOperation? op = use.Parent; op is not null; child = op, op = op.Parent)
        {
            // if (t.IsCompleted) { ... } and t.IsCompleted ? ... : ...
            if (op is IConditionalOperation conditional && ReferenceEquals(conditional.WhenTrue, child)
                && Asserts(conditional.Condition, symbol) && !Assigns(conditional.WhenTrue, symbol))
                return true;

            // t.IsCompleted && ...
            if (op is IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and && ReferenceEquals(and.RightOperand, child)
                && Asserts(and.LeftOperand, symbol))
                return true;

            // if (!t.IsCompleted) return; earlier in the same block
            if (op is IBlockOperation block && ExitsEarlierUnlessComplete(block, child, symbol))
                return true;
        }
        return false;
    }

    private static bool ExitsEarlierUnlessComplete(IBlockOperation block, IOperation statement, ISymbol symbol)
    {
        int index = block.Operations.IndexOf(statement);
        for (int i = index - 1; i >= 0; i--)
        {
            IOperation earlier = block.Operations[i];
            if (earlier is IConditionalOperation { WhenFalse: null } check
                && check.Condition is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } not
                && Asserts(not.Operand, symbol) && Exits(check.WhenTrue))
                return true;

            if (Assigns(earlier, symbol)) return false;
        }
        return false;
    }

    // Holds only if the task is complete: the check itself, or one operand of an && chain.
    private static bool Asserts(IOperation condition, ISymbol symbol) => condition switch
    {
        IPropertyReferenceOperation { Property.Name: "IsCompleted" or "IsCompletedSuccessfully" } check
            => SymbolEqualityComparer.Default.Equals(StableSymbol(check.Instance), symbol),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and
            => Asserts(and.LeftOperand, symbol) || Asserts(and.RightOperand, symbol),
        _ => false,
    };

    private static bool Exits(IOperation statement) => statement switch
    {
        IReturnOperation or IThrowOperation => true,
        IExpressionStatementOperation { Operation: IThrowOperation } => true,
        IBlockOperation { Operations.Length: > 0 } block => Exits(block.Operations[block.Operations.Length - 1]),
        _ => false,
    };

    private static bool Assigns(IOperation scope, ISymbol symbol)
    {
        foreach (IOperation op in scope.DescendantsAndSelf())
            if (op is IAssignmentOperation assignment && SymbolEqualityComparer.Default.Equals(StableSymbol(assignment.Target), symbol))
                return true;
        return false;
    }

    // A variable the analyzer can follow. A call or an arbitrary expression may give a different task each time.
    private static ISymbol? StableSymbol(IOperation? operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation } field => field.Field,
        _ => null,
    };

    // A zero timeout only polls, whichever of Wait, WaitAll or WaitAny takes it.
    private static bool IsZeroTimeout(IInvocationOperation invocation)
    {
        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name is not ("millisecondsTimeout" or "timeout")) continue;

            IOperation value = argument.Value;
            if (value.ConstantValue is { HasValue: true, Value: 0 }) return true;
            if (value is IFieldReferenceOperation { Field.Name: "Zero" } field
                && field.Field.ContainingType?.ToDisplayString() == "System.TimeSpan")
                return true;
        }
        return false;
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
