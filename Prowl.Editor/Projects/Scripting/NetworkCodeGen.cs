// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Prowl.Runtime.Networking;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Rewrites game scripts before they compile so <c>[Sync]</c> properties and <c>[Rpc.*]</c> methods
/// network themselves, the way s&amp;box does: written as plain C#, no base class beyond <c>Component</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each member is rewritten where it stands. A <c>[Sync]</c> auto-property gets a backing field and a setter
/// that goes through <c>Component.__Sync_Set</c>; an RPC gets a prologue that sends the call and, unless it
/// should also run here, returns. Each also gets a static descriptor (<c>SyncMember</c>, <c>RpcMember</c>)
/// with typed accessors, which the runtime finds by reflection. Nothing is gathered per type, so a partial
/// class can spread its network members over any number of files, and everything generated stays in the
/// file it describes, where that file's usings apply.
/// </para>
/// <para>
/// Everything added lands on lines that already exist, so every line keeps its number: compiler
/// errors, stack traces and breakpoints still point at the code as written. Attributes are matched by
/// name, before binding, so this costs nothing for files that use neither. What takes binding (that the
/// class is a <c>Component</c>, that a value can be sent, that ids don't collide) <see cref="Validate"/>
/// checks once the compilation is built.
/// </para>
/// </remarks>
internal static class NetworkCodeGen
{
    private static readonly DiagnosticDescriptor s_syncShape = new(
        "PROWLNET001", "[Sync] needs an auto-property",
        "[Sync] property '{0}' must be a non-static auto-property with get and set accessors, like { get; set; }",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_rpcShape = new(
        "PROWLNET002", "RPC shape not supported",
        "RPC '{0}' must be a non-static, non-generic method returning void, with a body and no ref, out or params parameters",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_notInClass = new(
        "PROWLNET003", "Network member outside a component",
        "'{0}' is networked, which only works on a Component class",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_collision = new(
        "PROWLNET004", "Network id collision",
        "'{0}' and '{1}' hash to the same network id. Rename one of them.",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsendable = new(
        "PROWLNET005", "Value can't be sent over the network",
        "'{0}' is a {1}, which can't be sent over the network. Use one of the types listed on NetSerializer<T>.",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private const string SyncFlagsType = "global::Prowl.Runtime.Networking.SyncFlags";
    private const string RpcFlagsType = "global::Prowl.Runtime.Networking.RpcFlags";
    private const string RpcModeType = "global::Prowl.Runtime.Networking.RpcMode";
    private const string Serializer = "global::Prowl.Runtime.Networking.NetSerializer";
    private const string WriterType = "global::Prowl.Warp.NetWriter";
    private const string ReaderType = "global::Prowl.Warp.NetReader";

    // Hot reload would carry the previous assembly's descriptor over; the new one's own initializer builds the right one.
    private const string Descriptor = "[global::Prowl.Ember.ReloadIgnore] private static readonly";

    /// <summary>The trees of one assembly after the rewrite, in the order given, and what was wrong with them.</summary>
    public sealed record Result(IReadOnlyList<SyntaxTree> Trees, IReadOnlyList<Diagnostic> Diagnostics);

    // Each tree's rewrite depends on nothing but the tree, so an unchanged file gets back the very same
    // rewritten tree, and the compiler's incremental reuse holds.
    private static readonly ConditionalWeakTable<SyntaxTree, Result> s_cache = new();

    // Whether each tree mentions Sync or Rpc at all, so an unchanged file is only scanned once.
    private static readonly ConditionalWeakTable<SyntaxTree, StrongBox<bool>> s_mayHaveNetworking = new();

    /// <summary>Rewrites one assembly's trees. Trees with nothing to rewrite come back as they are.</summary>
    public static Result Rewrite(IReadOnlyList<SyntaxTree> trees)
    {
        SyntaxTree[] output = new SyntaxTree[trees.Count];
        List<Diagnostic> diagnostics = new();
        for (int i = 0; i < trees.Count; i++)
        {
            Result rewritten = MayHaveNetworking(trees[i]) ? s_cache.GetValue(trees[i], RewriteTree) : new([trees[i]], []);
            output[i] = rewritten.Trees[0];
            diagnostics.AddRange(rewritten.Diagnostics);
        }
        return new Result(output, diagnostics);
    }

    private static Result RewriteTree(SyntaxTree tree)
    {
        Rewriter rewriter = new();
        SyntaxNode root = rewriter.Visit(tree.GetRoot());
        return new Result([rewriter.Changed ? tree.WithRootAndOptions(root, tree.Options) : tree], rewriter.Diagnostics);
    }

    /// <summary>
    /// What <see cref="Rewrite"/> can't tell from syntax alone: that each networked member is on a
    /// <c>Component</c>, that every [Sync] and RPC argument type can be sent, and that no two of a type's
    /// members share an id.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Validate(Compilation compilation)
    {
        List<Diagnostic> diagnostics = new();
        INamedTypeSymbol? component = compilation.GetTypeByMetadataName("Prowl.Runtime.Component");
        INamedTypeSymbol? gameObject = compilation.GetTypeByMetadataName("Prowl.Runtime.GameObject");
        if (component == null || gameObject == null)
            return diagnostics;

        Dictionary<ITypeSymbol, bool> sendable = new(SymbolEqualityComparer.Default);
        Dictionary<(INamedTypeSymbol Type, bool Rpc, uint Id), string> ids = new();
        foreach (SyntaxTree tree in compilation.SyntaxTrees.Where(MayHaveNetworking))
        {
            SemanticModel? model = null;
            foreach (MemberDeclarationSyntax member in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (member is not (PropertyDeclarationSyntax or MethodDeclarationSyntax) || FindAttribute(member.AttributeLists, out string? kind) == null
                    || member.Parent is not ClassDeclarationSyntax)
                    continue;

                model ??= compilation.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(member) is not { } symbol)
                    continue;

                SyntaxToken name = member is PropertyDeclarationSyntax p ? p.Identifier : ((MethodDeclarationSyntax)member).Identifier;
                if (!DerivesFrom(symbol.ContainingType, component))
                {
                    diagnostics.Add(Diagnostic.Create(s_notInClass, name.GetLocation(), name.Text));
                    continue;
                }

                IEnumerable<(ITypeSymbol Type, string Name, Location Location)> values = symbol switch
                {
                    IPropertySymbol property when kind == "Sync" => [(property.Type, property.Name, name.GetLocation())],
                    IMethodSymbol method when kind != "Sync" => method.Parameters.Select(a => (a.Type, a.Name, a.Locations.FirstOrDefault() ?? name.GetLocation())),
                    _ => [],
                };
                foreach ((ITypeSymbol type, string valueName, Location location) in values)
                {
                    if (!sendable.TryGetValue(type, out bool ok))
                        sendable[type] = ok = IsSendable(type, component, gameObject);
                    if (!ok)
                        diagnostics.Add(Diagnostic.Create(s_unsendable, location, valueName, type.ToDisplayString()));
                }

                // The same key the rewrite hashed: a property's name, a method's name and parameter types as written.
                string key = member switch
                {
                    PropertyDeclarationSyntax => name.Text,
                    MethodDeclarationSyntax m => $"{name.Text}({string.Join(",", m.ParameterList.Parameters.Select(x => x.Type == null ? "" : OneLine(x.Type)))})",
                    _ => name.Text,
                };
                var id = (symbol.ContainingType, kind != "Sync", NetworkId(key));
                if (!ids.TryAdd(id, key) && ids[id] != key)
                    diagnostics.Add(Diagnostic.Create(s_collision, name.GetLocation(), key, ids[id]));
            }
        }
        return diagnostics;
    }

    private static readonly SymbolDisplayFormat s_runtimeName = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    /// <summary>Whether <c>NetSerializer&lt;T&gt;</c> handles the type, asked of the runtime itself where it can be.</summary>
    private static bool IsSendable(ITypeSymbol type, INamedTypeSymbol component, INamedTypeSymbol gameObject)
    {
        if (type.TypeKind == TypeKind.Enum || DerivesFrom(type, component) || DerivesFrom(type, gameObject))
            return true;

        // Nothing else the serializer takes is generic or nested.
        if (type is not INamedTypeSymbol { IsGenericType: false, ContainingType: null, ContainingAssembly: { } assembly } named)
            return false;

        string name = named.ToDisplayString(s_runtimeName);
        Type? runtime = Type.GetType($"{name}, {assembly.Identity.Name}") ?? Type.GetType(name);
        return runtime != null && (bool)typeof(NetSerializer<>).MakeGenericType(runtime)
            .GetProperty(nameof(NetSerializer<int>.IsSupported))!.GetValue(null)!;
    }

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? t = type; t != null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        return false;
    }

    /// <summary>FNV-1a, so ids are stable across builds and machines.</summary>
    internal static uint NetworkId(string key)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }

    private static bool MayHaveNetworking(SyntaxTree tree) =>
        s_mayHaveNetworking.GetValue(tree, t =>
        {
            string text = t.GetText().ToString();
            return new StrongBox<bool>(text.Contains("Sync", StringComparison.Ordinal) || text.Contains("Rpc", StringComparison.Ordinal));
        }).Value!;

    // --- What a member networks --------------------------------------------------------------------

    private sealed record SyncInfo(string Name, string Type, uint Slot, string Flags, string? Initializer)
    {
        public string Field => "__sync_" + Name;

        public string Descriptor => "__syncnet_" + Name;
    }

    private sealed record RpcInfo(string Name, uint Id, string Mode, string Flags, IReadOnlyList<string> ParameterTypes)
    {
        public string Signature => $"{Name}({string.Join(",", ParameterTypes)})";

        public string Descriptor => $"__rpc_{Id}";
    }

    /// <summary>The [Sync] property's description, or null (with a reason in <paramref name="error"/> if it is misused).</summary>
    private static SyncInfo? AnalyzeSync(PropertyDeclarationSyntax node, AttributeSyntax attribute, out DiagnosticDescriptor? error)
    {
        error = null;
        if (node.Parent is not ClassDeclarationSyntax)
        {
            error = s_notInClass;
            return null;
        }

        AccessorListSyntax? accessors = node.AccessorList;
        bool isAuto = accessors != null && accessors.Accessors.Count == 2
            && accessors.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))
            && accessors.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))
            && accessors.Accessors.All(a => a.Body == null && a.ExpressionBody == null);
        if (!isAuto || node.Modifiers.Any(SyntaxKind.StaticKeyword) || node.Modifiers.Any(SyntaxKind.AbstractKeyword))
        {
            error = s_syncShape;
            return null;
        }

        string name = node.Identifier.Text;
        string flags = $"({SyncFlagsType})({ArgumentOrDefault(attribute, "Flags", $"{SyncFlagsType}.None")})";
        return new SyncInfo(name, OneLine(node.Type), NetworkId(name), flags, node.Initializer == null ? null : OneLine(node.Initializer.Value));
    }

    /// <summary>The RPC's description, or null (with a reason in <paramref name="error"/> if it is misused).</summary>
    private static RpcInfo? AnalyzeRpc(MethodDeclarationSyntax node, AttributeSyntax attribute, string kind, out DiagnosticDescriptor? error)
    {
        error = null;
        if (node.Parent is not ClassDeclarationSyntax)
        {
            error = s_notInClass;
            return null;
        }

        bool returnsVoid = node.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        bool plainParameters = node.ParameterList.Parameters.All(p =>
            !p.Modifiers.Any(SyntaxKind.RefKeyword) && !p.Modifiers.Any(SyntaxKind.OutKeyword) && !p.Modifiers.Any(SyntaxKind.ParamsKeyword)
            && !p.Modifiers.Any(SyntaxKind.ThisKeyword) && p.Type != null);
        if (!returnsVoid || !plainParameters || node.TypeParameterList != null || (node.Body == null && node.ExpressionBody == null)
            || node.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            error = s_rpcShape;
            return null;
        }

        List<string> types = node.ParameterList.Parameters.Select(p => OneLine(p.Type!)).ToList();
        string flags = $"({RpcFlagsType})({ArgumentOrDefault(attribute, "flags", $"{RpcFlagsType}.None")})";
        return new RpcInfo(node.Identifier.Text, NetworkId($"{node.Identifier.Text}({string.Join(",", types)})"), $"{RpcModeType}.{kind}", flags, types);
    }

    /// <summary>Rewrites one tree's network members in place, adds their descriptors, and reports the misused ones.</summary>
    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        public bool Changed { get; private set; }

        public List<Diagnostic> Diagnostics { get; } = new();

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ClassDeclarationSyntax visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            List<string> members = GenerateMembers(node);
            if (members.Count == 0)
                return visited;

            Changed = true;

            // The new members go on the closing brace's line, after whatever leads up to the brace, so a
            // directive there (#endregion, say) stays at the start of its line.
            MemberDeclarationSyntax[] generated = members
                .Select(m => SyntaxFactory.ParseMemberDeclaration(m)!.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space))
                .ToArray();
            generated[0] = generated[0].WithLeadingTrivia(visited.CloseBraceToken.LeadingTrivia);
            visited = visited.WithCloseBraceToken(visited.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.Space));
            return visited.AddMembers(generated);
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (FindAttribute(node.AttributeLists, out string? kind) is not { } attribute || kind != "Sync")
                return base.VisitPropertyDeclaration(node);
            if (AnalyzeSync(node, attribute, out DiagnosticDescriptor? error) is not { } sync)
            {
                Diagnostics.Add(Diagnostic.Create(error!, node.Identifier.GetLocation(), node.Identifier.Text));
                return base.VisitPropertyDeclaration(node);
            }

            Changed = true;
            AccessorDeclarationSyntax getter = node.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            AccessorDeclarationSyntax setter = node.AccessorList.Accessors.First(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
            AccessorListSyntax accessors = node.AccessorList.ReplaceNodes([getter, setter], (original, _) => original == getter
                ? WithArrow(original, $"this.{sync.Field}")
                : WithArrow(original, $"__Sync_Set(ref this.{sync.Field}, value, {sync.Descriptor})"));

            // The initializer moves to the field. Keep the line breaks it spanned, and what followed it, on the property.
            if (node.Initializer != null)
            {
                SyntaxTriviaList trailing = LineBreaksIn(node.Initializer.DescendantTrivia().Concat(node.SemicolonToken.LeadingTrivia))
                    .AddRange(node.SemicolonToken.TrailingTrivia);
                accessors = accessors.WithCloseBraceToken(accessors.CloseBraceToken.WithTrailingTrivia(trailing));
                node = node.WithInitializer(null).WithSemicolonToken(default);
            }

            return node.WithAccessorList(accessors);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (FindAttribute(node.AttributeLists, out string? kind) is not { } attribute || kind is not ("Broadcast" or "Host" or "Owner"))
                return base.VisitMethodDeclaration(node);
            if (AnalyzeRpc(node, attribute, kind, out DiagnosticDescriptor? error) is not { } rpc)
            {
                Diagnostics.Add(Diagnostic.Create(error!, node.Identifier.GetLocation(), node.Identifier.Text));
                return base.VisitMethodDeclaration(node);
            }

            Changed = true;
            StringBuilder prologue = new();
            prologue.Append($"if (__Rpc_Begin({rpc.Descriptor}, out {WriterType} __rpc_w)) {{ ");
            prologue.Append("using (global::Prowl.Runtime.Diagnostics.FrameStats.Network.Scope()) { ");
            foreach (ParameterSyntax parameter in node.ParameterList.Parameters)
                prologue.Append($"{Serializer}<{OneLine(parameter.Type!)}>.Write(__rpc_w, {parameter.Identifier.Text}); ");
            prologue.Append($"if (!__Rpc_Send({rpc.Descriptor}, __rpc_w)) return; }} }} ");
            StatementSyntax check = SyntaxFactory.ParseStatement(prologue.ToString());

            // Inserted right after the opening brace, on its line, taking over whatever followed the brace, so
            // the body's own lines don't change at all.
            if (node.Body != null)
            {
                SyntaxToken open = node.Body.OpenBraceToken;
                StatementSyntax placed = check.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(open.TrailingTrivia);
                BlockSyntax body = node.Body.WithOpenBraceToken(open.WithTrailingTrivia(SyntaxFactory.TriviaList()));
                return node.WithBody(body.WithStatements(body.Statements.Insert(0, placed)));
            }

            // The block opens where the arrow was and the expression keeps everything around it, so it stays on its own line.
            ArrowExpressionClauseSyntax arrow = node.ExpressionBody!;
            ExpressionStatementSyntax statement = SyntaxFactory.ExpressionStatement(
                arrow.Expression.WithLeadingTrivia(arrow.ArrowToken.TrailingTrivia.AddRange(arrow.Expression.GetLeadingTrivia())),
                node.SemicolonToken.WithTrailingTrivia(SyntaxFactory.Space));
            BlockSyntax block = SyntaxFactory.Block(check.WithTrailingTrivia(), statement)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(node.SemicolonToken.TrailingTrivia));
            return node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block);
        }
    }

    // --- Generated members --------------------------------------------------------------------------

    /// <summary>The members one class part gets for the [Sync] properties and RPCs it declares: backing fields and descriptors, each on one line.</summary>
    private static List<string> GenerateMembers(ClassDeclarationSyntax part)
    {
        List<string> members = new();
        string self = part.Identifier.Text + (part.TypeParameterList == null ? "" : OneLine(part.TypeParameterList));
        foreach (MemberDeclarationSyntax member in part.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property
                    when FindAttribute(property.AttributeLists, out string? kind) is { } attribute && kind == "Sync"
                    && AnalyzeSync(property, attribute, out _) is { } sync:
                    members.Add($"private {sync.Type} {sync.Field}{(sync.Initializer == null ? "" : " = " + sync.Initializer)};");
                    members.Add($"{Descriptor} global::Prowl.Runtime.Networking.SyncMember<{self}, {sync.Type}> {sync.Descriptor} = new({sync.Slot}u, "
                        + $"{SymbolDisplay.FormatLiteral(sync.Name, true)}, {sync.Flags}, static __c => __c.{sync.Field}, static (__c, __v) => __c.{sync.Field} = __v);");
                    break;

                case MethodDeclarationSyntax method
                    when FindAttribute(method.AttributeLists, out string? kind) is { } attribute && kind is "Broadcast" or "Host" or "Owner"
                    && AnalyzeRpc(method, attribute, kind, out _) is { } rpc:
                    StringBuilder invoke = new($"static ({self} __c, ref {ReaderType} __r, bool __execute) => {{ ");
                    for (int i = 0; i < rpc.ParameterTypes.Count; i++)
                        invoke.Append($"{rpc.ParameterTypes[i]} __a{i} = {Serializer}<{rpc.ParameterTypes[i]}>.Read(ref __r); ");
                    invoke.Append($"if (!__execute) return; __Rpc_ArgumentsRead(); __c.{rpc.Name}({string.Join(", ", Enumerable.Range(0, rpc.ParameterTypes.Count).Select(i => "__a" + i))}); }}");
                    members.Add($"{Descriptor} global::Prowl.Runtime.Networking.RpcMember<{self}> {rpc.Descriptor} = new({rpc.Id}u, "
                        + $"{SymbolDisplay.FormatLiteral(rpc.Signature, true)}, {rpc.Mode}, {rpc.Flags}, {invoke});");
                    break;
            }
        }
        return members;
    }

    // --- Syntax helpers -------------------------------------------------------------------------------

    /// <summary>The [Sync] or [Rpc.*] attribute in these lists, and which ("Sync", "Broadcast", "Host", "Owner").</summary>
    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> lists, out string? kind)
    {
        foreach (AttributeListSyntax list in lists)
        {
            foreach (AttributeSyntax attribute in list.Attributes)
            {
                // Name segments, so short and fully qualified Sync and Rpc attributes all match.
                string[] parts = attribute.Name.ToString().Split('.');
                string last = parts[^1].EndsWith("Attribute", StringComparison.Ordinal) ? parts[^1][..^"Attribute".Length] : parts[^1];
                string? previous = parts.Length > 1 ? parts[^2] : null;

                if (last == "Sync" && previous is null or "Networking")
                {
                    kind = last;
                    return attribute;
                }
                if (last is "Broadcast" or "Host" or "Owner" && previous == "Rpc")
                {
                    kind = last;
                    return attribute;
                }
            }
        }
        kind = null;
        return null;
    }

    /// <summary>The attribute's first positional argument, or the one named <paramref name="named"/>, as source text.</summary>
    private static string ArgumentOrDefault(AttributeSyntax attribute, string named, string fallback)
    {
        if (attribute.ArgumentList == null)
            return fallback;

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            string? argumentName = argument.NameEquals?.Name.Identifier.Text ?? argument.NameColon?.Name.Identifier.Text;
            if (argumentName == null || string.Equals(argumentName, named, StringComparison.OrdinalIgnoreCase))
                return OneLine(argument.Expression);
        }
        return fallback;
    }

    private static AccessorDeclarationSyntax WithArrow(AccessorDeclarationSyntax accessor, string expression) =>
        accessor.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
            SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.ParseExpression(expression)));

    private static SyntaxTriviaList LineBreaksIn(IEnumerable<SyntaxTrivia> trivia) =>
        SyntaxFactory.TriviaList(trivia.Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)));

    /// <summary>A node's source on one line, every gap (line breaks and comments included) a single space, so generated code never adds a line.</summary>
    private static string OneLine(SyntaxNode node)
    {
        SyntaxNode bare = node.WithoutTrivia();
        return bare.ReplaceTrivia(bare.DescendantTrivia(), (_, _) => SyntaxFactory.Space).ToFullString();
    }
}
