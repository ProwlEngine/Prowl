// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

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
    private const int StateRunning = 0;
    private const int StateCancelling = 1;
    private const int StateComplete = 2;

    private readonly object _lock = new();
    private readonly List<BuildLogEntry> _entries = [];
    private readonly CancellationTokenSource _cancellation = new();
    private float _progressValue;
    private int _state;
    private BuildResult? _result;

    /// <summary>Passed to the pipeline, so every stage and every tool it starts observes a stop.</summary>
    public CancellationToken Token => _cancellation.Token;

    public bool IsCancelled => _cancellation.IsCancellationRequested;

    /// <summary>
    /// Asks the running build to stop. Not instant: a tool already running is killed with its process
    /// tree, and the stage that started it unwinds afterwards.
    /// </summary>
    /// <remarks>
    /// The claim on the running state is atomic, so a build finishing at the same moment as the click
    /// either completes or is cancelled, never both. Signalling happens outside any lock, because
    /// cancelling runs the waiting operations' callbacks on this thread.
    /// </remarks>
    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _state, StateCancelling, StateRunning) != StateRunning)
            return;

        _cancellation.Cancel();
    }

    /// <summary>
    /// The progress value of the build, between 0 and 1.
    /// Read under the same lock as everything else here, so the UI thread cannot see a torn or
    /// reordered view of a build the worker thread is still updating.
    /// </summary>
    public float ProgressValue
    {
        get { lock (_lock) { return _progressValue; } }
        set { lock (_lock) { _progressValue = value; } }
    }

    /// <summary>
    /// Whether the build has finished (success, failure or cancellation).
    /// </summary>
    public bool IsComplete => Volatile.Read(ref _state) == StateComplete;

    /// <summary>
    /// The final result, available once <see cref="IsComplete"/> is true.
    /// </summary>
    public BuildResult? Result
    {
        get { lock (_lock) { return _result; } }
    }

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
            _result = result;
        }

        // Published last, so anything that sees the build as complete also sees its result.
        Interlocked.Exchange(ref _state, StateComplete);
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
    /// Returns the last entry as the current build state.
    /// </summary>
    public BuildLogEntry? GetState()
    {
        lock (_lock)
        {
            if (_entries.Count > 0)
                return _entries[^1];
            else
                return null;
        }
    }

}

