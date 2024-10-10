// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

namespace Prowl.Editor;

/// <summary>
/// Project is a static class that handles all Project related information and actions
/// </summary>
public static class ProjectCompiler
{
    private static bool CheckForSDKInstallation(string sdkVersion)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = new()
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                Runtime.Debug.LogError($"`dotnet --list-sdks` failed. Could not find any .NET SDK installation");
                return false;
            }

            List<string> outputLines = [];

            process.OutputDataReceived += (sender, dataArgs) =>
            {
                if (dataArgs.Data != null)
                    outputLines.AddRange(dataArgs.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            };

            process.BeginOutputReadLine();
            process.WaitForExit();

            bool foundSDK = outputLines.Exists(x => x.Contains(sdkVersion));

            if (!foundSDK)
                Runtime.Debug.LogError($"Failed to find SDK of version: {sdkVersion}");

            process.Close();

            return foundSDK;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogException(ex);
            return false;
        }
    }


    public static int CompileCSProject(FileInfo project, DotnetCompileOptions options)
    {
        if (!CheckForSDKInstallation("8.0"))
            return 1;

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = options.ConstructDotnetArgs(project),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        Process process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        process.OutputDataReceived += (sender, dataArgs) =>
        {
            string? data = dataArgs.Data;

            if (string.IsNullOrWhiteSpace(data))
                return;

            if (data.Contains("Warning", StringComparison.OrdinalIgnoreCase))
                Runtime.Debug.LogWarning(data);
            else if (data.Contains("Error", StringComparison.OrdinalIgnoreCase))
                Runtime.Debug.LogError(data);
            else
                Runtime.Debug.Log(data);
        };

        process.ErrorDataReceived += (sender, dataArgs) =>
        {
            if (dataArgs.Data is not null)
                Runtime.Debug.LogError(dataArgs.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        int exitCode = process.ExitCode;

        process.Close();

        return exitCode;
    }
}
