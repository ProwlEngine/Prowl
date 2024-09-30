// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using CommandLine;

namespace Prowl.Editor.Editor.CLI;

/// <summary>
/// Repetitive options.
/// </summary>
internal class CliOptionsBase
{
    /// <summary>
    /// The path of the project to perform the command (open, build, create, etc).
    /// </summary>
    [Option('p', "project", Required = false, HelpText = "Project path")]
    public required DirectoryInfo? ProjectPath { get; init; }
}
