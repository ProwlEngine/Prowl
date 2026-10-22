// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Theming;

/// <summary>Registers a class as a section of the Preferences window. The class must derive from <see cref="EditorPreferences"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorPreferencesAttribute(string name, string icon = "", int order = 100) : Attribute
{
    public string Name { get; } = name;
    public string Icon { get; } = icon;
    public int Order { get; } = order;

    /// <summary>The section can be overridden for one project, saved in its ProjectSettings folder so it is shared with the team.</summary>
    public bool ProjectOverridable { get; init; }
}

/// <summary>
/// A section of per user editor preferences. Public fields are the settings: they are drawn with the inspector's
/// property grid, so [Header], [Range], [Tooltip] and the rest work, and saved to their own file in the user's
/// Prowl folder. Read the live values with <see cref="Preferences.Get{T}"/>.
/// </summary>
public abstract class EditorPreferences
{
    /// <summary>Pushes the values into the editor. Runs after loading and after every change.</summary>
    public virtual void Apply() { }

    /// <summary>Draws extra controls above the section's fields.</summary>
    public virtual void OnGUI(Paper paper, float width) { }
}

/// <summary>One registered preferences section.</summary>
public sealed class PreferencesSection
{
    public required Type Type { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required int Order { get; init; }
    public required bool ProjectOverridable { get; init; }
    public required EditorPreferences Instance { get; init; }

    /// <summary>True while the open project's own values are in use.</summary>
    public bool IsProjectOverride { get; internal set; }

    /// <summary>The files as this editor last wrote or read them, to tell its own saves from edits made elsewhere.</summary>
    internal string? KnownText { get; set; }

    /// <summary>A fresh instance, for reading the default values.</summary>
    public required EditorPreferences Defaults { get; init; }

    public string FileName => Type.Name + ".yaml";
}

/// <summary>The registry and store of every <see cref="EditorPreferences"/> section.</summary>
public static class Preferences
{
    private static readonly List<PreferencesSection> _sections = [];
    private static FileSystemWatcher? s_watcher;
    private static volatile bool s_filesChanged;

    /// <summary>Every section, in display order.</summary>
    public static IReadOnlyList<PreferencesSection> Sections => _sections;

    /// <summary>The folder the section files are saved in.</summary>
    public static string FolderPath { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prowl", "Preferences");

    /// <summary>The live instance of a section.</summary>
    public static T Get<T>() where T : EditorPreferences
    {
        foreach (PreferencesSection section in _sections)
            if (section.Instance is T t) return t;

        EditorRegistries.Initialize();
        foreach (PreferencesSection section in _sections)
            if (section.Instance is T t) return t;

        throw new InvalidOperationException($"{typeof(T).Name} has no [EditorPreferences] attribute.");
    }

    internal static void Clear() => _sections.Clear();

    internal static void Scan(Type type)
    {
        if (type.IsAbstract || !typeof(EditorPreferences).IsAssignableFrom(type)) return;
        if (type.GetCustomAttribute<EditorPreferencesAttribute>() == null) return;

        try
        {
            PreferencesSection section = Create(type);
            int index = _sections.FindIndex(s => s.Order > section.Order);
            _sections.Insert(index < 0 ? _sections.Count : index, section);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Preferences] '{type.Name}' could not be created: {ex.Message}");
        }
    }

    /// <summary>Builds a section for an [EditorPreferences] type and loads its saved values.</summary>
    internal static PreferencesSection Create(Type type)
    {
        EditorPreferencesAttribute attr = type.GetCustomAttribute<EditorPreferencesAttribute>()!;
        var section = new PreferencesSection
        {
            Type = type,
            Name = attr.Name,
            Icon = attr.Icon,
            Order = attr.Order,
            ProjectOverridable = attr.ProjectOverridable,
            Instance = (EditorPreferences)Activator.CreateInstance(type)!,
            Defaults = (EditorPreferences)Activator.CreateInstance(type)!,
        };
        Load(section);
        return section;
    }

    /// <summary>The ProjectSettings folder project overrides are saved in, or null with no project open.</summary>
    public static string? ProjectFolderPath => Projects.Project.Current is { } project ? Path.Combine(project.ProjectSettingsPath, "Preferences") : null;

    /// <summary>Loads every section again, so project overrides follow the project that was opened.</summary>
    public static void OnProjectOpened()
    {
        foreach (PreferencesSection section in _sections) Load(section);
    }

