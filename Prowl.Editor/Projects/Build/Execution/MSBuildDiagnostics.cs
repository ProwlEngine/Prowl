// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Prowl.Editor.Build;

/// <summary>
/// Reads MSBuild's canonical diagnostic format into <see cref="BuildIssue"/>.
/// </summary>
/// <remarks>
/// MSBuild emits diagnostics in one documented shape, <c>origin : severity code : message [project]</c>,
/// where the origin may carry a line and column. Matching that shape recovers the code, the file and the
/// line, which a search for the substring ": error " throws away.
/// <para>
/// The severity words are localised when the toolchain is, so callers must run the tool with an
/// invariant UI language. <see cref="InvariantEnvironment"/> carries the variables that do it.
/// </para>
/// </remarks>
public static partial class MSBuildDiagnostics
{
    /// <summary>
    /// Environment that forces English tool output, so the severity words are the ones parsed here.
    /// </summary>
    public static IReadOnlyDictionary<string, string> InvariantEnvironment { get; } = new Dictionary<string, string>
    {
        ["DOTNET_CLI_UI_LANGUAGE"] = "en",
        ["VSLANG"] = "1033",
    };

    [GeneratedRegex(
        @"^\s*(?<origin>.+?)\s*:\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.*?)\s*(?:\[(?<project>[^\[\]]*)\])?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPattern { get; }

    // "file(12,34)" or "file(12)". Anchored at the end so a Windows drive letter cannot be mistaken for it.
    [GeneratedRegex(@"^(?<file>.*?)\((?<line>\d+)(?:,(?<column>\d+))?\)$", RegexOptions.CultureInvariant)]
    private static partial Regex OriginPattern { get; }

    /// <summary>Parses one line of tool output, or returns false when it is not a diagnostic.</summary>
    public static bool TryParse(string line, BuildStage stage, out BuildIssue issue)
    {
        issue = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var match = DiagnosticPattern.Match(line);
        if (!match.Success) return false;

        string origin = match.Groups["origin"].Value;
        string? file = null;
        int? number = null;

        var originMatch = OriginPattern.Match(origin);
        if (originMatch.Success)
        {
            file = originMatch.Groups["file"].Value;
            number = int.Parse(originMatch.Groups["line"].Value);
        }
        else if (origin.Length > 0 && !origin.Equals("MSBUILD", StringComparison.OrdinalIgnoreCase))
        {
            // A bare origin is still useful: it is the project or tool the diagnostic came from.
            file = origin;
        }

        issue = new BuildIssue
        {
            Severity = match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)
                ? BuildSeverity.Error
                : BuildSeverity.Warning,
            Code = match.Groups["code"].Value,
            Message = match.Groups["message"].Value,
            File = string.IsNullOrEmpty(file) ? null : file,
            Line = number,
            Project = match.Groups["project"].Success && match.Groups["project"].Value.Length > 0
                ? match.Groups["project"].Value
                : null,
            Stage = stage,
        };

        return true;
    }

    /// <summary>Every diagnostic in a block of tool output, deduplicated because MSBuild repeats them.</summary>
    public static IReadOnlyList<BuildIssue> Parse(string output, BuildStage stage)
    {
        var issues = new List<BuildIssue>();
        if (string.IsNullOrEmpty(output)) return issues;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in output.Split('\n'))
        {
            if (!TryParse(line.TrimEnd('\r'), stage, out var issue)) continue;

            // MSBuild prints the same diagnostic once per project that pulled the file in.
            if (!seen.Add($"{issue.Code}|{issue.File}|{issue.Line}|{issue.Message}")) continue;

            issues.Add(issue);
        }

        return issues;
    }
}
