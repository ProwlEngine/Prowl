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

            // A component that writes its own state takes one of the values a type may have, with or without a
            // [Sync] of its own to bring the count up.
            if (model.GetDeclaredSymbol(node) is INamedTypeSymbol declared &&
                declared.GetMembers("OnSerialize").OfType<IMethodSymbol>().Any(m => m.IsOverride) &&
                DerivesFrom(declared, context.Symbols.NetworkBehaviour))
            {
                context.CountSyncs(declared, node.Identifier.GetLocation());
            }

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
                return Guard(node, false) is { } guard ? WithChecks(node, [guard], false) : base.VisitMethodDeclaration(node);
            }

            Guard(node, true);
            Changed = true;
            StringBuilder prologue = new();
            prologue.Append($"if ({rpc.Descriptor}.Begin(this, out {WriterType} __rpc_w)) {{ ");
            prologue.Append("using (global::Prowl.Runtime.Diagnostics.FrameStats.Network.Scope()) { ");
            foreach (ParameterSyntax parameter in node.ParameterList.Parameters.Skip(rpc.Targeted ? 1 : 0))
            {
                prologue.Append(
                    $"{Serializer}<{OneLine(parameter.Type!)}>.Write(__rpc_w, {parameter.Identifier.Text}); ");
            }

            string sendTo = rpc.Targeted ? $", {node.ParameterList.Parameters[0].Identifier.Text}" : "";
            prologue.Append($"if (!{rpc.Descriptor}.Send(this, __rpc_w{sendTo})) return; }} }} ");
            List<StatementSyntax> checks = [SyntaxFactory.ParseStatement(prologue.ToString())];

            // RPCs can be overridden: a base call made from the running body runs here, not sent again.
            checks.Add(SyntaxFactory.ParseStatement(
                $"using global::Prowl.Runtime.Networking.RpcBodyScope __rpc_body = {rpc.Descriptor}.EnterBody(this); "));
            return WithChecks(node, checks, true);
        }

        /// <summary>
        ///     Puts <paramref name="checks" /> at the top of the method's body, on the line its body opens on, so no line
        ///     of the method moves. An expression body becomes a block on the arrow's line.
        /// </summary>
        private MethodDeclarationSyntax WithChecks(MethodDeclarationSyntax node, List<StatementSyntax> checks, bool returnsVoid)
        {
            Changed = true;

            // Inserted right after the opening brace, on its line, taking over whatever followed the brace, so
            // the body's own lines don't change at all.
            if (node.Body != null)
            {
                SyntaxToken open = node.Body.OpenBraceToken;
                List<StatementSyntax> placed = checks.Select(c => c.WithLeadingTrivia(SyntaxFactory.Space)).ToList();
                placed[^1] = placed[^1].WithTrailingTrivia(open.TrailingTrivia);
                BlockSyntax body = node.Body.WithOpenBraceToken(open.WithTrailingTrivia(SyntaxFactory.TriviaList()));
                return node.WithBody(body.WithStatements(body.Statements.InsertRange(0, placed)));
            }

            // The block opens where the arrow was and the expression keeps everything around it, so it stays on its own line.
            ArrowExpressionClauseSyntax arrow = node.ExpressionBody!;
            ExpressionSyntax expression = arrow.Expression.WithLeadingTrivia(
                arrow.ArrowToken.TrailingTrivia.AddRange(arrow.Expression.GetLeadingTrivia()));
            SyntaxToken semicolon = node.SemicolonToken.WithTrailingTrivia(SyntaxFactory.Space);
            StatementSyntax statement = expression is ThrowExpressionSyntax thrown
                ? SyntaxFactory.ThrowStatement(default, thrown.ThrowKeyword, thrown.Expression, semicolon)
                : returnsVoid || ReturnsNothing(node)
                    ? SyntaxFactory.ExpressionStatement(expression, semicolon)
                    : SyntaxFactory.ReturnStatement(SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space), expression, semicolon);
            List<StatementSyntax> statements = checks.Select(c => c.WithTrailingTrivia(SyntaxFactory.Space)).ToList();
            statements[^1] = statements[^1].WithTrailingTrivia();
            statements.Add(statement);
            BlockSyntax block = SyntaxFactory.Block(statements)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithTrailingTrivia(node.SemicolonToken.TrailingTrivia));
            return node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block);
        }

        /// <summary>
        ///     The check a [Server] or [Client] method starts with, or null when it has neither or misuses one, which is
        ///     reported. An RPC only gets the report: its own rules say where it runs.
        /// </summary>
        private StatementSyntax? Guard(MethodDeclarationSyntax node, bool rpc)
        {
            if (node.AttributeLists.Count == 0 || model.GetDeclaredSymbol(node) is not { } method)
            {
                return null;
            }

            AttributeData? server = Find(method, context.Symbols.Server), client = Find(method, context.Symbols.Client);
            if ((server ?? client) is not { } guard)
            {
                return null;
            }

            string kind = server != null ? "Server" : "Client";
            Location location = node.Identifier.GetLocation();
            string? problem = server != null && client != null ? "can't go with the other guard on the same method"
                : rpc ? "can't go on an RPC, whose kind already says where it runs"
                : !DerivesFrom(method.ContainingType, context.Symbols.NetworkBehaviour) ? "only works on a method of a NetworkBehaviour"
                : method.IsStatic ? "only works on an instance method"
                : node.Body == null && node.ExpressionBody == null ? "needs a method with a body"
                : null;
            if (problem != null)
            {
                context.Report(s_guardShape, location, method.Name, kind, problem);
                return null;
            }

            bool quiet = guard.NamedArguments.Any(a => a.Key == "Quiet" && a.Value.Value is true);
            string leave = IsIterator(node) ? "yield break;" : ReturnsNothing(node) ? "return;" : "return default!;";
            return SyntaxFactory.ParseStatement(
                $"if (!global::Prowl.Runtime.Networking.NetworkGuard.Allows(this, {SymbolDisplay.FormatLiteral(method.Name, true)}, "
                + $"{(server != null ? "true" : "false")}, {(quiet ? "true" : "false")})) {leave} ");
        }

        // True for a method whose body returns no value: void, and async ones returning a plain Task or ValueTask.
        private bool ReturnsNothing(MethodDeclarationSyntax node)
        {
            if (model.GetDeclaredSymbol(node) is not { } method)
            {
                return true;
            }

            if (method.ReturnsVoid)
            {
                return true;
            }

            return method.IsAsync && method.ReturnType is INamedTypeSymbol { IsGenericType: false } returned &&
                   returned.Name is "Task" or "ValueTask";
        }

        private static bool IsIterator(MethodDeclarationSyntax node) =>
            node.Body != null && node.Body
                .DescendantNodes(n => n is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
                .OfType<YieldStatementSyntax>().Any();

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
                    if (sync.Collection)
                    {
                        members.Add($"private readonly {sync.Type} {sync.Field} = {sync.Initializer};");
                        members.Add(
                            $"{Descriptor} global::Prowl.Runtime.Networking.SyncCollectionMember<{self}, {sync.Type}> {sync.Descriptor} = new("
                            + $"{sync.Slot}u, {SymbolDisplay.FormatLiteral(sync.Name, true)}, static __c => __c.{sync.Field});");
                        continue;
                    }

                    members.Add(
                        $"private {sync.Type} {sync.Field}{(sync.Initializer == null ? "" : " = " + sync.Initializer)};");
                    members.Add(
                        $"{Descriptor} global::Prowl.Runtime.Networking.SyncMember<{self}, {sync.Type}> {sync.Descriptor} = new({sync.Slot}u, "
                        + $"{SymbolDisplay.FormatLiteral(sync.Name, true)}, static __c => __c.{sync.Field}, static (__c, __v) => __c.{sync.Field} = __v"
                        + (sync.Hook == null ? "" : ", " + sync.Hook) + ");");
                }
                else if (member is MethodDeclarationSyntax method && Rpc(method) is { } rpc)
                {
                    StringBuilder invoke = new($"static ({self} __c, ref {ReaderType} __r, bool __execute) => {{ ");
                    List<string> sent = rpc.SentTypes.ToList();
                    for (int i = 0; i < sent.Count; i++)
                    {
                        invoke.Append($"{sent[i]} __a{i} = {Serializer}<{sent[i]}>.Read(ref __r); ");
                    }

                    const string argumentsRead = "global::Prowl.Runtime.Networking.RpcMember.ArgumentsRead()";
                    IEnumerable<string> arguments = Enumerable.Range(0, sent.Count).Select(i => "__a" + i);
                    if (rpc.Targeted)
                    {
                        arguments = arguments.Prepend("global::Prowl.Runtime.Networking.Networking.Session!.LocalPeer!");
                    }

                    invoke.Append(
                        $"if (!__execute) return; {argumentsRead}; __c.{rpc.Name}({string.Join(", ", arguments)}); }}");
                    members.Add(
                        $"{Descriptor} global::Prowl.Runtime.Networking.RpcMember<{self}> {rpc.Descriptor} = new({rpc.Id}u, "
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

            CheckOverride(node, node.Identifier);
            return _syncs[node] = node.AttributeLists.Count == 0 ? null : AnalyzeSync(node);
        }

        private RpcInfo? Rpc(MethodDeclarationSyntax node)
        {
            if (_rpcs.TryGetValue(node, out RpcInfo? known))
            {
                return known;
            }

            CheckOverride(node, node.Identifier);
            return _rpcs[node] = node.AttributeLists.Count == 0 ? null : AnalyzeRpc(node);
        }

        /// <summary>Reports an override that leaves out the [Sync] or RPC attribute of the member it overrides.</summary>
        private void CheckOverride(MemberDeclarationSyntax node, SyntaxToken identifier)
        {
            if (!node.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                return;
            }

            Symbols symbols = context.Symbols;
            switch (model.GetDeclaredSymbol(node))
            {
                case IMethodSymbol method when !HasRpc(method):
                    for (IMethodSymbol? m = method.OverriddenMethod; m != null; m = m.OverriddenMethod)
                    {
                        if (HasRpc(m))
                        {
                            context.Report(s_unnetworkedOverride, identifier.GetLocation(), method.Name, "[Rpc.*]");
                            return;
                        }
                    }

                    break;

                case IPropertySymbol property when Find(property, symbols.Sync) == null:
                    for (IPropertySymbol? p = property.OverriddenProperty; p != null; p = p.OverriddenProperty)
                    {
                        if (Find(p, symbols.Sync) != null)
                        {
                            context.Report(s_unnetworkedOverride, identifier.GetLocation(), property.Name, "[Sync]");
                            return;
                        }
                    }

                    break;
            }
        }

        private bool HasRpc(IMethodSymbol method) =>
            method.GetAttributes().Any(a => DerivesFrom(a.AttributeClass, context.Symbols.Rpc));

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

            if (node.Parent is not ClassDeclarationSyntax || !DerivesFrom(property.ContainingType, symbols.NetworkBehaviour))
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

            context.ClaimId(property.ContainingType, false, name, location);
            return new SyncInfo(name, OneLine(node.Type), NetworkId(name),
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
            if (method.IsStatic)
            {
                context.Report(s_staticRpc, location, method.Name);
                return null;
            }

            if (node.Parent is not ClassDeclarationSyntax || !DerivesFrom(method.ContainingType, context.Symbols.NetworkBehaviour))
            {
                context.Report(s_notInClass, location, method.Name);
                return null;
            }

            bool plainParameters = method.Parameters.All(p => p.RefKind == RefKind.None && !p.IsParams && !p.IsThis);
            if (!method.ReturnsVoid || !plainParameters || method.IsGenericMethod || method.IsAsync ||
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

            // Flags that only mean something for another kind of RPC, which compile and then do nothing.
            bool hostRpc = kind.Name == "HostAttribute", broadcast = kind.Name == "BroadcastAttribute";
            if ((flags & 4u) != 0 && !hostRpc)
            {
                context.Report(s_uselessFlag, location, method.Name, "AnyCaller", broadcast
                    ? "only the host calls a broadcast. For a client to reach everyone, have it call a host RPC that calls the broadcast"
                    : "only the host calls it");
            }

            if ((flags & 8u) != 0 && !broadcast)
            {
                context.Report(s_uselessFlag, location, method.Name, "ExcludeOwner",
                    "it skips the owner of the object a broadcast is called on");
            }

            bool targeted = kind.Name == "TargetAttribute";
            if (targeted && (method.Parameters.Length == 0 ||
                             method.Parameters[0].Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                                 .ToDisplayString() != "Prowl.Courier.NetPeer"))
            {
                context.Report(s_targetRpc, location, method.Name);
                return null;
            }

            foreach (IParameterSymbol parameter in method.Parameters.Skip(targeted ? 1 : 0))
            {
                context.RequireSendable(parameter.Type, parameter.Name,
                    parameter.Locations.FirstOrDefault() ?? location);
            }

            // Hashed as written, so the id only changes when the declaration does. An override is the method it overrides.
            List<string> types = node.ParameterList.Parameters.Select(p => OneLine(p.Type!)).ToList();
            string key = $"{method.Name}({string.Join(",", WrittenParameterTypes(RootDeclaration(method)) ?? types)})";
            context.ClaimId(method.ContainingType, true, key, location);
            return new RpcInfo(method.Name, NetworkId(key), kind.Name[..^"Attribute".Length], flags, types, channel,
                targeted);
        }

        private static MethodDeclarationSyntax? RootDeclaration(IMethodSymbol method)
        {
            IMethodSymbol root = method;
            while (root.OverriddenMethod is { } overridden)
            {
                root = overridden;
            }

            return root.OriginalDefinition.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault();
        }

        private static IEnumerable<string>? WrittenParameterTypes(MethodDeclarationSyntax? declaration) =>
            declaration?.ParameterList.Parameters.Select(p => OneLine(p.Type!));

        private static AttributeData? Find(ISymbol symbol, INamedTypeSymbol attribute) =>
            symbol.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
    }
}