    /// <summary>Turns a project override on, starting from the current values, or off, going back to the user's own.</summary>
    public static void SetProjectOverride(PreferencesSection section, bool on)
    {
        if (!section.ProjectOverridable || ProjectFolderPath is not { } folder || on == section.IsProjectOverride) return;

        string path = Path.Combine(folder, section.FileName);
        if (on)
        {
            section.IsProjectOverride = true;
            Save(section);
            return;
        }

        try { File.Delete(path); }
        catch (Exception ex) { Debug.LogWarning($"[Preferences] Could not delete '{path}': {ex.Message}"); }
        Load(section);
    }

    /// <summary>Starts watching the user's Prowl folder, so settings edited outside the editor apply without a restart.</summary>
    public static void WatchFiles()
    {
        if (s_watcher != null) return;
        string root = Path.GetDirectoryName(FolderPath)!;
        try
        {
            Directory.CreateDirectory(FolderPath);
            s_watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            s_watcher.Changed += (_, _) => s_filesChanged = true;
            s_watcher.Created += (_, _) => s_filesChanged = true;
            s_watcher.Renamed += (_, _) => s_filesChanged = true;
            s_watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Preferences] Could not watch '{root}' for changes: {ex.Message}");
        }
    }

    /// <summary>Reloads whatever changed on disk since the last frame. Called once per frame on the main thread.</summary>
    public static void ApplyFileChanges()
    {
        if (!s_filesChanged) return;
        s_filesChanged = false;

        EditorSettings.ReloadIfChangedOnDisk();
        foreach (PreferencesSection section in _sections) ReloadIfChanged(section);
    }

    internal static void ReloadIfChanged(PreferencesSection section)
    {
        if (ReadFiles(section) != section.KnownText) Load(section);
    }

    // The user's file then the project's, as one text, so a change to either is noticed.
    private static string ReadFiles(PreferencesSection section)
    {
        string text = ReadOrEmpty(Path.Combine(FolderPath, section.FileName));
        if (section.ProjectOverridable && ProjectFolderPath is { } folder)
            text += "\n" + ReadOrEmpty(Path.Combine(folder, section.FileName));
        return text;

        static string ReadOrEmpty(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch (IOException) { return ""; }
        }
    }

    /// <summary>Saves and applies a section after one of its values changed.</summary>
    public static void Changed(PreferencesSection section)
    {
        Save(section);
        ApplySafely(section);
    }

    /// <summary>The names of the fields whose value differs from the default.</summary>
    public static HashSet<string> ChangedFields(PreferencesSection section)
    {
        var changed = new HashSet<string>();
        foreach (FieldInfo field in PropertyGridUtils.GetSerializableFields(section.Type))
            if (!SameValue(field.GetValue(section.Instance), field.GetValue(section.Defaults)))
                changed.Add(field.Name);
        return changed;
    }

