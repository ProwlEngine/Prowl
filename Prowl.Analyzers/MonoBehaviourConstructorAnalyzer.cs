using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Prowl.Analyzers;

/// <summary>
/// Flags constructors declared on a <c>MonoBehaviour</c>. The engine constructs components itself,
/// through <c>AddComponent</c> and through deserialization, and a constructor runs before the
/// component is attached to anything: <c>GameObject</c> and <c>Transform</c> are not set yet, and
/// serialized field values have not been written. Anything that needs those belongs in
/// <c>OnEnable</c>, <c>Awake</c> or <c>Start</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MonoBehaviourConstructorAnalyzer : DiagnosticAnalyzer
{
    public const string DeclaredConstructorId = "PROWLMB001";
    public const string NoParameterlessConstructorId = "PROWLMB002";
    public const string RunsBeforeAttachId = "PROWLMB003";
    public const string InvokesDelegateId = "PROWLMB004";

    private const string MonoBehaviourMetadataName = "Prowl.Runtime.MonoBehaviour";

    public static readonly DiagnosticDescriptor DeclaredConstructor = new(
        DeclaredConstructorId,
        title: "Constructor declared on a MonoBehaviour",
        messageFormat: "'{0}' declares a constructor. The engine constructs components itself, and a constructor runs before the component is attached, so GameObject, Transform and serialized field values are not available yet. Move the work to OnEnable, Awake or Start.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A MonoBehaviour is constructed by AddComponent and by deserialization, both of which set the object up after the constructor has already run. Field initializers and constructor bodies therefore see an unattached component, and anything they assign is overwritten when serialized values are applied.");

    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        NoParameterlessConstructorId,
        title: "MonoBehaviour cannot be constructed",
        messageFormat: "'{0}' has no public parameterless constructor. The engine creates components with no arguments, so it can never be added to a GameObject, loaded from a scene, or dragged onto the inspector.",
        category: "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The engine creates components with no arguments. A MonoBehaviour whose only constructors take parameters cannot be added to a GameObject, loaded from a scene, or dragged onto the inspector.");

    public static readonly DiagnosticDescriptor RunsBeforeAttach = new(
        RunsBeforeAttachId,
        title: "Field initializer on a MonoBehaviour calls into game or engine code",
        messageFormat: "This initializer runs in '{0}'s constructor, while the scene is still being deserialized. Nothing has been attached and no Start or OnEnable has run anywhere yet, so anything it reaches for may not be set up. Move it to OnEnable or Start unless the call stands entirely on its own.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A field initializer is compiled into the constructor, so it runs while the scene is being loaded, before any component is attached and before any lifecycle method anywhere has run. Calling game or engine code from there sees the world half built. Some such calls are pure and perfectly safe, which is why this is a warning rather than a refusal.");

    public static readonly DiagnosticDescriptor InvokesDelegate = new(
        InvokesDelegateId,
        title: "Field initializer on a MonoBehaviour invokes a delegate",
        messageFormat: "This initializer invokes a delegate in '{0}'s constructor, which runs while the scene is still being deserialized. A delegate is only as set up as whoever assigned it, and nothing has run yet to assign this one, so it is still null. Assign the field in OnEnable or Start instead.",
        category: "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Field initializers are compiled into the constructor, which the engine runs while loading a scene, before any Start or OnEnable anywhere. A delegate is assigned by other code at run time, so at that point it is almost always null and invoking it throws out of scene loading.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DeclaredConstructor, NoParameterlessConstructor, RunsBeforeAttach, InvokesDelegate);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var monoBehaviour = start.Compilation.GetTypeByMetadataName(MonoBehaviourMetadataName);
            if (monoBehaviour is null) return; // no reference to Prowl.Runtime in this compilation

            start.RegisterSymbolAction(ctx => Analyze(ctx, monoBehaviour), SymbolKind.NamedType);

            start.RegisterOperationAction(ctx => AnalyzeInitializer(ctx, monoBehaviour, start.Compilation.Assembly),
                OperationKind.FieldInitializer, OperationKind.PropertyInitializer);
        });
    }

    private static void Analyze(SymbolAnalysisContext ctx, INamedTypeSymbol monoBehaviour)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        if (type.TypeKind != TypeKind.Class || !DerivesFrom(type, monoBehaviour)) return;

        // MonoBehaviour itself declares one, and it is the base every component chains through.
        if (SymbolEqualityComparer.Default.Equals(type, monoBehaviour)) return;

        var declared = type.InstanceConstructors
            .Where(c => !c.IsImplicitlyDeclared)
            .ToArray();

        foreach (var constructor in declared)
        {
            foreach (var reference in constructor.DeclaringSyntaxReferences)
                ctx.ReportDiagnostic(Diagnostic.Create(DeclaredConstructor, reference.GetSyntax().GetLocation(), type.Name));
        }

        // An abstract component is never constructed directly, so only its derived types have to
        // answer for being creatable.
        if (type.IsAbstract || declared.Length == 0) return;

        bool creatable = type.InstanceConstructors.Any(
            c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        if (creatable) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            NoParameterlessConstructor, type.Locations.FirstOrDefault() ?? Location.None, type.Name));
    }

    /// <summary>
    /// Flags an initializer that calls out to something. A constant or a plain object is harmless;
    /// an invocation is what reaches for a system the scene has not stood up yet.
    /// </summary>
    private static void AnalyzeInitializer(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour,
                                           IAssemblySymbol ownAssembly)
    {
        var initializer = (ISymbolInitializerOperation)ctx.Operation;
        if (!DerivesFrom(ctx.ContainingSymbol?.ContainingType, monoBehaviour)) return;

        // A static field is not part of constructing the component, so it is nothing to do with this.
        if (InitializedSymbols(initializer).Any(symbol => symbol.IsStatic)) return;

        var calls = initializer.Value.Descendants().Prepend(initializer.Value)
            .OfType<IInvocationOperation>()
            .ToArray();

        // A delegate cannot be defended: nothing has run yet to assign it, so it is still null.
        // Anything else reaching into game or engine code is suspect but may well be pure.
        DiagnosticDescriptor? rule =
            calls.Any(call => call.TargetMethod.MethodKind == MethodKind.DelegateInvoke) ? InvokesDelegate
            : calls.Any(call => ReachesIntoTheWorld(call.TargetMethod, ownAssembly)) ? RunsBeforeAttach
            : null;

        if (rule is null) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            rule, initializer.Value.Syntax.GetLocation(),
            ctx.ContainingSymbol!.ContainingType.Name));
    }

    /// <summary>
    /// Whether a call reaches into game or engine code rather than standing on its own. Framework
    /// helpers are left alone, since they do not care how far through loading a scene is.
    /// </summary>
    private static bool ReachesIntoTheWorld(IMethodSymbol method, IAssemblySymbol ownAssembly)
    {
        IAssemblySymbol? from = method.ContainingAssembly;
        if (from is null) return false;

        if (SymbolEqualityComparer.Default.Equals(from, ownAssembly)) return true;

        string name = from.Name;
        return name == "Prowl.Runtime" || name.StartsWith("Prowl.", System.StringComparison.Ordinal);
    }

    private static IEnumerable<ISymbol> InitializedSymbols(ISymbolInitializerOperation initializer) =>
        initializer switch
        {
            IFieldInitializerOperation field => field.InitializedFields,
            IPropertyInitializerOperation property => property.InitializedProperties,
            _ => Enumerable.Empty<ISymbol>(),
        };

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol monoBehaviour)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, monoBehaviour))
                return true;
        return false;
    }
}
