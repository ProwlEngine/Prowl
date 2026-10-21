// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Microsoft.CodeAnalysis;

using Xunit;

namespace Prowl.CodeGen.Test.Networking;

/// <summary>The rewrite keeps every line where it was, touches only what it must, and hands back the same tree for the same result.</summary>
public class RewriteTests
{
    [Fact]
    public void RewriteKeepsEveryLineWhereItWas()
    {
        SyntaxTree original = Scripts.Parse(TestScripts.Ship);
        SyntaxTree rewritten = Scripts.Generate(original).Trees[0];

        Assert.NotSame(original, rewritten);
        string[] before = original.GetText().ToString().Split('\n');
        string[] after = rewritten.GetText().ToString().Split('\n');
        Assert.Equal(before.Length, after.Length);

        // Lines the rewrite has no business with are untouched, and marked ones are still on their line.
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].Contains("Log.Add($\"fire") || before[i].Contains("#region") || before[i].Contains("#endregion")
                || before[i].Contains("public List<string> Log") || before[i].Contains("MARKER-AFTER"))
                Assert.Equal(before[i], after[i]);
            if (before[i].Contains("MARKER"))
                Assert.Contains("MARKER", after[i]);

            // Statements in RPC bodies, block or expression bodied, stay on their line for errors and breakpoints.
            int statement = before[i].IndexOf("Log.Add(", StringComparison.Ordinal);
            if (statement >= 0)
                Assert.Contains(before[i][statement..before[i].IndexOf(';', statement)], after[i]);
        }
        Assert.Contains(after, line => line.Contains("__Sync_Set(ref this.__sync_Health"));
    }

    [Fact]
    public void CollectionRewriteKeepsEveryLineWhereItWas()
    {
        SyntaxTree original = Scripts.Parse(TestScripts.Fleet);
        ScriptCodeGen.Result result = Scripts.Generate(original);
        Assert.Empty(result.Diagnostics);

        string[] before = original.GetText().ToString().Split('\n');
        string[] after = result.Trees[0].GetText().ToString().Split('\n');
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].Contains("public int Ammo;") || before[i].Contains("Log.Add("))
                Assert.Equal(before[i].Trim(), after[i].Trim());
            if (before[i].Contains("MARKER"))
                Assert.Contains("MARKER", after[i]);
        }
        Assert.Contains(after, line => line.Contains("__Sync_Collection(this.__sync_Crew, __syncnet_Crew)"));

        // The game's own types get their serializers in a file of their own.
        Assert.Contains("RegisterClass<global::Bow>", Assert.Single(result.Added).GetText().ToString());
    }

    [Fact]
    public void FilesWithoutNetworkingAreLeftAlone()
    {
        SyntaxTree tree = Scripts.Parse("public class Plain { public int Value { get; set; } }");
        ScriptCodeGen.Result result = Scripts.Generate(tree);
        Assert.Same(tree, result.Trees[0]);
        Assert.Empty(result.Added);
    }

    [Fact]
    public void PartialRewritesKeepLinesInEveryPart()
    {
        SyntaxTree a = Scripts.Parse(TestScripts.ShipPartA, "A.cs");
        SyntaxTree b = Scripts.Parse(TestScripts.ShipPartB, "B.cs");
        ScriptCodeGen.Result result = Scripts.Generate(b, a);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(TestScripts.ShipPartB.Split('\n').Length, result.Trees[0].GetText().ToString().Split('\n').Length);
        Assert.Equal(TestScripts.ShipPartA.Split('\n').Length, result.Trees[1].GetText().ToString().Split('\n').Length);

        // Each part carries the descriptors of its own members, and only those.
        Assert.Contains("__syncnet_Ammo", result.Trees[0].GetText().ToString());
        Assert.DoesNotContain("__syncnet_Health", result.Trees[0].GetText().ToString());
        Assert.Contains("__syncnet_Health", result.Trees[1].GetText().ToString());
    }

    [Fact]
    public void UnchangedFilesKeepTheirRewrittenTree()
    {
        SyntaxTree a = Scripts.Parse(TestScripts.ShipPartA, "A.cs");
        SyntaxTree b = Scripts.Parse(TestScripts.ShipPartB, "B.cs");
        SyntaxTree first = Scripts.Generate(a, b).Trees[0];
        Assert.Same(first, Scripts.Generate(a, b).Trees[0]);

        // A new [Sync] in another part leaves this part's rewrite as it was, so its tree is reused.
        SyntaxTree b2 = Scripts.Parse(TestScripts.ShipPartB.Replace("public int Ammo", "public int Ammo2 { get; set; } [Sync] public int Ammo"), "B.cs");
        Assert.Same(first, Scripts.Generate(a, b2).Trees[0]);
    }
}
