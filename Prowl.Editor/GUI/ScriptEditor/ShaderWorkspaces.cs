using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Projects;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.OrigamiUI.Graphite;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.ScriptEditor;

/// <summary>
/// The shader workspace every open shader shares. Includes resolve like <see cref="Importers.ShaderImporter"/>:
/// Prowl/ names from the engine's built in shaders, then beside the file, then from the Assets root.
/// </summary>
internal static class ShaderWorkspaces
{
    private const string BuiltInFolder = ".Defaults.Shaders.";

    private static ShaderWorkspace? s_shared;

    public static ShaderWorkspace Shared
    {
        get
        {
            if (s_shared != null) return s_shared;
            ShaderLibrary.Register(typeof(Renderer).Assembly);
            return s_shared = new ShaderWorkspace(Resolve, IncludeNames);
        }
    }

    private static ShaderSource? Resolve(string name, string? fromPath)
    {
        if (name.StartsWith(BuiltInShaders.Prefix, StringComparison.Ordinal))
            return BuiltInShaders.Resolve(name) is { } builtIn ? new ShaderSource(name, builtIn) : null;

        string? dir = fromPath != null && Path.IsPathRooted(fromPath) ? Path.GetDirectoryName(fromPath) : null;
        return Find(dir, name) ?? (Path.HasExtension(name) ? null : Find(dir, name + ".glsl"));
    }

    private static ShaderSource? Find(string? dir, string name)
    {
        if (dir != null && Read(Path.Combine(dir, name)) is { } beside) return beside;
        if (Project.Current is { } project && Read(Path.Combine(project.AssetsPath, name)) is { } inAssets) return inAssets;
        return null;
    }

    private static ShaderSource? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try { return new ShaderSource(Path.GetFullPath(path), File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> IncludeNames(string? fromPath)
    {
        foreach (string resource in typeof(BuiltInShaders).Assembly.GetManifestResourceNames())
        {
            int at = resource.IndexOf(BuiltInFolder, StringComparison.Ordinal);
            if (at >= 0 && resource.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase))
                yield return BuiltInShaders.Prefix + resource[(at + BuiltInFolder.Length)..];
        }

        if (Project.Current is not { } project || !Directory.Exists(project.AssetsPath)) yield break;
        string? dir = fromPath != null && Path.IsPathRooted(fromPath) ? Path.GetDirectoryName(fromPath) : null;
        foreach (string file in Directory.EnumerateFiles(project.AssetsPath, "*.glsl", SearchOption.AllDirectories))
        {
            string full = Path.GetFullPath(file);
            if (string.Equals(full, fromPath, StringComparison.OrdinalIgnoreCase)) continue;
            bool beside = dir != null && string.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase);
            yield return (beside ? Path.GetFileName(full) : Path.GetRelativePath(project.AssetsPath, full)).Replace('\\', '/');
        }
    }

    /// <summary>Drops cached include indexes after a shader file changed, so other shaders see its new text.</summary>
    public static void Invalidate() => s_shared?.Invalidate();
}
