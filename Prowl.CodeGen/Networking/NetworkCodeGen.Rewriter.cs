// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Prowl.CodeGen.Networking;

internal sealed partial class NetworkCodeGen
{
    /// <summary>
    ///     Rewrites one tree's network members in place and adds their descriptors, reporting the misused ones.
    ///     Every node it's handed is the original's, so the semantic model knows it.
    /// </summary>
    private sealed class Rewriter(SemanticModel model, Context context) : CSharpSyntaxRewriter
    {
        private readonly Dictionary<SyntaxNode, RpcInfo?> _rpcs = new();
        private readonly Dictionary<SyntaxNode, SyncInfo?> _syncs = new();

        public bool Changed { get; private set; }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ClassDeclarationSyntax visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            List<string> members = GenerateMembers(node);
            if (members.Count == 0)
            {
                return visited;
            }

            Changed = true;

            // The new members go on the closing brace's line, after whatever leads up to the brace, so a
            // directive there (#endregion, say) stays at the start of its line.
            MemberDeclarationSyntax[] generated = members
                .Select(m =>
                    SyntaxFactory.ParseMemberDeclaration(m)!.WithLeadingTrivia(SyntaxFactory.Space)
                        .WithTrailingTrivia(SyntaxFactory.Space))
                .ToArray();
            generated[0] = generated[0].WithLeadingTrivia(visited.CloseBraceToken.LeadingTrivia);
            visited = visited.WithCloseBraceToken(visited.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.Space));
            return visited.AddMembers(generated);
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (Sync(node) is not { } sync)
            {
                return base.VisitPropertyDeclaration(node);
            }

            Changed = true;
            AccessorDeclarationSyntax getter =
                node.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            AccessorListSyntax accessors;
            if (sync.Collection)
            {
                accessors = node.AccessorList.ReplaceNode(getter,
                    WithArrow(getter, $"{sync.Descriptor}.Bind(this)"));
            }
            else
            {
                AccessorDeclarationSyntax setter =
                    node.AccessorList.Accessors.First(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
                accessors = node.AccessorList.ReplaceNodes([getter, setter], (original, _) => original == getter
                    ? WithArrow(original, $"this.{sync.Field}")
                    : WithArrow(original, $"{sync.Descriptor}.Set(this, ref this.{sync.Field}, value)"));
            }

            // The initializer moves to the field. Keep the line breaks it spanned, and what followed it, on the property.
            if (node.Initializer != null)
            {
                SyntaxTriviaList trailing = LineBreaksIn(accessors.CloseBraceToken.TrailingTrivia
                        .Concat(node.Initializer.DescendantTrivia())
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
            {
                return base.VisitMethodDeclaration(node);
            }

            Changed = true;
            StringBuilder prologue = new();
            prologue.Append(rpc.Static
                ? $"if ({rpc.Descriptor}.Begin(out {WriterType} __rpc_w)) {{ using (__rpc_w) {{ "
                : $"if ({rpc.Descriptor}.Begin(this, out {WriterType} __rpc_w)) {{ ");
            prologue.Append("using (global::Prowl.Runtime.Diagnostics.FrameStats.Network.Scope()) { ");
            foreach (ParameterSyntax parameter in node.ParameterList.Parameters)
            {
                prologue.Append(
                    $"{Serializer}<{OneLine(parameter.Type!)}>.Write(__rpc_w, {parameter.Identifier.Text}); ");
            }

            prologue.Append(rpc.Static
                ? $"if (!{rpc.Descriptor}.Send(__rpc_w)) return; }} }} }} "
                : $"if (!{rpc.Descriptor}.Send(this, __rpc_w)) return; }} }} ");
            StatementSyntax check = SyntaxFactory.ParseStatement(prologue.ToString());

            // Inserted right after the opening brace, on its line, taking over whatever followed the brace, so
            // the body's own lines don't change at all.
            if (node.Body != null)
            {
                SyntaxToken open = node.Body.OpenBraceToken;
                StatementSyntax placed = check.WithLeadingTrivia(SyntaxFactory.Space)
                    .WithTrailingTrivia(open.TrailingTrivia);
                BlockSyntax body = node.Body.WithOpenBraceToken(open.WithTrailingTrivia(SyntaxFactory.TriviaList()));
                return node.WithBody(body.WithStatements(body.Statements.Insert(0, placed)));
            }

            // The block opens where the arrow was and the expression keeps everything around it, so it stays on its own line.
            ArrowExpressionClauseSyntax arrow = node.ExpressionBody!;
            ExpressionStatementSyntax statement = SyntaxFactory.ExpressionStatement(
                arrow.Expression.WithLeadingTrivia(
                    arrow.ArrowToken.TrailingTrivia.AddRange(arrow.Expression.GetLeadingTrivia())),
                node.SemicolonToken.WithTrailingTrivia(SyntaxFactory.Space));
            BlockSyntax block = SyntaxFactory.Block(check.WithTrailingTrivia(), statement)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithTrailingTrivia(node.SemicolonToken.TrailingTrivia));
            return node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block);
        }