    /// <summary>Puts one field back to its default value.</summary>
    public static void ResetField(PreferencesSection section, string fieldName)
    {
        FieldInfo? field = section.Type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) return;
        field.SetValue(section.Instance, Copy(field.GetValue(section.Defaults), field.FieldType));
        Changed(section);
    }

    /// <summary>Puts every field of a section back to its default value.</summary>
    public static void Reset(PreferencesSection section)
    {
        CopyFields(section.Defaults, section.Instance, section.Type);
        Changed(section);
    }

    /// <summary>Puts every preference back to its default: theme, shortcuts, general settings and every section.</summary>
    public static void ResetAll()
    {
        EditorSettings.Replace(new EditorSettings());
        // Only the user's own files go: project overrides belong to their projects.
        foreach (PreferencesSection section in _sections)
        {
            string userFile = Path.Combine(FolderPath, section.FileName);
            if (File.Exists(userFile)) File.Delete(userFile);
            Load(section);
        }
    }

    /// <summary>The file extension of a settings profile.</summary>
    public const string ProfileExtension = ".prowlprefs";

    private const string ProfileSettingsEntry = "EditorSettings.json";
    private const string ProfileSectionFolder = "Preferences/";

    // Placement on this machine, left out of a profile.
    private static readonly string[] s_machineSettings =
        [nameof(EditorSettings.WindowX), nameof(EditorSettings.WindowY), nameof(EditorSettings.WindowWidth),
         nameof(EditorSettings.WindowHeight), nameof(EditorSettings.WindowMaximized), nameof(EditorSettings.DefaultProjectsPath)];

    /// <summary>Writes every preference into one profile file, to move to another machine or share.</summary>
    public static void ExportProfile(string path)
    {
        JsonObject settings = JsonSerializer.SerializeToNode(EditorSettings.Instance)!.AsObject();
        foreach (string name in s_machineSettings) settings.Remove(name);

        if (File.Exists(path)) File.Delete(path);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, ProfileSettingsEntry, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        foreach (PreferencesSection section in _sections)
        {
            // A project override belongs to the project, so the profile takes the user's own file.
            string userFile = Path.Combine(FolderPath, section.FileName);
            if (!section.IsProjectOverride)
                WriteEntry(zip, ProfileSectionFolder + section.FileName, Serializer.Serialize(section.Instance, TypeMode.Auto).WriteToYaml());
            else if (File.Exists(userFile))
                WriteEntry(zip, ProfileSectionFolder + section.FileName, File.ReadAllText(userFile));
        }
    }

    /// <summary>Replaces every preference with a profile's. Sections the profile does not have go back to their defaults.</summary>
    public static void ImportProfile(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        string json = ReadEntry(zip, ProfileSettingsEntry) ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a settings profile.");
        EditorSettings settings = JsonSerializer.Deserialize<EditorSettings>(json) ?? throw new InvalidDataException("The profile's settings are empty.");
        settings.Theme.InitRamps();
        EditorSettings.Replace(settings);

        foreach (PreferencesSection section in _sections)
        {
            string userFile = Path.Combine(FolderPath, section.FileName);
            if (ReadEntry(zip, ProfileSectionFolder + section.FileName) is { } yaml)
            {
                Directory.CreateDirectory(FolderPath);
                File.WriteAllText(userFile, yaml);
            }
            else if (File.Exists(userFile))
                File.Delete(userFile);
            Load(section);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static string? ReadEntry(ZipArchive zip, string name)
    {
        ZipArchiveEntry? entry = zip.GetEntry(name);
        if (entry == null) return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    // Defaults, then the user's file, then the open project's override when it has one.
    private static void Load(PreferencesSection section)
    {
        CopyFields(section.Defaults, section.Instance, section.Type);
        ReadInto(section, Path.Combine(FolderPath, section.FileName));

        section.IsProjectOverride = false;
        if (section.ProjectOverridable && ProjectFolderPath is { } folder)
            section.IsProjectOverride = ReadInto(section, Path.Combine(folder, section.FileName));

        section.KnownText = ReadFiles(section);
        ApplySafely(section);
    }

    private static bool ReadInto(PreferencesSection section, string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            EchoObject echo = EchoObject.ReadFromYaml(File.ReadAllText(path));
            if (Serializer.Deserialize(echo, section.Type) is not EditorPreferences loaded) return false;
            EditorRegistries.CopySettingsFields(loaded, section.Instance);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Preferences] Could not read '{path}': {ex.Message}");
            return false;
        }
    }

    private static void Save(PreferencesSection section)
    {
        string folder = section.IsProjectOverride && ProjectFolderPath is { } project ? project : FolderPath;
        string path = Path.Combine(folder, section.FileName);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, Serializer.Serialize(section.Instance, TypeMode.Auto).WriteToYaml());
            section.KnownText = ReadFiles(section);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Preferences] Could not save '{path}': {ex.Message}");
        }
    }

    private static void CopyFields(object source, object target, Type type)
    {
        foreach (FieldInfo field in PropertyGridUtils.GetSerializableFields(type))
            field.SetValue(target, Copy(field.GetValue(source), field.FieldType));
    }

    private static void ApplySafely(PreferencesSection section)
    {
        try { section.Instance.Apply(); }
        catch (Exception ex) { Debug.LogError($"[Preferences] {section.Type.Name}.Apply threw: {ex}"); }
    }

    private static bool SameValue(object? a, object? b)
    {
        if (Equals(a, b)) return true;
        if (a == null || b == null) return false;
        return Serializer.Serialize(a).WriteToString() == Serializer.Serialize(b).WriteToString();
    }

    private static object? Copy(object? value, Type type)
        => value == null || type.IsValueType || value is string ? value : Serializer.Deserialize(Serializer.Serialize(value), type);
}
