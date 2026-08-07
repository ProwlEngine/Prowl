// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.Runtime;


namespace Prowl.Editor.Build;

/// <summary>
/// Main class that should handle build starting/logging.
/// It also supports starting the build as a separate process and receive logs from that.
/// </summary>
public static class ProjectBuilder
{
    public static void BuildLog(string message, LogSeverity severity = LogSeverity.Normal)
    {
        if (Program.BuildMode)
        {
            var logMessage = new BuildSettingsPanel.BuildStatusReport()
            {
                Severity = severity,
                Type = BuildSettingsPanel.BuildStatusReport.BuildStatusReportType.Info,
                Message = message,
            };
            var serializedOutput = Serializer.Serialize(logMessage).WriteToString();
            string serializedBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serializedOutput));
            Console.WriteLine(serializedBase64);
        }
        else
        {
            switch (severity)
            {
                case LogSeverity.Error:
                    Runtime.Debug.LogError(message);
                    break;
                case LogSeverity.Warning:
                    Runtime.Debug.LogWarning(message);
                    break;
                case LogSeverity.Success:
                    Runtime.Debug.LogSuccess(message);
                    break;
                default:
                    Runtime.Debug.Log(message);
                    break;
            }
        }
    }

    public static void BuildProgressLog(string message, float progress, LogSeverity severity = LogSeverity.Normal)
    {
        if (Program.BuildMode)
        {
            var logMessage = new BuildSettingsPanel.BuildStatusReport()
            {
                Severity = severity,
                Type = BuildSettingsPanel.BuildStatusReport.BuildStatusReportType.Progress,
                Message = message,
                Progress = progress
            };
            var serializedOutput = Serializer.Serialize(logMessage).WriteToString();
            string serializedBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serializedOutput));
            Console.WriteLine(serializedBase64);
        }
        else
        {
            switch (severity)
            {
                case LogSeverity.Error:
                    Runtime.Debug.LogError(message);
                    break;
                case LogSeverity.Warning:
                    Runtime.Debug.LogWarning(message);
                    break;
                case LogSeverity.Success:
                    Runtime.Debug.LogSuccess(message);
                    break;
                default:
                    Runtime.Debug.Log(message);
                    break;
            }
        }
    }

    public static void ProcessBuildLog(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.Data))
        {
            try
            {
                Console.WriteLine($"[BEGIN]{args.Data}[END]");
                var serializedData = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args.Data));
                var echoData = EchoObject.ReadFromString(serializedData);
                var logData = Serializer.Deserialize<BuildSettingsPanel.BuildStatusReport>(echoData);

                if (logData != null)
                {
                    BuildSettingsPanel.BuildState = logData.Message;
                    if (logData.Type == BuildSettingsPanel.BuildStatusReport.BuildStatusReportType.Progress)
                        BuildSettingsPanel.BuildProgress = logData.Progress;

                    switch (logData.Severity)
                    {
                        case LogSeverity.Error:
                            Runtime.Debug.LogError(logData.Message);
                            break;
                        case LogSeverity.Warning:
                            Runtime.Debug.LogWarning(logData.Message);
                            break;
                        case LogSeverity.Success:
                            Runtime.Debug.LogSuccess(logData.Message);
                            break;
                        default:
                            Runtime.Debug.Log(logData.Message);
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }

    /// <summary>
    /// Runs the build as a separate process that runs separate from the main editor process.
    /// To do, it launches the editor with build arguments to trigger an automatic build of the project.
    /// </summary>
    /// <param name="outputPath">The output path for the build.</param>
    /// <returns></returns>
    public static BuildProgress StartBuildProcess(string? outputPath)
    {
        BuildProgress progress = null;
        // Ask for output folder
        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, outputPath => {
            {
                Runtime.Debug.Log($"{Project.Current.RootPath}");
                Runtime.Debug.Log($"{outputPath}");

                ProcessStartInfo psi = new ProcessStartInfo()
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = $"-build \"{Project.Current.RootPath}\" -o \"{outputPath}\"",

                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                progress = new BuildProgress();
                var process = new System.Diagnostics.Process()
                {
                    StartInfo = psi
                };
                process.OutputDataReceived += ProcessBuildLog;
                process.Start();
                process.BeginOutputReadLine();
            }
        }, outputPath);

        return progress;
    }

    public static BuildProgress StartBuildAsync(bool andRun, string? outputPath)
    {
        BuildSettings? settings;
        try
        {
            settings = EditorRegistries.GetSettings<BuildSettings>();
        }
        catch
        {
            Runtime.Debug.LogError("BuildSettings not found.");

            return null;
        }

        if (string.IsNullOrEmpty(outputPath)) return null;

        settings.OutputDirectory = outputPath;
        EditorRegistries.SaveSettings();

        var pipeline = CreateSelectedPipeline(settings);
        if (pipeline == null)
        {
            Runtime.Debug.LogError($"[Build] Build pipeline '{settings.SelectedPipeline}' was not found.");
            return null;
        }

        // The build reads the Library caches straight off disk, so reconcile them against the actual
        // files first. Waiting on the watcher would ship whatever the assets were before the last save.
        EditorAssetBackend.Instance?.Refresh();

        Runtime.Debug.Log($"[Build] Starting {pipeline.DisplayName} build to {outputPath}...", LogSeverity.Normal);

        var progress = new BuildProgress();
        var projectPath = Project.Current?.RootPath ?? "";

        var assetSettings = EditorRegistries.GetSettings<AssetSettings>();
        if (assetSettings != null && assetSettings.AsyncAssetLoading)
        {
            // Now that rendering is handled by a separate thread, we should be able to run
            // the build in a separate thread as well without having any issues
            System.Threading.Tasks.Task task = System.Threading.Tasks.Task.Run(() =>
            {
                ProcessBuild(projectPath, pipeline, settings, outputPath, progress, andRun);
            });

            if (Program.BuildMode)
            {
                task.Wait();
            }
        }
        else
        {
            ProcessBuild(projectPath, pipeline, settings, outputPath, progress, andRun);
        }

        return progress;
    }

    /// <summary>
    /// Every pipeline the editor can build with, in a stable order.
    /// </summary>
    /// <remarks>
    /// Constructed here rather than only reflected over, because a pipeline that cannot be constructed
    /// cannot be built with either, and the Build window needs a live instance for its name and icon
    /// anyway. One that throws is left out with a warning instead of being offered and failing later.
    /// </remarks>
    public static List<BuildPipeline> DiscoverPipelines()
    {
        var found = new List<BuildPipeline>();

        foreach (var type in EditorUtils.GetAllTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(BuildPipeline))) continue;

            try
            {
                if (Activator.CreateInstance(type) is BuildPipeline pipeline)
                    found.Add(pipeline);
            }
            catch (Exception e)
            {
                Runtime.Debug.LogWarning($"[Build] Skipping pipeline '{type.Name}': {e.Message}");
            }
        }

        // Type enumeration promises no order, and the first entry is what a fresh project defaults to.
        return found
            .OrderBy(p => p.DisplayName, StringComparer.Ordinal)
            .ThenBy(p => p.GetType().FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The pipeline the Build window selected, or the desktop one when nothing is stored.</summary>
    /// <remarks>
    /// Returns null rather than throwing for anything it cannot produce, so a settings file naming a
    /// pipeline that has since been deleted or become unconstructable reports a build failure instead of
    /// taking the editor down from a button press.
    /// </remarks>
    public static BuildPipeline? CreateSelectedPipeline(BuildSettings settings)
        => string.IsNullOrEmpty(settings.SelectedPipeline)
            ? new DesktopBuildPipeline()
            : DiscoverPipelines().FirstOrDefault(p => p.GetType().FullName == settings.SelectedPipeline);

    public static void ProcessBuild(string projectPath, BuildPipeline pipeline, BuildSettings settings, string outputPath, BuildProgress progress, bool andRun)
    {
        try
        {
            Console.WriteLine($"[BEGIN]{projectPath}[END]");
            var result = pipeline.BuildAsync(
                projectPath, settings, outputPath, progress, progress.Token).GetAwaiter().GetResult();
            progress.Complete(result);

            HandleBuildResult(pipeline, result, settings, andRun);
        }
        catch (OperationCanceledException)
        {
            progress.Log("Build cancelled.", Runtime.LogSeverity.Warning);
            progress.Complete(new BuildResult { Success = false, Cancelled = true });
        }
        catch (Exception ex)
        {
            progress.Log($"FATAL: {ex.Message}", Runtime.LogSeverity.Error);
            progress.Complete(new BuildResult { Success = false, Errors = ex.ToString(), });
        }
    }

    private static void HandleBuildResult(BuildPipeline pipeline, BuildResult result, BuildSettings settings, bool andRun)
    {
        if (result.Success)
        {
            BuildLog($"[Build] SUCCESS: {result.AssetCount} assets -> {result.OutputPath} ({result.Duration.TotalSeconds:F1}s)", LogSeverity.Success);

            if (!andRun) return;

            if (!pipeline.CanRunOnHost(settings))
            {
                BuildLog("[Build] Built for another platform, so it was not launched.", LogSeverity.Normal);
                return;
            }

            string exe = pipeline.GetExecutablePath(result.OutputPath, settings);

            if (!File.Exists(exe))
            {
                Runtime.Debug.LogError($"[Build] Nothing to launch at '{exe}'.");
                return;
            }

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); }
            catch (Exception ex) { Runtime.Debug.LogError($"[Build] Failed to launch: {ex.Message}"); }
        }
        else if (result.Cancelled)
        {
            BuildLog(string.IsNullOrEmpty(result.OutputPath)
                ? "[Build] CANCELLED."
                : $"[Build] CANCELLED. A partial build is left in {result.OutputPath}", LogSeverity.Warning);
        }
        else
        {
            BuildLog($"[Build] FAILED: {result.Errors}", LogSeverity.Error);
        }
    }
}
