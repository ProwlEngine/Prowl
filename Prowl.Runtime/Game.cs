// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Echo.Logging;

using Prowl.Runtime.Audio;

using Prowl.PaperUI;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;

using Silk.NET.Maths;

namespace Prowl.Runtime;

public class EchoLogger : IEchoLogger
{
    public void Debug(string message) => Prowl.Runtime.Debug.Log(message);

    public void Error(string message, Exception? exception = null) => Prowl.Runtime.Debug.LogError(message);

    public void Info(string message) => Prowl.Runtime.Debug.Log(message);

    public void Warning(string message) => Prowl.Runtime.Debug.LogWarning(message);
}

public abstract class Game
{
    private PaperRenderer _paperRenderer;
    private Paper _paper;
    private int frameCounter;
    private bool consoleTitleAvailable = true;

    public Paper PaperInstance => _paper;

    public bool DrawGizmos { get; set; }

    /// <summary>What the GUI frame counts as in <see cref="FrameStats"/>: the game's UI here, the editor's own in the editor.</summary>
    protected virtual FrameStat GuiFrameStat => FrameStats.UI;

    /// <summary>
    /// Added a separate method to initialize the window as it might be needed to restore the latest saved state of the window (size, position, etc.) when the game is launched again.
    /// This allows for better user experience by remembering their preferences.
    /// </summary>
    public virtual void InitializeWindow(string title, int width, int height)
    {
        // A game runs unpaced until its own code says otherwise, so no vsync and no frame limit.
        Window.InitWindow(title, width, height, Silk.NET.Windowing.WindowState.Normal, vsync: false);
    } 

