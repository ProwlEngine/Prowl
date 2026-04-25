using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Attribute to register a project settings class. Each settings class is a singleton
/// saved to ProjectSettings/{Name}.json.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ProjectSettingsAttribute : Attribute
{
    public string Name { get; }
    public string Icon { get; }
    public int Order { get; }
    /// <summary>Whether this setting is exported to standalone builds.</summary>
    public bool ExportToBuild { get; }

    public ProjectSettingsAttribute(string name, string icon = "", int order = 100, bool exportToBuild = true)
    {
        Name = name;
        Icon = icon;
        Order = order;
        ExportToBuild = exportToBuild;
    }
}

public enum SerializerType
{
    Standard,
    Echo
}

/// <summary>
/// Base class for all project settings. Subclass and add [ProjectSettings] attribute.
/// Settings are automatically discovered, created as singletons, and saved/loaded per project.
/// </summary>
public abstract class ProjectSettingsBase
{
    public virtual bool DrawInProjectSettingsPanel => true;

    /// <summary>Called after settings are loaded or reset. Override to apply values to runtime systems.</summary>
    public virtual void Apply() { }

    /// <summary>Called when a new project is opened. Override to reset to defaults.</summary>
    public virtual void ResetToDefaults() { }

    /// <summary>Draw the settings UI. Called by the Project Settings panel.</summary>
    public abstract void OnGUI(PaperUI.Paper paper, float width);
}

/// <summary>
/// Central registry for all project settings. Handles discovery, save/load, and project switching.
/// </summary>
public static class ProjectSettingsRegistry
{
    private static readonly List<SettingsEntry> _entries = new();
    private static bool _initialized;

    public struct SettingsEntry
    {
        public Type Type;
        public string Name;
        public string Icon;
        public int Order;
        public bool ExportToBuild;
        public ProjectSettingsBase Instance;
    }

    public static IReadOnlyList<SettingsEntry> Entries => _entries;

    public static void Reinitialize() { _initialized = false; _entries.Clear(); Initialize(); }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); } catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(ProjectSettingsBase).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<ProjectSettingsAttribute>();
                if (attr == null) continue;

                var instance = (ProjectSettingsBase)Activator.CreateInstance(type)!;
                _entries.Add(new SettingsEntry
                {
                    Type = type,
                    Name = attr.Name,
                    Icon = attr.Icon,
                    Order = attr.Order,
                    ExportToBuild = attr.ExportToBuild,
                    Instance = instance,
                });
            }
        }

        _entries.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>Get the singleton instance of a specific settings type.</summary>
    public static T Get<T>() where T : ProjectSettingsBase
    {
        foreach (var entry in _entries)
            if (entry.Instance is T t) return t;
        throw new InvalidOperationException($"Settings type {typeof(T).Name} not registered.");
    }

    /// <summary>Load all settings from the current project's ProjectSettings folder.</summary>
    public static void LoadAll()
    {
        var project = Project.Current;
        if (project == null) return;

        foreach (var entry in _entries)
        {
            string path = Path.Combine(project.ProjectSettingsPath, $"{entry.Name}.yaml");
            if (File.Exists(path))
            {
                try
                {
                    string yaml = File.ReadAllText(path);

                    EchoObject serialized = EchoObject.ReadFromYaml(yaml);
                    var loaded = (ProjectSettingsBase?)Serializer.Deserialize(serialized, entry.Type);
                    if (loaded != null)
                    {
                        // Copy fields from loaded to singleton
                        CopyFields(loaded, entry.Instance);
                        Debug.Log($"Loaded settings: {entry.Name}");
                    }

                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load settings '{entry.Name}': {ex.Message}");
                }
            }
            // If loading up the YAML fails, try loading up the JSON instead.
            else if (File.Exists(Path.Combine(project.ProjectSettingsPath, $"{entry.Name}.json")))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var loaded = (ProjectSettingsBase?)JsonSerializer.Deserialize(json, entry.Type,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
                    if (loaded != null)
                    {
                        // Copy fields from loaded to singleton
                        CopyFields(loaded, entry.Instance);
                        Debug.Log($"Loaded settings: {entry.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load settings '{entry.Name}': {ex.Message}");
                }
            }
            else
            {
                entry.Instance.ResetToDefaults();
            }

            entry.Instance.Apply();
        }
    }

    /// <summary>Save all settings to the current project's ProjectSettings folder.</summary>
    public static void SaveAll()
    {
        var project = Project.Current;
        if (project == null) return;

        Directory.CreateDirectory(project.ProjectSettingsPath);

        foreach (var entry in _entries)
        {
            Save(entry);
        }
    }

    /// <summary>Save a single settings entry.</summary>
    public static void Save(SettingsEntry entry)
    {
        var project = Project.Current;
        if (project == null) return;

        string path = Path.Combine(project.ProjectSettingsPath, $"{entry.Name}.yaml");
        try
        {
            var echoObject = Prowl.Echo.Serializer.Serialize(entry.Instance, TypeMode.Auto);
            string yaml = echoObject.WriteToYaml();
            File.WriteAllText(path, yaml);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save settings '{entry.Name}': {ex.Message}");
        }
    }

    /// <summary>Reset all settings and reload for a new project.</summary>
    public static void OnProjectOpened()
    {
        foreach (var entry in _entries)
            entry.Instance.ResetToDefaults();

        LoadAll();
    }

    internal static void CopyFields(object source, object target)
    {
        var type = source.GetType();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            field.SetValue(target, field.GetValue(source));
        }
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(target, prop.GetValue(source));
        }
    }
}
