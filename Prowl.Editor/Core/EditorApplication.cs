using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using Prowl.Editor.Thumbnails;
using Prowl.OrigamiUI;
using Prowl.Editor.GraphTools.ShaderGraphs.Editors;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Vector;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;

namespace Prowl.Editor.Core;

public class EditorApplication : Game
{
    public static EditorApplication? Instance { get; private set; }

    /// <summary>The editor's PropertyGrid configuration (drawers, handlers, callbacks).</summary>
    public static OrigamiUI.PropertyGridConfig PropertyGridConfig { get; private set; } = null!;

    private DockSpace _dockSpace = null!;
    private double _time;
    private GUI.NebulaBackground? _nebula;
    private double _introTime = double.MaxValue;
    private const double IntroCloseDuration = 2.0; // bars close over launcher
    private const double IntroOpenDuration = 3.0;  // bars open revealing editor
    private const double IntroDuration = 5.0;      // total
    private bool _introClosing; // true = closing phase (bars sliding in)
    private bool _launcherWasOpen = true;
    private IDisposable? _origamiScope;

    private string _curDefaultFont;
    private string _curDefaultBoldFont;

    // Play mode state
    private Echo.EchoObject? _savedEditorScene;
    private int _savedActiveTabIndex = -1;
    private DockNode? _savedActiveTabNode;
    private TimeData? _savedEditorTime;

    // All registered panel types (from [EditorWindow] attribute scan)
    private readonly List<(Type type, string path)> _registeredPanels = new();

    public override void InitializeWindow(string title, int width, int height)
    {
        var instance = EditorSettings.Instance;
        Window.InitWindow(title, width, height, instance.WindowMaximized ? Silk.NET.Windowing.WindowState.Maximized : Silk.NET.Windowing.WindowState.Normal, false);

        Window.Position = new Silk.NET.Maths.Vector2D<int>(
            instance.WindowX > 0 ? instance.WindowX : Window.Position.X,
            instance.WindowY > 0 ? instance.WindowY : Window.Position.Y);
    }

    public override void Initialize()
    {
        Instance = this;
        Application.IsEditor = true;
        Application.IsPlaying = false;

        InitializeFont();

        Resize(Window.Size.X, Window.Size.Y);

        // Load Font Awesome as fallback fonts for icons. Keep the FontFile handles so icons can also
        // be drawn in a chosen weight directly (e.g. an outline vs filled star), independent of the
        // fallback resolution order.
        EditorTheme.FontIconOutline = LoadFallbackFont("Prowl.Editor.Resources.fa-regular-400.ttf");
        EditorTheme.FontIconSolid   = LoadFallbackFont("Prowl.Editor.Resources.fa-solid-900.ttf");

        // Load CJK/international fallback fonts from system for localization support
        LoadSystemFallbackFonts();

        // Load editor settings (global, persists across projects)
        _ = EditorSettings.Instance; // triggers load + ApplyTheme

        _dockSpace = new DockSpace(CreateDefaultLayout());

        // If launched with --project arg, open the project and load assemblies
        // BEFORE registries scan so user types are visible to all registries
        bool projectAlreadyInitialized = false;
        if (Program.StartupProjectPath != null)
        {
            try
            {
                var project = Project.Open(Program.StartupProjectPath);
                project.SetActive();

                // Load user script assemblies before registry scanning
                ScriptAssemblyManager.LoadAssemblies(project);

                projectAlreadyInitialized = true;
                Window.InternalWindow.Title = $"Prowl Editor - {project.Name}";
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"Failed to open project from --project arg: {ex.Message}");
            }
        }

        // Initialize localization
        Prowl.Rosetta.Loc.Configure(cfg => cfg
            .SetFallbackLocale("en")
            .AddProvider(new EmbeddedResourceProvider(
                System.Reflection.Assembly.GetExecutingAssembly(), "Prowl.Editor.Resources.Locale"))
            .SetLocale(EditorSettings.Instance.Locale)
            .OnMissingKey(Prowl.Rosetta.MissingKeyBehavior.ReturnKey)
        );

        // Initialize editor registries (now sees user types if assemblies loaded above)
        InitializeOnLoadRegistry.Initialize();
        PropertyEditorRegistry.Initialize();
        CustomEditorRegistry.Initialize();
        GraphTools.NodeRendererRegistry.Initialize();
        GraphTools.NodePreviewRegistry.Initialize();
        Runtime.GraphTools.GraphValidatorRegistry.Initialize();
        Inspector.AssetImporterEditorRegistry.Initialize();
        ProjectSettingsRegistry.Initialize();
        CreateAssetMenuRegistry.Initialize();
        ShaderTypeCreateMenu.Register();
        ThumbnailGeneratorRegistry.Initialize();
        SceneDropHandlerRegistry.Initialize();
        CreateGameObjectMenuRegistry.Initialize();
        FileIconRegistry.Initialize();
        AssetDoubleClickRegistry.Initialize();
        ScriptTemplateRegistry.Initialize();
        EditorCallbacks.Initialize();

        // Cursor lock toasts
        Input.OnCursorLocked += () =>
            Toasts.Show(Loc.Get("toast.cursor_locked"), Loc.Get("toast.cursor_locked_msg"), ToastType.Info, 3f);
        Input.OnCursorLockFailed += () =>
            Toasts.Show(Loc.Get("toast.cursor_lock_failed"), Loc.Get("toast.cursor_lock_failed_msg"), ToastType.Warning, 3f);

        // Menus depend on registries above, so register after initialization
        ScanAndRegisterPanels();
        RegisterMenus();

        if (projectAlreadyInitialized)
        {
            // Initialize asset database for the already-opened project
            var db = new EditorAssetDatabase(Project.Current!);
            db.Initialize();

            // Load project settings
            ProjectSettingsRegistry.OnProjectOpened();

            // Restore layout
            var savedLayout = LoadDockLayout();
            if (savedLayout != null)
                _dockSpace.Root = savedLayout;

            // Restore scene (from --restore-scene or last saved)
            if (Program.RestoreScenePath != null && System.IO.File.Exists(Program.RestoreScenePath))
            {
                RestoreAutoSavedScene(Program.RestoreScenePath);
            }
            else
            {
                EditorSceneManager.EnsureSceneLoaded();
            }

            // Skip launcher and intro animation entirely
            ProjectLauncher.Close();
            _launcherWasOpen = false;
            _introClosing = false;
            _introTime = IntroDuration + 1; // past the end no animation
        }
        else
        {
            // Start with the project launcher
            ProjectLauncher.Initialize();
        }

        // Initialize status bar log tracking
        InitializeStatusBar();

        // Build the editor's PropertyGrid config
        PropertyGridConfig = new OrigamiUI.PropertyGridConfig();
        OrigamiUI.BuiltInFieldDrawers.Register(PropertyGridConfig.Drawers);
        BuiltInAttributeHandlers.Register(PropertyGridConfig.Handlers);
        PropertyGridConfig.OnBeginRoot = target => Undo.Snapshot(target);
        PropertyGridConfig.OnFieldChanged = target => (target as Runtime.EngineObject)?.OnValidate();
        PropertyGridConfig.OnBeforeDrawField = (fieldType, value) =>
        {
            if (typeof(Runtime.EngineObject).IsAssignableFrom(fieldType))
                EngineObjectPropertyEditor.SetFieldType(fieldType);
        };
        PropertyGridConfig.DrawTypePicker = (paper, id, baseType, currentValue, onChange) =>
        {
            // Find all concrete types implementing the base type
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .Take(20).ToArray();

            if (types.Length == 0) return;

            Type? currentType = currentValue?.GetType();
            int selectedIndex = currentType != null ? Array.IndexOf(types, currentType) + 1 : 0;
            var names = types.Select(t => t.Name).Prepend("(null)").ToArray();

            OrigamiUI.Origami.Dropdown(paper, $"{id}_dd", selectedIndex,
                idx =>
                {
                    if (idx == 0) onChange(null);
                    else if (idx >= 1 && idx <= types.Length) onChange(Activator.CreateInstance(types[idx - 1]));
                }, names).Show();
        };
        PropertyGridConfig.FallbackFieldDrawer = (paper, id, label, fieldType, value, onChange, depth) =>
        {
            if (typeof(Runtime.EngineObject).IsAssignableFrom(fieldType))
                EngineObjectPropertyEditor.SetFieldType(fieldType);
            var editor = PropertyEditorRegistry.GetEditor(fieldType);
            if (editor != null)
            {
                editor.OnGUI(paper, id, label, value, onChange, depth);
                return true;
            }
            return false;
        };
        // A collection element is a "simple" (one-line object-picker row) rather than an expanded
        // per-element foldout when the host has a single-control editor for it - the same set the
        // FallbackFieldDrawer above handles: EngineObject-derived types (GameObject, Material, ...) and
        // AssetRef<T>. Without this, e.g. GameObject[] renders each element's internal fields instead.
        PropertyGridConfig.IsSimpleFieldType = fieldType => PropertyEditorRegistry.GetEditor(fieldType) != null;

        // Register save handlers
        SaveManager.OnSave += () =>
        {
            if (PrefabEditingMode.IsEditing)
            {
                return PrefabEditingMode.Save()
                    ? Loc.Get("save.prefab", new { name = System.IO.Path.GetFileNameWithoutExtension(PrefabEditingMode.EditingPrefabPath) })
                    : null;
            }
            if (EditorSceneManager.Save())
                return Loc.Get("save.scene", new { name = Runtime.Resources.Scene.Current?.Name ?? "Untitled" });
            return null;
        };
        SaveManager.OnSave += () =>
        {
            if (Project.Current == null) return null;
            SaveProjectState();
            return Loc.Get("save.layout");
        };