        /// <summary>
        ///     The members one class part gets for the [Sync] properties and RPCs it declares: backing fields and
        ///     descriptors, each on one line.
        /// </summary>
        private List<string> GenerateMembers(ClassDeclarationSyntax part)
        {
            List<string> members = new();
            string self = part.Identifier.Text +
                          (part.TypeParameterList == null ? "" : OneLine(part.TypeParameterList));
            foreach (MemberDeclarationSyntax member in part.Members)
            {
                if (member is PropertyDeclarationSyntax property && Sync(property) is { } sync)
                {
                    string flags = $"({SyncFlagsType}){sync.Flags}u";
                    if (sync.Collection)
                    {
                        members.Add($"private readonly {sync.Type} {sync.Field} = {sync.Initializer};");
                        members.Add(
                            $"{Descriptor} global::Prowl.Runtime.Networking.SyncCollectionMember<{self}, {sync.Type}> {sync.Descriptor} = new("
                            + $"{sync.Slot}u, {SymbolDisplay.FormatLiteral(sync.Name, true)}, {flags}, static __c => __c.{sync.Field});");
                        continue;
                    }

                    members.Add(
                        $"private {sync.Type} {sync.Field}{(sync.Initializer == null ? "" : " = " + sync.Initializer)};");
                    members.Add(
                        $"{Descriptor} global::Prowl.Runtime.Networking.SyncMember<{self}, {sync.Type}> {sync.Descriptor} = new({sync.Slot}u, "
                        + $"{SymbolDisplay.FormatLiteral(sync.Name, true)}, {flags}, static __c => __c.{sync.Field}, static (__c, __v) => __c.{sync.Field} = __v"
                        + (sync.Hook == null ? "" : ", " + sync.Hook) + ");");
                }
                else if (member is MethodDeclarationSyntax method && Rpc(method) is { } rpc)
                {
                    StringBuilder invoke = new(rpc.Static
                        ? $"static (ref {ReaderType} __r, bool __execute) => {{ "
                        : $"static ({self} __c, ref {ReaderType} __r, bool __execute) => {{ ");
                    for (int i = 0; i < rpc.ParameterTypes.Count; i++)
                    {
                        invoke.Append(
                            $"{rpc.ParameterTypes[i]} __a{i} = {Serializer}<{rpc.ParameterTypes[i]}>.Read(ref __r); ");
                    }

                    string argumentsRead = rpc.Static
                        ? "global::Prowl.Runtime.Networking.StaticRpcMember.ArgumentsRead()"
                        : "global::Prowl.Runtime.Networking.RpcMember.ArgumentsRead()";
                    string target = rpc.Static ? self : "__c";
                    invoke.Append(
                        $"if (!__execute) return; {argumentsRead}; {target}.{rpc.Name}({string.Join(", ", Enumerable.Range(0, rpc.ParameterTypes.Count).Select(i => "__a" + i))}); }}");
                    string descriptorType = rpc.Static ? "StaticRpcMember" : $"RpcMember<{self}>";
                    string declaringType = rpc.Static ? $"typeof({self}), " : "";
                    members.Add(
                        $"{Descriptor} global::Prowl.Runtime.Networking.{descriptorType} {rpc.Descriptor} = new({declaringType}{rpc.Id}u, "
                        + $"{SymbolDisplay.FormatLiteral(rpc.Signature, true)}, {RpcModeType}.{rpc.Mode}, ({RpcFlagsType}){rpc.Flags}u, {invoke}"
                        + (rpc.Channel == 0 ? "" : $", {rpc.Channel}") + ");");
                }
            }

            return members;
        }

        /// <summary>The property's [Sync] description, or null if it isn't one or is misused (reported, once).</summary>
        private SyncInfo? Sync(PropertyDeclarationSyntax node)
        {
            if (_syncs.TryGetValue(node, out SyncInfo? known))
            {
                return known;
            }

            return _syncs[node] = node.AttributeLists.Count == 0 ? null : AnalyzeSync(node);
        }

        private RpcInfo? Rpc(MethodDeclarationSyntax node)
        {
            if (_rpcs.TryGetValue(node, out RpcInfo? known))
            {
                return known;
            }

            return _rpcs[node] = node.AttributeLists.Count == 0 ? null : AnalyzeRpc(node);
        }

