// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using CommandLine;

namespace Prowl.Editor.Editor.CLI;

/// <summary>
/// Command line options for the `create` command.
/// </summary>
[Verb("create", false, HelpText = "create a project")]
internal class CliCreateOptions
{
    /// <summary>
    /// The path of the project to be created.
    /// </summary>
    [Option('p', "project", Required = false, HelpText = "Project path", Default = "./")]
    public required DirectoryInfo ProjectPath { get; set; }
}
