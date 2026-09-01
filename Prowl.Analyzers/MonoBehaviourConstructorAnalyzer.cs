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
        title: "Field initializer on a MonoBehaviour calls something",
        messageFormat: "This initializer runs in '{0}'s constructor, before the component is attached and before any Start or OnEnable has run anywhere in the scene. Anything it reaches for is very likely not set up yet. Assign it in OnEnable or Start instead.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A field initializer is compiled into the constructor, so it runs while the scene is still being deserialized. Calling into other systems from there sees them before they have been initialised, and whatever it assigns is overwritten when serialized values are applied.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DeclaredConstructor, NoParameterlessConstructor, RunsBeforeAttach);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var monoBehaviour = start.Compilation.GetTypeByMetadataName(MonoBehaviourMetadataName);
            if (monoBehaviour is null) return; // no reference to Prowl.Runtime in this compilation

            start.RegisterSymbolAction(ctx => Analyze(ctx, monoBehaviour), SymbolKind.NamedType);

            start.RegisterOperationAction(ctx => AnalyzeInitializer(ctx, monoBehaviour),
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
    private static void AnalyzeInitializer(OperationAnalysisContext ctx, INamedTypeSymbol monoBehaviour)
    {
        var initializer = (ISymbolInitializerOperation)ctx.Operation;
        if (!DerivesFrom(ctx.ContainingSymbol?.ContainingType, monoBehaviour)) return;

        // A static field is not part of constructing the component, so it is nothing to do with this.
        if (InitializedSymbols(initializer).Any(symbol => symbol.IsStatic)) return;

        if (!initializer.Value.Descendants().Prepend(initializer.Value).Any(op => op is IInvocationOperation))
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            RunsBeforeAttach, initializer.Value.Syntax.GetLocation(),
            ctx.ContainingSymbol!.ContainingType.Name));
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