        private SyncInfo? AnalyzeSync(PropertyDeclarationSyntax node)
        {
            if (model.GetDeclaredSymbol(node) is not { } property)
            {
                return null;
            }

            Symbols symbols = context.Symbols;
            Location location = node.Identifier.GetLocation();
            string name = property.Name;
            AttributeData? sync = Find(property, symbols.Sync);
            AttributeData? change = Find(property, symbols.Change);
            if (sync == null)
            {
                if (change != null)
                {
                    context.Report(s_changeHook, location, name, "only works on a [Sync] property");
                }

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
            bool getAndSet =
                accessors.Accessors.Count == 2 && accessors.Accessors.Any(a =>
                                                   a.IsKind(SyntaxKind.GetAccessorDeclaration))
                                               && accessors.Accessors.Any(a =>
                                                   a.IsKind(SyntaxKind.SetAccessorDeclaration));
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
                {
                    context.Report(s_collectionShape, location, name,
                        "is get-only, which only a NetList, NetDictionary or NetSet can be. Give it a setter: { get; set; }");
                }
                else
                {
                    context.Report(s_syncShape, location, name);
                }

                return null;
            }

            // A collection sends its items, keys and values, never itself.
            if (collection && property.Type is INamedTypeSymbol collectionType)
            {
                foreach (ITypeSymbol item in collectionType.TypeArguments)
                {
                    context.RequireSendable(item, name, location);
                }
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

            uint flags = sync.ConstructorArguments
                .Concat(sync.NamedArguments.Where(a => a.Key == "Flags").Select(a => a.Value))
                .Select(a => Convert.ToUInt32(a.Value)).LastOrDefault();
            context.ClaimId(property.ContainingType, false, name, location);
            return new SyncInfo(name, OneLine(node.Type), NetworkId(name), flags,
                node.Initializer == null ? null : OneLine(node.Initializer.Value),
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
                candidates.AddRange(t.GetMembers(method).OfType<IMethodSymbol>().Where(m => !m.IsStatic &&
                    m.TypeParameters.IsEmpty
                    && context.Compilation.IsSymbolAccessibleWithin(m, property.ContainingType)));
            }

            bool TakesValues(IMethodSymbol m) => m.Parameters.Length == 2 && m.Parameters.All(p =>
                p.RefKind == RefKind.None
                && SymbolEqualityComparer.Default.Equals(p.Type, property.Type));

            if (candidates.Any(TakesValues))
            {
                return $"static (__c, __o, __n) => __c.{method}(__o, __n)";
            }

            if (candidates.Any(m => m.Parameters.IsEmpty))
            {
                return $"static (__c, __o, __n) => __c.{method}()";
            }

            string type = property.Type.ToDisplayString();
            problem =
                $"names '{method}', which must be a method of the component taking ({type} oldValue, {type} newValue), or nothing";
            return null;
        }

        private RpcInfo? AnalyzeRpc(MethodDeclarationSyntax node)
        {
            if (model.GetDeclaredSymbol(node) is not { } method
                || method.GetAttributes().FirstOrDefault(a => DerivesFrom(a.AttributeClass, context.Symbols.Rpc)) is not
                    { AttributeClass: { } kind } rpc)
            {
                return null;
            }

            Location location = node.Identifier.GetLocation();
            if (node.Parent is not ClassDeclarationSyntax ||
                (!method.IsStatic && !DerivesFrom(method.ContainingType, context.Symbols.Component)))
            {
                context.Report(s_notInClass, location, method.Name);
                return null;
            }

            bool plainParameters = method.Parameters.All(p => p.RefKind == RefKind.None && !p.IsParams && !p.IsThis);
            if (!method.ReturnsVoid || !plainParameters || method.IsGenericMethod ||
                (node.Body == null && node.ExpressionBody == null))
            {
                context.Report(s_rpcShape, location, method.Name);
                return null;
            }

            uint flags = rpc.ConstructorArguments.Select(a => Convert.ToUInt32(a.Value)).FirstOrDefault();
            byte channel = rpc.NamedArguments.Where(a => a.Key == "Channel")
                .Select(a => Convert.ToByte(a.Value.Value)).LastOrDefault();
            if (channel != 0 && (flags & 1u) != 0)
            {
                context.Report(s_unreliableChannel, location, method.Name);
            }

            if (method.IsStatic && (kind.Name == "OwnerAttribute" || (flags & 4u) != 0 ||
                                    GenericContainer(method.ContainingType)))
            {
                context.Report(s_staticRpc, location, method.Name);
                return null;
            }

            foreach (IParameterSymbol parameter in method.Parameters)
            {
                context.RequireSendable(parameter.Type, parameter.Name,
                    parameter.Locations.FirstOrDefault() ?? location);
            }

            // Hashed as written, so the id only changes when the declaration does.
            List<string> types = node.ParameterList.Parameters.Select(p => OneLine(p.Type!)).ToList();
            string key = $"{method.Name}({string.Join(",", types)})";
            context.ClaimId(method.ContainingType, true, key, location);
            return new RpcInfo(method.Name, NetworkId(key), kind.Name[..^"Attribute".Length], flags, types,
                method.IsStatic, channel);
        }

        private static bool GenericContainer(INamedTypeSymbol? type)
        {
            for (; type != null; type = type.ContainingType)
            {
                if (type.IsGenericType)
                {
                    return true;
                }
            }

            return false;
        }

        private static AttributeData? Find(ISymbol symbol, INamedTypeSymbol attribute) =>
            symbol.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
    }
}
