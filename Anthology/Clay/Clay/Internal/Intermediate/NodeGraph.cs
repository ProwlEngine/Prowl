// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Traversal helpers for the <see cref="IntermediateNode"/> hierarchy.
/// </summary>
/// <remarks>
/// Model files are untrusted input, so a malformed or hostile one must fail as an
/// <see cref="ImportException"/> the caller can catch. Plain recursion cannot do that: a cyclic
/// child graph recurses forever and a deep enough chain exhausts the stack, and a
/// StackOverflowException takes the whole process down with no chance to handle it.
/// </remarks>
internal static class NodeGraph
{
    /// <summary>
    /// Deepest hierarchy accepted. Well past anything a real skeleton or DCC grouping tree reaches,
    /// and low enough that the post-process steps still walking the tree recursively stay within
    /// their stack.
    /// </summary>
    public const int MaxDepth = 1024;

    /// <summary>
    /// Flattens the hierarchy under <paramref name="root"/> into <paramref name="into"/> in
    /// depth-first pre-order, so a parent always precedes its children.
    /// </summary>
    /// <exception cref="ImportException">
    /// The graph is not a tree (a node is reachable twice) or is deeper than <see cref="MaxDepth"/>.
    /// </exception>
    public static void Flatten(IntermediateNode root, List<IntermediateNode> into)
    {
        var visited = new HashSet<IntermediateNode>();
        var stack = new Stack<(IntermediateNode Node, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            if (!visited.Add(node))
                throw new ImportException(
                    $"Node '{node.Name}' is reachable more than once, so the hierarchy is not a tree.");

            if (depth > MaxDepth)
                throw new ImportException(
                    $"Node hierarchy is deeper than {MaxDepth} levels at '{node.Name}'.");

            into.Add(node);

            // Reversed so the children come off the stack in their authored order.
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push((node.Children[i], depth + 1));
        }
    }

    /// <summary>
    /// Verifies no parent chain among <paramref name="nodes"/> loops back on itself.
    /// </summary>
    /// <remarks>
    /// A reader that keeps each node to a single parent cannot produce a cycle reachable from the
    /// scene root, because every node in one has a parent inside it and so is never a root child.
    /// That makes the cycle invisible to <see cref="Flatten"/> and turns a malformed file into
    /// silently missing geometry, which is far harder to diagnose than an outright failure.
    /// </remarks>
    /// <exception cref="ImportException">A node is its own ancestor.</exception>
    public static void ValidateNoCycles(IEnumerable<IntermediateNode> nodes)
    {
        var settled = new HashSet<IntermediateNode>();
        var chain = new HashSet<IntermediateNode>();

        foreach (var start in nodes)
        {
            if (settled.Contains(start)) continue;
            chain.Clear();

            for (var node = start; node is not null; node = node.Parent)
            {
                if (settled.Contains(node)) break;
                if (!chain.Add(node))
                    throw new ImportException($"Node '{node.Name}' is its own ancestor.");
            }

            settled.UnionWith(chain);
        }
    }
}
