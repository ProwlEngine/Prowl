// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;

using Xunit;

namespace Prowl.Origami.CSharp.Tests;

public class AnalyzerTests
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class NoBadNamesAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = new("TEST001", "Bad name", "Type '{0}' is named Bad",
            "Naming", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(c =>
            {
                if (c.Symbol.Name == "Bad")
                    c.ReportDiagnostic(Diagnostic.Create(Rule, c.Symbol.Locations[0], c.Symbol.Name));
            }, SymbolKind.NamedType);
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new("TEST002", "Throws", "Throws", "Test", DiagnosticSeverity.Warning, true);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
        public override void Initialize(AnalysisContext context)
            => context.RegisterSyntaxTreeAction(_ => throw new InvalidOperationException("boom"));
    }

    [Fact]
    public async Task WorkspaceAnalyzersReportInDiagnostics()
    {
        var workspace = new CSharpWorkspace(CSharpWorkspace.GetPlatformReferences())
        {
            Analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new NoBadNamesAnalyzer()),
        };
        var session = new RoslynCSharpSession(workspace, null, "class Good { } class Bad { }");

        var diagnostics = await session.GetDiagnosticsAsync(default);

        var hit = Assert.Single(diagnostics, d => d.Id == "TEST001");
        Assert.Equal(CodeDiagnosticSeverity.Warning, hit.Severity);
        Assert.Equal("Bad", "class Good { } class Bad { }".Substring(hit.Start, hit.Length));
    }

    [Fact]
    public async Task ThrowingAnalyzerKeepsCompilerDiagnostics()
    {
        var workspace = new CSharpWorkspace(CSharpWorkspace.GetPlatformReferences())
        {
            Analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ThrowingAnalyzer()),
        };
        var session = new RoslynCSharpSession(workspace, null, "class C { void M() { undefinedName(); } }");

        var diagnostics = await session.GetDiagnosticsAsync(default);

        Assert.Contains(diagnostics, d => d.Id == "CS0103");
    }
}
