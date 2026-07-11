using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Prowl.Editor.Projects;

public class RecentProjectEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastOpened { get; set; }
    public bool Favorite { get; set; }
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

        // Trim the non-favorite tail to MaxRecent entries. Favorites are never evicted regardless
        // of count or age - starring a project is meant to pin it in this list permanently.
        int nonFavoriteCount = _entries.Count(e => !e.Favorite);
        if (nonFavoriteCount > MaxRecent)
        {
            int toRemove = nonFavoriteCount - MaxRecent;
            for (int i = _entries.Count - 1; i >= 0 && toRemove > 0; i--)
            {
                if (!_entries[i].Favorite)
                {
                    _entries.RemoveAt(i);
                    toRemove--;
                }
            }
        }

        Save();
    }

    public static void Remove(string path)
    {
        _entries ??= Load();
        _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>Mark a recent project as a favorite (or clear it) and persist.</summary>
    public static void SetFavorite(string path, bool favorite)
    {
        _entries ??= Load();
        var entry = _entries.Find(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Favorite == favorite) return;
        entry.Favorite = favorite;
        Save();
    }

    /// <summary>Recent entries with favorites floated to the top; each group keeps recency order.</summary>
    public static List<RecentProjectEntry> FavoritesFirst()
    {
        _entries ??= Load();
        var ordered = new List<RecentProjectEntry>(_entries.Count);
        foreach (var e in _entries) if (e.Favorite) ordered.Add(e);
        foreach (var e in _entries) if (!e.Favorite) ordered.Add(e);
        return ordered;
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
