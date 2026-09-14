// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// Provides global application state flags.
/// </summary>
public static class Application
{
    private static bool s_isPlaying;

    /// <summary>
    /// True when the game is actively running (play mode in editor, or standalone player).
    /// </summary>
    public static bool IsPlaying
    {
        get => s_isPlaying;
        set
        {
            if (s_isPlaying == value) return;
            s_isPlaying = value;

            // Entering or leaving play mode is a fresh run, so conditions that were reported once
            // during the last one should be allowed to report again.
            Debug.ClearReportedOnce();
        }
    }

    /// <summary>
    /// True when running inside the editor (false in standalone builds).
    /// </summary>
    public static bool IsEditor { get; set; }

    /// <summary>
    /// True when running without a window or graphics device (e.g. a dedicated server, or a build
    /// launched with --headless). Gameplay, physics and scripts still run; rendering does not.
    /// </summary>
    public static bool IsHeadless { get; set; }

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

    private static readonly FrameLimiter s_limiter = new();
    private static bool s_vsync;

    /// <summary>
    /// Frames per second the loop is paced to, whether that loop is the editor's, a standalone
    /// game's or a headless server's. Zero or less runs unlimited, which is what a game starts at
    /// until its own code says otherwise. This is a ceiling, so with <see cref="VSync"/> on the
    /// lower of the two wins.
    /// <para>
    /// In the editor this belongs to the editor's own preferences until play mode starts, which
    /// hands it back to this default so the game's code decides, exactly as in a build.
    /// </para>
    /// </summary>
    public static int TargetFrameRate
    {
        get => s_limiter.TargetFrameRate;
        set => s_limiter.TargetFrameRate = value;
    }

    /// <summary>
    /// Whether a frame is held back until the display is ready for it, which caps the rate to the
    /// refresh rate and removes tearing. Off in a game until its own code turns it on, and on in
    /// the editor unless its preferences say otherwise. Has nothing to act on when
    /// <see cref="IsHeadless"/>.
    /// </summary>
    public static bool VSync
    {
        get => s_vsync;
        set
        {
            s_vsync = value;
            // Silk.NET's own window property is only read by the automatic Run loop, which Prowl
            // does not use. The swap interval is the whole setting, and the render thread applies
            // it because it is the thread holding the context.
            Graphics.SetSwapInterval(value ? 1 : 0);
        }
    }

    /// <summary>Blocks until the next frame is due. The run loops call this, once per frame.</summary>
    internal static void WaitForNextFrame() => s_limiter.Wait();

    /// <summary>
    /// Directory containing the running executable (standalone) or project root (editor).
    /// Used by PlayerAssetBackend to locate assets relative to the executable.
    /// </summary>
    public static string DataPath { get; set; } = System.AppContext.BaseDirectory;
}
