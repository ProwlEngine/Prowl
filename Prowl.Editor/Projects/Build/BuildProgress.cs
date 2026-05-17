// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Runtime;

namespace Prowl.Editor.Build;

/// <summary>
/// A single log entry produced during a build, carrying severity metadata
/// so the UI can render it with the same style as the console panel.
/// </summary>
public sealed class BuildLogEntry
{
    public string Message { get; init; } = string.Empty;
    public LogSeverity Severity { get; init; } = LogSeverity.Normal;
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>
/// Container for build progress information.
/// The build pipeline writes log lines from a background thread,
/// while the editor UI reads them from the main thread.
/// </summary>
public sealed class BuildProgress
{
    /// <summary>
    /// The progress value of the build, between 0 and 1.
    /// It's thread-safe and can be updated from the build pipeline to reflect progress in the UI.
    /// </summary>
    public float ProgressValue;

    private readonly object _lock = new();
    private readonly List<BuildLogEntry> _entries = [];

    /// <summary>
    /// Whether the build has finished (success or failure).
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// The final result, available once <see cref="IsComplete"/> is true.
    /// </summary>
    public BuildResult? Result { get; private set; }

    public Action<string, float> OnLog;

    /// <summary>
    /// Appends a log line with default (Normal) severity and updates the <see cref="ProgressValue"/>.
    /// </summary>
    public void Log(string message, float value)
    {
        ProgressValue = value;
        Log(message, LogSeverity.Normal);
    }

    /// <summary>
    /// Appends a log line with default (Normal) severity.
    /// </summary>
    public void Log(string message)
    {
        Log(message, LogSeverity.Normal);
    }

    /// <summary>
    /// Appends a log line with the given severity.
    /// </summary>
    public void Log(string message, LogSeverity severity)
    {
        lock (_lock)
        {
            _entries.Add(new BuildLogEntry
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
            });
        }
    }

    /// <summary>
    /// Marks the build as complete with the given result. It's thread-safe but should only be called once at the end of the build pipeline.
    /// </summary>
    public void Complete(BuildResult result)
    {
        lock (_lock)
        {
            Result = result;
            IsComplete = true;
        }
    }

    public string ToString(LogSeverity severity)
    {
        var sb = new StringBuilder();
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Severity == severity)
                    sb.AppendLine(entry.Message);
            }
        }
        return sb.ToString();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                sb.AppendLine(entry.Message);
            }
        }
        return sb.ToString();
    }


    /// <summary>
    /// Returns a thread-safe snapshot of all log entries accumulated so far.
    /// </summary>
    public List<BuildLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return [.. _entries];
        }
    }

    /// <summary>
    /// Returns the last entry as the current build state.
    /// </summary>
    public BuildLogEntry GetState()
    {
        lock (_lock)
        {
            if (_entries.Count > 0)
                return _entries[^1];
            else
                return null;
        }
    }

    /// <summary>
    /// Returns the thread-safe number of log entries.
    /// </summary>
    public int EntryCount
    {
        get { lock (_lock) { return _entries.Count; } }
    }
}

