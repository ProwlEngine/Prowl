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

namespace Prowl.CodeGen.Networking;

/// <summary>
///     Turns game scripts' networking into code before they compile, so <c>[Sync]</c> properties and
///     <c>[Rpc.*]</c> methods network themselves, the way s&amp;box does: written as plain C#, no base class beyond
///     <c>Component</c>, and any struct or class of the game's own can be sent.
/// </summary>
/// <remarks>
///     <para>
///         It works from the scripts as written, bound, so it knows what every attribute, type and method really is.
///         Each networked member is rewritten where it stands. A <c>[Sync]</c> auto-property gets a backing field and
///         a setter that goes through its <c>SyncMember</c> descriptor; a network collection, a getter that ties it to its
///         component; an RPC, a prologue that sends the call and, unless it should also run here, returns. Each also
///         gets a static descriptor (<c>SyncMember</c>, <c>RpcMember</c>) with typed accessors, which the runtime finds
///         by reflection, and which carries the property's <c>[Change]</c> hook. The descriptors stay in the file
///         they describe, where its usings apply, so a partial class can spread its network members over any number of
///         files.
///     </para>
///     <para>
///         Everything added to a script lands on lines that already exist, so every line keeps its number: compiler
///         errors, stack traces and breakpoints still point at the code as written (see <see cref="Rewriter" />). The
///         serializers of the game's own types those members send go into one more file of their own (see
///         <see cref="SerializerPlan" />).
///     </para>
/// </remarks>
internal sealed partial class NetworkCodeGen : IScriptGenerator
{
    private const string SyncFlagsType = "global::Prowl.Runtime.Networking.SyncFlags";
    private const string RpcFlagsType = "global::Prowl.Runtime.Networking.RpcFlags";
    private const string RpcModeType = "global::Prowl.Runtime.Networking.RpcMode";
    private const string Serializer = "global::Prowl.Runtime.Networking.NetSerializer";
    private const string TypeSerializer = "global::Prowl.Runtime.Networking.NetTypeSerializer";
    private const string WriterType = "global::Prowl.Warp.NetWriter";
    private const string ReaderType = "global::Prowl.Warp.NetReader";

    // Hot reload would carry the previous assembly's descriptor over; the new one's own initializer builds the right one.
    private const string Descriptor = "[global::Prowl.Ember.ReloadIgnore] private static readonly";

