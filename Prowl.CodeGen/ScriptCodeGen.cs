// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;

using Prowl.CodeGen.Networking;

namespace Prowl.CodeGen;

/// <summary>
/// One pass of script code generation. It sees the scripts bound, so it knows what every attribute, type and
/// method really is, and may rewrite them in place (keeping every line where it was, so errors, stack traces
/// and breakpoints still point at the code as written) and add files of its own.
/// </summary>
internal interface IScriptGenerator
{
    /// <summary>Generates for <paramref name="trees"/>, which <paramref name="compilation"/> is made of.</summary>
    ScriptCodeGen.Result Generate(Compilation compilation, IReadOnlyList<SyntaxTree> trees);
}

/// <summary>
/// Runs every script generator over one assembly's scripts, the way both the editor's script compiler and
/// <c>dotnet build</c> (for projects that set <c>ProwlCodeGen</c>) compile them.
/// </summary>
internal static class ScriptCodeGen
{
    /// <summary>
    /// The scripts after generation, in the order given; the files the generators added; and what was wrong.
    /// </summary>
    public sealed record Result(IReadOnlyList<SyntaxTree> Trees, IReadOnlyList<SyntaxTree> Added, IReadOnlyList<Diagnostic> Diagnostics);

    /// <summary>The passes, in the order they run. Each sees what the ones before it generated.</summary>
    private static readonly IScriptGenerator[] s_generators = [new NetworkCodeGen()];

    /// <summary>
    /// Runs the generators over <paramref name="trees"/>, which <paramref name="compilation"/> must be made of,
    /// as written. A file no generator touches comes back as the very same tree.
    /// </summary>
    public static Result Run(Compilation compilation, IReadOnlyList<SyntaxTree> trees)
    {
        IReadOnlyList<SyntaxTree> current = trees;
        List<SyntaxTree> added = new();
        List<Diagnostic> diagnostics = new();
        for (int i = 0; i < s_generators.Length; i++)
        {
            Result result = s_generators[i].Generate(compilation, current);
            diagnostics.AddRange(result.Diagnostics);
            added.AddRange(result.Added);

            // The next pass binds what this one made. The last needs no compilation after it.
            if (i < s_generators.Length - 1)
            {
                for (int t = 0; t < current.Count; t++)
                {
                    if (!ReferenceEquals(current[t], result.Trees[t]))
                        compilation = compilation.ReplaceSyntaxTree(current[t], result.Trees[t]);
                }
                compilation = compilation.AddSyntaxTrees(result.Added);
            }
            current = result.Trees;
        }
        return new Result(current, added, diagnostics);
    }

    /// <summary>A generator's result when there's nothing for it to do.</summary>
    internal static Result Unchanged(IReadOnlyList<SyntaxTree> trees) => new(trees, [], []);
}