        // Set Windows title bar to match Darkest theme color
        ApplyDarkTitleBar();

        // Attach to the window events to save the position and state of the window
        Window.InternalWindow.Move += (position) =>
        {
            EditorSettings.Instance.WindowX = position.X;
            EditorSettings.Instance.WindowY = position.Y;

            EditorSettings.Instance.Save();
        };

        Window.InternalWindow.Resize += (size) =>
        {
            EditorSettings.Instance.WindowWidth = size.X;
            EditorSettings.Instance.WindowHeight = size.Y;
            EditorSettings.Instance.Save();
        };

        Window.InternalWindow.StateChanged += (state) =>
        {
            EditorSettings.Instance.WindowMaximized = state == Silk.NET.Windowing.WindowState.Maximized;

            EditorSettings.Instance.Save();
        };

        Window.InternalWindow.Closing += () =>
        {
            SaveEditorWindowState();
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public void InitializeFont()
    {
        if (EditorTheme.DefaultFont != null) return;

        EditorTheme.DefaultFont  = LoadBundledFont("Geist-Regular.ttf");
        EditorTheme.FontMedium   = LoadBundledFont("Geist-Medium.ttf");
        EditorTheme.FontSemiBold = LoadBundledFont("Geist-SemiBold.ttf");
        EditorTheme.DefaultBoldFont = LoadBundledFont("Geist-Bold.ttf");
        EditorTheme.FontMono     = LoadBundledFont("JetBrainsMono-Regular.ttf");
        EditorTheme.FontDisplay  = LoadBundledFont("SpaceGrotesk-Bold.ttf");
        EditorTheme.FontLogo     = LoadBundledFont("Audiowide-Regular.ttf");

        _curDefaultFont = EditorTheme.DefaultFontName;
        _curDefaultBoldFont = EditorTheme.DefaultBoldFontName;

        // Rebuild the pushed Origami theme now that fonts exist.
        EditorTheme.SyncOrigami(0f);
    }

    private static Prowl.Scribe.FontFile? LoadBundledFont(string fileName)
    {
        using var stream = GetEmbeddedResource(fileName);
        if (stream == null)
        {
            Runtime.Debug.LogWarning($"Missing bundled font: {fileName}");
            return null;
        }
        return new Prowl.Scribe.FontFile(stream);
    }

    protected override void PreparePaperFrame()
    {
        var fbSize = Window.InternalWindow.FramebufferSize;
        float cs = Math.Max(0.01f, Window.ContentScale);
        float us = Math.Max(0.01f, EditorTheme.UserScale);
        PaperInstance.SetResolution(fbSize.X / (cs * us), fbSize.Y / (cs * us));
        PaperInstance.DisplayFramebufferScale = new Float2(cs * us, cs * us);
    }

    protected override Float2 GetPaperMousePosition()
    {
        var p = Input.MousePosition;
        var fb = Window.InternalWindow.FramebufferSize;
        var win = Window.InternalWindow.Size;
        float cs = Math.Max(0.01f, Window.ContentScale);
        float csFbWin = win.X > 0 ? (float)fb.X / win.X : 1f;
        float us = Math.Max(0.01f, EditorTheme.UserScale);
        return new Float2(p.X * csFbWin / (cs * us), p.Y * csFbWin / (cs * us));
    }

    private void ApplyDarkTitleBar()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var native = Window.InternalWindow.Native;
        nint hwnd = native?.Win32?.Hwnd ?? 0;
        if (hwnd == 0) return;

        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
    }

    public override void BeginGui(Paper paper)
    {
        // Flush undo system FIRST Paper callbacks fired in the previous frame's EndFrame(),
        // so mutations from OnValueChanged are now visible. FlushFrame compares the Snapshot
        // (taken last frame) against the current state to detect changes.
        Undo.FlushFrame();

        // Escape always unlocks cursor in editor
        if (Input.GetKeyDown(KeyCode.Escape) && Input.CursorLocked)
        {
            Input.UnlockCursor();
        }

        // Global keyboard shortcuts
        if (!ShortcutManager.IsRebinding)
        {
            if (ShortcutManager.IsPressed("Global/SaveAs"))
            {
                if (Application.IsPlaying)
                    Toasts.Warning(Loc.Get("save.cant_play_mode"),
                        Loc.Get("save.cant_play_mode_msg"));
                else
                    PromptSaveAs();
            }
            else if (ShortcutManager.IsPressed("Global/NewScene"))
            {
                EditorSceneManager.NewScene();
            }
            else if (ShortcutManager.IsPressed("Global/Undo"))
            {
                Undo.PerformUndo();
            }
            else if (ShortcutManager.IsPressed("Global/Redo"))
            {
                Undo.PerformRedo();
            }
        }

        // Reset per-frame state
        GameViewInputHandler.IsGameViewFocused = false;

        // Keeps the editor view outside of UI input's reach, to prevent false positives when clicking around
        if (Runtime.UI.EventSystem.Current is { } eventSystem)
            eventSystem.Viewport = new Runtime.UI.EventSystem.HostViewport { ReceivesInput = false };

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        _time += Time.UnscaledDeltaTime;
        TickPerfStats((float)Time.UnscaledDeltaTime);
        Selection.UpdatePing((float)Time.UnscaledDeltaTime);
        EditorTheme.TickOrigami((float)Time.UnscaledDeltaTime);

        // Push the editor's Origami theme and begin frame
        _origamiScope?.Dispose();
        _origamiScope = EditorTheme.PushOrigami();
        OrigamiUI.Origami.BeginFrame(paper, (float)Time.UnscaledDeltaTime);

        // Global visual-effect toggles (Preferences > Theme > Effects). Origami owns the drop-shadow /
        // glow distinction and gates its DropShadow()/Glow() helpers on these; Paper just draws shadows.
        OrigamiUI.Origami.DropShadowsEnabled = EditorTheme.DropShadows;
        OrigamiUI.Origami.GlowsEnabled = EditorTheme.AccentGlow;
        paper.Canvas.SetAntiAlias(EditorTheme.AntiAliasing);

        // Save system update (Ctrl+S + auto-save) - after theme push so toasts get icons
        SaveManager.Update((float)Time.UnscaledDeltaTime);

        // Detect project opened (launcher closed since last frame)
        if (!ProjectLauncher.IsOpen && !_introClosing && _launcherWasOpen)
        {
            _introTime = 0;
            _introClosing = true;
            _launcherWasOpen = false;
            GUI.EditorGuide.ArmAutoStart(); // let the tour play once for this freshly-opened project
            if (Project.Current != null)
            {
                Window.InternalWindow.Title = $"Prowl Editor - {Project.Current.Name}";

                // Initialize the asset database for the opened project
                var db = new EditorAssetDatabase(Project.Current);
                db.Initialize();

                // Load user script assemblies and re-register all types
                ScriptAssemblyManager.LoadAssemblies(Project.Current);

                // ReinitializeRegistries() runs the [OnAssemblyLoad] hooks, which include the project-settings reload.
                ReinitializeRegistries();

                // Restore layout from project (or use default)
                var savedLayout = LoadDockLayout();
                if (savedLayout != null)
                    _dockSpace.Root = savedLayout;
                else
                    _dockSpace.Root = CreateDefaultLayout();

                // Ensure a scene is always loaded
                EditorSceneManager.EnsureSceneLoaded();
            }
        }

        // Process file changes optionally only when window is focused
        bool canProcessAssets = !EditorSettings.Instance.ReimportOnFocusOnly || Window.IsFocused;
        if (canProcessAssets)
        {
            EditorAssetDatabase.Instance?.ProcessFileChanges();

            // Check for script recompilation
            ScriptAssemblyManager.Update();

            // Lazy thumbnail generation one per frame
            ThumbnailGenerator.ProcessOne();

            // Periodically scan for missing thumbnails (every ~120 frames)
            if (_time % 2.0 < Time.UnscaledDeltaTime)
                ThumbnailGenerator.EnqueueMissing();
        }

        // Layout auto-save is handled by SaveManager's auto-save timer.

        // Show project launcher or intro close phase
        if (ProjectLauncher.IsOpen || _introClosing)
        {
            // Always draw the launcher background during close transition
            ProjectLauncher.Draw(paper, (float)Time.UnscaledDeltaTime, forceDraw: _introClosing);

            if (ProjectLauncher.IsOpen)
                _launcherWasOpen = true;

            // Block input during close animation
            if (_introClosing)
            {
                paper.Box("intro_blocker")
                    .PositionType(PositionType.SelfDirected).Position(0, 0)
                    .Size(w, h)
                    .Layer(Layer.Overlay)
                    .OnClick(0, (_, _) => { }); // absorb all clicks

                // Switch to editor once screen is fully covered (hold phase)
                if (_introTime >= IntroCloseDuration)
                    _introClosing = false; // next frame draws editor behind open phase
                else
                    return;
            }
            else
            {
                return; // launcher still active, don't draw editor
            }
        }

        // Ensure a scene always exists
        if (Project.Current != null && Runtime.Resources.Scene.Current == null)
            EditorSceneManager.EnsureSceneLoaded();

        // Editor backdrop (behind the translucent glass panels) shared with the launcher.
        _nebula ??= new GUI.NebulaBackground(paper);
        GUI.NebulaBackground.DrawEditorBackground(paper, _nebula, "nebula_bg", w, h, (float)Time.UnscaledDeltaTime);

        DrawHeader(paper, w, h);

        float pad = EditorTheme.DockPadding;
        float dockY = EditorTheme.MenuBarHeight + pad;
        float dockH = h - dockY - pad - EditorTheme.StatusBarHeight;
        _dockSpace.Draw(paper, pad, dockY, w - pad * 2, dockH);

        DrawStatusBar(paper, w, h);

        // First-run UI tour (once per user; reset from Preferences > General).
        GUI.EditorGuide.SetDockSpace(_dockSpace);
        if (Project.Current != null && !ProjectLauncher.IsOpen && _introTime >= IntroDuration)
            GUI.EditorGuide.TryAutoStart(GUI.EditorGuide.WelcomeTour());
    }

