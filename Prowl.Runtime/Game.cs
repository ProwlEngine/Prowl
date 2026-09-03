// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Echo.Logging;

using Prowl.Runtime.Audio;

using Prowl.PaperUI;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;

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
    private TimeData time = new();

    private PaperRenderer _paperRenderer;
    private Paper _paper;
    private int frameCounter;

    public Paper PaperInstance => _paper;

    public bool DrawGizmos { get; set; }

    /// <summary>
    /// Added a separate method to initialize the window as it might be needed to restore the latest saved state of the window (size, position, etc.) when the game is launched again.
    /// This allows for better user experience by remembering their preferences.
    /// </summary>
    public virtual void InitializeWindow(string title, int width, int height)
    {
        Window.InitWindow(title, width, height, Silk.NET.Windowing.WindowState.Normal, false);
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
        // where the scene actually lives. The editor starts and ends a session per play instead.
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
                Debug.LogError("An exception occurred while starting up:");
                Debug.LogError(e.ToString());
                throw;
            }
        };

        void Load()
        {
            AudioContext.Initialize(44100, 2, 2048);

            // Renderer projection uses framebuffer (physical) pixels;
            // Paper resolution uses window (logical) size.
            var fbSize = Window.InternalWindow.FramebufferSize;
            var winSize = Window.InternalWindow.Size;

            _paperRenderer = new PaperRenderer();
            _paperRenderer.Initialize(fbSize.X, fbSize.Y);
            _paper = new Paper(_paperRenderer, winSize.X, winSize.Y, new Prowl.Quill.FontAtlasSettings());
            _paper.SetClipboardHandler(new RuntimeClipboardHandler());
            // Apply the hovered element's requested cursor to the OS window.
            _paper.OnCursorChange += c => Input.SetCursorShape(c);

            _paper.DevTools.Enabled = true;

            BuiltInAssets.Initialize();

            Initialize();
        }

        Window.Update += (delta) =>
        {
            try
            {
                UpdatePaperInput();

                AudioContext.Update();

                time.Update();
                Time.TimeStack.Clear();
                Time.TimeStack.Push(time);

                // Input interaction timing (Hold/Tap/MultiTap windows) is real-world timing, so it uses
                // UNSCALED delta
                Input.UpdateActions(Time.UnscaledDeltaTime);

                BeginUpdate();

                SimulationStep(Time.DeltaTime);

                EndUpdate();

                if (frameCounter++ % 60 == 0)
                {
                    double fps = delta > 0 ? 1.0 / delta : 0; // real (unscaled) frame rate
                    Console.Title = $"{title} - {Window.InternalWindow.FramebufferSize.X}x{Window.InternalWindow.FramebufferSize.Y} - FPS: {fps:F0}";
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
                Scene? currentScene = Scene.Current;

                // === Start Graphics ===

                {
                    using var frameStart = Graphics.GetCommandBuffer("Frame Start");
                    frameStart.SetRenderTarget(null);
                    frameStart.SetViewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
                    frameStart.SetRasterState(new RasterizerState());
                    frameStart.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil, new Color(0, 0, 0, 1));
                    Graphics.Submit(frameStart);
                }

                Rendering.ShadowAtlas.TryInitialize();
                Rendering.ShadowAtlas.Clear();

                // === End of Start Graphics ===

                BeginRender();

                OnRender(currentScene);

                EndRender();

                {
                    using var preGui = Graphics.GetCommandBuffer("Pre-GUI");
                    preGui.SetRenderTarget(null);
                    preGui.SetViewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
                    Graphics.Submit(preGui);
                }

                // Sync Paper's logical resolution + DPI to the window each frame.
                PreparePaperFrame();
                _paper.BeginFrame(delta, -1f); // negative = keep DisplayFramebufferScale set by PreparePaperFrame

                BeginGui(_paper);

                OnGui(currentScene, _paper);

                EndGui(_paper);

                _paper.EndFrame();

                // Give presentation hosts a point after all scene and Paper commands have been
                // submitted but before the backbuffer is swapped. Development screenshot capture
                // uses this hook to read the actual rendered client on the render thread.
                AfterGui(currentScene);

                // === End Graphics ===

                RenderTexture.UpdatePool();
                // Dispose any GPU resources that were replaced mid-frame (e.g.
                // grown instance buffers). This only ENQUEUES delete CBs; the render
                // thread is still draining this frame's queue. Because the deletes are
                // submitted after every draw that referenced the old handle, submit
                // order guarantees they execute last on the render thread.
                Graphics.FlushDeferredDisposes();

                // === End of End Graphics ===

                Debug.ClearGizmos();

                // Last thing in the frame: everything Destroy()ed stayed usable right through
                // update, render and GUI, and is torn down here where nothing is mid-callback.
                EngineObject.ProcessDestroyed();

                // Then the scene swap, so a load requested this frame tears the outgoing scene down
                // here rather than under whatever was still running.
                Scene.ProcessPendingLoad();
            }
            catch (Exception e)
            {
                ReportLoopFailure("Render", e);
            }
        };

        void ReportLoopFailure(string loop, Exception e)
        {
            Debug.LogError($"An exception occurred during the {loop} loop:");
            Debug.LogError(e.ToString());

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

            // Dispose the current scene so everything in it runs its teardown callbacks.
            Scene.Shutdown();

            AudioContext.Deinitialize();

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

        Tasks.MainThreadContext.Install();
        // Registers built-in asset loaders (no GPU work happens until something resolves them).
        BuiltInAssets.Initialize();
        Initialize();

        Debug.LogSuccess("Headless initialization complete");

        _headlessQuitRequested = false;
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; _headlessQuitRequested = true; };
        try { Console.CancelKeyPress += cancelHandler; } catch { /* no console in some hosts */ }

        float targetFrameTime = options.TargetFps > 0 ? 1.0f / options.TargetFps : 0.0f;
        var runClock = System.Diagnostics.Stopwatch.StartNew();
        long frame = 0;

        try
        {
            while (!_headlessQuitRequested)
            {
                long frameStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();

                time.Update();
                Time.TimeStack.Clear();
                Time.TimeStack.Push(time);

                Input.UpdateActions(Time.UnscaledDeltaTime);

                BeginUpdate();
                SimulationStep(Time.DeltaTime);
                EndUpdate();

                // No render phase here, so the end of the simulation step is the end of the frame.
                EngineObject.ProcessDestroyed();

                // Then the scene swap, so a load requested this frame tears the outgoing scene down
                // here rather than under whatever was still running.
                Scene.ProcessPendingLoad();

                frame++;
                if (options.MaxFrames > 0 && frame >= options.MaxFrames) break;
                if (options.MaxSeconds > 0 && runClock.Elapsed.TotalSeconds >= options.MaxSeconds) break;

                // Throttle to the target tick rate so a server doesn't spin a core at 100%.
                if (targetFrameTime > 0.0f)
                {
                    float elapsed = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - frameStartTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
                    int sleepMs = (int)((targetFrameTime - elapsed) * 1000.0f);
                    if (sleepMs > 0) System.Threading.Thread.Sleep(sleepMs);
                }
            }
        }
        finally
        {
            try { Console.CancelKeyPress -= cancelHandler; } catch { }
            Closing();
            Scene.Shutdown();
            Application.IsHeadless = false;
        }
    }

    /// <summary>Signals a running <see cref="RunHeadless"/> loop to exit after the current frame.</summary>
    public void RequestHeadlessQuit() => _headlessQuitRequested = true;

    /// <summary>
    /// Advances one simulation step: the fixed-update loop (when gameplay should run) followed by
    /// <see cref="OnUpdate"/>. Shared by the windowed and headless loops.
    /// </summary>
    private void SimulationStep(float delta)
    {
        Scene? currentScene = Scene.Current;

        // Before the scene runs, so a continuation resumed this frame sees the same world the rest of
        // the frame will. Anything left over from a finished play session is dropped here.
        Tasks.MainThreadContext.Current?.Pump();

        // Pausing play mode has to stop the sound as well as the simulation, or the music carries on
        // over a frozen game. Only in the editor: pausing there is a debugging tool that freezes
        // everything, while a shipped game pausing itself is a design decision, and that one reaches
        // for AudioContext.Suspended when it wants the audio to stop too.
        AudioContext.SuspendedByPause = Application.IsEditor && Application.IsPlaying && Application.IsPaused;

        // Fixed update loop only when gameplay should run
        Time.FixedAccumulator += delta;
        if (Application.ShouldRunGameplay)
        {
            Application.IsGameplayExecuting = true;
            int count = 0;
            while (Time.FixedAccumulator >= Time.FixedDeltaTime && count++ < Time.MaxFixedIterations)
            {
                if (currentScene.IsValid()) currentScene.FixedUpdate();
                Time.FixedAccumulator -= Time.FixedDeltaTime;
            }

            // If the iteration cap was hit there is still a backlog; discard it rather than replaying
            // it over the next frames (avoids post-hitch slow-motion and the spiral of death).
            if (Time.FixedAccumulator >= Time.FixedDeltaTime)
                Time.FixedAccumulator = 0f;

            Application.IsGameplayExecuting = false;
        }
        else
        {
            // Clamp accumulator to prevent burst when unpausing/starting play
            Time.FixedAccumulator = MathF.Min(Time.FixedAccumulator, Time.FixedDeltaTime);
        }

        OnUpdate(currentScene);

        // Consume step request re-pause after one frame
        if (Application.StepRequested)
        {
            Application.StepRequested = false;
            Application.IsPaused = true;
        }
    }

    public virtual void Initialize() { }

    public virtual void BeginUpdate() { }
    public virtual void EndUpdate() { }
    public virtual void BeginRender() { }
    public virtual void EndRender() { }
    public virtual void BeginGui(Paper paper) { }
    public virtual void EndGui(Paper paper) { }

    /// <summary>Called after the GUI frame is submitted and before the backbuffer is swapped.</summary>
    public virtual void AfterGui(Scene? scene) { }

    /// <summary>Called during update. Override to control scene update/gizmo behavior.</summary>
    public virtual void OnUpdate(Scene? scene)
    {
        if (scene.IsValid()) scene.Update();
        if (DrawGizmos && scene.IsValid())
            scene.DrawGizmos();
    }

    /// <summary>Called during render. Override to control scene rendering.</summary>
    public virtual void OnRender(Scene? scene)
    {
        if (scene.IsValid()) scene.Render();
    }

    /// <summary>Called during GUI phase. Override to control scene GUI rendering.</summary>
    public virtual void OnGui(Scene? scene, Paper paper)
    {
        if (scene.IsValid()) scene.OnGui(paper);
    }

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
        var fbSize = Window.InternalWindow.FramebufferSize;
        float cs = Math.Max(0.01f, Window.ContentScale);
        // resolution × cs = fbSize, so vertices always span exactly [0, fbSize].
        // Using fbSize (not winSize) handles DPI-unaware Windows where winSize == fbSize
        // but glfwGetWindowContentScale still returns the system DPI factor (e.g. 1.25).
        _paper.SetResolution(fbSize.X / cs, fbSize.Y / cs);
        _paper.DisplayFramebufferScale = new Float2(cs, cs);
    }

    protected virtual Float2 GetPaperMousePosition()
    {
        var p = Input.MousePosition;
        var fb = Window.InternalWindow.FramebufferSize;
        var win = Window.InternalWindow.Size;
        float cs = Math.Max(0.01f, Window.ContentScale);
        // Mouse is in winSize coords; paper space is [0, fbSize/cs].
        // csFbWin converts winSize → fbSize; dividing by cs then lands in paper space.
        // On macOS cs == csFbWin so the ratio is 1. On DPI-unaware Windows csFbWin == 1
        // and cs is the system DPI, so we divide mouse by cs.
        float csFbWin = win.X > 0 ? (float)fb.X / win.X : 1f;
        return new Float2(p.X * csFbWin / cs, p.Y * csFbWin / cs);
    }

    private void UpdatePaperInput() => PaperInputBridge.Pump(_paper, GetPaperMousePosition());

    /// <summary>Reset the fixed-update accumulator. Call when entering play mode to prevent a burst.</summary>
    protected void ResetFixedTimeAccumulator() => Time.FixedAccumulator = 0f;


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

    /// <summary>Tick rate to throttle the loop to. 0 = run as fast as possible.</summary>
    public int TargetFps = 60;
}

/// <summary>
/// Clipboard handler that bridges Paper's clipboard interface to Prowl's Input system.
/// </summary>
internal class RuntimeClipboardHandler : PaperUI.IClipboardHandler
{
    public string GetClipboardText() => Input.Clipboard ?? "";
    public void SetClipboardText(string text) => Input.Clipboard = text;
}
