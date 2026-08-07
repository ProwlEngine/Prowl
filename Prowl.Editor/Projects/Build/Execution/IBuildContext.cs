// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Prowl.Editor.Build;

/// <summary>
/// What one stage hands to the stages that depend on it, plus the issue sink.
/// </summary>
/// <remarks>
/// Deliberately typed rather than a string keyed bag. A stage asking for the output it needs, and
/// failing loudly when the producing stage did not run, keeps the dependency visible in code instead of
/// hidden in a convention nobody checks.
/// </remarks>
public interface IBuildContext
{
    BuildRequest Request { get; }

    /// <summary>The output published by an earlier stage. Throws when nothing published it.</summary>
    T GetOutput<T>() where T : class;

    bool TryGetOutput<T>(out T? value) where T : class;

    void SetOutput<T>(T value) where T : class;

    void Report(BuildIssue issue);

    /// <summary>Progress and log text for a human watching the build.</summary>
    void Log(string message, BuildSeverity severity = BuildSeverity.Info);
}

public sealed class BuildContext : IBuildContext
{
    private readonly ConcurrentDictionary<Type, object> _outputs = new();
    private readonly ConcurrentBag<BuildIssue> _issues = new();
    private readonly Action<string, BuildSeverity>? _log;

    public BuildContext(BuildRequest request, Action<string, BuildSeverity>? log = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _log = log;
    }

    public BuildRequest Request { get; }

    public IReadOnlyCollection<BuildIssue> Issues => _issues;

    public bool HasErrors
    {
        get
        {
            foreach (var issue in _issues)
                if (issue.Severity == BuildSeverity.Error) return true;
            return false;
        }
    }

    public T GetOutput<T>() where T : class
        => _outputs.TryGetValue(typeof(T), out var value)
            ? (T)value
            : throw new InvalidOperationException(
                $"No stage published a {typeof(T).Name}. Declare a dependency on the stage that produces it.");

    public bool TryGetOutput<T>(out T? value) where T : class
    {
        if (_outputs.TryGetValue(typeof(T), out var stored))
        {
            value = (T)stored;
            return true;
        }

        value = null;
        return false;
    }

    public void SetOutput<T>(T value) where T : class
        => _outputs[typeof(T)] = value ?? throw new ArgumentNullException(nameof(value));

    public void Report(BuildIssue issue) => _issues.Add(issue);

    public void Log(string message, BuildSeverity severity = BuildSeverity.Info) => _log?.Invoke(message, severity);
}
