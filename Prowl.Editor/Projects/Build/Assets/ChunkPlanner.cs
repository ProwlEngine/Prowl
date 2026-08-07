// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Build;

/// <summary>A named group of assets that ships together.</summary>
public sealed record AssetChunk(string Name, IReadOnlyList<Guid> Assets);

/// <summary>
/// Decides which assets ship together.
/// </summary>
/// <remarks>
/// Splitting purely on a byte ceiling produces archives whose contents have nothing to do with each
/// other, which cannot be streamed by level and cannot be shipped as downloadable content. Grouping by
/// the entry point that pulls an asset in gives archives that mean something: one per scene, one for
/// what several scenes share.
/// <para>
/// Every list this returns is ordered, because an archive whose contents shuffle between builds is a
/// patch that has to ship bytes nobody changed.
/// </para>
/// </remarks>
public static class ChunkPlanner
{
    public const string SharedChunk = "shared";

    /// <summary>Shipped, but nothing reachable pulls it in. Happens when a build ships every asset.</summary>
    public const string CommonChunk = "common";

    public const string ResourcesChunk = "resources";

    public static string SceneChunkName(Guid scene) => $"scene_{scene:N}";

    /// <summary>
    /// Groups <paramref name="shipped"/> by which entry points reach it. An asset reached by exactly one
    /// entry point joins that entry point's chunk; one reached by several joins the shared chunk.
    /// </summary>
    /// <param name="subAssets">
    /// Sub-assets by parent GUID. The dependency graph has no edge from a parent to the sub-assets it
    /// produced, so without this a texture's sprites reach no scene and every one of them falls into the
    /// common chunk instead of shipping beside the scene that pulled the texture in.
    /// </param>
    public static IReadOnlyList<AssetChunk> Plan(
        DependencyGraph dependencies,
        IReadOnlyList<Guid> scenes,
        IReadOnlyCollection<Guid> resources,
        IReadOnlySet<Guid> shipped,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> subAssets)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(subAssets);

        var owners = new Dictionary<Guid, string>();
        var contested = new HashSet<Guid>();

        void Claim(IEnumerable<Guid> assets, string chunk)
        {
            foreach (var guid in assets)
            {
                if (!shipped.Contains(guid)) continue;

                if (owners.TryGetValue(guid, out string? existing))
                {
                    if (existing != chunk) contested.Add(guid);
                    continue;
                }

                owners[guid] = chunk;
            }
        }

        foreach (var scene in scenes)
            Claim(ClosureOf(dependencies, [scene], subAssets), SceneChunkName(scene));

        if (resources.Count > 0)
            Claim(ClosureOf(dependencies, resources, subAssets), ResourcesChunk);

        foreach (var guid in contested)
            owners[guid] = SharedChunk;

        // Anything shipped that no entry point reached still has to go somewhere.
        foreach (var guid in shipped)
            owners.TryAdd(guid, CommonChunk);

        return owners
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .Select(group => new AssetChunk(group.Key, group.OrderBy(g => g).ToList()))
            .OrderBy(chunk => chunk.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Everything the roots reach, following both references and sub-assets. Repeated until nothing new
    /// turns up, since a sub-asset can reference an asset that is itself a parent.
    /// </summary>
    private static IEnumerable<Guid> ClosureOf(
        DependencyGraph dependencies,
        IReadOnlyCollection<Guid> roots,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> subAssets)
    {
        var closure = dependencies.GetTransitiveDependencies(roots);
        foreach (var root in roots)
            closure.Add(root);

        int previous;
        do
        {
            previous = closure.Count;

            var discovered = new List<Guid>();
            foreach (var guid in closure)
                if (subAssets.TryGetValue(guid, out var subs))
                    foreach (var sub in subs)
                        if (!closure.Contains(sub))
                            discovered.Add(sub);

            if (discovered.Count > 0)
                closure.UnionWith(dependencies.GetTransitiveDependencies(discovered));

        } while (closure.Count > previous);

        return closure;
    }
}
