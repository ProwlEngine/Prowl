// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Echo;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// A managed or native plugin discovered under a <c>Plugins/</c> folder. Managed plugins are
/// referenced by user code and copied next to the player; native plugins are copied into the
/// platform-specific <c>runtimes/{rid}/native</c> layout.
/// </summary>
public sealed class PluginInfo
{
    /// <summary>Meta setting keys (stored in the asset's .meta Settings compound).</summary>
    public static class Keys
    {
        public const string EditorOnly = "editorOnly";
        public const string AutoReferenced = "autoReferenced";
        public const string AnyPlatform = "anyPlatform";
        public const string Windows = "platWindows";
        public const string Linux = "platLinux";
        public const string MacOS = "platMacOS";
        public const string Cpu = "cpu"; // "AnyCPU" | "x64" | "arm64"
    }

    public required string AbsolutePath { get; init; }
    public string FileName => Path.GetFileName(AbsolutePath);

    /// <summary>True for native libraries (.so/.dylib, or a .dll without a managed assembly header).</summary>
    public required bool IsNative { get; init; }

    /// <summary>Plugin is available only inside the editor and never shipped in a build.</summary>
    public required bool EditorOnly { get; init; }

    /// <summary>Managed plugins only: whether user assemblies reference it automatically.</summary>
    public bool AutoReferenced { get; init; } = true;

    /// <summary>When true the plugin applies to every player platform.</summary>
    public bool AnyPlatform { get; init; } = true;

    /// <summary>Explicit player platforms (only consulted when <see cref="AnyPlatform"/> is false).</summary>
    public HashSet<string> Platforms { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Native CPU architecture: "x64", "arm64", or "AnyCPU".</summary>
    public string Cpu { get; init; } = "x64";

    public bool IsManaged => !IsNative;

    /// <summary>Whether this plugin should be shipped when building for the given player platform.</summary>
    public bool AppliesToBuild(string platform)
        => !EditorOnly && (AnyPlatform || Platforms.Contains(platform));

    /// <summary>Runtime identifier this native plugin maps to for a given player platform.</summary>
    public string RuntimeIdentifierFor(string platform)
    {
        string prefix = platform switch
        {
            BuildPlatforms.Linux => "linux",
            BuildPlatforms.MacOS => "osx",
            _ => "win",
        };
        string arch = Cpu.Equals("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64";
        return $"{prefix}-{arch}";
    }
}

/// <summary>
/// Marker asset produced by the plugin importer so a plugin gets a main-asset type and uses the
/// normal type-keyed inspector. The plugin's settings live in its .meta (a binary DLL can't carry
/// them like a text .asmdef does). Editor-only; never shipped as a runtime asset.
/// </summary>
public sealed class PluginAsset : Prowl.Runtime.EngineObject { }

/// <summary>Scans a project for plugins and applies the Plugins-folder convention.</summary>
public static class PluginScanner
{
    private static readonly string[] s_extensions = [".dll", ".so", ".dylib"];

    /// <summary>
    /// Find every plugin under a <c>Plugins/</c> folder in the project's Assets directory.
    /// DLLs outside a Plugins folder are ignored (treated as build artefacts, not plugins).
    /// </summary>
    public static List<PluginInfo> ScanAll(Project project)
    {
        var result = new List<PluginInfo>();
        if (!Directory.Exists(project.AssetsPath))
            return result;

        foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (!s_extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;

            string relative = Path.GetRelativePath(project.AssetsPath, file);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!segments.Any(s => s.Equals("Plugins", StringComparison.OrdinalIgnoreCase)))
                continue; // only files inside a Plugins/ folder are plugins

            result.Add(Build(file, segments, ext));
        }

        return result;
    }

    private static PluginInfo Build(string file, string[] segments, string ext)
    {
        bool underEditor = segments.Any(s => s.Equals("Editor", StringComparison.OrdinalIgnoreCase));
        bool isNative = !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || !IsManagedAssembly(file);

        // Defaults; overridden by any values stored in the .meta companion.
        bool editorOnly = underEditor;
        bool autoReferenced = true;
        bool anyPlatform = true;
        var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string cpu = "x64";

        string metaPath = MetaFile.GetMetaPath(file);
        if (File.Exists(metaPath))
        {
            try
            {
                var settings = MetaFile.Read(metaPath).Settings;
                if (settings != null)
                {
                    if (settings.TryGet(PluginInfo.Keys.EditorOnly, out var eo)) editorOnly = underEditor || eo.BoolValue;
                    if (settings.TryGet(PluginInfo.Keys.AutoReferenced, out var arf)) autoReferenced = arf.BoolValue;
                    if (settings.TryGet(PluginInfo.Keys.AnyPlatform, out var ap)) anyPlatform = ap.BoolValue;
                    if (settings.TryGet(PluginInfo.Keys.Windows, out var w) && w.BoolValue) platforms.Add(BuildPlatforms.Windows);
                    if (settings.TryGet(PluginInfo.Keys.Linux, out var l) && l.BoolValue) platforms.Add(BuildPlatforms.Linux);
                    if (settings.TryGet(PluginInfo.Keys.MacOS, out var m) && m.BoolValue) platforms.Add(BuildPlatforms.MacOS);
                    if (settings.TryGet(PluginInfo.Keys.Cpu, out var c) && !string.IsNullOrWhiteSpace(c.StringValue)) cpu = c.StringValue;
                }
            }
            catch { /* fall back to defaults */ }
        }

        return new PluginInfo
        {
            AbsolutePath = file,
            IsNative = isNative,
            EditorOnly = editorOnly,
            AutoReferenced = autoReferenced,
            AnyPlatform = anyPlatform,
            Platforms = platforms,
            Cpu = cpu,
        };
    }

    /// <summary>True when the file is a managed .NET assembly (false for native libraries).</summary>
    public static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException) { return false; } // native binary
        catch (FileLoadException) { return true; }         // already loaded => was managed
        catch { return false; }
    }
}
