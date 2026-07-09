// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Prowl.Editor.Projects;

namespace Prowl.Editor.Core;

/// <summary>
/// Cached Git status for the currently open project. Shells out to <c>git</c> on a background thread
/// and refreshes on a throttle, so UI (e.g. the footer) can read the properties every frame for free.
/// </summary>
public static class GitInfo
{
    /// <summary>Whether a <c>git</c> executable is available on PATH.</summary>
    public static bool GitInstalled { get; private set; }

    /// <summary>Whether the open project sits inside a Git working tree.</summary>
    public static bool IsRepository { get; private set; }

    /// <summary>Current branch name (or a short commit hash when detached), empty if not a repo.</summary>
    public static string Branch { get; private set; } = "";

    /// <summary>Whether the working tree has uncommitted changes (per <c>git status --porcelain</c>).</summary>
    public static bool HasChanges { get; private set; }

    private const long PollIntervalMs = 3000;
    private static bool _installChecked;
    private static bool _polling;
    private static long _nextPollAt;

    /// <summary>Refreshes the cached status at most once every few seconds. Safe to call every frame.</summary>
    public static void Poll()
    {
        if (_polling || Environment.TickCount64 < _nextPollAt)
            return;

        _nextPollAt = Environment.TickCount64 + PollIntervalMs;
        _polling = true;
        string? root = Project.Current?.RootPath;
        Task.Run(() =>
        {
            try { Refresh(root); }
            finally { _polling = false; }
        });
    }

    private static void Refresh(string? root)
    {
        if (!_installChecked)
        {
            GitInstalled = TryRun(null, "--version", out _);
            _installChecked = true;
        }

        if (!GitInstalled || root == null || !Directory.Exists(root)
            || !TryRun(root, "rev-parse --is-inside-work-tree", out string inside)
            || inside.Trim() != "true")
        {
            IsRepository = false;
            Branch = "";
            HasChanges = false;
            return;
        }

        IsRepository = true;
        TryRun(root, "rev-parse --abbrev-ref HEAD", out string branch);
        branch = branch.Trim();
        // Detached HEAD reports "HEAD"; fall back to the short commit hash so there's always a label.
        if (branch is "HEAD" or "" && TryRun(root, "rev-parse --short HEAD", out string hash))
            branch = hash.Trim();
        Branch = branch;

        HasChanges = TryRun(root, "status --porcelain", out string status) && !string.IsNullOrWhiteSpace(status);
    }

    private static bool TryRun(string? workingDir, string args, out string output)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (workingDir != null)
                psi.WorkingDirectory = workingDir;

            using Process? p = Process.Start(psi);
            if (p == null)
                return false;

            output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false; // git missing, or the launch failed
        }
    }
}