    public void Run(string title, int width, int height)
    {
        // Invariant culture ensures consistent float parsing (dot decimal separator)
        // across all locales. Without this, embedded .mat/.shader files with "0.5"
        // break on cultures that use comma separators.
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

        // A bare standalone game is always "playing" so gameplay (component lifecycle, FixedUpdate,
        // physics stepping) runs. The editor overrides this back to false in its own Initialize and
        // drives play/pause itself. Set before Window.Load -> Initialize so that override wins.
        Application.IsPlaying = true;

        // Installed on this thread, which is the one the loop runs on, so an await in game code resumes
        // where the scene actually lives. The editor restarts the session when play starts and stops.
        Tasks.MainThreadContext.Install();
        InitializeWindow(title, width, height);

        Window.Load += () =>
        {
            try
            {
                Load();
            }
            catch (Exception e)
            {
                // Nothing above this catches, so without it a failure here closes the window with no
                // message at all. It is still fatal, since a half built editor is worse than none,
                // but it is reported first.
                Debug.LogException("An exception occurred while starting up", e);
                throw;
            }
        };

        void Load()
        {
            AudioContext.Initialize(44100, 2, 2048);

            // Renderer projection uses framebuffer (physical) pixels;
            // Paper resolution uses window (logical) size.
            Vector2D<int> fbSize = Window.InternalWindow.FramebufferSize;
            Vector2D<int> winSize = Window.InternalWindow.Size;

            _paperRenderer = new PaperRenderer();
            _paperRenderer.Initialize(fbSize.X, fbSize.Y);
            _paperRenderer.Target = null;
            _paper = new Paper(_paperRenderer, winSize.X, winSize.Y, new Prowl.Quill.FontAtlasSettings());
            _paper.SetClipboardHandler(new RuntimeClipboardHandler());
            // Apply the hovered element's requested cursor to the OS window.
            _paper.OnCursorChange += c => Input.SetCursorShape(c);

            _paper.DevTools.Enabled = true;

            BuiltInAssets.Initialize();
            AssetLoader.SetMainThread();

            SceneInstance.Main = CreateMainWorld();
            Initialize();
        }

        Window.Update += (delta) =>
        {
            try
            {
                // First, so the wait for the headset's frame lands before this frame's time is measured and
                // everything that updates sees the poses for when the frame is displayed.
                XR.BeginFrame();

                UpdatePaperInput();

                AudioContext.Update();

                Time.AdvanceFrame();
                SceneInstance.FrameRunning = true;

                UpdateInputActions();

                BeginUpdate();

                SimulationStep();

                EndUpdate();

                if (Debug.LogToConsole && consoleTitleAvailable && (frameCounter++ % 60 == 0))
                {
                    double fps = delta > 0 ? 1.0 / delta : 0; // real (unscaled) frame rate
                    try
                    {
                        Console.Title = $"{title} - {Window.InternalWindow.FramebufferSize.X}x{Window.InternalWindow.FramebufferSize.Y} - FPS: {fps:F0}";
                    }
                    catch (IOException)
                    {
                        // No console to title, e.g. a process started without a window and its output redirected.
                        consoleTitleAvailable = false;
                    }
                }

            }
            catch (Exception e)
            {
                ReportLoopFailure("Update", e);
            }
        };

        Window.Render += (delta) =>
        {
            try
            {
                SceneInstance? main = SceneInstance.HasMain ? SceneInstance.Main : null;

                BeginRender();

                if (PresentsWorlds) RenderPresentedWorlds();
                OnRender();

                EndRender();

                using (GuiFrameStat.Scope())
                {
                    // Sync Paper's logical resolution + DPI to the window each frame.
                    PreparePaperFrame();
                    _paper.BeginFrame(delta, -1f); // negative = keep DisplayFramebufferScale set by PreparePaperFrame

                    BeginGui(_paper);

                    if (PresentsWorlds)
                        foreach (SceneInstance world in PresentedWorlds())
                            world.OnGui(_paper);
                    OnGui(_paper);

                    EndGui(_paper);

                    _paper.EndFrame();
                }

                // After the GUI, since the editor renders its Game View, and with it the eyes, from there.
                XR.RenderMissedEyes(main);
                XR.EndFrame();

                // A point after every view and the GUI joined the frame, before it is submitted.
                AfterGui();

                RenderTexture.UpdatePool();

                Debug.ClearGizmos();

                // Last thing in the frame: everything Destroy()ed stayed usable right through
                // update, render and GUI, and is torn down here where nothing is mid-callback.
                EngineObject.ProcessDestroyed();

                // Then queued loads and unloads, so one requested this frame tears its sections down here rather
                // than under whatever was still running.
                SceneInstance.ProcessPendingLoad();
                SceneInstance.FrameRunning = false;

                // Frees what nothing reaches any more, after rendering, so no draw in flight still uses it.
                AssetDatabase.EndFrame();
            }
            catch (Exception e)
            {
                SceneInstance.FrameRunning = false;
                ReportLoopFailure("Render", e);
            }
        };

        void ReportLoopFailure(string loop, Exception e)
        {
            Debug.LogException($"An exception occurred during the {loop} loop:", e);

            // A game has nowhere useful to carry on to, so it still fails fast. The editor does: the
            // scene, the panels and whatever is unsaved are all still there, and taking the process
            // down over one bad frame loses all of it.
            if (!Application.IsEditor)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
        }

        Window.Resize += (size) =>
        {
            // Paper's resolution is resynced from PreparePaperFrame each render frame.
            Resize(size.X, size.Y);
        };

        Window.FramebufferResize += (size) =>
        {
            _paperRenderer.UpdateProjection(size.X, size.Y);
        };

        Window.Closing += () =>
        {
            Closing();

            XR.Stop();
            Networking.Networking.Shutdown();

            // Before anything shuts down, so work blocked on the main thread runs while it still can and
            // worker loops watching the session token stop. Workers asking for the main thread from here
            // on are refused rather than run alongside the shutdown.
            Tasks.MainThreadContext.Stop();

            // Dispose every world so everything in them runs its teardown callbacks.
            SceneInstance.Shutdown(everyWorld: true);

            // Asset payloads go while the audio and graphics they belong to are still up.
            AssetDatabase.Shutdown();

            AudioContext.Deinitialize();

            Tasks.MainThreadContext.Uninstall();

            Debug.Log("Is terminating...");
        };

        Debug.LogSuccess("Initialization complete");
        Window.Start();

    }

