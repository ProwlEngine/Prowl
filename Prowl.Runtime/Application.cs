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
    public static bool IsPlaying { get; set; }

    /// <summary>
    /// True when running inside the editor (false in standalone builds).
    /// </summary>
    public static bool IsEditor { get; set; }

    /// <summary>
    /// True when play mode is paused. Update/FixedUpdate/LateUpdate stop, but rendering continues.
    /// </summary>
    public static bool IsPaused { get; set; }

    /// <summary>
    /// When true, one frame of gameplay executes then IsPaused reasserts.
    /// Set by the editor Step button, consumed by the game loop.
    /// </summary>
    internal static bool StepRequested { get; set; }

    /// <summary>Whether gameplay should execute this frame (playing and not paused, or stepping).</summary>
    public static bool ShouldRunGameplay => IsPlaying && (!IsPaused || StepRequested);

    /// <summary>
    /// True while gameplay code (Update/FixedUpdate) is executing.
    /// Used by the editor's input filtering to distinguish gameplay input from editor input.
    /// </summary>
    public static bool IsGameplayExecuting { get; set; }

    /// <summary>
    /// Directory containing the running executable (standalone) or project root (editor).
    /// Used by PlayerAssetDatabase to locate assets relative to the executable.
    /// </summary>
    public static string DataPath { get; set; } = System.AppContext.BaseDirectory;
}
