// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;

using Prowl.Runtime;

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

        process.OutputDataReceived += LogCompilationMessage;
        process.ErrorDataReceived += LogCompilationMessage;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        int exitCode = process.ExitCode;

        process.Close();

        return exitCode;
    }


    private static string ParseMessageCSFile(string message, out string? csFile)
    {
        int validIndex = -1;
        csFile = null;

        foreach (int possibleIndex in AllIndexesOf(message, ".cs("))
        {
            string potentialFile = message.Substring(0, Math.Min(message.Length - 1, possibleIndex + 3));

            if (File.Exists(potentialFile))
            {
                csFile = potentialFile;
                validIndex = possibleIndex + 3;
                break;
            }
        }

        if (validIndex == -1)
            return message;

        return message.Substring(validIndex);
    }


    private static string ParseMessageCSProj(string message, out string? csprojFile)
    {
        int validIndex = -1;
        csprojFile = null;

        foreach (int possibleIndex in AllIndexesOf(message, "["))
        {
            string potentialFile = message.Substring(possibleIndex + 1, message.Length - (possibleIndex + 2));

            if (File.Exists(potentialFile))
            {
                csprojFile = potentialFile;
                validIndex = possibleIndex + 1;
                break;
            }
        }

        if (validIndex == -1)
            return message;

        return message.Substring(0, validIndex - 2);
    }


    private static string ParseFileLocations(string message, out int? line, out int? column)
    {
        line = null;
        column = null;

        int valueIndex = message.IndexOf(':');

        if (valueIndex == -1)
            return message;

        string[] sourceLocations = message
            .Substring(1, valueIndex - 2)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (sourceLocations.Length > 0)
            if (int.TryParse(sourceLocations[0], out int l))
                line = l;

        if (sourceLocations.Length > 1)
            if (int.TryParse(sourceLocations[1], out int c))
                column = c;

        return message.Substring(valueIndex + 2);
    }


    private static string ParseSeverityAndType(string message, out LogSeverity severity, out string? type)
    {
        severity = LogSeverity.Normal;
        type = null;

        int valueIndex = message.IndexOf(':');

        if (valueIndex == -1)
            return message;

        // Extract the (severity,warningType) from the message
        string[] severityAndType = message.Substring(0, valueIndex).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (severityAndType.Length > 0)
        {
            if (string.Equals(severityAndType[0], "warning", StringComparison.OrdinalIgnoreCase))
                severity = LogSeverity.Warning;
            else if (string.Equals(severityAndType[0], "error", StringComparison.OrdinalIgnoreCase))
                severity = LogSeverity.Error;
            else
                severity = LogSeverity.Normal;
        }

        if (severityAndType.Length > 1)
        {
            type = severityAndType[1];
        }

        return message.Substring(valueIndex + 2);
    }


    private static void LogCompilationMessage(object? sender, DataReceivedEventArgs args)
    {
        string? message = args.Data;

        if (string.IsNullOrWhiteSpace(message))
            return;

        message = ParseMessageCSFile(message, out string? csFile);

        // Ignore info messages (restore, error/warn count, time elapsed, etc...)
        if (csFile == null)
            return;

        message = ParseMessageCSProj(message, out string? csprojFile);
        message = ParseFileLocations(message, out int? line, out int? column);
        message = ParseSeverityAndType(message, out LogSeverity severity, out string? type);

        if (type != null)
            message = $"error {type}: " + message;

        if (csFile != null)
            message = $"{Path.GetFileName(csFile)}: " + message;

        DebugStackTrace trace = csFile == null ?
            new DebugStackTrace() :
            new DebugStackTrace(
                new DebugStackFrame(csFile, line, column)
            );

        Runtime.Debug.Log(message, severity, trace);
    }


    private static IEnumerable<int> AllIndexesOf(string str, string searchstring)
    {
        int minIndex = str.IndexOf(searchstring);
        while (minIndex != -1)
        {
            yield return minIndex;
            minIndex = str.IndexOf(searchstring, minIndex + searchstring.Length);
        }
    }
}
