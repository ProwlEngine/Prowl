// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// Provides global application state flags.
/// </summary>
public static class Application
{
    /// <summary>
    /// True when the game is actively running (play mode in editor, or standalone player).
    /// </summary>
    public static bool IsPlaying { get; internal set; }

    /// <summary>
    /// True when running inside the editor (false in standalone builds).
    /// </summary>
    public static bool IsEditor { get; internal set; }
}
