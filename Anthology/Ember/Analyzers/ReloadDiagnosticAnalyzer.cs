using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Prowl.Ember.Analyzers;

/// <summary>
/// Compile time rules for code that will not survive hot reload. One analyzer carries every rule, because they
/// share the same symbol walk.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReloadDiagnosticAnalyzer : DiagnosticAnalyzer
{
    private const string IgnoreAttribute = "Prowl.Ember.ReloadIgnoreAttribute";
    private const string InitializerAttribute = "Prowl.Ember.ReloadInitializerAttribute";
    private const string AwareInterface = "Prowl.Ember.IReloadAware";
    private const string ObserverInterface = "Prowl.Ember.IReloadObserver";

    private const string Category = "HotReload";

    public static readonly DiagnosticDescriptor StaticOnGenericType = new(
        "EMBA001",
        "Static member of a generic type will not survive hot reload",
        "Static member '{0}' of generic type '{1}' cannot be migrated and resets on reload. Add [ReloadIgnore], or move it out of the generic type.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "The statics of an open generic type cannot be enumerated, so hot reload never sees them.");

    public static readonly DiagnosticDescriptor UnmigratableField = new(
        "EMBA002",
        "Field type cannot be migrated by hot reload",
        "Field '{0}' has type '{1}', which hot reload skips silently. Add [ReloadIgnore] to make that explicit.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Pointers, function pointers, and ref struct fields are left at their default with no runtime diagnostic.");

    public static readonly DiagnosticDescriptor InitializerTargetInvalid = new(
        "EMBA003",
        "[ReloadInitializer] target is not usable",
        "'{0}' is not a parameterless, non-generic instance method on '{1}', so field '{2}' cannot be initialized on reload",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "The named method is resolved at reload time, which is far too late to discover it is missing.");

    public static readonly DiagnosticDescriptor IgnoredTypeWithHooks = new(
        "EMBA005",
        "[ReloadIgnore] type implements reload hooks",
        "Type '{0}' is marked [ReloadIgnore], so it is never visited and its {1} members are never called",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "An ignored type is skipped entirely, including its lifecycle hooks.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        StaticOnGenericType, UnmigratableField, InitializerTargetInvalid, IgnoredTypeWithHooks);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Field, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!HasAttribute(type, IgnoreAttribute)) return;

        var implemented = type.AllInterfaces
            .Where(x => x.ToDisplayString() is AwareInterface or ObserverInterface)
            .Select(x => x.Name)
            .ToArray();

        if (implemented.Length == 0) return;

        context.ReportDiagnostic(Diagnostic.Create(IgnoredTypeWithHooks,
            type.Locations.FirstOrDefault(), type.Name, string.Join(" and ", implemented)));
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        if (symbol is IFieldSymbol field)
        {
            ReportUnmigratableField(context, field);
            ReportInvalidInitializer(context, field);
        }

        ReportStaticOnGenericType(context, symbol);
    }

    private static void ReportStaticOnGenericType(SymbolAnalysisContext context, ISymbol symbol)
    {
        if (!symbol.IsStatic) return;
        if (symbol.ContainingType is not { IsGenericType: true } containing) return;

        switch (symbol)
        {
            case IFieldSymbol { IsConst: true }:
                return; // a const is never migrated in the first place
            case IFieldSymbol { AssociatedSymbol: IPropertySymbol }:
                return; // reported through the property instead
            case IPropertySymbol property when !IsAutoProperty(property):
                return; // no stored state to lose
        }

        if (HasAttribute(symbol, IgnoreAttribute)) return;

        context.ReportDiagnostic(Diagnostic.Create(StaticOnGenericType,
            symbol.Locations.FirstOrDefault(), symbol.Name, containing.Name));
    }

    private static void ReportUnmigratableField(SymbolAnalysisContext context, IFieldSymbol field)
    {
        if (field.IsConst || field.IsStatic) return;
        if (HasAttribute(field, IgnoreAttribute)) return;
        if (field.AssociatedSymbol is IPropertySymbol property && HasAttribute(property, IgnoreAttribute)) return;

        bool unmigratable = field.Type.TypeKind == TypeKind.Pointer
                            || field.Type.TypeKind == TypeKind.FunctionPointer
                            || field.Type.IsRefLikeType;

        if (!unmigratable) return;

        context.ReportDiagnostic(Diagnostic.Create(UnmigratableField,
            field.Locations.FirstOrDefault(), field.Name, field.Type.ToDisplayString()));
    }

    private static void ReportInvalidInitializer(SymbolAnalysisContext context, IFieldSymbol field)
    {
        var attribute = field.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == InitializerAttribute);

        if (attribute == null) return;
        if (attribute.ConstructorArguments.Length != 1) return;
        if (attribute.ConstructorArguments[0].Value is not string methodName) return; // null means "leave it alone"

        bool usable = field.ContainingType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Any(m => !m.IsStatic && m.Parameters.Length == 0 && !m.IsGenericMethod);

        if (usable) return;

        context.ReportDiagnostic(Diagnostic.Create(InitializerTargetInvalid,
            field.Locations.FirstOrDefault(), methodName, field.ContainingType.Name, field.Name));
    }

    private static bool IsAutoProperty(IPropertySymbol property)
        => property.ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));

    private static bool HasAttribute(ISymbol symbol, string attributeName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName);
}