    private volatile bool _headlessQuitRequested;

    /// <summary>
    /// Runs the game without a window, graphics device or audio - only the simulation loop
    /// (gameplay, physics, scripts). Intended for dedicated servers and for headless build
    /// verification. The loop runs until <see cref="RequestHeadlessQuit"/> / Ctrl+C, or until the
    /// frame/time limit in <paramref name="options"/> is reached.
    /// </summary>
    public void RunHeadless(HeadlessRunOptions? options = null)
    {
        options ??= new HeadlessRunOptions();

        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

        Application.IsPlaying = true;
        Application.IsHeadless = true;
        Application.HeadlessQuit = RequestHeadlessQuit;

        Tasks.MainThreadContext.Install();
        // Registers built-in asset loaders (no GPU work happens until something resolves them).
        BuiltInAssets.Initialize();
        AssetLoader.SetMainThread();
        SceneInstance.Main = CreateMainWorld();
        Initialize();

        Debug.LogSuccess("Headless initialization complete");

        _headlessQuitRequested = false;
        void cancelHandler(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; _headlessQuitRequested = true; }
        try { Console.CancelKeyPress += cancelHandler; } catch { /* no console in some hosts */ }

        Application.TargetFrameRate = options.TargetFrameRate;
        var runClock = System.Diagnostics.Stopwatch.StartNew();
        long frame = 0;

        FrameStats.Start();

        try
        {
            while (!_headlessQuitRequested)
            {
                Time.AdvanceFrame();
                SceneInstance.FrameRunning = true;

                UpdateInputActions();

                BeginUpdate();
                SimulationStep();
                EndUpdate();

                // No render phase here, so the end of the simulation step is the end of the frame.
                EngineObject.ProcessDestroyed();

                // Then queued loads and unloads, applied where nothing is mid callback.
                SceneInstance.ProcessPendingLoad();
                SceneInstance.FrameRunning = false;

                // Frees what nothing reaches any more, after rendering, so no draw in flight still uses it.
                AssetDatabase.EndFrame();

                frame++;
                bool lastFrame = (options.MaxFrames > 0 && frame >= options.MaxFrames)
                    || (options.MaxSeconds > 0 && runClock.Elapsed.TotalSeconds >= options.MaxSeconds);

                // Throttle to the target tick rate so a server doesn't spin a core at 100%. Not after the last
                // frame, since nothing follows it.
                if (!lastFrame)
                    Application.WaitForNextFrame();

                // Before leaving, so a bounded run's final frame is recorded too.
                FrameStats.EndFrame();
                if (lastFrame) break;
            }
        }
        finally
        {
            try { Console.CancelKeyPress -= cancelHandler; } catch { }
            Closing();
            Networking.Networking.Shutdown();
            Tasks.MainThreadContext.Stop();
            SceneInstance.Shutdown(everyWorld: true);
            AssetDatabase.Shutdown();
            Tasks.MainThreadContext.Uninstall();
            Application.TargetFrameRate = 0; // and with it the finer system timer a limit holds
            Application.IsHeadless = false;
            Application.HeadlessQuit = null;
        }
    }

    /// <summary>Signals a running <see cref="RunHeadless"/> loop to exit after the current frame.</summary>
    public void RequestHeadlessQuit() => _headlessQuitRequested = true;

    /// <summary>
    /// Updates the input action maps. Shared by the windowed and headless loops.
    /// </summary>
    private static void UpdateInputActions()
    {
        // Actions are gameplay input and their callbacks are gameplay code, so in the editor they read input the
        // way the scene update does: from the game view only, in its pixels, with simulated input included.
        if (SceneInstance.HasMain && SceneInstance.Main is { IsPlaying: true, Paused: false })
            Application.IsGameplayExecuting = true;

        // Input interaction timing (Hold/Tap/MultiTap windows) is real-world timing, so it uses
        // UNSCALED delta
        Input.UpdateActions(Time.UnscaledDeltaTime);

        Application.IsGameplayExecuting = false;
    }

