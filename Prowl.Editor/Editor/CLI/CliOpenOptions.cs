// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using CommandLine;

namespace Prowl.Editor.Editor.CLI;

/// <summary>
/// Command line options for the `open` command.
/// </summary>
[Verb("open", true, HelpText = "Open a given project")]
internal class CliOpenOptions : CliOptionsBase { }