    // Shared height for the header's interactive elements (menu bar + right-side status chips) so
    // they line up. Centered within the taller band, which also holds the dock-padding gap.
    private const float HeaderChipHeight = 30f;

    private void DrawHeader(Paper paper, float w, float h)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // The full top band = menu bar height + the dock-padding gap above the dock. Everything lives
        // inside this transparent container and vertically centers within it, so the menu bar, play
        // pill and status cluster all sit centered in the available space.
        float band = EditorTheme.MenuBarHeight + EditorTheme.DockPadding;

        using (paper.Box("header").PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, band).Enter())
        {
            DrawMenuBar(paper, w, band);
            DrawPlayPill(paper, w, band, font);
            DrawHeaderStatus(paper, w, band, font);
        }
    }

    /// <summary>Centered rounded "pill" holding the play / pause / step transport buttons.</summary>
    private void DrawPlayPill(Paper paper, float w, float band, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("play_pill").PositionType(PositionType.SelfDirected)
            .Size(UnitValue.Auto).Rounded(EditorTheme.Roundness)
            .Margin(UnitValue.StretchOne)
            .BackdropBlur(Origami.Current.Metrics.WindowBackdropBlur)
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Enter())
        {
            // Ghost buttons tint their icon by variant: green Play when stopped, red Stop while playing,
            // amber Pause when paused; step stays neutral.
            var play = Origami.IconButton(paper, "btn_play", Application.IsPlaying ? EditorIcons.CircleStop_I : EditorIcons.Play_I)
                .OnClick(() => { if (Application.IsPlaying) ExitPlayMode(); else EnterPlayMode(); })
                .Style(ButtonStyle.Ghost);
            if (Application.IsPlaying) play.Danger(); else play.Success();
            play.Show();

            var pause = Origami.IconButton(paper, "btn_pause", EditorIcons.Pause_I, TogglePause).Style(ButtonStyle.Ghost);
            if (Application.IsPaused) pause.Warning();
            pause.Show();

            Origami.IconButton(paper, "btn_step", EditorIcons.ForwardStep_I, StepOneFrame)
                .Style(ButtonStyle.Ghost).Show();
        }
    }

    // ── Smoothed perf readouts ──────────────────────────────────────────
    // Raw per-frame FPS / frame-time flicker far too fast to read. The frame time is an exponential
    // moving average (~0.5s time constant) so the FPS/ms readout glides continuously instead of
    // snapping; memory samples once a second (GC total moves in coarse steps anyway).
    private static double _perfWindow;
    private static float _emaMs;
    private static int _dispFps;
    private static float _dispMs;
    private static long _dispMemMb;

    private static void TickPerfStats(float dt)
    {
        if (dt <= 0f) return;

        float ms = dt * 1000f;
        if (_emaMs <= 0f) _emaMs = ms;                    // seed on the first frame
        float alpha = 1f - MathF.Exp(-dt / 2f);           // ~2s time constant, dt-based -> frame-rate independent
        _emaMs += (ms - _emaMs) * alpha;
        _dispMs = _emaMs;
        _dispFps = Math.Min(9999, (int)MathF.Round(1000f / _emaMs));

        _perfWindow += dt;
        if (_perfWindow >= 1.0 || _dispMemMb == 0)
        {
            _dispMemMb = GC.GetTotalMemory(false) / (1024 * 1024);
            _perfWindow = 0.0;
        }
    }

    /// <summary>Right-side status cluster: FPS, editor version and project name as themed chips,
    /// then a ghost cog that opens Project Settings.</summary>
    private void DrawHeaderStatus(Paper paper, float w, float band, Prowl.Scribe.FontFile font)
    {
        float clH = HeaderChipHeight;
        float pad = EditorTheme.DockPadding;
        var ST = UnitValue.Stretch();
        float blur = Origami.Current.Metrics.WindowBackdropBlur;
        float rectPadX = 10f, dot = 8f;

        int fps = _dispFps;
        string fpsNum = fps.ToString();
        string msText = $"{_dispMs:F1}ms";
        var dotColor = fps >= 50 ? EditorTheme.Green400 : (fps >= 25 ? EditorTheme.Amber400 : EditorTheme.Red400);

        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        string versionText = $"v{version}";
        string projectText = Project.Current?.Name ?? Loc.Get("editor.no_project");

        // Cluster pinned to the right edge and vertically centered by margins; every child auto-sizes to
        // its text, so there's no width math or MeasureText.
        using (paper.Row("hdr_status").PositionType(PositionType.SelfDirected)
            .Width(UnitValue.Auto).Height(clH)
            .Margin(ST, UnitValue.Pixels(pad), ST, ST).RowBetween(6).Enter())
        {
            // FPS chip: [glowing dot + count] left-anchored, [FPS + X.Xms] right-anchored, spacer between.
            // Auto width with a 120px floor lets the count grow into the spacer without moving anything.
            using (paper.Row("hs_fps").Width(UnitValue.Auto).MinWidth(UnitValue.Pixels(120)).Height(clH).Rounded(7)
                .Padding(rectPadX, rectPadX, 0, 0).BackdropBlur(blur)
                .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                paper.Box("hs_fps_dot").Width(dot).Height(dot).Margin(0, 7, ST, ST).Rounded(dot / 2f)
                    .BackgroundColor(dotColor).Glow(0, 0, 8, 0, dotColor).IsNotInteractable();
                paper.Box("hs_fps_num").Width(UnitValue.Auto).Height(clH).Margin(0, 0, ST, ST).IsNotInteractable()
                    .Text(fpsNum, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleLeft);
                paper.Box("hs_fps_sp").Width(ST).Height(1).IsNotInteractable();
                paper.Box("hs_fps_lbl").Width(UnitValue.Auto).Height(clH).Margin(0, 4, ST, ST).IsNotInteractable()
                    .Text("FPS", font).TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleRight);
                paper.Box("hs_fps_ms").Width(UnitValue.Auto).MinWidth(UnitValue.Pixels(33)).Height(clH).Margin(0, 0, ST, ST).IsNotInteractable()
                    .Text(msText, font).TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleRight);
            }

            StatusChip(paper, "hs_ver", clH, versionText, font);
            StatusChip(paper, "hs_proj", clH, projectText, font);

            paper.Box("hs_cog").Width(clH).Height(clH).Rounded(7)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(EditorIcons.Gear, font).TextColor(EditorTheme.Ink400)
                .Hovered.TextColor(EditorTheme.Ink500).End()
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => OpenPanel(typeof(ProjectSettingsPanel)));
        }
    }

    // A themed glass chip that auto-sizes to its text (horizontal padding + Auto width, no MeasureText).
    private static void StatusChip(Paper paper, string id, float hRect, string text, Prowl.Scribe.FontFile font)
    {
        var ST = UnitValue.Stretch();
        using (paper.Row(id).Width(UnitValue.Auto).Height(hRect).Padding(10, 10, 0, 0).Rounded(7)
            .BackdropBlur(Origami.Current.Metrics.WindowBackdropBlur)
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .IsNotInteractable().Enter())
        {
            paper.Box(id + "_t").Width(UnitValue.Auto).Height(hRect).Margin(0, 0, ST, ST).IsNotInteractable()
                .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter);
        }
    }

    public override void EndGui(Paper paper)
    {
        // On-boarding guide overlay (above panels/header, below Origami's popovers/toasts).
        GUI.EditorGuide.Draw(paper, (float)Time.UnscaledDeltaTime);

        // Render all Origami overlay systems (drag-drop, context menus, modals, toasts, tooltips)
        OrigamiUI.Origami.EndFrame(paper);

        // Intro animation overlay
        if (_introTime < IntroDuration)
        {
            // Clamp delta to avoid jumping the animation after heavy loading frames.
            // A normal frame is ~16ms; anything above 100ms is a loading stall.
            double introDelta = Math.Min(Time.UnscaledDeltaTime, 0.05);
            _introTime += introDelta;
            DrawIntro(paper);
        }

        // Cycling tip strip drawn last so it sits on top of both the launcher card
        // and the intro animation bars, giving the user something to read while the
        // project loads. The strip fades out as the bars slide away to reveal the
        // editor so it never lingers over a loaded project.
        if (ProjectLauncher.IsOpen || _introTime < IntroDuration)
        {
            float tipAlpha = 1f;
            // Only run the fade while the intro is actually playing. Before any project
            // is opened _introTime sits at double.MaxValue, which would otherwise read
            // as "past the open phase" and zero the tip out on the launcher.
            if (_introTime < IntroDuration)
            {
                const double openStart = IntroCloseDuration + 0.5;
                const double fadeOutDuration = 0.8;
                if (_introTime >= openStart)
                {
                    float t = (float)((_introTime - openStart) / fadeOutDuration);
                    tipAlpha = 1f - Math.Clamp(t, 0f, 1f);
                }
            }

            ProjectLauncher.DrawTipStrip(paper, (float)Time.UnscaledDeltaTime, tipAlpha);
        }

        // Pop the editor Origami theme now that all rendering (including overlays) is done.
        _origamiScope?.Dispose();
        _origamiScope = null;
    }

    // ================================================================
    //  Menu Bar (Origami AppBar)
    // ================================================================

    private void DrawMenuBar(Paper paper, float w, float band)
    {
        float pad = EditorTheme.DockPadding;
        float barH = HeaderChipHeight;
        var font = EditorTheme.DefaultFont;
        var ST = UnitValue.Stretch();
        // Menu labels + a Theme quick-access button, pinned to the left edge and vertically centered by
        // margins; auto width hugs the menus.
        using (paper.Row("menubar_host").PositionType(PositionType.SelfDirected)
            .Width(UnitValue.Auto).Height(barH)
            .Margin(UnitValue.Pixels(pad), ST, ST, ST).RowBetween(4).Enter())
        {
            var bar = Origami.MenuBar(paper, "menubar").Height(barH);
            foreach (var root in MenuRegistry.RootMenus)
                if (root.HasSubItems)
                    bar.Menu(root.Label, ctx => BuildMenu(ctx, root.SubItems));
            bar.Show();

            // Quick-access to Preferences > Theme (theming is a big part of the editor now).
            paper.Box("hdr_theme_btn").Width(barH).Height(barH)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).Rounded(7)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(EditorIcons.Palette, font).TextColor(EditorTheme.Ink400)
                .Hovered.TextColor(EditorTheme.Ink500).End()
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("header.theme_settings"))
                .OnPostLayout((h2, rect) => GUI.EditorGuide.RegisterThemeButton(
                    (float)rect.Min.X, (float)rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y))
                .OnClick(0, (_, _) =>
                {
                    OpenPanel(typeof(GUI.Panels.PreferencesPanel));
                    (FindOpenPanel(typeof(GUI.Panels.PreferencesPanel)) as GUI.Panels.PreferencesPanel)?.ShowTheme();
                });
        }
    }

    /// <summary>
    /// Translates a registered <see cref="AppMenuItem"/> subtree into Origami's ContextBuilder,
    /// which is how MenuBar dropdowns are populated. Dynamic label/enabled/checked funcs are
    /// evaluated here since the dropdown is rebuilt each frame it is open.
    /// </summary>
    private static void BuildMenu(ContextBuilder ctx, IReadOnlyList<AppMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                ctx.Separator();
                continue;
            }

            string label = item.DynamicLabelFunc?.Invoke() ?? item.Label;
            bool enabled = item.IsEnabledFunc?.Invoke() ?? item.IsEnabled;

            if (item.HasSubItems)
            {
                List<AppMenuItem> sub = item.SubItems;
                ctx.Submenu(label, c => BuildMenu(c, sub));
            }
            else
            {
                ctx.Item(label, item.OnClick ?? (() => { }), enabled, on: item.IsCheckedFunc?.Invoke() ?? false);
            }
        }
    }

    // ================================================================
    //  Status Bar (Origami AppBar at bottom)
    // ================================================================

    // The status bar sources its log data from the shared ConsolePanel store; this just ensures
    // that store is subscribed to Debug.OnLog even before the Console panel is opened.
    private static void InitializeStatusBar() => ConsolePanel.EnsureSubscribed();

    // Backend name shown in the footer. Only OpenGL exists today; when more backends land this
    // should come from the graphics device.
    private const string GraphicsBackend = "OpenGL 4.1";

    private void DrawStatusBar(Paper paper, float w, float h)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        UnitValue ST = UnitValue.StretchOne;
        float sh = EditorTheme.StatusBarHeight;
        float fs = EditorTheme.FontSizeSmall;
        var dim = EditorTheme.Ink300;
        var mono = EditorTheme.FontMono ?? font;

        // Glyph-icon (EditorIcons/FontAwesome) + text cell.
        void GlyphCell(string id, string glyph, System.Drawing.Color glyphColor, string text,
            System.Drawing.Color textColor, string? tooltip = null)
        {
            var row = paper.Row(id).Width(UnitValue.Auto).Height(sh).Margin(0, 8, ST, ST);
            if (tooltip != null) row.Tooltip(tooltip);
            using (row.Enter())
            {
                paper.Box(id + "_i").Width(14).Height(sh).Margin(0, 4, ST, ST).IsNotInteractable()
                    .Text(glyph, font).TextColor(glyphColor).FontSize(fs).Alignment(PaperUI.TextAlignment.MiddleCenter);
                paper.Box(id + "_t").Width(UnitValue.Auto).Height(sh).IsNotInteractable()
                    .Text(text, font).TextColor(textColor).FontSize(fs).Alignment(PaperUI.TextAlignment.MiddleLeft);
            }
        }

        // Console severity icon (Origami icon) + number, for the log counters.
        void CounterCell(string id, LogSeverity sev, int n)
        {
            var (icon, color) = ConsolePanel.SeverityStyle(sev);
            using (paper.Row(id).Width(UnitValue.Auto).Height(sh).Margin(0, 8, ST, ST).Enter())
            {
                paper.Box(id + "_i").Width(14).Height(sh).Margin(0, 3, ST, ST).IsNotInteractable()
                    .Icon(paper, icon, color, size: 12f);
                paper.Box(id + "_t").Width(UnitValue.Auto).Height(sh).IsNotInteractable()
                    .Text(n.ToString(), font).TextColor(dim).FontSize(fs).Alignment(PaperUI.TextAlignment.MiddleLeft);
            }
        }

        void Divider(string id) => paper.Box(id).Width(1).Height(sh).Margin(0, 0, 5, 5)
            .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

        // Uniform edge padding: the footer's left/right edges and both sides of every divider
        // all use this, so the columns are evenly spaced and the dividers sit centered in the gap.
        float pad = 10f;

        using (paper.Row("statusbar").PositionType(PositionType.SelfDirected)
            .Position(0, h - sh).Size(w, sh)
            .BackgroundColor(EditorTheme.Neutral200).Enter())
        {
            // ---------- Column 1: console (last log on the left, counters on the right) ----------
            using (paper.Row("sb_console").Width(ST).Height(sh).Padding(pad, pad, 0, 0).Enter())
            {
                var last = ConsolePanel.LastLog();
                if (last.HasValue)
                {
                    var (sev, msg, src, cnt) = last.Value;
                    var (icon, color) = ConsolePanel.SeverityStyle(sev);
                    paper.Box("sb_log_i").Width(16).Height(sh).Margin(0, 5, ST, ST).IsNotInteractable()
                        .Icon(paper, icon, color, size: 13f);
                    paper.Box("sb_log_m").Width(UnitValue.Auto).Height(sh).Margin(0, 6, ST, ST).IsNotInteractable()
                        .Text(msg, font).TextColor(EditorTheme.Ink400).FontSize(fs)
                        .Alignment(PaperUI.TextAlignment.MiddleLeft).TextTruncate();
                    if (!string.IsNullOrEmpty(src))
                        paper.Box("sb_log_s").Width(UnitValue.Auto).Height(sh).Margin(0, 6, ST, ST).IsNotInteractable()
                            .Text($"[{src}]", mono).TextColor(EditorTheme.InkDim).FontSize(fs - 1)
                            .Alignment(PaperUI.TextAlignment.MiddleLeft);
                    if (cnt > 1)
                        paper.Box("sb_log_c").Width(UnitValue.Auto).Height(sh).IsNotInteractable()
                            .Text($"x{cnt}", font).TextColor(EditorTheme.InkDim).FontSize(fs - 1)
                            .Alignment(PaperUI.TextAlignment.MiddleLeft);
                }

                paper.Box("sb_console_spacer").Width(ST);

                var (info, warn, err) = ConsolePanel.LogCounts();
                CounterCell("sb_cnt_info", LogSeverity.Normal, info);
                CounterCell("sb_cnt_warn", LogSeverity.Warning, warn);
                CounterCell("sb_cnt_err", LogSeverity.Error, err);
            }

            Divider("sb_div1");

            // ---------- Column 2: current scene ----------
            string? scenePath = EditorSceneManager.CurrentScenePath;
            string sceneName = !string.IsNullOrEmpty(scenePath)
                ? System.IO.Path.GetFileNameWithoutExtension(scenePath)
                : (Runtime.Resources.Scene.Current != null ? Loc.Get("editor.untitled_scene") : Loc.Get("hierarchy.no_scene_loaded"));
            using (paper.Row("sb_scene").Width(UnitValue.Auto).Height(sh).Padding(pad, pad, 0, 0).Enter())
                GlyphCell("sb_scene_cell", EditorIcons.Shapes, EditorTheme.Ink300, sceneName, EditorTheme.Ink400);

            Divider("sb_div2");

            // ---------- Column 3: editor stats on the left, graphics backend + Git on the right ----------
            using (paper.Row("sb_stats").Width(ST).Height(sh).Padding(pad, pad, 0, 0).Enter())
            {
                long memMb = _dispMemMb;
                GlyphCell("sb_sel", EditorIcons.ArrowPointer, EditorTheme.Ink300, Selection.Count.ToString(), dim, Loc.Get("editor.stat_selected"));
                GlyphCell("sb_mem", EditorIcons.Microchip, EditorTheme.Ink300, $"{memMb} MB", dim, Loc.Get("editor.stat_memory"));

                paper.Box("sb_stats_spacer").Width(ST);

                GlyphCell("sb_gfx", EditorIcons.Display, EditorTheme.Ink300, GraphicsBackend, EditorTheme.Ink400);

                Divider("sb_div3");
                DrawGitCell();
            }
        }

        // Git status for the open project (branch + dirty/clean colour), far right of the footer.
        void DrawGitCell()
        {
            GitInfo.Poll();

            var iconColor = EditorTheme.InkDim;
            var textColor = EditorTheme.InkDim;
            string text, tip;
            if (!GitInfo.GitInstalled)
            {
                text = Loc.Get("editor.git_not_installed");
                tip = Loc.Get("editor.git_not_installed_tip");
            }
            else if (!GitInfo.IsRepository)
            {
                text = Loc.Get("editor.git_no_repo");
                tip = Loc.Get("editor.git_no_repo_tip");
            }
            else
            {
                text = GitInfo.Branch;
                iconColor = GitInfo.HasChanges ? EditorTheme.Amber400 : EditorTheme.Green400;
                textColor = EditorTheme.Ink400;
                tip = GitInfo.HasChanges
                    ? Loc.Get("editor.git_dirty", new { branch = GitInfo.Branch })
                    : Loc.Get("editor.git_clean", new { branch = GitInfo.Branch });
            }

            GlyphCell("sb_git", EditorIcons.CodeBranch, iconColor, text, textColor, tip);
        }
    }

    // ================================================================
    //  Editor File Dialog Helper
    // ================================================================

    private static OrigamiUI.FileDialogConfig? s_fileDialogConfig;

    public static OrigamiUI.FileDialogConfig FileDialogConfig
    {
        get
        {
            s_fileDialogConfig ??= new OrigamiUI.FileDialogConfig
            {
                GetIcon = (ext, isDir) => isDir ? EditorIcons.Folder : FileIconRegistry.GetIconForFile("file" + ext),
                QuickAccess =
                [
                    (Loc.Get("editor.dir_desktop"), EditorIcons.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                    (Loc.Get("editor.dir_documents"), EditorIcons.FolderOpen, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    (Loc.Get("editor.dir_downloads"), EditorIcons.Download, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
                    (Loc.Get("editor.dir_user"), EditorIcons.User, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                ],
                GetDrives = () => System.IO.DriveInfo.GetDrives()
                    .Where(d => d.IsReady).Select(d => (d.Name, d.Name)).ToArray(),
            };
            return s_fileDialogConfig;
        }
    }

    /// <summary>Open a file dialog with editor config (icons, quick access, drives).</summary>
    public static void OpenFileDialog(FileDialogMode mode, Action<string?> onComplete,
        string? startPath = null, string[]? filters = null, string[]? filterLabels = null)
        => OrigamiUI.FileDialog.Open(mode, onComplete, startPath, filters, filterLabels, FileDialogConfig);

    private const int BarCount = 10;

    public static string GetEmbeddedResourceText(string resource)
    {
        var stream = GetEmbeddedResource(resource);

        string data = "";
        using (StreamReader reader = new StreamReader(stream))
        {
            data = reader.ReadToEnd();
        }

        return data;
    }

    public static Stream? GetEmbeddedResource(string resource)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceName = "Prowl.Editor.Resources." + resource;

        var stream = assembly.GetManifestResourceStream(resourceName);
        return stream;
    }

    public static FileStream? GetResource(string resource)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceName = resource;

        var pathToFile = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) +
                          resourceName;

        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            using (var fileStream = File.Create(pathToFile))
            {
                stream.Seek(0, SeekOrigin.Begin);
                stream.CopyTo(fileStream);
            }
        }
        return File.OpenRead(pathToFile);
    }

    private void DrawIntro(Paper paper)
    {
        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        paper.Box("intro_overlay")
            .PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h)
            .IsNotInteractable()
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float cx = w / 2f;
                float cy = h / 2f;
                var font = EditorTheme.FontLogo ?? EditorTheme.DefaultBoldFont;
                var black = Prowl.Vector.Color32.FromArgb(255, 8, 8, 10);
                float barH = (float)h / BarCount;
                double time = _introTime;

                // -- CLOSE PHASE (0 -> IntroCloseDuration): Bars slide IN, text fades in --
                if (time < IntroCloseDuration)
                {
                    float t = (float)(time / IntroCloseDuration); // 0->1

                    // Bars slide in from off-screen
                    for (int i = 0; i < BarCount; i++)
                    {
                        float delay = i * 0.04f;
                        float slideDuration = 0.5f;
                        float barPhase = Math.Clamp((t - delay) / slideDuration, 0f, 1f);
                        float eased = EaseInOutQuart(barPhase);

                        // Slide from off-screen to on-screen (reverse of open)
                        float slideX = (i % 2 == 0) ? -(1f - eased) * w : (1f - eased) * w;

                        float barY = i * barH;
                        canvas.RectFilled(slideX, barY, w, barH + 1, black);
                    }

                    // Logo + wordmark fade in during second half
                    if (t > 0.5f)
                    {
                        float textPhase = (t - 0.5f) / 0.5f;
                        float eased = EaseOutQuart(textPhase);
                        DrawIntroBrand(canvas, cx, cy, (byte)(eased * 255), font);
                    }
                }
                // -- HOLD PHASE: brief pause with text visible --
                else if (time < IntroCloseDuration + 0.5)
                {
                    canvas.RectFilled(0, 0, w, h, black);

                    DrawIntroBrand(canvas, cx, cy, 255, font);
                }
                // -- OPEN PHASE: Bars slide OUT, text fades out --
                else
                {
                    float openStart = (float)(IntroCloseDuration + 0.5);
                    float openDuration = (float)(IntroDuration - openStart);
                    float t = Math.Clamp((float)(time - openStart) / openDuration, 0f, 1f);

                    // Bars slide off screen
                    for (int i = 0; i < BarCount; i++)
                    {
                        float delay = i * 0.05f;
                        float slideDuration = 0.5f;
                        float barPhase = Math.Clamp((t - delay) / slideDuration, 0f, 1f);
                        float eased = EaseInOutQuart(barPhase);

                        float slideX = (i % 2 == 0) ? -eased * w : eased * w;

                        float barY = i * barH;
                        canvas.RectFilled(slideX, barY, w, barH + 1, black);
                    }

                    // Logo + wordmark fade out quickly
                    if (t < 0.3f)
                    {
                        float textFade = 1f - (t / 0.3f);
                        byte alpha = (byte)(EaseOutQuart(textFade) * 255);
                        DrawIntroBrand(canvas, cx, cy, alpha, font);
                    }
                }
            }));
    }

    // The intro brand lockup: the Prowl logo to the LEFT of the PROWL wordmark, the pair centered on
    // (cx, cy) as one unit, both at the given fade alpha.
    private static void DrawIntroBrand(Prowl.Quill.Canvas canvas, float cx, float cy, byte alpha, Scribe.FontFile? font)
    {
        const string word = "PROWL";
        const float letterSpacing = 10f, gap = 8f;
        var tint = System.Drawing.Color.FromArgb(alpha, 230, 230, 230);

        // Size the logo to the wordmark's height and measure the text (with its spacing) so the
        // [logo | gap | text] lockup can be centered horizontally as a whole.
        float textW = 0f, lockupH = 88f;
        if (font != null)
        {
            var m = canvas.MeasureText(word, EditorTheme.FontSizeLogo, font, letterSpacing);
            textW = (float)m.X;
            lockupH = (float)m.Y;
        }
        float logoH = lockupH * 1.375f;        // logo drawn ~38% larger than the wordmark height
        float logoW = logoH * (282f / 264f);   // logo viewBox aspect
        float totalW = logoW + (textW > 0f ? gap + textW : 0f);
        float left = cx - totalW / 2f;

        EditorIcons.ProwlLogo.Draw(canvas,
            new Rect(left, cy - logoH / 2f, left + logoW, cy + logoH / 2f), tint, 1f);

        if (font != null)
        {
            var textColor = Prowl.Vector.Color32.FromArgb(alpha, 230, 230, 230);
            canvas.DrawText(word, left + logoW + gap, cy, textColor, EditorTheme.FontSizeLogo, font,
                letterSpacing, new Float2(0f, 0.5f), quality: Scribe.FontQuality.Ultra);
        }
    }

    private static float EaseOutQuart(float x) => 1f - MathF.Pow(1f - x, 4f);
    private static float EaseInOutQuart(float x) => x < 0.5f ? 8f * x * x * x * x : 1f - MathF.Pow(-2f * x + 2f, 4f) / 2f;

    private Scribe.FontFile? LoadFallbackFont(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null) { Runtime.Debug.LogWarning($"Could not load font resource: {resourceName}"); return null; }
        Runtime.Debug.Log($"Loaded fallback font: {resourceName} ({stream.Length} bytes)");
        var fontFile = new Scribe.FontFile(stream);
        PaperInstance.AddFallbackFont(fontFile);
        return fontFile;
    }

    private void LoadSystemFallbackFonts()
    {
        // CJK and international font fallback chain. Tries common system fonts
        // in priority order. These cover Japanese, Chinese, Korean, Cyrillic,
        // Arabic, Thai, and other scripts not in the primary Latin font.
        string[] fallbackFamilies =
        [
            // Japanese
            "Yu Gothic UI",        // Windows 10+
            "Meiryo",              // Windows Vista+
            "Hiragino Sans",       // macOS

            // Chinese (Simplified)
            "Microsoft YaHei",     // Windows
            "PingFang SC",         // macOS

            // Chinese (Traditional)
            "Microsoft JhengHei",  // Windows
            "PingFang TC",         // macOS

            // Korean
            "Malgun Gothic",       // Windows
            "Apple SD Gothic Neo", // macOS

            // Universal CJK fallback
            "Noto Sans CJK SC",    // Linux / installed
            "Noto Sans CJK",       // Linux / installed

            // Cyrillic/Greek (usually covered by primary font, but just in case)
            "Noto Sans",           // Linux
        ];

        var systemFonts = PaperInstance.EnumerateSystemFonts().ToArray();
        int loaded = 0;

        foreach (var family in fallbackFamilies)
        {
            var font = systemFonts.FirstOrDefault(f =>
                f.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase)
                && f.Style == Prowl.Scribe.FontStyle.Regular);

            if (font != null)
            {
                PaperInstance.AddFallbackFont(font);
                loaded++;
            }
        }

        if (loaded > 0)
            Runtime.Debug.Log($"Loaded {loaded} system fallback font(s) for international text.");
    }

    // ================================================================
    //  Panel Registration
    // ================================================================

    private void ScanAndRegisterPanels()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(DockPanel).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<EditorWindowAttribute>();
                if (attr == null) continue;
                _registeredPanels.Add((type, attr.Path));
            }
        }
    }

    /// <summary>
    /// Find an open panel of the given type across all docked and floating nodes.
    /// </summary>
    public DockPanel? FindOpenPanel(Type panelType)
    {
        return FindInNode(_dockSpace.Root, panelType)
            ?? _dockSpace.FloatingWindows
                .Select(fw => FindInNode(fw.Node, panelType))
                .FirstOrDefault(p => p != null);
    }

    private static DockPanel? FindInNode(DockNode? node, Type panelType)
    {
        if (node == null) return null;
        if (node.IsLeaf)
            return node.Tabs?.FirstOrDefault(t => t.GetType() == panelType);
        return FindInNode(node.ChildA, panelType) ?? FindInNode(node.ChildB, panelType);
    }

    /// <summary>Enumerate every open panel across the docked tree and all floating windows.</summary>
    private IEnumerable<DockPanel> EnumerateAllPanels()
    {
        foreach (var p in EnumerateNodePanels(_dockSpace.Root))
            yield return p;
        foreach (var fw in _dockSpace.FloatingWindows)
            foreach (var p in EnumerateNodePanels(fw.Node))
                yield return p;
    }

    private static IEnumerable<DockPanel> EnumerateNodePanels(DockNode? node)
    {
        if (node == null) yield break;
        if (node.IsLeaf)
        {
            if (node.Tabs != null)
                foreach (var tab in node.Tabs)
                    yield return tab;
            yield break;
        }
        foreach (var p in EnumerateNodePanels(node.ChildA)) yield return p;
        foreach (var p in EnumerateNodePanels(node.ChildB)) yield return p;
    }

    /// <summary>
    /// Open a panel. If it's already open, focus it. Otherwise create a new instance as a floating window.
    /// </summary>
    public void OpenPanel(Type panelType)
    {
        // Check if already open
        var existing = FindOpenPanel(panelType);
        if (existing != null)
        {
            FocusPanel(panelType);
            return;
        }

        // Create new instance
        if (Activator.CreateInstance(panelType) is not DockPanel panel) return;
        OpenPanelInstance(panel);
    }

    /// <summary>Open a pre-created panel instance as a floating window.</summary>
    public void OpenPanelInstance(DockPanel panel, float width = 800, float height = 560)
    {
        var node = DockNode.Leaf(panel);
        _dockSpace.FloatingWindows.Add(new FloatingWindow(node,
            new Float2(200, 200),
            new Float2(width, height)));
    }

    /// <summary>
    /// Check if a panel of the given type is currently open.
    /// </summary>
    public bool IsPanelOpen(Type panelType) => FindOpenPanel(panelType) != null;

    private void PromptSaveAs()
    {
        if (Project.Current == null) return;
        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (path == null || Project.Current == null) return;
            string rel = EditorAssetDatabase.NormalizePath(
                System.IO.Path.GetRelativePath(Project.Current.AssetsPath, path));
            if (!rel.EndsWith(".scene")) rel += ".scene";
            EditorSceneManager.SaveAs(rel);
        }, Project.Current.AssetsPath,
           new[] { "*.scene" }, new[] { "Scene Files (*.scene)" });
    }

    // ================================================================
    //  Menu Registration
    // ================================================================

    private void RegisterMenus()
    {
        string file = Loc.Get("menu.file");
        string edit = Loc.Get("menu.edit");

        // File menu
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.new_scene")}", () => EditorSceneManager.NewScene());
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.open_scene")}", () =>
        {
            EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
            {
                if (path == null || Project.Current == null) return;
                string rel = EditorAssetDatabase.NormalizePath(
                    System.IO.Path.GetRelativePath(Project.Current.AssetsPath, path));
                EditorSceneManager.OpenScene(rel);
            }, Project.Current?.AssetsPath,
               new[] { "*.scene" }, new[] { Loc.Get("editor.filter_scene") });
        });
        MenuRegistry.RegisterSeparator(file);
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.save_scene")}", () =>
        {
            if (!EditorSceneManager.Save())
            {
                // No path yet prompt Save As
                if (Project.Current == null) return;
                EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
                {
                    if (path == null || Project.Current == null) return;
                    string rel = EditorAssetDatabase.NormalizePath(
                        System.IO.Path.GetRelativePath(Project.Current.AssetsPath, path));
                    if (!rel.EndsWith(".scene")) rel += ".scene";
                    EditorSceneManager.SaveAs(rel);
                }, Project.Current.AssetsPath,
                   new[] { "*.scene" }, new[] { Loc.Get("editor.filter_scene") });
            }
        });
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.save_scene_as")}", () =>
        {
            if (Project.Current == null) return;
            EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
            {
                if (path == null || Project.Current == null) return;
                string rel = EditorAssetDatabase.NormalizePath(
                    System.IO.Path.GetRelativePath(Project.Current.AssetsPath, path));
                if (!rel.EndsWith(".scene")) rel += ".scene";
                EditorSceneManager.SaveAs(rel);
            }, Project.Current.AssetsPath,
               new[] { "*.scene" }, new[] { Loc.Get("editor.filter_scene") });
        });
        MenuRegistry.RegisterSeparator(file);
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.open_project")}", () => ReturnToLauncher());
        MenuRegistry.RegisterSeparator(file);
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.build_project")}", () => OpenPanel(typeof(BuildSettingsPanel)));
        MenuRegistry.RegisterSeparator(file);
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.exit")}", () => Game.Quit());

        // Edit menu
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.undo")}", () => Undo.PerformUndo(),
            isEnabled: () => Undo.CanUndo,
            dynamicLabel: () => Undo.UndoDescription);
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.redo")}", () => Undo.PerformRedo(),
            isEnabled: () => Undo.CanRedo,
            dynamicLabel: () => Undo.RedoDescription);
        MenuRegistry.RegisterSeparator(edit);
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.project_settings")}", () => OpenPanel(typeof(ProjectSettingsPanel)));
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.save_layout")}", () => SaveProjectState());
        MenuRegistry.RegisterSeparator(edit);
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.preferences")}", () => OpenPanel(typeof(PreferencesPanel)));

        // Assets menu
        string assets = Loc.Get("menu.assets");
        AssetCreateMenu.RegisterMenus();
        MenuRegistry.RegisterSeparator(assets);
        MenuRegistry.Register($"{assets}/{Loc.Get("menu.assets.import_package")}", () =>
        {
            EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
            {
                if (path != null && System.IO.File.Exists(path))
                    GUI.Popups.PackageImportDialog.Open(path);
            },
            startPath: Project.Current?.PackagesPath,
            filters: new[] { "*.prowlpackage" },
            filterLabels: new[] { Loc.Get("editor.filter_package") });
        });

        // GameObject menu auto-populated from [CreateGameObjectMenu] attributes
        CreateGameObjectMenuRegistry.RegisterMenuBarItems();

        // Window menu auto-populated from [EditorWindow] attributes
        foreach (var (type, path) in _registeredPanels)
        {
            var capturedType = type;
            MenuRegistry.Register($"{Loc.Get("menu.window")}/{path}", () => OpenPanel(capturedType),
                isChecked: () => IsPanelOpen(capturedType));
        }
    }

    // ================================================================
    //  Script Compilation
    // ================================================================

    /// <summary>
    /// Called by <see cref="ScriptAssemblyManager"/> right after the new script assemblies are
    /// loaded. Re-scans every registry against the fresh assemblies and reloads project settings.
    /// </summary>
    public void ReinitializeAfterReload()
    {
        ReinitializeRegistries();
    }

    /// <summary>
    /// Drops every strong reference the editor holds into the script <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// so it can actually be collected when unloaded. This is the counterpart to
    /// <see cref="ReinitializeAfterReload"/>: tear everything down here, rebuild it there.
    ///
    /// Anything that survives this call and transitively reaches a user type (a live instance, a
    /// <see cref="Type"/> handle, a delegate bound to user code, a <see cref="FieldInfo"/>) pins
    /// the old context and forces a full editor restart instead of a hot-reload.
    /// </summary>
    public void ReleaseScriptReferences()
    {
        CaptureSelectionForReload();

        // 1. Live object graph: the scene's GameObjects hold user MonoBehaviour instances.
        //    The scene was already serialized to disk by SaveSceneForRestart().
        Selection.Clear();
        Undo.Clear();
        Runtime.Resources.Scene.Unload();

        // 1b. Long-lived editor panels cache scene objects (e.g. the Inspector's last target,
        //     the Hierarchy's drag target). Let each drop its references before the unload.
        if (_dockSpace != null)
            foreach (var panel in EnumerateAllPanels())
                if (panel is IScriptReloadCleanup cleanup)
                    try { cleanup.OnScriptReloadCleanup(); } catch { }

        // Release Paper callbacks as they might otherwise pin ALC types across a reload.
        ReleasePaperRetainedCallbacks();

        // 2. Play-mode leftovers (normally empty outside play mode; cleared defensively).
        _savedEditorScene = null;
        _savedEditorTime = null;
        MenuRegistry.Clear();
        _registeredPanels.Clear();

        // 3. The Echo serializer cache lives in an external package so we can't call OnAssemblyUnload there.
        Echo.Serializer.ClearCache();

        // 4. Everything tagged [OnAssemblyUnload]
        ScriptReloadCallbacks.InvokeAssemblyUnload();
    }

    private void ReleasePaperRetainedCallbacks()
    {
        try
        {
            var paper = PaperInstance;
            if (paper == null) return;

            Type t = paper.GetType();
            const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

            if (t.GetField("_elements", BF)?.GetValue(paper) is not Array elements) return;

            int count = t.GetField("_elementCount", BF)?.GetValue(paper) is int c ? c : 0;
            count = Math.Clamp(count, 0, elements.Length);
            if (count < elements.Length)
                Array.Clear(elements, count, elements.Length - count);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[EditorApplication] Could not reset PaperUI retained callbacks: {ex.Message}");
        }
    }

    // ================================================================
    //  Selection preserve/restore across a hot-reload
    // ================================================================

    private List<SelectionToken>? _reloadSelection;
    private SelectionToken _reloadActive;
    private bool _hasReloadActive;

    /// <summary>
    /// Snapshot the current selection as identifier tokens (called before the selection is cleared).
    /// </summary>
    private void CaptureSelectionForReload()
    {
        _reloadSelection = new List<SelectionToken>();
        _hasReloadActive = false;

        foreach (var obj in Selection.Selected)
        {
            if (!TryMakeSelectionToken(obj, out var token))
                continue;

            _reloadSelection.Add(token);
            if (ReferenceEquals(obj, Selection.ActiveObject))
            {
                _reloadActive = token;
                _hasReloadActive = true;
            }
        }
    }

    /// <summary>
    /// Tries to create a selection token to then restore the selection after script reload.
    /// </summary>
    private static bool TryMakeSelectionToken(object obj, out SelectionToken token)
    {
        switch (obj)
        {
            // Scene GameObject - restore by stable scene identifier.
            case GameObject go:
                token = new SelectionToken(SelKind.GameObject, go.Identifier, Guid.Empty, "", "", false);
                return true;
            // Scene component - restore by owning GameObject + component identifier.
            case MonoBehaviour mb when mb.GameObject.IsValid():
                token = new SelectionToken(SelKind.Component, mb.GameObject.Identifier, mb.Identifier, "", "", false);
                return true;
            // Project asset - restore by AssetID via the asset database.
            case EngineObject eo when eo.AssetID != Guid.Empty:
                token = new SelectionToken(SelKind.Asset, eo.AssetID, Guid.Empty, "", "", false);
                return true;
            // Project browser item - identifier-only, rebuilt from its path/guid.
            case ContentItem ci:
                token = new SelectionToken(SelKind.Content, ci.Guid, Guid.Empty, ci.RelativePath, ci.Name, ci.IsFolder);
                return true;
            default:
                token = default;
                return false;
        }
    }

    /// <summary>
    /// Re-resolve the captured selection tokens against the freshly reloaded scene/assets and re-select them.
    /// </summary>
    public void RestoreSelectionAfterReload()
    {
        if (_reloadSelection == null)
            return;

        var tokens = _reloadSelection;
        _reloadSelection = null;

        Selection.Clear();
        object? active = null;

        foreach (var token in tokens)
        {
            object? resolved = ResolveSelectionToken(token);
            if (resolved == null)
                continue;

            Selection.AddToSelection(resolved);
            if (_hasReloadActive && token.Equals(_reloadActive))
                active = resolved;
        }

        if (active != null)
            Selection.ActiveObject = active;

        _hasReloadActive = false;
    }

    private static object? ResolveSelectionToken(SelectionToken token)
    {
        switch (token.Kind)
        {
            case SelKind.GameObject:
                return Runtime.Resources.Scene.Current?.FindObjectByIdentifier<GameObject>(token.Id);
            case SelKind.Component:
                return Runtime.Resources.Scene.Current?.FindObjectByIdentifier<GameObject>(token.Id)?.GetComponentByIdentifier(token.CompId);
            case SelKind.Asset:
                return Runtime.AssetDatabase.Get(token.Id);
            case SelKind.Content:
                // ContentItem compares by Guid + RelativePath, so a rebuilt instance re-selects the same item.
                return new ContentItem { Guid = token.Id, RelativePath = token.Path, Name = token.Name, IsFolder = token.IsFolder };
            default:
                return null;
        }
    }

    private void ReinitializeRegistries()
    {
        // Panel scan is an editor-instance step (needed before the menu rebuild reads the panel list).
        _registeredPanels.Clear();
        ScanAndRegisterPanels();

        // Run every [OnAssemblyLoad] hook
        ScriptReloadCallbacks.InvokeAssemblyLoad();

        MenuRegistry.Clear();
        RegisterMenus();
    }

    public void RestoreAutoSavedScene(string path)
    {
        try
        {
            string text = System.IO.File.ReadAllText(path);
            var echo = Echo.EchoObject.ReadFromString(text);
            var ctx = Importers.ImportHelper.CreateTrackingContext(out _);
            var scene = Echo.Serializer.Deserialize<Runtime.Resources.Scene>(echo, ctx);
            if (scene != null)
            {
                // Sidecar (written by SaveSceneForRestart) carries the original Assets-relative
                // path + AssetID. Restoring both means subsequent Ctrl+S writes back to the
                // original scene file instead of prompting Save-As.
                string sidecarPath = path + ".meta";
                if (System.IO.File.Exists(sidecarPath))
                {
                    var lines = System.IO.File.ReadAllLines(sidecarPath);
                    if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0]))
                        EditorSceneManager.CurrentScenePath = lines[0];
                    if (lines.Length > 1 && Guid.TryParse(lines[1], out var id))
                        scene.AssetID = id;
                    try { System.IO.File.Delete(sidecarPath); } catch { }
                }

                Runtime.Resources.Scene.Load(scene);
                Undo.Clear();
                Runtime.Debug.Log("Restored auto-saved scene.");
            }

            // Clean up the temp file
            try { System.IO.File.Delete(path); } catch { }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to restore auto-saved scene: {ex.Message}");
            EditorSceneManager.EnsureSceneLoaded();
        }
    }

    // ================================================================
    //  Project Switching
    // ================================================================

    /// <summary>
    /// Saves the editor window position, size and maximization state.
    /// </summary>
    public void SaveEditorWindowState()
    {
        EditorSettings.Instance.WindowX = Window.Position.X;
        EditorSettings.Instance.WindowY = Window.Position.Y;

        EditorSettings.Instance.WindowWidth = Window.Size.X;
        EditorSettings.Instance.WindowHeight = Window.Size.Y;

        EditorSettings.Instance.WindowMaximized = Window.InternalWindow.WindowState == Silk.NET.Windowing.WindowState.Maximized;

        EditorSettings.Instance.Save();
    }

    /// <summary>Clear editor caches: cached thumbnails, the saved dock layout (reset to default), and more.</summary>
    public void ClearEditorCache()
    {
        try { Thumbnails.ThumbnailGenerator.DeleteAll(); } catch { }
        EditorAssetDatabase.Instance?.ClearThumbnailTextureCache();
        try
        {
            if (Project.Current != null && System.IO.File.Exists(Project.Current.EditorStatePath))
                System.IO.File.Delete(Project.Current.EditorStatePath);
        }
        catch (Exception ex) { Runtime.Debug.LogWarning($"Failed to clear layout: {ex.Message}"); }
        _dockSpace.Root = CreateDefaultLayout();

        EditorSettings.Instance.SeenGuides.Clear();
        EditorSettings.Instance.Save();
    }

    private void SaveDockLayout()
    {
        if (Project.Current == null) return;
        try
        {
            string json = DockSerializer.Serialize(_dockSpace);
            System.IO.File.WriteAllText(Project.Current.EditorStatePath, json);
        }
        catch (Exception ex) { Runtime.Debug.LogError($"Failed to save layout: {ex.Message}"); }
    }

    private DockNode? LoadDockLayout()
    {
        if (Project.Current == null) return null;
        string path = Project.Current.EditorStatePath;
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            string json = System.IO.File.ReadAllText(path);
            return DockSerializer.Deserialize(json, _dockSpace.FloatingWindows, (typeName, state) =>
            {
                Type? type = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName);
                    if (type != null && typeof(DockPanel).IsAssignableFrom(type)) break;
                    type = null;
                }
                if (type == null) return null;
                var panel = Activator.CreateInstance(type) as DockPanel;
                if (panel != null && state != null)
                    try { panel.RestoreState(state); } catch { }
                return panel;
            });
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to load layout: {ex.Message}");
            return null;
        }
    }

    /// <summary>Save layout and settings for the current project.</summary>
    public void SaveProjectState()
    {
        if (Project.Current == null) return;
        SaveEditorWindowState();

        // Skip layout persistence while the game is running panel state snapshots
        // (selection, scene-view camera) would capture the transient playmode state
        // and overwrite authoring state on next open.
        if (!Application.IsPlaying)
            SaveDockLayout();

        ProjectSettingsRegistry.SaveAll();
    }

    public void ReturnToLauncher()
    {
        // Save before closing
        SaveProjectState();

        // Clean up current project
        EditorAssetDatabase.Instance?.Dispose();
        Runtime.AssetDatabase.Current = null;

        // Reset state
        _introTime = double.MaxValue;
        _introClosing = false;
        _launcherWasOpen = true;

        // Reopen launcher
        ProjectLauncher.Initialize();

        Window.InternalWindow.Title = "Prowl Editor";
    }

    // ================================================================
    //  Play Mode
    // ================================================================

    private void EnterPlayMode()
    {
        if (Application.IsPlaying) return;

        var scene = Runtime.Resources.Scene.Current;
        if (scene == null) return;

        // Save active tab (to restore on stop)
        SaveActiveTab();

        // Serialize the current editor scene
        _savedEditorScene = Echo.Serializer.Serialize(scene);
        if (_savedEditorScene == null)
        {
            Runtime.Debug.LogError("Failed to serialize scene for play mode.");
            return;
        }

        // Clear selection (references will be invalid)
        Selection.Clear();

        // Unload the editor scene
        Runtime.Resources.Scene.Unload();

        // Deserialize a fresh play copy
        var playCtx = Importers.ImportHelper.CreateTrackingContext(out _);
        var playScene = Echo.Serializer.Deserialize<Runtime.Resources.Scene>(_savedEditorScene, playCtx);
        if (playScene == null)
        {
            Runtime.Debug.LogError("Failed to deserialize play scene.");
            return;
        }

        // Set play mode flags BEFORE loading so OnEnable/Start gates pass
        Application.IsPlaying = true;
        Application.IsPaused = false;
        Application.StepRequested = false;
        ResetFixedTimeAccumulator();

        // Push fresh play-mode time (game code sees Time.TimeSinceStartup = 0)
        _savedEditorTime = Runtime.Time.CurrentTime;
        Runtime.Time.TimeStack.Clear();
        Runtime.Time.TimeStack.Push(new TimeData());

        // Push play-mode input handler (only forwards input when Game View focused)
        Input.PushHandler(new GameViewInputHandler(Input.Current));

        // Load with full lifecycle (Enable -> OnEnable/Start will fire)
        Runtime.Resources.Scene.Load(playScene);
        Undo.Clear();

        // Apply physics settings to the new scene
        try { ProjectSettingsRegistry.Get<PhysicsSettings>().Apply(); } catch { }

        // Focus the Game View tab
        FocusPanel(typeof(GameViewPanel));

        Runtime.Debug.Log("Entered play mode.");
    }

    private void ExitPlayMode()
    {
        if (!Application.IsPlaying) return;

        // Clear flags first
        Application.IsPlaying = false;
        Application.IsPaused = false;
        Application.StepRequested = false;

        // Clear selection (play scene references)
        Selection.Clear();

        // Unload the play scene
        Runtime.Resources.Scene.Unload();

        // Restore the editor scene WITHOUT lifecycle callbacks
        if (_savedEditorScene != null)
        {
            var ctx = Importers.ImportHelper.CreateTrackingContext(out _);
            var restoredScene = Echo.Serializer.Deserialize<Runtime.Resources.Scene>(_savedEditorScene, ctx);
            if (restoredScene != null)
                Runtime.Resources.Scene.Load(restoredScene);
            Undo.Clear();
            _savedEditorScene = null;
        }

        // Pop play-mode input handler
        if (Input.Current is GameViewInputHandler)
            Input.PopHandler();

        // Restore editor time
        if (_savedEditorTime != null)
        {
            Runtime.Time.TimeStack.Clear();
            Runtime.Time.TimeStack.Push(_savedEditorTime);
            _savedEditorTime = null;
        }

        // Restore the previously active tab
        RestoreActiveTab();

        Runtime.Debug.Log("Exited play mode.");
    }

    private void TogglePause()
    {
        if (!Application.IsPlaying) return;
        Application.IsPaused = !Application.IsPaused;
    }

    private void StepOneFrame()
    {
        if (!Application.IsPlaying) return;
        Application.IsPaused = true;
        Application.StepRequested = true;
    }

    // ================================================================
    //  Tab Focus Helpers
    // ================================================================

    private void SaveActiveTab()
    {
        _savedActiveTabNode = FindNodeContainingPanel(_dockSpace.Root, typeof(GameViewPanel));
        if (_savedActiveTabNode == null)
        {
            foreach (var fw in _dockSpace.FloatingWindows)
            {
                _savedActiveTabNode = FindNodeContainingPanel(fw.Node, typeof(GameViewPanel));
                if (_savedActiveTabNode != null) break;
            }
        }
        _savedActiveTabIndex = _savedActiveTabNode?.ActiveTabIndex ?? -1;
    }

    private void RestoreActiveTab()
    {
        if (_savedActiveTabNode?.Tabs != null && _savedActiveTabIndex >= 0
            && _savedActiveTabIndex < _savedActiveTabNode.Tabs.Count)
        {
            _savedActiveTabNode.ActiveTabIndex = _savedActiveTabIndex;
        }
        _savedActiveTabNode = null;
        _savedActiveTabIndex = -1;
    }

    private void FocusPanel(Type panelType)
    {
        var node = FindNodeContainingPanel(_dockSpace.Root, panelType);
        if (node == null)
        {
            foreach (var fw in _dockSpace.FloatingWindows)
            {
                node = FindNodeContainingPanel(fw.Node, panelType);
                if (node != null) break;
            }
        }

        if (node?.Tabs != null)
        {
            for (int i = 0; i < node.Tabs.Count; i++)
            {
                if (node.Tabs[i].GetType() == panelType)
                {
                    node.ActiveTabIndex = i;
                    return;
                }
            }
        }
    }

    private static DockNode? FindNodeContainingPanel(DockNode? node, Type panelType)
    {
        if (node == null) return null;
        if (node.IsLeaf && node.Tabs != null)
        {
            foreach (var tab in node.Tabs)
                if (tab.GetType() == panelType)
                    return node;
            return null;
        }
        return FindNodeContainingPanel(node.ChildA, panelType)
            ?? FindNodeContainingPanel(node.ChildB, panelType);
    }

    // ================================================================
    //  Scene Control Editor overrides Game's default scene lifecycle
    // ================================================================

    /// <summary>
    /// Editor does NOT auto-update the scene. SceneView handles it.
    /// </summary>
    public override void OnUpdate(Runtime.Resources.Scene? scene)
    {
        // Always update lifecycle gating is per-component via ShouldExecuteGameplay.
        // Components only run Start/Update/LateUpdate if IsPlaying or [ExecuteAlways].
        if (Application.ShouldRunGameplay)
            Application.IsGameplayExecuting = true;

        scene?.Update();

        Application.IsGameplayExecuting = false;

        // Editor gizmos are a scene-view concept, and the scene view edits UI in world space, so queue
        // them with the world-space canvas override active - otherwise the canvas wireframe would be
        // laid out at the screen/RT size and disagree with the world-space UI it's drawn around.
        bool prevWorldSpace = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = true;
        try
        {
            // Always allow gizmos in editor (even when not playing)
            if (scene != null)
                scene.DrawGizmos();

            if (Selection.Count > 0)
            {
                // Draw selection gizmo
                var selectedGOs = Selection.GetSelected<GameObject>();
                foreach (var comp in selectedGOs.SelectMany(e => e.GetComponents()))
                    comp.DrawGizmosSelected();
            }
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevWorldSpace;
        }
    }

    /// <summary>
    /// Editor does NOT auto-render the scene. SceneView handles it via EditorCamera.
    /// </summary>
    public override void OnRender(Runtime.Resources.Scene? scene)
    {
        // Don't render SceneView panel renders the editor camera to its own RT.
    }

    /// <summary>
    /// Editor does NOT call scene OnGui. Paper UI is driven by editor panels.
    /// </summary>
    public override void OnGui(Runtime.Resources.Scene? scene, PaperUI.Paper paper)
    {
        // Don't call scene.OnGui editor controls all UI.
    }

    // ================================================================
    //  Default Layout
    // ================================================================

    private static DockNode CreateDefaultLayout()
    {
        // Blender/Unreal-style: a big viewport column on the left, a thin outliner/properties column
        // on the right.
        return DockNode.Split(SplitDirection.Horizontal, 0.8f,
            // Left column: Scene view on top, a Project | Console strip along the bottom.
            DockNode.Split(SplitDirection.Vertical, 0.7f,
                DockNode.Leaf(new SceneViewPanel(), new GameViewPanel()),
                // Bottom strip: Project (65%) | Console (35%, smaller).
                DockNode.Split(SplitDirection.Horizontal, 0.65f,
                    DockNode.Leaf(new ProjectPanel()),
                    DockNode.Leaf(new ConsolePanel())
                )
            ),
            // Right column: Hierarchy (top 30%) | Inspector (bottom 70%).
            DockNode.Split(SplitDirection.Vertical, 0.3f,
                DockNode.Leaf(new HierarchyPanel()),
                DockNode.Leaf(new InspectorPanel())
            )
        );
    }
}