    /// <summary>
    /// Advances one frame of simulation: every running game world no host claimed, in creation order, then
    /// <see cref="OnUpdate"/>. Shared by the windowed and headless loops.
    /// </summary>
    private void SimulationStep()
    {
        // Before the worlds run, so a continuation resumed this frame sees the same world the rest of the frame will.
        Tasks.MainThreadContext.Current?.Pump();

        // Assets that finished loading in the background join at the start of the frame, all at once.
        AssetDatabase.Pump();

        // Network messages too, so this frame simulates on what arrived. Nothing without a session.
        Networking.Networking.Receive();

        // Pausing play mode has to stop the sound as well as the simulation, or the music carries on
        // over a frozen game. Only in the editor: pausing there is a debugging tool that freezes
        // everything, while a shipped game pausing itself is a design decision, and that one reaches
        // for AudioContext.Suspended when it wants the audio to stop too.
        AudioContext.SuspendedByPause = Application.IsEditor && Application.IsPlaying && SceneInstance.HasMain && SceneInstance.Main.Paused;

        // Before any world's event system ticks, so a menu drawn over the game takes the pointer from it.
        UI.SceneInput.Arbitrate(InputWorlds());

        foreach (SceneInstance world in SceneInstance.All)
        {
            if (!world.TicksWithEngine) continue;
            try
            {
                world.Tick(Time.UnscaledDeltaTime);
                if (DrawGizmos) world.DrawGizmos();
            }
            catch (Exception e) { Debug.LogException($"[Scene] Ticking '{world.Name}' threw", e); }
        }

        OnUpdate();

        // Send what this frame produced.
        Networking.Networking.Flush();
    }

    /// <summary>Whether presented worlds draw to the window. A host that draws them itself, like the editor, turns it off.</summary>
    protected virtual bool PresentsWorlds => true;

    /// <summary>The worlds whose UI input is arbitrated each frame, lowest render order first.</summary>
    protected virtual List<SceneInstance> InputWorlds() => PresentedWorlds();

    /// <summary>Every running world drawn to the game's output, lowest <see cref="SceneInstance.RenderOrder"/> first.</summary>
    // Refilled on every call, so a caller iterates it before asking again.
    private static readonly List<SceneInstance> s_presented = [];

    protected static List<SceneInstance> PresentedWorlds()
    {
        s_presented.Clear();
        foreach (SceneInstance world in SceneInstance.All)
            if (world.Present && world.State == SceneState.Running)
                s_presented.Add(world);
        s_presented.Sort(static (a, b) => a.RenderOrder.CompareTo(b.RenderOrder));
        return s_presented;
    }

    private void RenderPresentedWorlds()
    {
        foreach (SceneInstance world in PresentedWorlds())
        {
            try { world.Render(null, DrawGizmos); }
            catch (Exception e) { Debug.LogException($"[Render] Drawing '{world.Name}' threw", e); }
        }
    }

    /// <summary>
    /// The world bound as Main before <see cref="Initialize"/> and kept until shutdown: a running, presented game world
    /// that levels load into. The editor makes its edit world instead.
    /// </summary>
    protected virtual SceneInstance CreateMainWorld()
    {
        SceneInstance main = SceneInstance.Create("Main");
        main.Present = true;
        main.Activate();
        return main;
    }

    public virtual void Initialize() { }

    public virtual void BeginUpdate() { }
    public virtual void EndUpdate() { }
    public virtual void BeginRender() { }
    public virtual void EndRender() { }
    public virtual void BeginGui(Paper paper) { }
    public virtual void EndGui(Paper paper) { }

    /// <summary>Called after the GUI frame is submitted and before the backbuffer is swapped.</summary>
    public virtual void AfterGui() { }

    /// <summary>Called each frame after the engine ticked its worlds.</summary>
    public virtual void OnUpdate() { }

