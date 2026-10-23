// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.Utils;

/// <summary>The external script editor used when scripts open outside the built in one.</summary>
public enum ExternalScriptEditor { SystemDefault, VisualStudioCode, Rider, VisualStudio, Custom }

/// <summary>A program the Project panel can open files with.</summary>
public class ExternalTool
{
    public string Name = "";

    [Tooltip("The program, as a full path or a name on PATH.")]
    public string Path = "";

    [Tooltip("Placeholders: {file}, {dir}, {project}.")]
    public string Arguments = "\"{file}\"";

    [Tooltip("File extensions this tool opens, separated by ; such as png;psd.")]
    public string Extensions = "";

    [Tooltip("Double clicking a matching file in the Project panel opens it with this tool.")]
    public bool OpenOnDoubleClick;
}

/// <summary>
/// Programs outside the editor: the script editor, tools per file type, a diff tool and a terminal. Arguments are
/// templates, so {file}, {line}, {column}, {dir}, {project}, {solution}, {left} and {right} are filled in when run.
/// </summary>
[EditorPreferences("External Tools", EditorIcons.ScrewdriverWrench, order: 15)]
public sealed class ExternalToolsPreferences : EditorPreferences
{
    [Header("Script Editor")]
    [Tooltip("Used when Script Editor > Open Scripts In is External. System Default opens the file with the system's app for it.")]
    public ExternalScriptEditor ScriptEditor = ExternalScriptEditor.SystemDefault;

    [Tooltip("Overrides the program, as a full path or a name on PATH. Leave empty to use the preset's.")]
    public string ScriptEditorPath = "";

    [Tooltip("Arguments for Custom. Placeholders: {file}, {line}, {column}, {project}, {solution}.")]
    public string ScriptEditorArguments = "\"{file}\"";

    [Header("Tools")]
    public List<ExternalTool> Tools = [];

    [Header("Diff Tool")]
    [Tooltip("Used by Compare in the Project panel when two files are selected.")]
    public string DiffToolPath = "";
    [Tooltip("Placeholders: {left}, {right}.")]
    public string DiffToolArguments = "\"{left}\" \"{right}\"";

    [Header("Terminal")]
    [Tooltip("Leave empty for the system's terminal.")]
    public string TerminalPath = "";
    [Tooltip("Placeholders: {dir}.")]
    public string TerminalArguments = "";
}

/// <summary>Runs the programs set in <see cref="ExternalToolsPreferences"/>.</summary>
public static class ExternalTools
{
    private static ExternalToolsPreferences Prefs => Preferences.Get<ExternalToolsPreferences>();

    /// <summary>Opens a script in the external script editor at a 1 based line and column. False when it is set to the system default, which the caller handles.</summary>
    public static bool OpenScript(string path, int line = 1, int column = 1)
    {
        ExternalToolsPreferences prefs = Prefs;
        if (prefs.ScriptEditor == ExternalScriptEditor.SystemDefault && prefs.ScriptEditorPath.Length == 0) return false;

        (string program, string arguments) = prefs.ScriptEditor switch
        {
            ExternalScriptEditor.VisualStudioCode => ("code", "\"{project}\" -g \"{file}:{line}:{column}\""),
            ExternalScriptEditor.Rider => ("rider", "\"{solution}\" --line {line} --column {column} \"{file}\""),
            ExternalScriptEditor.VisualStudio => ("devenv", "/Edit \"{file}\""),
            _ => ("", prefs.ScriptEditorArguments),
        };
        if (prefs.ScriptEditorPath.Length > 0) program = prefs.ScriptEditorPath;

        Run(program, arguments, Values(path, line, column));
        return true;
    }

