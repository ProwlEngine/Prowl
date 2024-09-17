// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using CommandLine;

namespace Prowl.Editor.Editor.CLI;

/// <summary>
/// Command line options for the `build` command.
/// </summary>
[Verb("build", false, HelpText = "build a given project")]
internal class CliBuildOptions
{
    /// <summary>
    /// The path of the project to be open.
    /// </summary>
    [Option('p', "project", Required = false, HelpText = "Project path")]
    public required DirectoryInfo ProjectPath { get; set; }

    /// <summary>
    /// The path of the output files.
    /// </summary>
    [Option('o', "output", Required = false, HelpText = "Output directory path")]
    public required DirectoryInfo Output { get; set; }
}
