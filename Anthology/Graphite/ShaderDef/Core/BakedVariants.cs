using System;
using System.Collections.Concurrent;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Compiled variants shipped with a build, for players without a compiler. A pass is found by its source name and a hash
/// of its source, so a pass made anywhere from the same source gets them, and an edited source gets none.
/// </summary>
public static class BakedVariants
{
    public sealed class Pass
    {
        public required VariantSpace[] Axes;
        public required string[] Globals;
        public List<Variant> Variants = new();

        /// <summary>Compute variants by backend and variant key.</summary>
        public Dictionary<(GraphicsBackend Backend, int Key), ComputeDescription> Compute = new();
    }

    private static readonly ConcurrentDictionary<(string Name, ulong Hash), Pass> s_passes = new();

    public static void Add(string sourceName, string source, Pass pass) => s_passes[(sourceName, Hash(source))] = pass;

    public static Pass? Find(ShaderPass pass) => s_passes.TryGetValue((pass.SourceName, Hash(pass.Source)), out Pass? baked) ? baked : null;

    public static void Clear() => s_passes.Clear();

    /// <summary>FNV-1a over the source's characters, the same in every run.</summary>
    public static ulong Hash(string source)
    {
        ulong hash = 14695981039346656037ul;
        foreach (char c in source)
        {
            hash = (hash ^ c) * 1099511628211ul;
        }
        return hash;
    }
}