    /// <summary>Opens a file with the system's app for it.</summary>
    public static void OpenWithSystem(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Toasts.Error("Could not open", ex.Message); }
    }

    /// <summary>The tools set up for a file's extension.</summary>
    public static IEnumerable<ExternalTool> ToolsFor(string path)
    {
        string ext = System.IO.Path.GetExtension(path).TrimStart('.');
        return Prefs.Tools.Where(t => t.Path.Length > 0 && t.Extensions.Split(';', ',', ' ')
            .Any(e => e.Trim().TrimStart('.').Equals(ext, StringComparison.OrdinalIgnoreCase)));
    }

    public static void OpenWith(ExternalTool tool, string path) => Run(tool.Path, tool.Arguments, Values(path, 1, 1));

    /// <summary>Opens a file with the tool set to open its extension on double click. False when there is none.</summary>
    public static bool OpenOnDoubleClick(string path)
    {
        ExternalTool? tool = ToolsFor(path).FirstOrDefault(t => t.OpenOnDoubleClick);
        if (tool == null) return false;
        OpenWith(tool, path);
        return true;
    }

    public static bool HasDiffTool => Prefs.DiffToolPath.Length > 0;

    public static void Diff(string left, string right)
    {
        ExternalToolsPreferences prefs = Prefs;
        var values = Values(left, 1, 1);
        values["left"] = left;
        values["right"] = right;
        Run(prefs.DiffToolPath, prefs.DiffToolArguments, values);
    }

    /// <summary>Opens the terminal in a folder.</summary>
    public static void OpenTerminal(string dir)
    {
        ExternalToolsPreferences prefs = Prefs;
        var values = new Dictionary<string, string> { ["dir"] = dir };
        if (prefs.TerminalPath.Length > 0)
        {
            Run(prefs.TerminalPath, prefs.TerminalArguments, values, dir);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (ResolveProgram("wt") != null) Run("wt", "-d \"{dir}\"", values, dir);
            else Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = dir, UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Run("open", "-a Terminal \"{dir}\"", values, dir);
        else
            Run("x-terminal-emulator", "", values, dir);
    }

    private static Dictionary<string, string> Values(string path, int line, int column)
    {
        Project? project = Project.Current;
        return new Dictionary<string, string>
        {
            ["file"] = path,
            ["line"] = Math.Max(1, line).ToString(),
            ["column"] = Math.Max(1, column).ToString(),
            ["dir"] = System.IO.Path.GetDirectoryName(path) ?? "",
            ["project"] = project?.RootPath ?? "",
            ["solution"] = project?.ProjectSolutionPath ?? "",
        };
    }

    /// <summary>Fills a template's {placeholders}.</summary>
    public static string Expand(string template, IReadOnlyDictionary<string, string> values)
    {
        foreach ((string key, string value) in values) template = template.Replace("{" + key + "}", value);
        return template;
    }

    private static void Run(string program, string arguments, IReadOnlyDictionary<string, string> values, string? workingDirectory = null)
    {
        string? resolved = ResolveProgram(program);
        if (resolved == null)
        {
            Toasts.Error("Program not found", $"'{program}' is not a file or on PATH. Set its path in Preferences > External Tools.");
            return;
        }

        string args = Expand(arguments, values);
        var info = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = workingDirectory ?? "" };

        // Batch files such as code.cmd run through the command prompt.
        string ext = System.IO.Path.GetExtension(resolved);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            info.FileName = "cmd.exe";
            info.Arguments = $"/c \"\"{resolved}\" {args}\"";
            info.CreateNoWindow = true;
        }
        else
        {
            info.FileName = resolved;
            info.Arguments = args;
        }
        Start(info);
    }

    private static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info); }
        catch (Exception ex) { Toasts.Error("Could not start", $"{info.FileName}: {ex.Message}"); }
    }

    /// <summary>A program as a full path, from a path or a name on PATH, or null when it cannot be found.</summary>
    public static string? ResolveProgram(string program)
    {
        if (string.IsNullOrWhiteSpace(program)) return null;
        if (System.IO.Path.IsPathRooted(program)) return File.Exists(program) || Directory.Exists(program) ? program : null;

        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string[] extensions = windows && !System.IO.Path.HasExtension(program)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (string ext in extensions)
            {
                string candidate = System.IO.Path.Combine(dir.Trim('"'), program + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}
