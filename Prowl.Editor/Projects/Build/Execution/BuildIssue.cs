// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Editor.Build;

/// <summary> Severity of a build issue. Members are Info, Warning and Error. </summary>
public enum BuildSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One thing the build has to say. Structured rather than a log line, so a caller can group, filter and
/// navigate to it instead of matching substrings against a tool's stdout.
/// </summary>
public sealed record BuildIssue
{
    public required BuildSeverity Severity { get; init; }

    /// <summary>A stable code, either the engine's own or the one a tool reported, such as an MSBuild code.</summary>
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? File { get; init; }
    public int? Line { get; init; }

    /// <summary>The project that produced it, when a tool reported one.</summary>
    public string? Project { get; init; }

    /// <summary> The build stage during which this issue was reported. </summary>
    public BuildStage Stage { get; init; }

    /// <summary> Returns a formatted string: Severity: Code: Message (File:Line). File and Line are omitted when null. </summary>
    public override string ToString()
    {
        string where = File is null ? "" : Line is null ? $" ({File})" : $" ({File}:{Line})";
        return $"{Severity}: {Code}: {Message}{where}";
    }
}
