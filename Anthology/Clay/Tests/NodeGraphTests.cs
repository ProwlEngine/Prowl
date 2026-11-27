// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Model files are untrusted input, so a malformed node graph has to come back as an
/// <see cref="ImportException"/>. The walks used to be plain recursion with no visited set and no
/// depth cap, and a StackOverflowException cannot be caught, so a cyclic or very deep file took the
/// whole process down.
/// </summary>
public sealed class NodeGraphTests
{
    private static Model Load(string nodes, string sceneNodes = "[ 0 ]")
    {
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": {{sceneNodes}} } ],
          "nodes": {{nodes}}
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw);
    }

    /// <summary>A chain of <paramref name="count"/> nodes, each the only child of the one before.</summary>
    private static string Chain(int count)
    {
        var parts = new List<string>(count);
        for (int i = 0; i < count; i++)
            parts.Add(i == count - 1
                ? $$"""{ "name": "N{{i}}" }"""
                : $$"""{ "name": "N{{i}}", "children": [ {{i + 1}} ] }""");
        return "[ " + string.Join(", ", parts) + " ]";
    }

    // ---------------------------------------------------------------- cycles

    // Each node in a ring takes its one legal parent, so nothing looks wrong link by link. No member
    // is ever a root child either, which keeps the ring out of the depth-first walk entirely. Without
    // the ancestor pass the file would import as silently missing geometry.
    [Fact]
    public void TwoNodesParentingEachOther_ThrowsRatherThanRecursingForever()
    {
        var ex = Assert.Throws<ImportException>(() => Load("""
        [ { "name": "A", "children": [ 1 ] }, { "name": "B", "children": [ 0 ] } ]
        """));

        Assert.Contains("own ancestor", ex.Message);
    }

    [Fact]
    public void LongerRingOfNodes_IsAlsoReported()
    {
        var ex = Assert.Throws<ImportException>(() => Load("""
        [ { "name": "A", "children": [ 1 ] },
          { "name": "B", "children": [ 2 ] },
          { "name": "C", "children": [ 0 ] },
          { "name": "Root" } ]
        """, "[ 3 ]"));

        Assert.Contains("own ancestor", ex.Message);
    }

    [Fact]
    public void NodeListingItself_Throws()
    {
        var ex = Assert.Throws<ImportException>(() => Load("""
        [ { "name": "A", "children": [ 0 ] } ]
        """));

        Assert.Contains("itself", ex.Message);
    }

    // ---------------------------------------------------------------- aliasing

    /// <summary>
    /// A node in two children lists ends up with one Parent pointer naming one of them while sitting
    /// in both, so the walk reaches it twice and it lands in the scene twice under conflicting
    /// transforms.
    /// </summary>
    [Fact]
    public void NodeSharedByTwoParents_Throws()
    {
        var ex = Assert.Throws<ImportException>(() => Load("""
        [ { "name": "Root", "children": [ 1, 2 ] },
          { "name": "A", "children": [ 3 ] },
          { "name": "B", "children": [ 3 ] },
          { "name": "Shared" } ]
        """));

        Assert.Contains("more than one node", ex.Message);
    }

    // The scene lists roots, so a node that is also someone's child is already reachable. It has to
    // appear once, not twice.
    [Fact]
    public void NodeListedAsSceneRootAndAsAChild_AppearsOnce()
    {
        var model = Load("""
        [ { "name": "Root", "children": [ 1 ] }, { "name": "Child" } ]
        """, "[ 0, 1 ]");

        Assert.Single(model.Nodes, n => n.Name == "Child");
    }

    // ---------------------------------------------------------------- depth

    [Fact]
    public void HierarchyDeeperThanTheCap_ThrowsRatherThanExhaustingTheStack()
    {
        var ex = Assert.Throws<ImportException>(() => Load(Chain(2100)));

        Assert.Contains("deeper than", ex.Message);
    }

    // Depth is capped well above anything a real skeleton reaches, so an ordinary deep chain imports.
    [Fact]
    public void DeepButLegalHierarchy_ImportsIntact()
    {
        var model = Load(Chain(500));

        Assert.Equal(501, model.Nodes.Count); // the chain plus the synthetic root
    }

    [Fact]
    public void SiblingsKeepTheirAuthoredOrder()
    {
        var model = Load("""
        [ { "name": "Root", "children": [ 1, 2, 3 ] },
          { "name": "First" }, { "name": "Second" }, { "name": "Third" } ]
        """);

        var names = model.Nodes.Where(n => n.Name.Length > 0 && n.Name != "<RootNode>" && n.Name != "Root")
                               .Select(n => n.Name).ToArray();
        Assert.Equal(["First", "Second", "Third"], names);
    }
}
