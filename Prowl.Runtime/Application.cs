// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Diagnostics;

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
    /// True while gameplay code (Update/FixedUpdate) is executing.
    /// Used by the editor's input filtering to distinguish gameplay input from editor input.
    /// </summary>
    public static bool IsGameplayExecuting { get; set; }

    private static readonly FrameLimiter s_limiter = new();
    private static VSyncMode s_vsync;

    /// <summary>
    /// Frames per second the loop is paced to, whether that loop is the editor's, a standalone
    /// game's or a headless server's. Zero or less runs unlimited, which is what a game starts at
    /// until its own code says otherwise. This is a ceiling, so while <see cref="VSync"/> holds frames to
    /// the refresh the lower of the two wins.
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
    /// How a frame waits for the display before it is shown. <see cref="VSyncMode.Mailbox"/> in a
    /// game until its own code says otherwise, and the editor's preference in the editor. Has
    /// nothing to act on when <see cref="IsHeadless"/>.
    /// </summary>
    public static VSyncMode VSync
    {
        get => s_vsync;
        set
        {
            s_vsync = value;
            Graphics.VSync = value;
        }
    }

    /// <summary>The vsync a game starts with, in a build and as play mode starts. Set from the project's Graphics settings.</summary>
    public static VSyncMode GameVSync { get; set; } = VSyncMode.Mailbox;

    /// <summary>The frame rate a game starts paced to, 0 for unlimited. Set from the project's Graphics settings.</summary>
    public static int GameTargetFrameRate { get; set; }

    /// <summary>Blocks until the next frame is due. The run loops call this, once per frame.</summary>
    internal static void WaitForNextFrame()
    {
        using FrameStatScope stats = FrameStats.Idle.Scope();

        // A running XR session paces the loop through the headset's own frame timing.
        if (XR.IsPacingFrames) return;
        s_limiter.Wait();
    }

    /// <summary>Replaces what <see cref="Quit"/> does. The editor sets this so quitting stops play mode instead of closing the editor.</summary>
    public static System.Action? QuitHandler { get; set; }

    /// <summary>Ends the running headless loop. Set by <see cref="Game.RunHeadless"/> for the duration of the run.</summary>
    internal static System.Action? HeadlessQuit { get; set; }

    /// <summary>
    /// Quits the game. In the editor this stops play mode, in a headless run it ends the loop, and in a
    /// standalone build it closes the window. Gameplay code should call this rather than <see cref="Game.Quit"/>.
    /// </summary>
    public static void Quit()
    {
        if (QuitHandler != null) QuitHandler();
        else if (IsHeadless) HeadlessQuit?.Invoke();
        else Game.Quit();
    }

    /// <summary>
    /// Directory containing the running executable (standalone) or project root (editor).
    /// Used by PlayerAssetBackend to locate assets relative to the executable.
    /// </summary>
    public static string DataPath { get; set; } = System.AppContext.BaseDirectory;
}
