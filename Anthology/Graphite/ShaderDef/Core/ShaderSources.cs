using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Every pass a program can make from source written in code rather than a shader file, registered by whoever builds them,
/// so a build can compile all their variants ahead of time.
/// </summary>
public static class ShaderSources
{
    public readonly record struct Source(ShaderPass Pass, bool Compute = false);

    private static readonly List<Func<IEnumerable<Source>>> s_providers = new();

    public static void Register(Func<IEnumerable<Source>> provider)
    {
        lock (s_providers)
            s_providers.Add(provider);
    }

    public static List<Source> All()
    {
        Func<IEnumerable<Source>>[] providers;
        lock (s_providers)
            providers = [.. s_providers];
        List<Source> sources = new();
        foreach (Func<IEnumerable<Source>> provider in providers)
            sources.AddRange(provider());
        return sources;
    }
}