    /// <summary>Called each frame after the presented worlds drew.</summary>
    public virtual void OnRender() { }

    /// <summary>Called each frame after the presented worlds' GUI.</summary>
    public virtual void OnGui(Paper paper) { }

    public virtual void Resize(int width, int height) { }
    public virtual void Closing() { }

    /// <summary>
    /// Called each frame right before <c>Paper.BeginFrame</c>. Sets Paper's logical resolution
    /// to the window's logical size (OS points) and <c>DisplayFramebufferScale</c> to
    /// <see cref="Window.ContentScale"/>. Quill's <c>TransformPoint</c> multiplies every vertex
    /// by <c>DisplayFramebufferScale</c>, so vertices span <c>[0, winSize × cs]</c> =
    /// <c>[0, fbSize]</c>, exactly matching the orthographic projection set up by
    /// <see cref="PaperRenderer.UpdateProjection"/>.
    /// <para>
    /// A widget declared <c>Width(100)</c> occupies 100 logical points = 100 physical pixels at
    /// 1× DPI and 200 physical pixels at 2× (Retina) DPI the same physical size on screen
    /// regardless of display density, with higher pixel quality on HiDPI displays.
    /// </para>
    /// </summary>
    protected virtual void PreparePaperFrame()
    {
        Vector2D<int> fbSize = Window.InternalWindow.FramebufferSize;
        float cs = Math.Max(0.01f, Window.ContentScale);
        // resolution × cs = fbSize, so vertices always span exactly [0, fbSize].
        // Using fbSize (not winSize) handles DPI-unaware Windows where winSize == fbSize
        // but glfwGetWindowContentScale still returns the system DPI factor (e.g. 1.25).
        _paper.SetResolution(fbSize.X / cs, fbSize.Y / cs);
        _paper.DisplayFramebufferScale = new Float2(cs, cs);
    }

    protected virtual Float2 GetPaperMousePosition()
    {
        Int2 p = Input.MousePosition;
        Vector2D<int> fb = Window.InternalWindow.FramebufferSize;
        Vector2D<int> win = Window.InternalWindow.Size;
        float cs = Math.Max(0.01f, Window.ContentScale);
        // Mouse is in winSize coords; paper space is [0, fbSize/cs].
        // csFbWin converts winSize → fbSize; dividing by cs then lands in paper space.
        // On macOS cs == csFbWin so the ratio is 1. On DPI-unaware Windows csFbWin == 1
        // and cs is the system DPI, so we divide mouse by cs.
        float csFbWin = win.X > 0 ? (float)fb.X / win.X : 1f;
        return new Float2(p.X * csFbWin / cs, p.Y * csFbWin / cs);
    }

    private void UpdatePaperInput() => PaperInputBridge.Pump(_paper, GetPaperMousePosition());


    /// <summary>Closes the window. In the editor this closes the editor itself, so gameplay code uses <see cref="Application.Quit"/>.</summary>
    public static void Quit()
    {
        Window.Stop();
        Debug.Log("Is terminating...");
    }
}

/// <summary>
/// Options for <see cref="Game.RunHeadless"/>.
/// </summary>
public sealed class HeadlessRunOptions
{
    /// <summary>Stop after this many frames. 0 = run until quit (server mode).</summary>
    public long MaxFrames = 0;

    /// <summary>Stop after this many seconds of wall-clock time. 0 = no time limit.</summary>
    public double MaxSeconds = 0;

    /// <summary>
    /// Tick rate the loop starts throttled to. 0 = run as fast as possible. This seeds
    /// <see cref="Application.TargetFrameRate"/>, which game code can change while running.
    /// </summary>
    public int TargetFrameRate = 60;
}

/// <summary>
/// Clipboard handler that bridges Paper's clipboard interface to Prowl's Input system.
/// </summary>
internal class RuntimeClipboardHandler : PaperUI.IClipboardHandler
{
    public string GetClipboardText() => Input.Clipboard ?? "";
    public void SetClipboardText(string text) => Input.Clipboard = text;
}
