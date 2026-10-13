using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Shader files any include can reach by a name starting with "Graphite/". Assemblies register their embedded resources
/// named that way, so the renderer and the compiler each ship part of the library.
/// </summary>
public static class ShaderLibrary
{
    /// <summary>Prefix every library file name starts with.</summary>
    public const string Prefix = "Graphite/";

    private static readonly ConcurrentDictionary<string, string> s_files = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Assembly, bool> s_assemblies = new();


    /// <summary>Adds every embedded resource of the assembly whose name starts with the prefix. Repeat calls do nothing.</summary>
    public static void Register(Assembly assembly)
    {
        if (s_assemblies.ContainsKey(assembly))
            return;

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);
            s_files[resource.Replace('\\', '/')] = reader.ReadToEnd();
        }
        s_assemblies.TryAdd(assembly, true);
    }


    /// <summary>Source of a library file, or null when no registered assembly has it.</summary>
    public static string? Get(string name) => s_files.TryGetValue(name, out string? source) ? source : null;

    /// <summary>Every registered file's name.</summary>
    public static IEnumerable<string> Names => s_files.Keys;
}
