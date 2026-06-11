// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Prowl.Generators;

[Generator]
public class EventDomainGenerator : IIncrementalGenerator
{
    private const string EventDomainAttrFqn = "Prowl.Runtime.Events.EventDomainAttribute";
    private const string EventArgsAttrFqn = "Prowl.Runtime.Events.EventArgsAttribute";
    private const string EventKeyFqn = "global::Prowl.Runtime.Events.EventKey";
    private const string UnitFqn = "global::Prowl.Runtime.Events.Unit";

    private static readonly DiagnosticDescriptor s_notPartialDiag = new(
        id: "PEVT0001",
        title: "EventDomain class must be partial",
        messageFormat: "The class '{0}' is marked with [EventDomain] but is not declared as 'partial'. Add the 'partial' modifier.",
        category: "Prowl",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EventDomainAttrFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetDomainInfo(ctx, ct))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(classDeclarations,
            static (spc, domain) => Execute(spc, domain!.Value));
    }

    #region Data model (value-equatable for incremental caching)

    private struct EventKeyInfo : IEquatable<EventKeyInfo>
    {
        public string Name;
        public string ArgsTypeFqn;
        public bool IsUnit;

        public bool Equals(EventKeyInfo other)
            => Name == other.Name
            && ArgsTypeFqn == other.ArgsTypeFqn
            && IsUnit == other.IsUnit;

        public override bool Equals(object? obj) => obj is EventKeyInfo o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Name?.GetHashCode() ?? 0);
                h = h * 31 + (ArgsTypeFqn?.GetHashCode() ?? 0);
                h = h * 31 + IsUnit.GetHashCode();
                return h;
            }
        }
    }

    private struct ContainingTypeInfo : IEquatable<ContainingTypeInfo>
    {
        public string Keyword;       // "class", "struct", "record class", etc.
        public string Name;
        public string Accessibility;  // "public", "internal", etc.
        public bool IsStatic;

        public bool Equals(ContainingTypeInfo other)
            => Keyword == other.Keyword
            && Name == other.Name
            && Accessibility == other.Accessibility
            && IsStatic == other.IsStatic;

        public override bool Equals(object? obj) => obj is ContainingTypeInfo o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Keyword?.GetHashCode() ?? 0);
                h = h * 31 + (Name?.GetHashCode() ?? 0);
                h = h * 31 + (Accessibility?.GetHashCode() ?? 0);
                h = h * 31 + IsStatic.GetHashCode();
                return h;
            }
        }
    }

    private struct EventDomainInfo : IEquatable<EventDomainInfo>
    {
        public string? Namespace;
        public string ClassName;
        public string ClassAccessibility;
        public bool IsStatic;
        public bool IsGlobal;
        public bool IsPartial;
        public EquatableArray<ContainingTypeInfo> ContainingTypes;
        public EquatableArray<EventKeyInfo> Events;
        public Location? DiagnosticLocation;

        public bool Equals(EventDomainInfo other)
            => Namespace == other.Namespace
            && ClassName == other.ClassName
            && ClassAccessibility == other.ClassAccessibility
            && IsStatic == other.IsStatic
            && IsGlobal == other.IsGlobal
            && IsPartial == other.IsPartial
            && ContainingTypes.Equals(other.ContainingTypes)
            && Events.Equals(other.Events);

        public override bool Equals(object? obj) => obj is EventDomainInfo o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Namespace?.GetHashCode() ?? 0);
                h = h * 31 + (ClassName?.GetHashCode() ?? 0);
                h = h * 31 + (ClassAccessibility?.GetHashCode() ?? 0);
                h = h * 31 + IsStatic.GetHashCode();
                h = h * 31 + IsGlobal.GetHashCode();
                h = h * 31 + IsPartial.GetHashCode();
                h = h * 31 + ContainingTypes.GetHashCode();
                h = h * 31 + Events.GetHashCode();
                return h;
            }
        }
    }

    #endregion

    private static EventDomainInfo? GetDomainInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

        bool isPartial = classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        bool isStatic = classSymbol.IsStatic;

        // Extract Global property from [EventDomain] attribute
        bool isGlobal = false;
        foreach (var attrData in classSymbol.GetAttributes())
        {
            if (attrData.AttributeClass?.ToDisplayString() == "Prowl.Runtime.Events.EventDomainAttribute")
            {
                foreach (var namedArg in attrData.NamedArguments)
                {
                    if (namedArg.Key == "Global" && namedArg.Value.Value is bool g)
                        isGlobal = g;
                }
                break;
            }
        }

        // Collect EventKey fields with optional [EventArgs]
        var events = new List<EventKeyInfo>();
        foreach (var member in classSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is IFieldSymbol field
                && field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EventKeyFqn)
            {
                string argsType = UnitFqn; // default to Unit if no [EventArgs]
                foreach (var attr in field.GetAttributes())
                {
                    if (attr.AttributeClass?.ToDisplayString() == "Prowl.Runtime.Events.EventArgsAttribute"
                        && attr.ConstructorArguments.Length == 1
                        && attr.ConstructorArguments[0].Value is ITypeSymbol typeArg
                        && typeArg.TypeKind != TypeKind.Error)
                    {
                        argsType = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        break;
                    }
                }

                bool isUnit = argsType == UnitFqn;
                string eventName = field.Name.StartsWith("_") ? field.Name.Substring(1) : field.Name;
                events.Add(new EventKeyInfo
                {
                    Name = eventName,
                    ArgsTypeFqn = argsType,
                    IsUnit = isUnit,
                });
            }
        }

        if (events.Count == 0 && isPartial)
            return null; // nothing to generate

        // Collect containing type chain for nested classes
        var containingTypes = new List<ContainingTypeInfo>();
        var parent = classSymbol.ContainingType;
        while (parent is not null)
        {
            containingTypes.Insert(0, new ContainingTypeInfo
            {
                Keyword = parent.IsRecord ? "record class" : "class",
                Name = parent.Name,
                Accessibility = AccessibilityToString(parent.DeclaredAccessibility),
                IsStatic = parent.IsStatic,
            });
            parent = parent.ContainingType;
        }

        string? ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new EventDomainInfo
        {
            Namespace = ns,
            ClassName = classSymbol.Name,
            ClassAccessibility = AccessibilityToString(classSymbol.DeclaredAccessibility),
            IsStatic = isStatic,
            IsGlobal = isGlobal,
            IsPartial = isPartial,
            ContainingTypes = new EquatableArray<ContainingTypeInfo>(containingTypes.ToArray()),
            Events = new EquatableArray<EventKeyInfo>(events.ToArray()),
            DiagnosticLocation = classDecl.Identifier.GetLocation(),
        };
    }

    private static void Execute(SourceProductionContext spc, EventDomainInfo domain)
    {
        // Emit diagnostic if class is not partial
        if (!domain.IsPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                s_notPartialDiag,
                domain.DiagnosticLocation,
                domain.ClassName));
            return;
        }

        if (domain.Events.Length == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by Prowl.Runtime.Generators — do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (domain.Namespace is not null)
        {
            sb.AppendLine($"namespace {domain.Namespace}");
            sb.AppendLine("{");
        }

        string indent = domain.Namespace is not null ? "    " : "";

        // Open containing types
        foreach (var ct in domain.ContainingTypes)
        {
            string staticMod = ct.IsStatic ? " static" : "";
            sb.AppendLine($"{indent}{ct.Accessibility}{staticMod} partial {ct.Keyword} {ct.Name}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        // Open the domain class
        string classMods = domain.IsStatic ? " static" : "";
        sb.AppendLine($"{indent}{domain.ClassAccessibility}{classMods} partial class {domain.ClassName}");
        sb.AppendLine($"{indent}{{");
        string ci = indent + "    "; // content indent

        // --- Generate enum ---
        sb.AppendLine($"{ci}/// <summary>Auto-generated enum backing the event keys in this domain.</summary>");
        sb.AppendLine($"{ci}public enum EventTypes");
        sb.AppendLine($"{ci}{{");
        foreach (var evt in domain.Events)
        {
            sb.AppendLine($"{ci}    [global::Prowl.Runtime.Events.EventArgs(typeof({evt.ArgsTypeFqn}))]");
            sb.AppendLine($"{ci}    {evt.Name},");
        }
        sb.AppendLine($"{ci}}}");
        sb.AppendLine();

        // --- Generate manager ---
        string globalArg = domain.IsGlobal ? "global: true" : "";
        string memberStatic = domain.IsStatic ? " static" : "";
        string managerField = domain.IsStatic ? "s_eventManager" : "_eventManager";
        string fieldDecl = domain.IsStatic ? "private static readonly" : "private readonly";
        sb.AppendLine($"{ci}{fieldDecl} global::Prowl.Runtime.Events.EventManager<EventTypes> {managerField} = new({globalArg});");
        sb.AppendLine();
        sb.AppendLine($"{ci}/// <summary>Gets the <see cref=\"global::Prowl.Runtime.Events.EventManager{{T}}\"/> for this event domain. For instance domains, dispose this when the owner is no longer needed.</summary>");
        sb.AppendLine($"{ci}public{memberStatic} global::Prowl.Runtime.Events.EventManager<EventTypes> Manager => {managerField};");
        sb.AppendLine();

        // --- Generate event accessor properties for += / -= subscription and .Invoke() ---
        foreach (var evt in domain.Events)
        {
            string argsType = evt.ArgsTypeFqn;

            if (evt.IsUnit)
            {
                sb.AppendLine($"{ci}/// <summary>Access <see cref=\"EventTypes.{evt.Name}\"/> using += / -= to subscribe, or .Invoke() to fire. For priority/tags/source capture or IDisposable container, use <c>{evt.Name}.Subscribe(...)</c> on this accessor.</summary>");
                sb.AppendLine($"{ci}public{memberStatic} global::Prowl.Runtime.Events.EventAccessor<EventTypes> {evt.Name}");
                sb.AppendLine($"{ci}{{");
                sb.AppendLine($"{ci}    get => new({managerField}, EventTypes.{evt.Name});");
                sb.AppendLine($"{ci}    set {{ }}");
                sb.AppendLine($"{ci}}}");
            }
            else
            {
                sb.AppendLine($"{ci}/// <summary>Access <see cref=\"EventTypes.{evt.Name}\"/> using += / -= to subscribe, or .Invoke({argsType}) to fire. For priority/tags/source capture or IDisposable container, use <c>{evt.Name}.Subscribe(...)</c> on this accessor.</summary>");
                sb.AppendLine($"{ci}public{memberStatic} global::Prowl.Runtime.Events.EventAccessor<EventTypes, {argsType}> {evt.Name}");
                sb.AppendLine($"{ci}{{");
                sb.AppendLine($"{ci}    get => new({managerField}, EventTypes.{evt.Name});");
                sb.AppendLine($"{ci}    set {{ }}");
                sb.AppendLine($"{ci}}}");
            }
        }
        sb.AppendLine();

        // --- Generate per-event convenience methods ---
        foreach (var evt in domain.Events)
        {
            string argsType = evt.ArgsTypeFqn;

            sb.AppendLine($"{ci}// --- {evt.Name} ---");
            sb.AppendLine();

            if (evt.IsUnit)
            {
                EmitUnitMethods(sb, ci, evt.Name, memberStatic, managerField);
            }
            else
            {
                EmitTypedMethods(sb, ci, evt.Name, argsType, memberStatic, managerField);
            }
        }

        // --- Generate tag-based management methods ---
        sb.AppendLine($"{ci}/// <summary>Enables all handlers in this domain that have the specified tag.</summary>");
        sb.AppendLine($"{ci}public{memberStatic} void EnableByTag(string tag) => {managerField}.EnableByTag(tag);");
        sb.AppendLine();
        sb.AppendLine($"{ci}/// <summary>Disables all handlers in this domain that have the specified tag.</summary>");
        sb.AppendLine($"{ci}public{memberStatic} void DisableByTag(string tag) => {managerField}.DisableByTag(tag);");
        sb.AppendLine();
        sb.AppendLine($"{ci}/// <summary>Removes all handlers in this domain that have the specified tag.</summary>");
        sb.AppendLine($"{ci}public{memberStatic} void RemoveByTag(string tag) => {managerField}.RemoveByTag(tag);");
        sb.AppendLine();
        sb.AppendLine($"{ci}/// <summary>Enables all handlers across all global managers of this domain that have the specified tag.</summary>");
        sb.AppendLine($"{ci}public static void GlobalEnableByTag(string tag) => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalEnableByTag(tag);");
        sb.AppendLine();
        sb.AppendLine($"{ci}/// <summary>Disables all handlers across all global managers of this domain that have the specified tag.</summary>");
        sb.AppendLine($"{ci}public static void GlobalDisableByTag(string tag) => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalDisableByTag(tag);");
        sb.AppendLine();
        sb.AppendLine($"{ci}/// <summary>Removes all handlers across all global managers of this domain that have the specified tag.</summary>");
        sb.AppendLine($"{ci}public static void GlobalRemoveByTag(string tag) => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalRemoveByTag(tag);");
        sb.AppendLine();

        // Close domain class
        sb.AppendLine($"{indent}}}");

        // Close containing types
        for (int i = domain.ContainingTypes.Length - 1; i >= 0; i--)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.AppendLine($"{indent}}}");
        }

        // Close namespace
        if (domain.Namespace is not null)
            sb.AppendLine("}");

        string hintName = domain.ContainingTypes.Length > 0
            ? string.Join(".", domain.ContainingTypes.Select(c => c.Name)) + "." + domain.ClassName + ".g.cs"
            : domain.ClassName + ".g.cs";

        spc.AddSource(hintName, sb.ToString());
    }

    private static void EmitUnitMethods(StringBuilder sb, string ci, string name, string memberStatic, string managerField)
    {
        // Invoke (parameterless)
        sb.AppendLine($"{ci}/// <summary>Invokes <see cref=\"EventTypes.{name}\"/> on this domain's manager.</summary>");
        sb.AppendLine($"{ci}[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{ci}public{memberStatic} void Invoke{name}()");
        sb.AppendLine($"{ci}    => {managerField}.InvokeEvent(EventTypes.{name});");
        sb.AppendLine();

        // InvokeAsync (parameterless)
        sb.AppendLine($"{ci}/// <summary>Asynchronously invokes <see cref=\"EventTypes.{name}\"/> on this domain's manager, awaiting async handlers.</summary>");
        sb.AppendLine($"{ci}public{memberStatic} global::System.Threading.Tasks.Task Invoke{name}Async()");
        sb.AppendLine($"{ci}    => {managerField}.InvokeEventAsync(EventTypes.{name});");
        sb.AppendLine();

        // GlobalInvoke (parameterless) — always static
        sb.AppendLine($"{ci}/// <summary>Invokes <see cref=\"EventTypes.{name}\"/> across all global managers of this domain.</summary>");
        sb.AppendLine($"{ci}[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{ci}public static void GlobalInvoke{name}()");
        sb.AppendLine($"{ci}    => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalInvokeEvent(EventTypes.{name});");
        sb.AppendLine();

        // GlobalInvokeAsync (parameterless) — always static
        sb.AppendLine($"{ci}/// <summary>Asynchronously invokes <see cref=\"EventTypes.{name}\"/> across all global managers of this domain, awaiting async handlers.</summary>");
        sb.AppendLine($"{ci}public static global::System.Threading.Tasks.Task GlobalInvoke{name}Async()");
        sb.AppendLine($"{ci}    => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalInvokeEventAsync(EventTypes.{name});");
        sb.AppendLine();
    }

    private static void EmitTypedMethods(StringBuilder sb, string ci, string name, string argsType, string memberStatic, string managerField)
    {
        // Invoke (typed)
        sb.AppendLine($"{ci}/// <summary>Invokes <see cref=\"EventTypes.{name}\"/> on this domain's manager.</summary>");
        sb.AppendLine($"{ci}[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{ci}public{memberStatic} void Invoke{name}({argsType} args)");
        sb.AppendLine($"{ci}    => {managerField}.InvokeEvent(EventTypes.{name}, args);");
        sb.AppendLine();

        // InvokeAsync (typed)
        sb.AppendLine($"{ci}/// <summary>Asynchronously invokes <see cref=\"EventTypes.{name}\"/> on this domain's manager, awaiting async handlers.</summary>");
        sb.AppendLine($"{ci}public{memberStatic} global::System.Threading.Tasks.Task Invoke{name}Async({argsType} args)");
        sb.AppendLine($"{ci}    => {managerField}.InvokeEventAsync(EventTypes.{name}, args);");
        sb.AppendLine();

        // GlobalInvoke (typed) — always static
        sb.AppendLine($"{ci}/// <summary>Invokes <see cref=\"EventTypes.{name}\"/> across all global managers of this domain.</summary>");
        sb.AppendLine($"{ci}[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{ci}public static void GlobalInvoke{name}({argsType} args)");
        sb.AppendLine($"{ci}    => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalInvokeEvent(EventTypes.{name}, args);");
        sb.AppendLine();

        // GlobalInvokeAsync (typed) — always static
        sb.AppendLine($"{ci}/// <summary>Asynchronously invokes <see cref=\"EventTypes.{name}\"/> across all global managers of this domain, awaiting async handlers.</summary>");
        sb.AppendLine($"{ci}public static global::System.Threading.Tasks.Task GlobalInvoke{name}Async({argsType} args)");
        sb.AppendLine($"{ci}    => global::Prowl.Runtime.Events.EventManager<EventTypes>.GlobalInvokeEventAsync(EventTypes.{name}, args);");
        sb.AppendLine();
    }

    private static string AccessibilityToString(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "internal",
    };
}
