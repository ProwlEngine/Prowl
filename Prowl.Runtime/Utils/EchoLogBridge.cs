// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Echo.Logging;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Routes Echo's serializer diagnostics into <see cref="Debug"/>.
/// <para/>
/// Echo defaults <see cref="Serializer.Logger"/> to a logger that discards everything, and every one
/// of its own failure sites is a swallowed exception: a field that throws while being written is
/// caught, omitted from the output, and the object is saved without it. Left unrouted that is silent
/// data loss - a scene saves "successfully" minus whatever failed, and the gap only surfaces on a
/// later load as missing values or broken references, far from the save that caused it.
/// <para/>
/// Installed by a module initializer rather than from a startup path, so it is live for the editor,
/// the players, headless runs and tests alike, before anything can read or write a scene. There is no
/// entry point left to forget it in.
/// </summary>
public static class EchoLogBridge
{
    [ModuleInitializer]
    internal static void Install() => Serializer.Logger = new ProwlEchoLogger();

    /// <summary>
    /// Echo reports per field per object, so one bad field in a scene would otherwise report once per
    /// object per save. Its messages name the field and type that failed, which is what makes the
    /// message usable as the dedup id; <see cref="Debug.ClearReportedOnce"/> (play mode, script reload)
    /// lets a condition report again once it has been fixed.
    /// </summary>
    private sealed class ProwlEchoLogger : IEchoLogger
    {
        public void Debug(string message) => Prowl.Runtime.Debug.Log($"[Echo] {message}");

        public void Info(string message) => Prowl.Runtime.Debug.Log($"[Echo] {message}");

        public void Warning(string message)
            => Prowl.Runtime.Debug.LogWarningOnce($"Echo.{message}", $"[Echo] {message}");

        public void Error(string message, Exception? exception = null)
            => Prowl.Runtime.Debug.LogErrorOnce($"Echo.{message}", exception == null
                ? $"[Echo] {message}"
                : $"[Echo] {message} - {exception.GetType().Name}: {exception.Message}");
    }
}
