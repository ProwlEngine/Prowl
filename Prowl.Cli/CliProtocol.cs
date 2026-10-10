// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Prowl.Cli;

/// <summary> Wire types shared by the prowl CLI and the editor's command server. Compiled into both. </summary>
public static class CliProtocol
{
    public const string TokenHeader = "X-Prowl-Token";
    public const string LockFileName = "Cli.lock";
    public const string CommandsPath = "/commands";
    public const string RunPath = "/run";

    // Relaxed escaping keeps generics, quotes and symbols readable, which matters to an agent reading the output.
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Indented = new(Json) { WriteIndented = true };
}

/// <summary> Written to the project's Library folder by a running editor so the CLI can find and authenticate with it. </summary>
public sealed class CliLockFile
{
    public int Port { get; set; }
    public string Token { get; set; } = "";
    public int ProcessId { get; set; }
    public string ProjectPath { get; set; } = "";
    public string EditorVersion { get; set; } = "";
}

public sealed class CliArgInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Required { get; set; }
    public string? Default { get; set; }
}

public sealed class CliCommandInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<CliArgInfo> Args { get; set; } = [];
}

public sealed class CliRunRequest
{
    public const int MaxTimeoutSeconds = 24 * 60 * 60;

    public string Command { get; set; } = "";

    /// <summary> Raw command line tokens, bound by the editor using each parameter's type: --name value, --name=value, bool flags, and -- to end options. </summary>
    public List<string> Argv { get; set; } = [];

    /// <summary> Already split arguments for programmatic callers. Bound before Argv. </summary>
    public Dictionary<string, string> Args { get; set; } = [];
    public List<string> Positional { get; set; } = [];

    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class CliLogLine
{
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class CliRunResponse
{
    public bool Ok { get; set; }
    public JsonNode? Result { get; set; }
    public string? Error { get; set; }
    public List<CliLogLine> Logs { get; set; } = [];
}
