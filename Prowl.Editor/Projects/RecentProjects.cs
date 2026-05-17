using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Prowl.Editor.Projects;

public class RecentProjectEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastOpened { get; set; }
}

/// <summary>
/// Persists the list of recently opened projects in the user's AppData.
/// </summary>
public static class RecentProjects
{
    private const int MaxRecent = 20;

    private static readonly string _filePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Prowl", "RecentProjects.json");

    private static List<RecentProjectEntry>? _entries;

    public static IReadOnlyList<RecentProjectEntry> Entries
    {
        get
        {
            _entries ??= Load();
            return _entries;
        }
    }

    public static void AddRecent(string path, string name)
    {
        _entries ??= Load();

        // Remove existing entry for this path
        _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        // Add at front
        _entries.Insert(0, new RecentProjectEntry
        {
            Path = path,
            Name = name,
            LastOpened = DateTime.UtcNow
        });

        // Trim
        if (_entries.Count > MaxRecent)
            _entries.RemoveRange(MaxRecent, _entries.Count - MaxRecent);

        Save();
    }

    public static void Remove(string path)
    {
        _entries ??= Load();
        _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private static List<RecentProjectEntry> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<RecentProjectEntry>>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    private static void Save()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to save recent projects: {ex.Message}");
        }
    }
}
