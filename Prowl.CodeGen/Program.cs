// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Prowl.CodeGen;

/// <summary>
///     Runs script code generation for <c>dotnet build</c> (see build/Prowl.CodeGen.targets): reads a project's
///     sources and references from a response file, writes what the generators made, and lists the files to
///     compile in their place. Problems are printed the way MSBuild reports compiler errors.
/// </summary>
/// <remarks>
///     The response file has one <c>key:value</c> per line: <c>out</c> (the folder to write to), <c>name</c>,
///     <c>lang</c>, <c>nullable</c>, <c>unsafe</c>, and any number of <c>define</c>, <c>ref</c> and <c>src</c>.
///     A rewritten file starts with a <c>#line</c> directive naming the original, and keeps its lines, so the
///     compiler's errors and the debugger point at the code as written.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("usage: Prowl.CodeGen <response file>");
            return 2;
        }

        Dictionary<string, List<string>> options = new();
        foreach (string line in File.ReadAllLines(args[0]))
        {
            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                string key = line[..colon];
                if (!options.TryGetValue(key, out List<string>? values))
                {
                    options[key] = values = new List<string>();
                }

                values.Add(line[(colon + 1)..].Trim());
            }
        }

        string One(string key, string fallback = "") =>
            options.TryGetValue(key, out List<string>? v) && v.Count > 0 ? v[^1] : fallback;

        List<string> All(string key) => options.TryGetValue(key, out List<string>? v) ? v : [];

        string output = Path.GetFullPath(One("out"));
        Directory.CreateDirectory(output);

        LanguageVersionFacts.TryParse(One("lang", "latest"), out LanguageVersion language);
        CSharpParseOptions parse =
            new CSharpParseOptions(language).WithPreprocessorSymbols(All("define").Where(d => d.Length > 0));
        List<string> sources = All("src").Select(Path.GetFullPath).ToList();
        List<SyntaxTree> trees = sources
            .Select(path =>
                CSharpSyntaxTree.ParseText(
                    SourceText.From(File.ReadAllBytes(path), File.ReadAllBytes(path).Length, Encoding.UTF8), parse,
                    path))
            .ToList();

        NullableContextOptions nullable = One("nullable").ToLowerInvariant() switch
        {
            "enable" => NullableContextOptions.Enable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            _ => NullableContextOptions.Disable
        };
        CSharpCompilation compilation = CSharpCompilation.Create(One("name", "Game"), trees,
            All("ref").Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullable)
                .WithAllowUnsafe(string.Equals(One("unsafe"), "true", StringComparison.OrdinalIgnoreCase)));

        ScriptCodeGen.Result result = ScriptCodeGen.Run(compilation, trees);

        HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
        List<string> compile = new();
        for (int i = 0; i < trees.Count; i++)
        {
            if (ReferenceEquals(trees[i], result.Trees[i]))
            {
                compile.Add(sources[i]);
                continue;
            }

            string path = Path.Combine(output, $"{i:D4}.{Path.GetFileName(sources[i])}");
            WriteIfChanged(path,
                $"#line 1 {SymbolDisplay.FormatLiteral(sources[i], true)}\n{result.Trees[i].GetText()}");
            compile.Add(path);
            written.Add(path);
        }

        for (int i = 0; i < result.Added.Count; i++)
        {
            string path = Path.Combine(output, $"Generated.{i}.{Path.GetFileName(result.Added[i].FilePath)}");
            WriteIfChanged(path, result.Added[i].GetText().ToString());
            compile.Add(path);
            written.Add(path);
        }

        // Files a previous run generated and this one didn't would otherwise linger in the folder.
        foreach (string stale in Directory.GetFiles(output, "*.cs").Where(f => !written.Contains(f)))
        {
            File.Delete(stale);
        }

        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            Console.WriteLine(Format(diagnostic));
        }

        // The build skips this step while the list is newer than every input, so the list is written on every
        // run that succeeds and is gone after one that doesn't, which then runs again and says why.
        string list = Path.Combine(output, "compile.txt");
        if (result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            File.Delete(list);
            return 1;
        }

        File.WriteAllText(list, string.Join("\n", compile));
        return 0;
    }

    /// <summary>Leaves a file alone when its text is the same, so the compile after it can be skipped too.</summary>
    private static void WriteIfChanged(string path, string text)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }

    /// <summary>As MSBuild reads a compiler's errors: <c>file(line,column): error ID: message</c>.</summary>
    private static string Format(Diagnostic diagnostic)
    {
        string severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
        string where = span.IsValid
            ? $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): "
            : "";
        return $"{where}{severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
    }
}
