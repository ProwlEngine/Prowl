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
/// Turns game scripts' networking into code before they compile, so <c>[Sync]</c> properties and
/// <c>[Rpc.*]</c> methods network themselves, the way s&amp;box does: written as plain C#, no base class beyond
/// <c>Component</c>, and any struct or class of the game's own can be sent.
/// </summary>
/// <remarks>
/// <para>
/// It works from the scripts as written, bound, so it knows what every attribute, type and method really is.
/// Each networked member is rewritten where it stands. A <c>[Sync]</c> auto-property gets a backing field and
/// a setter that goes through <c>Component.__Sync_Set</c>; a network collection, a getter that ties it to its
/// component; an RPC, a prologue that sends the call and, unless it should also run here, returns. Each also
/// gets a static descriptor (<c>SyncMember</c>, <c>RpcMember</c>) with typed accessors, which the runtime finds
/// by reflection, and which carries the property's <c>[Change]</c> hook. The descriptors stay in the file
/// they describe, where its usings apply, so a partial class can spread its network members over any number of files.
/// </para>
/// <para>
/// Everything added to a script lands on lines that already exist, so every line keeps its number: compiler
/// errors, stack traces and breakpoints still point at the code as written. The serializers of the game's own
/// types those members send go into one more file of their own (see <see cref="SerializerPlan"/>).
/// </para>
/// </remarks>
internal static class NetworkCodeGen
{
    private static readonly DiagnosticDescriptor s_syncShape = new(
        "PROWLNET001", "[Sync] needs an auto-property",
        "[Sync] property '{0}' must be a non-static auto-property with get and set accessors, like { get; set; }, or a get-only network collection, like { get; } = new();",
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
        "'{0}' can't be sent over the network: {1}",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_changeHook = new(
        "PROWLNET006", "[Change] hook not usable",
        "[Change] on '{0}' {1}",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_collectionShape = new(
        "PROWLNET007", "Network collection shape",
        "[Sync] property '{0}' {1}",
        "Networking", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private const string SyncFlagsType = "global::Prowl.Runtime.Networking.SyncFlags";
    private const string RpcFlagsType = "global::Prowl.Runtime.Networking.RpcFlags";
    private const string RpcModeType = "global::Prowl.Runtime.Networking.RpcMode";
    private const string Serializer = "global::Prowl.Runtime.Networking.NetSerializer";
    private const string TypeSerializer = "global::Prowl.Runtime.Networking.NetTypeSerializer";
    private const string WriterType = "global::Prowl.Warp.NetWriter";
    private const string ReaderType = "global::Prowl.Warp.NetReader";

    // Hot reload would carry the previous assembly's descriptor over; the new one's own initializer builds the right one.
    private const string Descriptor = "[global::Prowl.Ember.ReloadIgnore] private static readonly";

    /// <summary>
    /// One assembly's scripts after the rewrite, in the order given; the file of serializers to compile with
    /// them, if it needs any; and what was wrong.
    /// </summary>
    public sealed record Result(IReadOnlyList<SyntaxTree> Trees, SyntaxTree? Serializers, IReadOnlyList<Diagnostic> Diagnostics);

    // A file whose rewrite comes out the same gets back the very same tree, so the compiler's incremental reuse holds.
    private static readonly ConditionalWeakTable<SyntaxTree, Rewritten> s_rewritten = new();

    private sealed record Rewritten(string Text, SyntaxTree Tree);

    // Whether each tree mentions any networking at all, so files without it are never bound for it.
    private static readonly ConditionalWeakTable<SyntaxTree, StrongBox<bool>> s_mayHaveNetworking = new();

    /// <summary>
    /// Generates the networking of <paramref name="trees"/>, which <paramref name="compilation"/> must be made
    /// of, as written. Trees with nothing to rewrite come back as they are.
    /// </summary>
    public static Result Generate(Compilation compilation, IReadOnlyList<SyntaxTree> trees)
    {
        if (Symbols.Of(compilation) is not { } symbols)
            return new Result(trees, null, []);

        Context context = new(compilation, symbols);
        SyntaxTree[] output = new SyntaxTree[trees.Count];
        for (int i = 0; i < trees.Count; i++)
        {
            SyntaxTree tree = trees[i];
            output[i] = tree;
            if (!MayHaveNetworking(tree))
                continue;

            Rewriter rewriter = new(compilation.GetSemanticModel(tree), context);
            SyntaxNode root = rewriter.Visit(tree.GetRoot());
            if (!rewriter.Changed)
                continue;

            string text = root.ToFullString();
            if (s_rewritten.TryGetValue(tree, out Rewritten? previous) && previous.Text == text)
            {
                output[i] = previous.Tree;
                continue;
            }

            output[i] = tree.WithRootAndOptions(root, tree.Options);
            s_rewritten.AddOrUpdate(tree, new Rewritten(text, output[i]));
        }

        SyntaxTree? serializers = context.Serializers.Generate(context.Diagnostics);
        return new Result(output, serializers, context.Diagnostics);
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
            return new StrongBox<bool>(text.Contains("Sync", StringComparison.Ordinal) || text.Contains("Rpc", StringComparison.Ordinal)
                || text.Contains("Change", StringComparison.Ordinal));
        }).Value!;

    // --- What the checks need ------------------------------------------------------------------------

    /// <summary>The engine types networking is described with, or null when the compilation doesn't reference the engine.</summary>
    private sealed record Symbols(
        INamedTypeSymbol Component,
        INamedTypeSymbol GameObject,
        INamedTypeSymbol NetCollection,
        INamedTypeSymbol INetSerializable,
        INamedTypeSymbol Sync,
        INamedTypeSymbol Change,
        INamedTypeSymbol Rpc)
    {
        public static Symbols? Of(Compilation compilation)
        {
            INamedTypeSymbol? Get(string name) => compilation.GetTypeByMetadataName(name);
            return Get("Prowl.Runtime.Component") is { } component && Get("Prowl.Runtime.GameObject") is { } gameObject
                && Get("Prowl.Runtime.Networking.NetCollection") is { } collection
                && Get("Prowl.Runtime.Networking.INetSerializable`1") is { } selfSerializable
                && Get("Prowl.Runtime.Networking.SyncAttribute") is { } sync
                && Get("Prowl.Runtime.Networking.ChangeAttribute") is { } change
                && Get("Prowl.Runtime.Networking.Rpc+RpcAttribute") is { } rpc
                ? new Symbols(component, gameObject, collection, selfSerializable, sync, change, rpc)
                : null;
        }
    }

    /// <summary>What one assembly's rewrite shares across its files: what to serialize, the ids taken so far, and what was wrong.</summary>
    private sealed class Context(Compilation compilation, Symbols symbols)
    {
        public Compilation Compilation { get; } = compilation;

        public Symbols Symbols { get; } = symbols;

        public SerializerPlan Serializers { get; } = new(compilation, symbols);

        public List<Diagnostic> Diagnostics { get; } = new();

        private readonly Dictionary<(INamedTypeSymbol Type, bool Rpc, uint Id), string> _ids = new();

        public void Report(DiagnosticDescriptor descriptor, Location location, params object[] arguments) =>
            Diagnostics.Add(Diagnostic.Create(descriptor, location, arguments));

        /// <summary>Takes a network id for a member of <paramref name="type"/>; reports it if another member hashes to it too.</summary>
        public void ClaimId(INamedTypeSymbol type, bool rpc, string key, Location location)
        {
            var id = (type.OriginalDefinition, rpc, NetworkId(key));
            if (!_ids.TryAdd(id, key) && _ids[id] != key)
                Report(s_collision, location, key, _ids[id]);
        }

        /// <summary>Reports <paramref name="name"/> unless values of <paramref name="type"/> can be sent.</summary>
        public void RequireSendable(ITypeSymbol type, string name, Location location)
        {
            if (Serializers.ProblemWith(type) is { } problem)
                Report(s_unsendable, location, name, problem.Describe(name));
        }
    }

    // --- What a member networks ----------------------------------------------------------------------

    /// <summary>
    /// A [Sync] property: a value, or a get-only network <paramref name="Collection"/>. <paramref name="Hook"/>
    /// is the lambda that runs its [Change] method, if it has one.
    /// </summary>
    private sealed record SyncInfo(string Name, string Type, uint Slot, uint Flags, string? Initializer, bool Collection, string? Hook)
    {
        public string Field => "__sync_" + Name;

        public string Descriptor => "__syncnet_" + Name;
    }

    private sealed record RpcInfo(string Name, uint Id, string Mode, uint Flags, IReadOnlyList<string> ParameterTypes)
    {
        public string Signature => $"{Name}({string.Join(",", ParameterTypes)})";

        public string Descriptor => $"__rpc_{Id}";
    }

    /// <summary>
    /// Rewrites one tree's network members in place and adds their descriptors, reporting the misused ones.
    /// Every node it's handed is the original's, so the semantic model knows it.
    /// </summary>
    private sealed class Rewriter(SemanticModel model, Context context) : CSharpSyntaxRewriter
    {
        private readonly Dictionary<SyntaxNode, SyncInfo?> _syncs = new();
        private readonly Dictionary<SyntaxNode, RpcInfo?> _rpcs = new();

        public bool Changed { get; private set; }

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
            if (Sync(node) is not { } sync)
                return base.VisitPropertyDeclaration(node);

            Changed = true;
            AccessorDeclarationSyntax getter = node.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            AccessorListSyntax accessors;
            if (sync.Collection)
            {
                accessors = node.AccessorList.ReplaceNode(getter, WithArrow(getter, $"__Sync_Collection(this.{sync.Field}, {sync.Descriptor})"));
            }
            else
            {
                AccessorDeclarationSyntax setter = node.AccessorList.Accessors.First(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
                accessors = node.AccessorList.ReplaceNodes([getter, setter], (original, _) => original == getter
                    ? WithArrow(original, $"this.{sync.Field}")
                    : WithArrow(original, $"__Sync_Set(ref this.{sync.Field}, value, {sync.Descriptor})"));
            }

            // The initializer moves to the field. Keep the line breaks it spanned, and what followed it, on the property.
            if (node.Initializer != null)
            {
                SyntaxTriviaList trailing = LineBreaksIn(accessors.CloseBraceToken.TrailingTrivia.Concat(node.Initializer.DescendantTrivia())
                        .Concat(node.SemicolonToken.LeadingTrivia))
                    .AddRange(node.SemicolonToken.TrailingTrivia);
                accessors = accessors.WithCloseBraceToken(accessors.CloseBraceToken.WithTrailingTrivia(trailing));
                node = node.WithInitializer(null).WithSemicolonToken(default);
            }

            return node.WithAccessorList(accessors);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (Rpc(node) is not { } rpc)
                return base.VisitMethodDeclaration(node);

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

        /// <summary>The members one class part gets for the [Sync] properties and RPCs it declares: backing fields and descriptors, each on one line.</summary>
        private List<string> GenerateMembers(ClassDeclarationSyntax part)
        {
            List<string> members = new();
            string self = part.Identifier.Text + (part.TypeParameterList == null ? "" : OneLine(part.TypeParameterList));
            foreach (MemberDeclarationSyntax member in part.Members)
            {
                if (member is PropertyDeclarationSyntax property && Sync(property) is { } sync)
                {
                    string flags = $"({SyncFlagsType}){sync.Flags}u";
                    if (sync.Collection)
                    {
                        members.Add($"private readonly {sync.Type} {sync.Field} = {sync.Initializer};");
                        members.Add($"{Descriptor} global::Prowl.Runtime.Networking.SyncCollectionMember<{self}, {sync.Type}> {sync.Descriptor} = new("
                            + $"{sync.Slot}u, {SymbolDisplay.FormatLiteral(sync.Name, true)}, {flags}, static __c => __c.{sync.Field});");
                        continue;
                    }

                    members.Add($"private {sync.Type} {sync.Field}{(sync.Initializer == null ? "" : " = " + sync.Initializer)};");
                    members.Add($"{Descriptor} global::Prowl.Runtime.Networking.SyncMember<{self}, {sync.Type}> {sync.Descriptor} = new({sync.Slot}u, "
                        + $"{SymbolDisplay.FormatLiteral(sync.Name, true)}, {flags}, static __c => __c.{sync.Field}, static (__c, __v) => __c.{sync.Field} = __v"
                        + (sync.Hook == null ? "" : ", " + sync.Hook) + ");");
                }
                else if (member is MethodDeclarationSyntax method && Rpc(method) is { } rpc)
                {
                    StringBuilder invoke = new($"static ({self} __c, ref {ReaderType} __r, bool __execute) => {{ ");
                    for (int i = 0; i < rpc.ParameterTypes.Count; i++)
                        invoke.Append($"{rpc.ParameterTypes[i]} __a{i} = {Serializer}<{rpc.ParameterTypes[i]}>.Read(ref __r); ");
                    invoke.Append($"if (!__execute) return; __Rpc_ArgumentsRead(); __c.{rpc.Name}({string.Join(", ", Enumerable.Range(0, rpc.ParameterTypes.Count).Select(i => "__a" + i))}); }}");
                    members.Add($"{Descriptor} global::Prowl.Runtime.Networking.RpcMember<{self}> {rpc.Descriptor} = new({rpc.Id}u, "
                        + $"{SymbolDisplay.FormatLiteral(rpc.Signature, true)}, {RpcModeType}.{rpc.Mode}, ({RpcFlagsType}){rpc.Flags}u, {invoke});");
                }
            }
            return members;
        }

        /// <summary>The property's [Sync] description, or null if it isn't one or is misused (reported, once).</summary>
        private SyncInfo? Sync(PropertyDeclarationSyntax node)
        {
            if (_syncs.TryGetValue(node, out SyncInfo? known))
                return known;
            return _syncs[node] = node.AttributeLists.Count == 0 ? null : AnalyzeSync(node);
        }

        private RpcInfo? Rpc(MethodDeclarationSyntax node)
        {
            if (_rpcs.TryGetValue(node, out RpcInfo? known))
                return known;
            return _rpcs[node] = node.AttributeLists.Count == 0 ? null : AnalyzeRpc(node);
        }

        private SyncInfo? AnalyzeSync(PropertyDeclarationSyntax node)
        {
            if (model.GetDeclaredSymbol(node) is not { } property)
                return null;

            Symbols symbols = context.Symbols;
            Location location = node.Identifier.GetLocation();
            string name = property.Name;
            AttributeData? sync = Find(property, symbols.Sync);
            AttributeData? change = Find(property, symbols.Change);
            if (sync == null)
            {
                if (change != null)
                    context.Report(s_changeHook, location, name, "only works on a [Sync] property");
                return null;
            }

            if (node.Parent is not ClassDeclarationSyntax || !DerivesFrom(property.ContainingType, symbols.Component))
            {
                context.Report(s_notInClass, location, name);
                return null;
            }

            AccessorListSyntax? accessors = node.AccessorList;
            bool isAuto = accessors != null && accessors.Accessors.All(a => a.Body == null && a.ExpressionBody == null);
            if (!isAuto || property.IsStatic || property.IsAbstract)
            {
                context.Report(s_syncShape, location, name);
                return null;
            }

            bool getOnly = accessors!.Accessors is [{ RawKind: (int)SyntaxKind.GetAccessorDeclaration }];
            bool getAndSet = accessors.Accessors.Count == 2 && accessors.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))
                && accessors.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
            bool collection = DerivesFrom(property.Type, symbols.NetCollection);
            if (collection && !(getOnly && node.Initializer != null))
            {
                context.Report(s_collectionShape, location, name,
                    "is a network collection, which is made once and changed from then on. Make it get-only: { get; } = new();");
                return null;
            }

            if (!collection && !getAndSet)
            {
                if (getOnly)
                    context.Report(s_collectionShape, location, name, "is get-only, which only a NetList, NetDictionary or NetSet can be. Give it a setter: { get; set; }");
                else
                    context.Report(s_syncShape, location, name);
                return null;
            }

            // A collection sends its items, keys and values, never itself.
            if (collection && property.Type is INamedTypeSymbol collectionType)
            {
                foreach (ITypeSymbol item in collectionType.TypeArguments)
                    context.RequireSendable(item, name, location);
            }
            else
            {
                context.RequireSendable(property.Type, name, location);
            }

            string? hook = null;
            if (change != null && (hook = Hook(property, change, collection, out string? problem)) == null)
            {
                context.Report(s_changeHook, location, name, problem!);
                return null;
            }

            uint flags = sync.ConstructorArguments.Concat(sync.NamedArguments.Where(a => a.Key == nameof(SyncAttribute.Flags)).Select(a => a.Value))
                .Select(a => Convert.ToUInt32(a.Value)).LastOrDefault();
            context.ClaimId(property.ContainingType, false, name, location);
            return new SyncInfo(name, OneLine(node.Type), NetworkId(name), flags, node.Initializer == null ? null : OneLine(node.Initializer.Value),
                collection, hook);
        }

        /// <summary>The lambda that calls a [Change] method, with the old and new values if it takes them; or null, and why not.</summary>
        private string? Hook(IPropertySymbol property, AttributeData change, bool collection, out string? problem)
        {
            problem = null;
            if (collection)
            {
                problem = "doesn't work on a network collection. Handle its Changed event instead";
                return null;
            }

            string method = change.ConstructorArguments.FirstOrDefault().Value as string ?? "";
            List<IMethodSymbol> candidates = new();
            for (ITypeSymbol? t = property.ContainingType; t != null; t = t.BaseType)
            {
                candidates.AddRange(t.GetMembers(method).OfType<IMethodSymbol>().Where(m => !m.IsStatic && m.TypeParameters.IsEmpty
                    && context.Compilation.IsSymbolAccessibleWithin(m, property.ContainingType)));
            }

            bool TakesValues(IMethodSymbol m) => m.Parameters.Length == 2 && m.Parameters.All(p => p.RefKind == RefKind.None
                && SymbolEqualityComparer.Default.Equals(p.Type, property.Type));
            if (candidates.Any(TakesValues))
                return $"static (__c, __o, __n) => __c.{method}(__o, __n)";
            if (candidates.Any(m => m.Parameters.IsEmpty))
                return $"static (__c, __o, __n) => __c.{method}()";

            string type = property.Type.ToDisplayString();
            problem = $"names '{method}', which must be a method of the component taking ({type} oldValue, {type} newValue), or nothing";
            return null;
        }

        private RpcInfo? AnalyzeRpc(MethodDeclarationSyntax node)
        {
            if (model.GetDeclaredSymbol(node) is not { } method
                || method.GetAttributes().FirstOrDefault(a => DerivesFrom(a.AttributeClass, context.Symbols.Rpc)) is not { AttributeClass: { } kind } rpc)
                return null;

            Location location = node.Identifier.GetLocation();
            if (node.Parent is not ClassDeclarationSyntax || !DerivesFrom(method.ContainingType, context.Symbols.Component))
            {
                context.Report(s_notInClass, location, method.Name);
                return null;
            }

            bool plainParameters = method.Parameters.All(p => p.RefKind == RefKind.None && !p.IsParams && !p.IsThis);
            if (!method.ReturnsVoid || !plainParameters || method.IsGenericMethod || method.IsStatic || (node.Body == null && node.ExpressionBody == null))
            {
                context.Report(s_rpcShape, location, method.Name);
                return null;
            }

            foreach (IParameterSymbol parameter in method.Parameters)
                context.RequireSendable(parameter.Type, parameter.Name, parameter.Locations.FirstOrDefault() ?? location);

            // Hashed as written, so the id only changes when the declaration does.
            List<string> types = node.ParameterList.Parameters.Select(p => OneLine(p.Type!)).ToList();
            string key = $"{method.Name}({string.Join(",", types)})";
            context.ClaimId(method.ContainingType, true, key, location);
            uint flags = rpc.ConstructorArguments.Select(a => Convert.ToUInt32(a.Value)).FirstOrDefault();
            return new RpcInfo(method.Name, NetworkId(key), kind.Name[..^"Attribute".Length], flags, types);
        }

        private static AttributeData? Find(ISymbol symbol, INamedTypeSymbol attribute) =>
            symbol.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
    }

    // --- Serializers for the game's own types ---------------------------------------------------------

    /// <summary>Why a value can't be sent: what's wrong, said of a member <see cref="Path"/> below the value ("" for the value itself).</summary>
    private sealed record Problem(string Path, string Predicate)
    {
        public string Describe(string value) => $"{value}{(Path.Length == 0 ? "" : "." + Path)} {Predicate}";

        public Problem Under(string member) => this with { Path = Path.Length == 0 ? member : member + "." + Path };
    }

    /// <summary>
    /// <para>
    /// Which types can be sent, and the serializers the game's own need. A struct or class declared in these
    /// scripts is sent member by member, base class first: its public fields, its [SerializeField] fields and
    /// its public auto-properties, skipping [SerializeIgnore] and [NonSerialized]. The generated code reaches
    /// them through <c>UnsafeAccessor</c>, so private and readonly members are fine and nothing is reflected
    /// over at run time.
    /// </para>
    /// <para>
    /// A class or interface stands for itself and for every class of these scripts it can hold, abstract ones
    /// included as long as something concrete derives from them; references between the objects of one value,
    /// cycles too, arrive as they were (see <see cref="NetTypeSerializer"/>). A type that implements
    /// <c>INetSerializable</c> writes itself and is only registered.
    /// </para>
    /// </summary>
    private sealed class SerializerPlan(Compilation compilation, Symbols symbols)
    {
        private readonly Dictionary<ITypeSymbol, Problem?> _problems = new(SymbolEqualityComparer.Default);
        private readonly List<(INamedTypeSymbol Type, List<IFieldSymbol> Members)> _shapes = new();
        private readonly List<INamedTypeSymbol> _bases = new();
        private readonly List<INamedTypeSymbol> _selfWritten = new();
        private List<INamedTypeSymbol>? _classes;

        /// <summary>Null if values of the type can be sent; otherwise why not.</summary>
        public Problem? ProblemWith(ITypeSymbol type)
        {
            if (_problems.TryGetValue(type, out Problem? known))
                return known;

            // Taken as sendable while its own members are looked at, so a class can hold one of itself.
            _problems[type] = null;
            return _problems[type] = Check(type);
        }

        private Problem? Check(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum || DerivesFrom(type, symbols.Component) || DerivesFrom(type, symbols.GameObject))
                return null;

            string name = type.ToDisplayString();
            Problem unsupported = new("", $"is a {name}, which isn't a type the network can send (see NetSerializer<T>)");
            if (type is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Struct or TypeKind.Class or TypeKind.Interface)
                || named.IsStatic || named.IsRefLikeType)
                return unsupported;

            bool own = SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly);
            bool writesItself = named.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, symbols.INetSerializable)
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], named));
            if (!own && !writesItself)
                return IsBuiltIn(named) ? null : unsupported;

            if (!CanName(named))
                return new Problem("", $"is a {name}, which is private or protected, or inside a type that is. Make it internal or public");

            if (writesItself)
            {
                _selfWritten.Add(named);
                return null;
            }

            if (named.IsGenericType)
                return new Problem("", $"is a {name}, which is generic. Implement INetSerializable<T> on it to send it");

            if (named.TypeKind == TypeKind.Struct)
                return Shape(named);

            // A class or interface: itself, if it can be made, and whatever of ours derives from it.
            if (!named.IsAbstract && named.TypeKind == TypeKind.Class && Shape(named) is { } own_problem)
                return own_problem;

            foreach (INamedTypeSymbol derived in OwnClasses().Where(c => !SymbolEqualityComparer.Default.Equals(c, named) && IsA(c, named)))
            {
                if (ProblemWith(derived) is { } problem)
                    return new Problem("", $"can be a {derived.Name}, and {problem.Describe(derived.Name)}");
            }

            if (named.IsAbstract || named.TypeKind == TypeKind.Interface)
                _bases.Add(named);
            return null;
        }

        /// <summary>Plans a serializer for a struct or concrete class, member by member; or says why it can't have one.</summary>
        private Problem? Shape(INamedTypeSymbol type)
        {
            string name = type.ToDisplayString();
            if (type.TypeKind == TypeKind.Class && !type.InstanceConstructors.Any(c => c.Parameters.IsEmpty))
                return new Problem("", $"is a {name}, which has no parameterless constructor to make one with");

            List<IFieldSymbol> members = Members(type);
            if (members.Count == 0 && type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic && !f.IsConst))
                return new Problem("", $"is a {name}, which has nothing to send. Make the fields or auto-properties to send public, or mark fields [SerializeField]");

            foreach (IFieldSymbol member in members)
            {
                if (ProblemWith(member.Type) is { } problem)
                    return problem.Under((member.AssociatedSymbol ?? member).Name);
            }

            _shapes.Add((type, members));
            return null;
        }

        /// <summary>The fields a type is sent as, its base classes' first, auto-properties as their backing fields.</summary>
        private List<IFieldSymbol> Members(INamedTypeSymbol type)
        {
            Stack<INamedTypeSymbol> chain = new();
            for (INamedTypeSymbol? t = type; t != null && IsOwn(t); t = t.BaseType)
                chain.Push(t);
            return chain.SelectMany(t => t.GetMembers().OfType<IFieldSymbol>().Where(IsSent)).ToList();
        }

        private static bool IsSent(IFieldSymbol field)
        {
            if (field.IsStatic || field.IsConst || HasAttribute(field, "SerializeIgnore") || HasAttribute(field, "NonSerialized"))
                return false;
            if (field.AssociatedSymbol is IPropertySymbol property)
                return property.DeclaredAccessibility == Accessibility.Public && !HasAttribute(property, "SerializeIgnore");
            return !field.IsImplicitlyDeclared && (field.DeclaredAccessibility == Accessibility.Public || HasAttribute(field, "SerializeField"));
        }

        private static bool HasAttribute(ISymbol symbol, string name) =>
            symbol.GetAttributes().Any(a => a.AttributeClass?.Name == name + "Attribute");

        /// <summary>Whether generated code outside the type can write its name.</summary>
        private static bool CanName(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
            {
                if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                    return false;
            }
            return true;
        }

        private bool IsOwn(ITypeSymbol type) => SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);

        private static bool IsA(INamedTypeSymbol type, INamedTypeSymbol baseOrInterface) =>
            baseOrInterface.TypeKind == TypeKind.Interface
                ? type.AllInterfaces.Contains(baseOrInterface, SymbolEqualityComparer.Default)
                : DerivesFrom(type.BaseType, baseOrInterface);

        /// <summary>The non-generic classes these scripts declare, which a class or interface value can turn out to be. Components travel by reference instead.</summary>
        private List<INamedTypeSymbol> OwnClasses()
        {
            if (_classes != null)
                return _classes;

            _classes = new List<INamedTypeSymbol>();
            Stack<INamespaceOrTypeSymbol> pending = new([compilation.Assembly.GlobalNamespace]);
            while (pending.Count > 0)
            {
                INamespaceOrTypeSymbol container = pending.Pop();
                foreach (INamespaceOrTypeSymbol member in container is INamespaceSymbol space ? space.GetMembers() : container.GetTypeMembers())
                {
                    pending.Push(member);
                    if (member is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsGenericType: false } type
                        && !DerivesFrom(type, symbols.Component))
                        _classes.Add(type);
                }
            }
            return _classes;
        }

        /// <summary>Whether <c>NetSerializer&lt;T&gt;</c> handles the type itself, asked of the runtime.</summary>
        private static bool IsBuiltIn(INamedTypeSymbol type)
        {
            // Nothing the serializer takes is generic or nested.
            if (type is not { IsGenericType: false, ContainingType: null, ContainingAssembly: { } assembly })
                return false;

            string name = type.ToDisplayString(s_runtimeName);
            Type? runtime = Type.GetType($"{name}, {assembly.Identity.Name}") ?? Type.GetType(name);
            return runtime != null && (bool)typeof(NetSerializer<>).MakeGenericType(runtime)
                .GetProperty(nameof(NetSerializer<int>.IsSupported))!.GetValue(null)!;
        }

        private static readonly SymbolDisplayFormat s_runtimeName = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        /// <summary>
        /// One file that registers every serializer found needed, from a module initializer, so they're in
        /// place before the assembly's first value is sent. Null if none are. Two classes whose ids collide are
        /// reported to <paramref name="diagnostics"/>.
        /// </summary>
        public SyntaxTree? Generate(List<Diagnostic> diagnostics)
        {
            if (_shapes.Count == 0 && _bases.Count == 0 && _selfWritten.Count == 0)
                return null;

            const string accessor = "[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind";
            StringBuilder code = new();
            code.AppendLine("// <auto-generated/> Prowl's script compiler: serializers for this assembly's own types that go over the network.");
            code.AppendLine("#nullable disable");
            code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            code.AppendLine("internal static class __ProwlNetSerializers");
            code.AppendLine("{");
            code.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            code.AppendLine("    internal static void Register()");
            code.AppendLine("    {");
            foreach (INamedTypeSymbol type in _selfWritten)
                code.AppendLine($"        {TypeSerializer}.RegisterSelf<{Name(type)}>();");
            foreach (INamedTypeSymbol type in _bases)
                code.AppendLine($"        {TypeSerializer}.RegisterBase<{Name(type)}>();");

            Dictionary<uint, INamedTypeSymbol> ids = new();
            for (int i = 0; i < _shapes.Count; i++)
            {
                INamedTypeSymbol type = _shapes[i].Type;
                string create = Constructs(type) ? $"New{i}" : "static () => new()";
                if (type.TypeKind == TypeKind.Struct)
                {
                    code.AppendLine($"        {TypeSerializer}.RegisterStruct<{Name(type)}>({create}, Write{i}, Read{i});");
                    continue;
                }

                // Peers tell the classes apart by name, which is the same everywhere the same build runs.
                uint id = NetworkId(type.ToDisplayString(s_runtimeName));
                if (!ids.TryAdd(id, type))
                    diagnostics.Add(Diagnostic.Create(s_collision, type.Locations.FirstOrDefault() ?? Location.None, type.ToDisplayString(), ids[id].ToDisplayString()));
                code.AppendLine($"        {TypeSerializer}.RegisterClass<{Name(type)}>({id}u, {create}, Write{i}, Read{i});");
            }
            code.AppendLine("    }");

            for (int i = 0; i < _shapes.Count; i++)
            {
                (INamedTypeSymbol type, List<IFieldSymbol> members) = _shapes[i];
                string self = Name(type);
                string by = type.TypeKind == TypeKind.Struct ? "ref " : "";

                code.AppendLine();
                for (int j = 0; j < members.Count; j++)
                {
                    INamedTypeSymbol owner = members[j].ContainingType;
                    string target = owner.TypeKind == TypeKind.Struct ? $"ref {Name(owner)}" : Name(owner);
                    code.AppendLine($"    {accessor}.Field, Name = {SymbolDisplay.FormatLiteral(members[j].MetadataName, true)})] "
                        + $"private static extern ref {Name(members[j].Type)} F{i}_{j}({target} o);");
                }

                if (Constructs(type))
                    code.AppendLine($"    {accessor}.Constructor)] private static extern {self} New{i}();");

                code.Append($"    private static void Write{i}({WriterType} w, ref {self} v) {{ ");
                for (int j = 0; j < members.Count; j++)
                    code.Append($"{Serializer}<{Name(members[j].Type)}>.Write(w, F{i}_{j}({by}v)); ");
                code.AppendLine("}");

                code.Append($"    private static void Read{i}(ref {ReaderType} r, ref {self} v) {{ ");
                for (int j = 0; j < members.Count; j++)
                    code.Append($"F{i}_{j}({by}v) = {Serializer}<{Name(members[j].Type)}>.Read(ref r); ");
                code.AppendLine("}");
            }
            code.AppendLine("}");

            CSharpParseOptions? options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
            return CSharpSyntaxTree.ParseText(code.ToString(), options, path: "ProwlNetSerializers.g.cs", encoding: Encoding.UTF8);
        }

        /// <summary>
        /// Whether a value is made by calling its parameterless constructor whatever its access: always for a
        /// class, and for a struct whose own one only it can call. Otherwise a struct starts from <c>new()</c>.
        /// </summary>
        private static bool Constructs(INamedTypeSymbol type) =>
            type.TypeKind == TypeKind.Class || type.InstanceConstructors.Any(c => c.Parameters.IsEmpty && !c.IsImplicitlyDeclared
                && c.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal));

        private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    // --- Helpers --------------------------------------------------------------------------------------

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? t = type; t != null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, baseType))
                return true;
        return false;
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
