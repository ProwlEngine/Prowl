using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prowl.Editor;

public enum FileEventType { Created, Modified, Deleted, Renamed }

public struct FileEvent
{
    public FileEventType Type;
    public string Path;
    public string? OldPath; // For renames
}

/// <summary>
/// Watches the Assets/ directory for file changes using FileSystemWatcher.
/// Debounces and coalesces events for processing on the main thread.
/// </summary>
public class AssetWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly object _lock = new();
    private readonly List<FileEvent> _pendingEvents = new();
    private DateTime _lastEventTime = DateTime.MinValue;
    private const double DebounceMs = 300;

    public void Start(string assetsPath)
    {
        if (!Directory.Exists(assetsPath)) return;

        _watcher = new FileSystemWatcher(assetsPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += (_, e) => QueueEvent(FileEventType.Created, e.FullPath);
        _watcher.Changed += (_, e) => QueueEvent(FileEventType.Modified, e.FullPath);
        _watcher.Deleted += (_, e) => QueueEvent(FileEventType.Deleted, e.FullPath);
        _watcher.Renamed += (_, e) => QueueEvent(FileEventType.Renamed, e.FullPath, e.OldFullPath);
        _watcher.Error += (_, e) =>
        {
            Runtime.Debug.LogWarning($"AssetWatcher error: {e.GetException().Message}");
        };
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void QueueEvent(FileEventType type, string path, string? oldPath = null)
    {
        lock (_lock)
        {
            _pendingEvents.Add(new FileEvent { Type = type, Path = path, OldPath = oldPath });
            _lastEventTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Called on the main thread each frame. Returns debounced, coalesced events.
    /// </summary>
    public List<FileEvent> DrainEvents()
    {
        lock (_lock)
        {
            if (_pendingEvents.Count == 0) return new List<FileEvent>();

            // Wait for debounce period
            if ((DateTime.UtcNow - _lastEventTime).TotalMilliseconds < DebounceMs)
                return new List<FileEvent>();

            // Coalesce events on the same path
            var coalesced = new Dictionary<string, FileEvent>(StringComparer.OrdinalIgnoreCase);
            foreach (var evt in _pendingEvents)
            {
                if (coalesced.TryGetValue(evt.Path, out var existing))
                {
                    // Created + Deleted = cancel out
                    if (existing.Type == FileEventType.Created && evt.Type == FileEventType.Deleted)
                    {
                        coalesced.Remove(evt.Path);
                        continue;
                    }
                    // Created + Modified = just Created
                    if (existing.Type == FileEventType.Created && evt.Type == FileEventType.Modified)
                        continue;
                    // Modified + Modified = just Modified
                    if (existing.Type == FileEventType.Modified && evt.Type == FileEventType.Modified)
                        continue;
                    // Otherwise, latest event wins
                    coalesced[evt.Path] = evt;
                }
                else
                {
                    coalesced[evt.Path] = evt;
                }
            }

            _pendingEvents.Clear();
            return coalesced.Values.ToList();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
