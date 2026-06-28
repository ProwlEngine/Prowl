// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Projects.Settings;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Well-known platform names used by assembly definitions and plugin platform settings.
/// "Editor" is a virtual platform that only exists while running inside the editor.
/// </summary>
public static class BuildPlatforms
{
    public const string Editor = "Editor";
    public const string Windows = "Windows";
    public const string Linux = "Linux";
    public const string MacOS = "macOS";

    /// <summary>All concrete (non-editor) platforms that a build can target.</summary>
    public static readonly string[] Players = [Windows, Linux, MacOS];

    /// <summary>Every platform an assembly can be assigned to, editor included.</summary>
    public static readonly string[] All = [Editor, Windows, Linux, MacOS];

    public static string FromBuildTarget(BuildTarget target) => target switch
    {
        BuildTarget.Linux => Linux,
        BuildTarget.MacOS => MacOS,
        _ => Windows,
    };

    /// <summary>Case-insensitive match against a known platform name; returns the canonical name or null.</summary>
    public static string? Normalize(string name)
        => All.FirstOrDefault(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A user assembly definition. Splits user scripts into a separately compiled assembly
/// with its own references and platform constraints. Stored as a Prowl-native (Echo) text
/// file with the <c>.asmdef</c> extension; the file is the source of truth and is edited
/// through the inspector. Assembly definitions are pure compile-time metadata and never ship
/// in a build.
/// </summary>
public sealed class AssemblyDefinition
{
    /// <summary>Output assembly name (also how other asmdefs reference this one).</summary>
    public string Name = "NewAssembly";

    /// <summary>Names of other assembly definitions this assembly depends on.</summary>
    public List<string> References = new();

    /// <summary>
    /// If non-empty, the assembly is only included on exactly these platforms.
    /// Mutually exclusive with <see cref="ExcludePlatforms"/>.
    /// </summary>
    public List<string> IncludePlatforms = new();

    /// <summary>If non-empty, the assembly is included everywhere except these platforms.</summary>
    public List<string> ExcludePlatforms = new();

    public bool AllowUnsafeCode = false;

    /// <summary>
    /// When true (default), the project's default assemblies automatically reference this one,
    /// so loose scripts can use its types without an explicit reference. When false, only
    /// assemblies that list it in <see cref="References"/> can see it.
    /// </summary>
    public bool AutoReferenced = true;

    /// <summary>When true, only the plugins in <see cref="PrecompiledReferences"/> are referenced
    /// (auto-referenced managed plugins are not added automatically).</summary>
    public bool OverrideReferences = false;

    /// <summary>Managed plugin file names (e.g. "MyLibrary.dll") referenced explicitly.
    /// Only consulted when <see cref="OverrideReferences"/> is true.</summary>
    public List<string> PrecompiledReferences = new();

    /// <summary>Scripting define symbols that must all be set for this assembly to be compiled.</summary>
    public List<string> DefineConstraints = new();

    /// <summary>When true the assembly does not reference the engine assemblies (Prowl.Runtime/Editor).</summary>
    public bool NoEngineReferences = false;

    // ------------------------------------------------------------------
    //  Platform logic
    // ------------------------------------------------------------------

    /// <summary>Whether this assembly is compiled/included for the given platform name.</summary>
    public bool IncludedFor(string platform)
    {
        if (IncludePlatforms.Count > 0)
            return IncludePlatforms.Any(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
        if (ExcludePlatforms.Count > 0)
            return !ExcludePlatforms.Any(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    /// <summary>
    /// True when the assembly is usable in the editor but on no player platform: it should be
    /// available for editor tooling/reflection but must never be shipped in a build.
    /// </summary>
    public bool IsEditorOnly =>
        IncludedFor(BuildPlatforms.Editor) && BuildPlatforms.Players.All(p => !IncludedFor(p));

    // ------------------------------------------------------------------
    //  Serialization (Echo text)
    // ------------------------------------------------------------------

    public EchoObject ToEcho()
    {
        var echo = EchoObject.NewCompound();
        echo["name"] = new EchoObject(Name);
        echo["references"] = StringList(References);
        echo["includePlatforms"] = StringList(IncludePlatforms);
        echo["excludePlatforms"] = StringList(ExcludePlatforms);
        echo["allowUnsafeCode"] = new EchoObject(AllowUnsafeCode);
        echo["autoReferenced"] = new EchoObject(AutoReferenced);
        echo["overrideReferences"] = new EchoObject(OverrideReferences);
        echo["precompiledReferences"] = StringList(PrecompiledReferences);
        echo["defineConstraints"] = StringList(DefineConstraints);
        echo["noEngineReferences"] = new EchoObject(NoEngineReferences);
        return echo;
    }

    public static AssemblyDefinition FromEcho(EchoObject echo)
    {
        var def = new AssemblyDefinition();
        if (echo.TryGet("name", out var n) && !string.IsNullOrWhiteSpace(n.StringValue))
            def.Name = n.StringValue;
        ReadStringList(echo, "references", def.References);
        ReadStringList(echo, "includePlatforms", def.IncludePlatforms);
        ReadStringList(echo, "excludePlatforms", def.ExcludePlatforms);
        if (echo.TryGet("allowUnsafeCode", out var au)) def.AllowUnsafeCode = au.BoolValue;
        if (echo.TryGet("autoReferenced", out var ar)) def.AutoReferenced = ar.BoolValue;
        if (echo.TryGet("overrideReferences", out var or)) def.OverrideReferences = or.BoolValue;
        ReadStringList(echo, "precompiledReferences", def.PrecompiledReferences);
        ReadStringList(echo, "defineConstraints", def.DefineConstraints);
        if (echo.TryGet("noEngineReferences", out var ne)) def.NoEngineReferences = ne.BoolValue;
        return def;
    }

    public void WriteToFile(string path) => File.WriteAllText(path, ToEcho().WriteToString());

    public static AssemblyDefinition ReadFromFile(string path)
    {
        string text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return new AssemblyDefinition { Name = Path.GetFileNameWithoutExtension(path) };
        return FromEcho(EchoObject.ReadFromString(text));
    }

    private static EchoObject StringList(List<string> values)
    {
        var list = EchoObject.NewList();
        foreach (var v in values)
            list.ListAdd(new EchoObject(v));
        return list;
    }

    private static void ReadStringList(EchoObject echo, string key, List<string> into)
    {
        into.Clear();
        if (echo.TryGet(key, out var tag) && tag.TagType == EchoType.List)
            foreach (var item in tag.List)
                if (!string.IsNullOrWhiteSpace(item.StringValue))
                    into.Add(item.StringValue);
    }
}

/// <summary>
/// Marker asset produced by the assembly-definition importer so the .asmdef gets a main-asset type
/// and can use the normal type-keyed inspector (<see cref="AssemblyDefinition"/> data lives in the
/// file itself). It is editor-only and never shipped.
/// </summary>
public sealed class AssemblyDefinitionAsset : Prowl.Runtime.EngineObject { }

/// <summary>An assembly definition file discovered on disk, paired with its owning directory.</summary>
public sealed class AsmDefFile
{
    public required AssemblyDefinition Definition { get; init; }
    public required string FilePath { get; init; }

    /// <summary>Absolute directory that the asmdef governs (its own folder and all subfolders
    /// not claimed by a nested asmdef).</summary>
    public required string Directory { get; init; }

    public string Name => Definition.Name;
}

/// <summary>Discovery and ownership resolution for assembly definition files.</summary>
public static class AssemblyDefinitionDatabase
{
    public const string Extension = ".asmdef";

    /// <summary>Load every <c>.asmdef</c> under the project's Assets folder.</summary>
    public static List<AsmDefFile> LoadAll(Project project)
    {
        var result = new List<AsmDefFile>();
        if (!System.IO.Directory.Exists(project.AssetsPath))
            return result;

        foreach (var file in System.IO.Directory.EnumerateFiles(project.AssetsPath, "*" + Extension, SearchOption.AllDirectories))
        {
            AssemblyDefinition def;
            try { def = AssemblyDefinition.ReadFromFile(file); }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"[AsmDef] Failed to parse '{Path.GetFileName(file)}': {ex.Message}");
                continue;
            }

            result.Add(new AsmDefFile
            {
                Definition = def,
                FilePath = file,
                Directory = Path.GetFullPath(Path.GetDirectoryName(file)!),
            });
        }

        return result;
    }

    /// <summary>
    /// Resolve which assembly definition owns a given script. A script belongs to the asmdef
    /// in the nearest ancestor directory; if none, it belongs to a default assembly (null).
    /// </summary>
    public static AsmDefFile? FindOwner(string scriptAbsolutePath, IReadOnlyList<AsmDefFile> asmdefs)
    {
        string scriptDir = Path.GetFullPath(Path.GetDirectoryName(scriptAbsolutePath)!);

        AsmDefFile? best = null;
        int bestLen = -1;
        foreach (var asm in asmdefs)
        {
            if (IsWithin(scriptDir, asm.Directory) && asm.Directory.Length > bestLen)
            {
                best = asm;
                bestLen = asm.Directory.Length;
            }
        }
        return best;
    }

    private static bool IsWithin(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }
}
