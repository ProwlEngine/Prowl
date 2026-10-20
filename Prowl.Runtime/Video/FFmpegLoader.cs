// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace Prowl.Runtime.Video;

/// <summary>
/// Dynamically locates and binds the optional LGPL FFmpeg shared libraries at runtime.
/// If the shared libraries are not present, <see cref="IsAvailable"/> returns false without crashing.
/// </summary>
public static class FFmpegLoader
{
    private static readonly object s_lock = new();
    private static bool s_initialized;
    private static bool s_available;

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return s_available;
        }
    }

    public static bool EnsureInitialized()
    {
        if (s_initialized) return s_available;

        lock (s_lock)
        {
            if (s_initialized) return s_available;
            s_initialized = true;

            try
            {
                string? nativeDir = FindNativeLibraryDirectory();
                if (nativeDir != null)
                    DynamicallyLoadedBindings.LibrariesPath = nativeDir;

                DynamicallyLoadedBindings.Initialize();

                // Probe a lightweight avutil call to confirm the native libraries actually bound.
                uint version = ffmpeg.avutil_version();
                s_available = version > 0;

                if (s_available)
                {
                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                    Debug.LogSuccess($"[FFmpeg] Loaded LGPL shared libraries (avutil v{version}) from '{nativeDir ?? "system path"}'.");
                }
            }
            catch (Exception ex)
            {
                s_available = false;
                Debug.LogWarningOnce("FFmpeg.Unavailable",
                    $"[FFmpeg] Optional FFmpeg shared libraries were not found or failed to load ({ex.Message}). Video playback is disabled.");
            }

            return s_available;
        }
    }

    private static string? FindNativeLibraryDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64"
                   : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
                   : "linux-x64";

        string[] candidates =
        [
            Path.Combine(baseDir, "runtimes", rid, "native"),
            Path.Combine(baseDir, "runtimes", rid),
            Path.Combine(baseDir, "runtimes"),
            Application.DataPath,
            baseDir
        ];

        foreach (string dir in candidates)
        {
            if (Directory.Exists(dir))
                return dir;
        }

        return null;
    }
}
