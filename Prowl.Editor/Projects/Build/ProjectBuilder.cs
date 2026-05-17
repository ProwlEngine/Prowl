// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Settings;
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
        try { settings = ProjectSettingsRegistry.Get<BuildSettings>(); }
        catch
        {
            Runtime.Debug.LogError("BuildSettings not found.");

            return null;
        }

        if (string.IsNullOrEmpty(outputPath)) return null;

        settings.OutputDirectory = outputPath;
        ProjectSettingsRegistry.SaveAll();

        var pipeline = new DesktopBuildPipeline();

        Runtime.Debug.Log($"[Build] Starting build to {outputPath}...", LogSeverity.Normal);

        var progress = new BuildProgress();
        var projectPath = Project.Current?.RootPath ?? "";

        // THREADING DISABLED: OpenGL is thread-affine, and the build pipeline currently
        // touches GL during asset reimport (SceneImporter -> RenderTexture.Deserialize ->
        // GenTexture()), which crashes with 0xC0000005 when invoked from a ThreadPool
        // worker. Running the build inline on the main thread until GPU resource creation
        // is removed from the import path (or marshaled back to the GL thread).
        // TODO: restore the Task.Run wrapper once that's fixed.
        //Task task = Task.Run(async () =>
        //{
        try
        {
            Console.WriteLine($"[BEGIN]{projectPath}[END]");
            var result = pipeline.BuildAsync(
                projectPath, settings, outputPath, progress).GetAwaiter().GetResult();
            progress.Complete(result);

            HandleBuildResult(pipeline, result, settings, andRun);
        }
        catch (Exception ex)
        {
            progress.Log($"FATAL: {ex.Message}", Runtime.LogSeverity.Error);
            progress.Complete(new BuildResult
            {
                Success = false,
                Errors = ex.ToString(),
            });
        }
        //});

        //if (Program.BuildMode)
        //{
        //    task.Wait();
        //}

        return progress;
    }


    private static void HandleBuildResult(BuildPipeline pipeline, BuildResult result, BuildSettings settings, bool andRun)
    {
        if (result.Success)
        {
            BuildLog($"[Build] SUCCESS: {result.AssetCount} assets -> {result.OutputPath} ({result.Duration.TotalSeconds:F1}s)", LogSeverity.Success);

            if (andRun)
            {
                string exe = pipeline.GetExecutablePath(result.OutputPath, settings);

                if (File.Exists(exe))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); }
                    catch (Exception ex) { Runtime.Debug.LogError($"[Build] Failed to launch: {ex.Message}"); }
                }
            }
        }
        else
        {
            BuildLog($"[Build] FAILED: {result.Errors}", LogSeverity.Error);
        }
    }
}
