// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Prowl.Runtime;
using Prowl.Runtime.Networking;

using Xunit;

namespace Prowl.CodeGen.Test;

/// <summary>Compiles game scripts the way the script compiler and <c>dotnet build</c> both do: bound, generated, then emitted.</summary>
internal static class Scripts
{
    // Everything this test process runs on, the engine included.
    private static readonly Lazy<MetadataReference[]> s_references = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray());

    private static readonly CSharpParseOptions s_parse = new(LanguageVersion.Latest);

    public static SyntaxTree Parse(string source, string path = "Script.cs") =>
        CSharpSyntaxTree.ParseText(source, s_parse, path, Encoding.UTF8);

    /// <summary>Runs code generation over <paramref name="trees"/>, bound as written.</summary>
    public static ScriptCodeGen.Result Generate(params SyntaxTree[] trees) => ScriptCodeGen.Run(Bind(trees), trees);

    /// <summary>What generation and the compiler report for <paramref name="sources"/>, one file each, and the assembly if there were no errors.</summary>
    public static (Assembly? Assembly, IReadOnlyList<Diagnostic> Errors) Build(params string[] sources)
    {
        SyntaxTree[] trees = sources.Select((source, i) => Parse(source, $"Script{i}.cs")).ToArray();
        ScriptCodeGen.Result generated = Generate(trees);
        CSharpCompilation compilation = Bind([.. generated.Trees, .. generated.Added]);

        using MemoryStream image = new();
        IEnumerable<Diagnostic> compiler = compilation.Emit(image).Diagnostics;
        List<Diagnostic> errors = generated.Diagnostics.Concat(compiler).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return (errors.Count == 0 ? Assembly.Load(image.ToArray()) : null, errors);
    }

    /// <summary>Compiles <paramref name="sources"/>, which must have no errors, and loads them.</summary>
    public static Assembly Compile(params string[] sources)
    {
        (Assembly? assembly, IReadOnlyList<Diagnostic> errors) = Build(sources);
        Assert.True(assembly != null, string.Join("\n", errors));
        return assembly!;
    }

    /// <summary>A new component of <paramref name="type"/>, on an object of its own that <paramref name="use"/> gets and that's gone afterwards.</summary>
    public static void WithComponent(Type type, Action<Component> use)
    {
        GameObject go = new(type.Name);
        try
        {
            use((Component)go.AddComponent(type)!);
        }
        finally
        {
            go.Dispose();
        }
    }

    public static List<uint> Slots(Component component) => NetworkMembers.Of(component).Syncs.Select(s => s.Slot).ToList();

    // A fresh name each time, so assemblies of different tests never meet in one load context.
    private static CSharpCompilation Bind(IEnumerable<SyntaxTree> trees) =>
        CSharpCompilation.Create("Game" + Guid.NewGuid().ToString("N"), trees, s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