    private static readonly DiagnosticDescriptor s_syncShape = new(
        "PROWLNET001", "[Sync] needs an auto-property",
        "[Sync] property '{0}' must be a non-static auto-property with get and set accessors, like { get; set; }, or a get-only network collection, like { get; } = new();",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_rpcShape = new(
        "PROWLNET002", "RPC shape not supported",
        "RPC '{0}' must be a non-generic method returning void, with a body and no ref, out or params parameters",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_notInClass = new(
        "PROWLNET003", "Network member outside a component",
        "'{0}' is networked, which only works on a Component class",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_collision = new(
        "PROWLNET004", "Network id collision",
        "'{0}' and '{1}' hash to the same network id. Rename one of them.",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_unsendable = new(
        "PROWLNET005", "Value can't be sent over the network",
        "'{0}' can't be sent over the network: {1}",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_changeHook = new(
        "PROWLNET006", "[Change] hook not usable",
        "[Change] on '{0}' {1}",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_collectionShape = new(
        "PROWLNET007", "Network collection shape",
        "[Sync] property '{0}' {1}",
        "Networking", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor s_staticRpc = new(
        "PROWLNET008", "Static RPC shape not supported",
        "Static RPC '{0}' must be declared in a non-generic class and cannot use Owner or OwnerOnly",
        "Networking", DiagnosticSeverity.Error, true);

    // A file whose rewrite comes out the same gets back the very same tree, so the compiler's incremental reuse holds.
    private static readonly ConditionalWeakTable<SyntaxTree, Rewritten> s_rewritten = new();

    // Whether each tree mentions any networking at all, so files without it are never bound for it.
    private static readonly ConditionalWeakTable<SyntaxTree, StrongBox<bool>> s_mayHaveNetworking = new();

    /// <summary>
    ///     Generates the networking of <paramref name="trees" />, which <paramref name="compilation" /> must be made
    ///     of, as written: the trees rewritten in place, and a file of serializers if they need one. Trees with
    ///     nothing to rewrite come back as they are.
    /// </summary>
    public ScriptCodeGen.Result Generate(Compilation compilation, IReadOnlyList<SyntaxTree> trees)
    {
        if (Symbols.Of(compilation) is not { } symbols)
        {
            return ScriptCodeGen.Unchanged(trees);
        }

        Context context = new(compilation, symbols);
        SyntaxTree[] output = new SyntaxTree[trees.Count];
        for (int i = 0; i < trees.Count; i++)
        {
            SyntaxTree tree = trees[i];
            output[i] = tree;
            if (!MayHaveNetworking(tree))
            {
                continue;
            }

            Rewriter rewriter = new(compilation.GetSemanticModel(tree), context);
            SyntaxNode root = rewriter.Visit(tree.GetRoot());
            if (!rewriter.Changed)
            {
                continue;
            }

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
        return new ScriptCodeGen.Result(output, serializers == null ? [] : [serializers], context.Diagnostics);
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
            return new StrongBox<bool>(text.Contains("Sync", StringComparison.Ordinal) ||
                                       text.Contains("Rpc", StringComparison.Ordinal)
                                       || text.Contains("Change", StringComparison.Ordinal));
        }).Value!;

    // --- Helpers --------------------------------------------------------------------------------------

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? t = type; t != null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static AccessorDeclarationSyntax WithArrow(AccessorDeclarationSyntax accessor, string expression) =>
        accessor.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
            SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken).WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.ParseExpression(expression)));

    private static SyntaxTriviaList LineBreaksIn(IEnumerable<SyntaxTrivia> trivia) =>
        SyntaxFactory.TriviaList(trivia.Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)));

    /// <summary>
    ///     A node's source on one line, every gap (line breaks and comments included) a single space, so generated code
    ///     never adds a line.
    /// </summary>
    private static string OneLine(SyntaxNode node)
    {
        SyntaxNode bare = node.WithoutTrivia();
        return bare.ReplaceTrivia(bare.DescendantTrivia(), (_, _) => SyntaxFactory.Space).ToFullString();
    }

    private sealed record Rewritten(string Text, SyntaxTree Tree);

    // --- What the checks need ------------------------------------------------------------------------

    /// <summary>The engine types networking is described with, or null when the compilation doesn't reference the engine.</summary>
    private sealed record Symbols(
        INamedTypeSymbol Component,
        INamedTypeSymbol GameObject,
        INamedTypeSymbol NetCollection,
        INamedTypeSymbol INetSerializable,
        INamedTypeSymbol Sync,
        INamedTypeSymbol Change,
        INamedTypeSymbol Rpc,
        IReadOnlySet<ITypeSymbol> BuiltIns)
    {
        public static Symbols? Of(Compilation compilation)
        {
            INamedTypeSymbol? Get(string name) => compilation.GetTypeByMetadataName(name);
            return Get("Prowl.Runtime.Component") is { } component && Get("Prowl.Runtime.GameObject") is { } gameObject
                                                                   && Get("Prowl.Runtime.Networking.NetCollection") is
                                                                       { } collection
                                                                   && Get("Prowl.Runtime.Networking.INetSerializable`1")
                                                                       is { } selfSerializable
                                                                   && Get("Prowl.Runtime.Networking.SyncAttribute") is
                                                                       { } sync
                                                                   && Get("Prowl.Runtime.Networking.ChangeAttribute") is
                                                                       { } change
                                                                   && Get("Prowl.Runtime.Networking.Rpc+RpcAttribute")
                                                                       is { } rpc
                                                                   && Get("Prowl.Runtime.Networking.NetSerializer`1") is
                                                                       { } serializer
                ? new Symbols(component, gameObject, collection, selfSerializable, sync, change, rpc,
                    BuiltInTypes(serializer))
                : null;
        }

        /// <summary>What <c>NetSerializer&lt;T&gt;</c> writes itself, as its <c>[BuiltInTypes]</c> lists.</summary>
        private static HashSet<ITypeSymbol> BuiltInTypes(INamedTypeSymbol serializer) =>
            new(serializer.GetAttributes()
                .Where(a => a.AttributeClass?.Name == "BuiltInTypesAttribute")
                .SelectMany(a => a.ConstructorArguments.SelectMany(argument => argument.Values))
                .Select(type => type.Value).OfType<ITypeSymbol>(), SymbolEqualityComparer.Default);
    }

    /// <summary>
    ///     What one assembly's rewrite shares across its files: what to serialize, the ids taken so far, and what was
    ///     wrong.
    /// </summary>
    private sealed class Context(Compilation compilation, Symbols symbols)
    {
        private readonly Dictionary<(INamedTypeSymbol Type, bool Rpc, uint Id), string> _ids = new();
        public Compilation Compilation { get; } = compilation;

        public Symbols Symbols { get; } = symbols;

        public SerializerPlan Serializers { get; } = new(compilation, symbols);

        public List<Diagnostic> Diagnostics { get; } = new();

        public void Report(DiagnosticDescriptor descriptor, Location location, params object[] arguments) =>
            Diagnostics.Add(Diagnostic.Create(descriptor, location, arguments));

        /// <summary>Takes a network id for a member of <paramref name="type" />; reports it if another member hashes to it too.</summary>
        public void ClaimId(INamedTypeSymbol type, bool rpc, string key, Location location)
        {
            (INamedTypeSymbol OriginalDefinition, bool rpc, uint) id = (type.OriginalDefinition, rpc, NetworkId(key));
            if (!_ids.TryAdd(id, key) && _ids[id] != key)
            {
                Report(s_collision, location, key, _ids[id]);
            }
        }

        /// <summary>Reports <paramref name="name" /> unless values of <paramref name="type" /> can be sent.</summary>
        public void RequireSendable(ITypeSymbol type, string name, Location location)
        {
            if (Serializers.ProblemWith(type) is { } problem)
            {
                Report(s_unsendable, location, name, problem.Describe(name));
            }
        }
    }

    // --- What a member networks ----------------------------------------------------------------------

    /// <summary>
    ///     A [Sync] property: a value, or a get-only network <paramref name="Collection" />. <paramref name="Hook" />
    ///     is the lambda that runs its [Change] method, if it has one.
    /// </summary>
    private sealed record SyncInfo(
        string Name,
        string Type,
        uint Slot,
        uint Flags,
        string? Initializer,
        bool Collection,
        string? Hook)
    {
        public string Field => "__sync_" + Name;

        public string Descriptor => "__syncnet_" + Name;
    }

    private sealed record RpcInfo(
        string Name,
        uint Id,
        string Mode,
        uint Flags,
        IReadOnlyList<string> ParameterTypes,
        bool Static = false)
    {
        public string Signature => $"{Name}({string.Join(",", ParameterTypes)})";

        public string Descriptor => $"__rpc_{Id}";
    }
}
