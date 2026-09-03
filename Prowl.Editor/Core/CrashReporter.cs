// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Prowl.Editor.Projects;

namespace Prowl.Editor.Core;

/// <summary>
/// Catches whatever gets past everything else and writes it down before the process goes. A windowed
/// application has nowhere to print to, so an unhandled exception otherwise closes the window with no
/// dialog, no console and nothing on disk, which is indistinguishable from the editor vanishing.
/// </summary>
public static class CrashReporter
{
    private static bool s_installed;
    private static readonly object s_gate = new();

    /// <summary>Installs the handlers. Call this first, before anything that can fail.</summary>
    public static void Install()
    {
        if (s_installed) return;
        s_installed = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("unhandled exception", e.ExceptionObject as Exception);

        // A faulted task nobody awaited does not take the process down on its own, but it is almost
        // always the first sign of the thing that will.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Records a failure. Returns the file it was written to, or null when even that did not work.
    /// </summary>
    public static string? Write(string what, Exception? exception)
    {
        string report = Compose(what, exception);

        // The console is the one place that costs nothing and sometimes survives.
        try { Console.Error.WriteLine(report); } catch { }
        try { Runtime.Debug.LogError(report); } catch { }

        lock (s_gate)
        {
            foreach (string directory in Destinations())
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    File.WriteAllText(path, report);
                    return path;
                }
                catch { }
            }
        }

        return null;
    }

    private static string Compose(string what, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Prowl editor {what} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        try
        {
            if (Project.Current is not null) sb.AppendLine($"Project: {Project.Current.RootPath}");
        }
        catch { }

        sb.AppendLine();
        sb.AppendLine(exception?.ToString() ?? "(no exception object)");
        return sb.ToString();
    }

    // The project's own Logs folder first, since that is where someone would look, then somewhere
    // that exists even when no project is open.
    private static IEnumerable<string> Destinations()
    {
        string? project = null;
        try { project = Project.Current?.LogsPath; } catch { }
        if (!string.IsNullOrEmpty(project)) yield return project!;

        yield return Path.Combine(AppContext.BaseDirectory, "Logs");
        yield return Path.Combine(Path.GetTempPath(), "ProwlEditor");
    }
}
