// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Events {

/// <summary>
/// Configurable logging hooks for the event system.
/// The hosting application should assign
/// <see cref="LogWarning"/> and <see cref="LogError"/> during startup
/// so that diagnostic messages are routed through the engine's logging
/// infrastructure.
/// </summary>
public static class EventSystemDiagnostics
{
    /// <summary>
    /// Called for non-critical diagnostic messages (e.g. slow handlers, sync-over-async).
    /// </summary>
    public static Action<string>? LogWarning { get; set; }

    /// <summary>
    /// Called for error-level diagnostic messages (e.g. type-mismatch on invoke).
    /// </summary>
    public static Action<string>? LogError { get; set; }
}

}
