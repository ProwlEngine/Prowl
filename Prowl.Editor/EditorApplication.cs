using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using Prowl.Editor.Thumbnails;
using Prowl.Editor.Docking;
using Prowl.Editor.GraphTools.ShaderGraphs.Editors;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Panels;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor;

public class EditorApplication : Game
{
    public static EditorApplication? Instance { get; private set; }

    /// <summary>The editor's PropertyGrid configuration (drawers, handlers, callbacks).</summary>
    public static OrigamiUI.PropertyGridConfig PropertyGridConfig { get; private set; } = null!;

    private DockSpace _dockSpace = null!;
    private double _time;
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

        // Load Font Awesome as fallback fonts for icons
        LoadFallbackFont("Prowl.Editor.Resources.fa-regular-400.ttf");
        LoadFallbackFont("Prowl.Editor.Resources.fa-solid-900.ttf");

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
                Scripting.ScriptAssemblyManager.LoadAssemblies(project);

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
            .AddProvider(new Prowl.Rosetta.EmbeddedResourceProvider(
                System.Reflection.Assembly.GetExecutingAssembly(), "Prowl.Editor.Localization"))
            .SetLocale(EditorSettings.Instance.Locale)
            .OnMissingKey(Prowl.Rosetta.MissingKeyBehavior.ReturnKey)
        );

        // Initialize editor registries (now sees user types if assemblies loaded above)
        InitializeOnLoadRegistry.Initialize();
        Inspector.PropertyEditorRegistry.Initialize();
        Inspector.CustomEditorRegistry.Initialize();
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
        GUI.ScriptTemplateRegistry.Initialize();
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
            var savedLayout = Docking.LayoutSerializer.Load(_dockSpace);
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
        AttributeHandlers.BuiltInAttributeHandlers.Register(PropertyGridConfig.Handlers);
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
            var editor = Inspector.PropertyEditorRegistry.GetEditor(fieldType);
            if (editor != null)
            {
                editor.OnGUI(paper, id, label, value, onChange, depth);
                return true;
            }
            return false;
        };

        // Register save handlers
        SaveManager.OnSave += () =>
        {
            if (Prefabs.PrefabEditingMode.IsEditing)
            {
                return Prefabs.PrefabEditingMode.Save()
                    ? Loc.Get("save.prefab", new { name = System.IO.Path.GetFileNameWithoutExtension(Prefabs.PrefabEditingMode.EditingPrefabPath) })
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
        // Pick a good system font prefer Segoe UI (Windows), then Arial, then any Regular font
        if (EditorTheme.DefaultFontName != _curDefaultFont)
        {
            EditorTheme.DefaultFont = PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.FamilyName == EditorTheme.DefaultFontName && f.Style == Prowl.Scribe.FontStyle.Regular)
                ?? PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.FamilyName == "segoe ui" && f.Style == Prowl.Scribe.FontStyle.Regular)
                ?? PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.FamilyName == "arial" && f.Style == Prowl.Scribe.FontStyle.Regular)
                ?? PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.Style == Prowl.Scribe.FontStyle.Regular)
                ?? PaperInstance.EnumerateSystemFonts().FirstOrDefault();

            _curDefaultFont = EditorTheme.DefaultFontName;
        }

        if (EditorTheme.DefaultBoldFontName != _curDefaultBoldFont)
        {
            EditorTheme.DefaultBoldFont = PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.FamilyName == EditorTheme.DefaultBoldFontName && f.Style == Prowl.Scribe.FontStyle.Bold)
                ?? PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.FamilyName == "segoe ui" && f.Style == Prowl.Scribe.FontStyle.Bold)
                ?? PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.FamilyName == "arial" && f.Style == Prowl.Scribe.FontStyle.Bold)
                ?? PaperInstance.EnumerateSystemFonts()
                .FirstOrDefault(f => f.Style == Prowl.Scribe.FontStyle.Bold)
                ?? PaperInstance.EnumerateSystemFonts().FirstOrDefault();

            _curDefaultBoldFont = EditorTheme.DefaultBoldFontName;
        }
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
        Runtime.UI.UIEventSystem.Viewport = new Runtime.UI.UIEventSystem.HostViewport { ReceivesInput = false };

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        _time += Time.UnscaledDeltaTime;
        Selection.UpdatePing((float)Time.UnscaledDeltaTime);
        EditorTheme.TickOrigami((float)Time.UnscaledDeltaTime);

        // Push the editor's Origami theme and begin frame
        _origamiScope?.Dispose();
        _origamiScope = EditorTheme.PushOrigami();
        OrigamiUI.Origami.BeginFrame(paper, (float)Time.UnscaledDeltaTime);

        // Save system update (Ctrl+S + auto-save) - after theme push so toasts get icons
        SaveManager.Update((float)Time.UnscaledDeltaTime);

        // Detect project opened (launcher closed since last frame)
        if (!ProjectLauncher.IsOpen && !_introClosing && _launcherWasOpen)
        {
            _introTime = 0;
            _introClosing = true;
            _launcherWasOpen = false;
            if (Project.Current != null)
            {
                Window.InternalWindow.Title = $"Prowl Editor - {Project.Current.Name}";

                // Initialize the asset database for the opened project
                var db = new EditorAssetDatabase(Project.Current);
                db.Initialize();

                // Load user script assemblies and re-register all types
                Scripting.ScriptAssemblyManager.LoadAssemblies(Project.Current);
                ReinitializeRegistries();

                // Load project settings
                ProjectSettingsRegistry.OnProjectOpened();

                // Restore layout from project (or use default)
                var savedLayout = Docking.LayoutSerializer.Load(_dockSpace);
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
            Scripting.ScriptAssemblyManager.Update();

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

        // Animated background gradients
        paper.Box("bg_gradients")
            .PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h)
            .IsNotInteractable()
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float cx = (float)r.Size.X / 2f;
                float cy = (float)r.Size.Y / 2f;
                float radius = Math.Max(cx, cy) * 1.5f;
                float t = (float)_time * 0.075f;

                // Figure-eight: x = sin(t), y = sin(2t) / 2
                float purple_x = cx + (float)Math.Sin(t) * cx * 0.85f;
                float purple_y = cy + (float)Math.Sin(t * 2) * cy * 0.5f;

                float blue_x = cx - (float)Math.Sin(t) * cx * 0.85f;
                float blue_y = cy - (float)Math.Sin(t * 2) * cy * 0.5f;

                var transparent = Prowl.Vector.Color32.FromArgb(0, 0, 0, 0);

                float rx = (float)r.Min.X, ry = (float)r.Min.Y;
                float rw = (float)r.Size.X, rh = (float)r.Size.Y;

                // Purple blob
                var purple = Prowl.Vector.Color32.FromArgb(50, 170, 80, 240);
                canvas.SetRadialBrush(purple_x, purple_y, 0, radius, purple, transparent);
                canvas.BeginPath();
                canvas.Rect(rx, ry, rw, rh);
                canvas.Fill();

                // Blue blob
                var blue = Prowl.Vector.Color32.FromArgb(50, 80, 170, 255);
                canvas.SetRadialBrush(blue_x, blue_y, 0, radius, blue, transparent);
                canvas.BeginPath();
                canvas.Rect(rx, ry, rw, rh);
                canvas.Fill();
            }));

        DrawMenuBar(paper);
        DrawTitleFlap(paper, w, h);

        float pad = EditorTheme.DockPadding;
        float dockY = EditorTheme.MenuBarHeight + pad;
        float dockH = h - dockY - pad - EditorTheme.MenuBarHeight;
        _dockSpace.Draw(paper, pad, dockY, w - pad * 2, dockH);

        DrawStatusBar(paper);
    }

    private void DrawTitleFlap(Paper paper, float w, float h)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Version label goes to the right side of the menu bar
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        paper.Box("version_label")
            .PositionType(PositionType.SelfDirected)
            .Position(w - 130, 0).Size(120, EditorTheme.MenuBarHeight)
            .IsNotInteractable()
            .Text($"Prowl v{version}", font)
            .TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize - 2)
            .Alignment(TextAlignment.MiddleRight);

        // Flap content: buttons + project + fps
        int fps = Math.Min(9999, Time.UnscaledDeltaTime > 0 ? (int)(1.0 / Time.UnscaledDeltaTime) : 0);
        string projectText = Project.Current?.Name ?? "No Project";
        string fpsText = $"FPS: {fps}";

        // Measure to size the flap
        float btnW = 90f; // play/pause/step buttons area
        var projMeasured = paper.MeasureText(projectText, EditorTheme.FontSize - 2, font, 1f);
        float projW = (float)projMeasured.X + 16f;
        float fpsW = 60f; // fixed width for FPS
        float separatorW = 20f; // space for | separators
        float contentW = btnW + projW + fpsW + separatorW;

        float padH = 16f;
        float topW = contentW + padH * 2 + 40f;
        float botW = contentW + padH * 2;
        float flapH = EditorTheme.MenuBarHeight + 6f;
        float flapX = (w - topW) / 2f;
        float flapY = 0f;
        float rad = 8f;

        // Draw trapezoid shape via canvas
        paper.Box("title_flap_bg")
            .PositionType(PositionType.SelfDirected)
            .Position(flapX, flapY).Size(topW, flapH)
            .IsNotInteractable()
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, lr) =>
            {
                float cx = (float)lr.Min.X + topW / 2f;
                float top = (float)lr.Min.Y;
                float bot = top + flapH;

                float tl = cx - topW / 2f;
                float tr = cx + topW / 2f;
                float bl = cx - botW / 2f;
                float br = cx + botW / 2f;

                var nc = EditorTheme.Neutral300;
                var flapColor = Prowl.Vector.Color32.FromArgb(nc.A, nc.R, nc.G, nc.B);

                float r = rad;
                float taperX = (tr - br);
                float stopFrac = r / flapH;
                float rsX = br + taperX * stopFrac;
                float rsY = bot - r;
                float lsX = bl - taperX * stopFrac;
                float lsY = bot - r;

                // Fill
                canvas.SetFillColor(flapColor);
                canvas.BeginPath();
                canvas.MoveTo(tl, top);
                canvas.LineTo(tr, top);
                canvas.LineTo(rsX, rsY);
                canvas.BezierCurveTo(br, bot, br, bot, br - r, bot);
                canvas.LineTo(bl + r, bot);
                canvas.BezierCurveTo(bl, bot, bl, bot, lsX, lsY);
                canvas.LineTo(tl, top);
                canvas.ClosePath();
                canvas.FillComplexAA();

                // Outline (sides + bottom only)
                var bc = EditorTheme.Ink200;
                canvas.SetStrokeColor(Prowl.Vector.Color32.FromArgb(bc.A, bc.R, bc.G, bc.B));
                canvas.SetStrokeWidth(1f);
                canvas.BeginPath();
                canvas.MoveTo(tl, top);
                canvas.LineTo(lsX, lsY);
                canvas.BezierCurveTo(bl, bot, bl, bot, bl + r, bot);
                canvas.LineTo(br - r, bot);
                canvas.BezierCurveTo(br, bot, br, bot, rsX, rsY);
                canvas.LineTo(tr, top);
                canvas.Stroke();
            }));

        // Interactive content inside flap centered row: Project | Buttons | FPS
        float flapCenterX = (w - contentW) / 2f;
        float flapContentY = 2f;
        float btnSize = 20f;
        float btnY = flapContentY + (EditorTheme.MenuBarHeight - btnSize) / 2f;
        float bx = flapCenterX;

        // Project name
        paper.Box("flap_project")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY).Size(projW, EditorTheme.MenuBarHeight)
            .IsNotInteractable()
            .Text(projectText, font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);

        bx += projW + 2f;

        // Separator |
        paper.Box("flap_sep1")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY + 4).Size(1, EditorTheme.MenuBarHeight - 8)
            .IsNotInteractable()
            .BackgroundColor(EditorTheme.Ink200);

        bx += 10f;

        // Play button
        paper.Box("btn_play")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, btnY).Size(btnSize, btnSize)
            .Rounded(4)
            .BackgroundColor(Application.IsPlaying ? System.Drawing.Color.FromArgb(255, 60, 160, 60) : System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(80, 255, 255, 255)).End()
            .Text(Application.IsPlaying ? EditorIcons.CircleStop : EditorIcons.Play, font)
            .TextColor(EditorTheme.Ink500).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => { if (Application.IsPlaying) ExitPlayMode(); else EnterPlayMode(); });

        bx += btnSize + 4f;

        // Pause button
        paper.Box("btn_pause")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, btnY).Size(btnSize, btnSize)
            .Rounded(4)
            .BackgroundColor(Application.IsPaused ? System.Drawing.Color.FromArgb(255, 160, 160, 60) : System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(80, 255, 255, 255)).End()
            .Text(EditorIcons.Pause, font)
            .TextColor(Application.IsPlaying ? EditorTheme.Ink500 : EditorTheme.Ink300).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => TogglePause());

        bx += btnSize + 4f;

        // Step button
        paper.Box("btn_step")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, btnY).Size(btnSize, btnSize)
            .Rounded(4)
            .BackgroundColor(System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(80, 255, 255, 255)).End()
            .Text(EditorIcons.ForwardStep, font)
            .TextColor(Application.IsPlaying ? EditorTheme.Ink500 : EditorTheme.Ink300).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => StepOneFrame());

        bx += btnSize + 8f;

        // Separator |
        paper.Box("flap_sep2")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY + 4).Size(1, EditorTheme.MenuBarHeight - 8)
            .IsNotInteractable()
            .BackgroundColor(EditorTheme.Ink200);

        bx += 10f;

        // FPS (fixed width)
        paper.Box("flap_fps")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY).Size(fpsW, EditorTheme.MenuBarHeight)
            .IsNotInteractable()
            .Text(fpsText, font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);
    }

    public override void EndGui(Paper paper)
    {
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

    private void DrawMenuBar(Paper paper)
    {
        var items = MenuRegistry.RootMenus;
        var bar = Origami.AppBar(paper, "menubar")
            .Height(EditorTheme.MenuBarHeight)
            .Background(EditorTheme.Neutral200);

        foreach (var root in items)
            if (root.HasSubItems)
                bar.Menu(root.Label, ConvertMenuItems(root.SubItems));

        bar.Show();
    }

    private static List<AppMenuItem> ConvertMenuItems(List<MenuItem> source)
    {
        var result = new List<AppMenuItem>();
        foreach (var item in source)
        {
            if (item.IsSeparator) { result.Add(AppMenuItem.Separator()); continue; }
            var c = new AppMenuItem(item.Label, item.OnClick)
            {
                IsCheckedFunc = item.IsCheckedFunc,
                IsEnabledFunc = item.IsEnabledFunc,
                DynamicLabelFunc = item.DynamicLabelFunc,
                IsEnabled = item.IsEnabled,
            };
            if (item.HasSubItems) c.SubItems = ConvertMenuItems(item.SubItems);
            result.Add(c);
        }
        return result;
    }

    // ================================================================
    //  Status Bar (Origami AppBar at bottom)
    // ================================================================

    private static string _statusBarMessage = "Ready";
    private static string _statusBarIcon = "";
    private static System.Drawing.Color _statusBarColor;

    private static void InitializeStatusBar()
    {
        Runtime.Debug.OnLog += (message, _, severity) =>
        {
            _statusBarMessage = message.Contains('\n') ? message.Split('\n')[0] : message;
            switch (severity)
            {
                case Runtime.LogSeverity.Warning:   _statusBarIcon = EditorIcons.TriangleExclamation; _statusBarColor = System.Drawing.Color.FromArgb(255, 230, 200, 80); break;
                case Runtime.LogSeverity.Error:
                case Runtime.LogSeverity.Exception: _statusBarIcon = EditorIcons.CircleExclamation;   _statusBarColor = System.Drawing.Color.FromArgb(255, 230, 80, 80); break;
                case Runtime.LogSeverity.Success:   _statusBarIcon = EditorIcons.CircleCheck;         _statusBarColor = System.Drawing.Color.FromArgb(255, 80, 200, 80); break;
                default:                            _statusBarIcon = EditorIcons.CircleInfo;          _statusBarColor = EditorTheme.Ink400; break;
            }
        };
    }

    private void DrawStatusBar(Paper paper)
    {
        var icon = string.IsNullOrEmpty(_statusBarIcon) ? EditorIcons.CircleInfo : _statusBarIcon;
        var color = _statusBarColor.A == 0 ? EditorTheme.Ink400 : _statusBarColor;

        Origami.AppBar(paper, "statusbar")
            .Height(EditorTheme.MenuBarHeight)
            .Bottom()
            .Background(EditorTheme.Neutral200)
            .Left(p =>
            {
                var font = EditorTheme.DefaultFont;
                if (font == null) return;
                p.Box("status_icon").Width(UnitValue.Auto).Height(EditorTheme.MenuBarHeight).ChildLeft(6)
                    .IsNotInteractable().Text(icon, font).TextColor(color)
                    .FontSize(EditorTheme.FontSize - 2).Alignment(PaperUI.TextAlignment.MiddleLeft);
                p.Box("status_label").Width(UnitValue.Stretch()).Height(EditorTheme.MenuBarHeight).ChildLeft(4)
                    .IsNotInteractable().Text(_statusBarMessage, font).TextColor(color)
                    .FontSize(EditorTheme.FontSize - 2).Alignment(PaperUI.TextAlignment.MiddleLeft);
            })
            .Show();
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
                    ("Desktop", EditorIcons.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                    ("Documents", EditorIcons.FolderOpen, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    ("Downloads", EditorIcons.Download, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
                    ("User", EditorIcons.User, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
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
                var font = EditorTheme.DefaultBoldFont;
                var black = Prowl.Vector.Color32.FromArgb(255, 8, 8, 10);
                float barH = (float)h / BarCount;
                double time = _introTime;

                // ── CLOSE PHASE (0 → IntroCloseDuration): Bars slide IN, text fades in ──
                if (time < IntroCloseDuration)
                {
                    float t = (float)(time / IntroCloseDuration); // 0→1

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

                    // Text fades in during second half
                    if (font != null && t > 0.5f)
                    {
                        float textPhase = (t - 0.5f) / 0.5f;
                        float eased = EaseOutQuart(textPhase);
                        byte alpha = (byte)(eased * 255);
                        var textColor = Prowl.Vector.Color32.FromArgb(alpha, 230, 230, 230);
                        canvas.DrawText("PROWL", cx, cy, textColor, 72f, font, 10f,
                            new Prowl.Vector.Float2(0.5f, 0.5f));
                    }
                }
                // ── HOLD PHASE: brief pause with text visible ──
                else if (time < IntroCloseDuration + 0.5)
                {
                    canvas.RectFilled(0, 0, w, h, black);

                    if (font != null)
                    {
                        var textColor = Prowl.Vector.Color32.FromArgb(255, 230, 230, 230);
                        canvas.DrawText("PROWL", cx, cy, textColor, 72f, font, 10f,
                            new Prowl.Vector.Float2(0.5f, 0.5f));
                    }
                }
                // ── OPEN PHASE: Bars slide OUT, text fades out ──
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

                    // Text fades out quickly
                    if (font != null && t < 0.3f)
                    {
                        float textFade = 1f - (t / 0.3f);
                        byte alpha = (byte)(EaseOutQuart(textFade) * 255);
                        var textColor = Prowl.Vector.Color32.FromArgb(alpha, 230, 230, 230);
                        canvas.DrawText("PROWL", cx, cy, textColor, 72f, font, 10f,
                            new Prowl.Vector.Float2(0.5f, 0.5f));
                    }
                }
            }));
    }

    private static float EaseOutQuart(float x) => 1f - MathF.Pow(1f - x, 4f);
    private static float EaseInOutQuart(float x) => x < 0.5f ? 8f * x * x * x * x : 1f - MathF.Pow(-2f * x + 2f, 4f) / 2f;

    private void LoadFallbackFont(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null) { Runtime.Debug.LogWarning($"Could not load font resource: {resourceName}"); return; }
        Runtime.Debug.Log($"Loaded fallback font: {resourceName} ({stream.Length} bytes)");
        PaperInstance.AddFallbackFont(new Prowl.Scribe.FontFile(stream));
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
    public void OpenPanelInstance(DockPanel panel, float width = 400, float height = 300)
    {
        var node = DockNode.Leaf(panel);
        _dockSpace.FloatingWindows.Add(new FloatingWindow(node,
            new Prowl.Vector.Float2(200, 200),
            new Prowl.Vector.Float2(width, height)));
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
               new[] { "*.scene" }, new[] { "Scene Files (*.scene)" });
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
                   new[] { "*.scene" }, new[] { "Scene Files (*.scene)" });
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
               new[] { "*.scene" }, new[] { "Scene Files (*.scene)" });
        });
        MenuRegistry.RegisterSeparator(file);
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.open_project")}", () => ReturnToLauncher());
        MenuRegistry.RegisterSeparator(file);
        MenuRegistry.Register($"{file}/{Loc.Get("menu.file.build_project")}", () => OpenPanel(typeof(Panels.BuildSettingsPanel)));
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
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.project_settings")}", () => OpenPanel(typeof(Panels.ProjectSettingsPanel)));
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.save_layout")}", () => SaveProjectState());
        MenuRegistry.RegisterSeparator(edit);
        MenuRegistry.Register($"{edit}/{Loc.Get("menu.edit.preferences")}", () => OpenPanel(typeof(Panels.PreferencesPanel)));

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
            filterLabels: new[] { "ProwlPackage (*.prowlpackage)" });
        });

        // GameObject menu auto-populated from [CreateGameObjectMenu] attributes
        CreateGameObjectMenuRegistry.RegisterMenuBarItems();

        // Window menu auto-populated from [EditorWindow] attributes
        foreach (var (type, path) in _registeredPanels)
        {
            var capturedType = type;
            MenuRegistry.Register($"Window/{path}", () => OpenPanel(capturedType),
                isChecked: () => IsPanelOpen(capturedType));
        }
    }

    // ================================================================
    //  Script Compilation
    // ================================================================

    /// <summary>Called by ScriptAssemblyManager after hot-reload to re-scan all registries.</summary>
    public void ReinitializeAfterReload() => ReinitializeRegistries();

    private void ReinitializeRegistries()
    {
        _registeredPanels.Clear();
        ScanAndRegisterPanels();
        InitializeOnLoadRegistry.Reinitialize();
        Inspector.PropertyEditorRegistry.Reinitialize();
        Inspector.CustomEditorRegistry.Reinitialize();
        GraphTools.NodeRendererRegistry.Reinitialize();
        GraphTools.NodePreviewRegistry.Reinitialize();
        Runtime.GraphTools.GraphValidatorRegistry.Reinitialize();
        Inspector.AssetImporterEditorRegistry.Reinitialize();
        GUI.Popups.AddComponentPopup.Reinitialize();
        Importers.ImporterRegistry.Reinitialize();
        ProjectSettingsRegistry.Reinitialize();
        CreateAssetMenuRegistry.Reinitialize();
        ShaderTypeCreateMenu.Register();
        ThumbnailGeneratorRegistry.Reinitialize();
        SceneDropHandlerRegistry.Reinitialize();
        CreateGameObjectMenuRegistry.Reinitialize();
        FileIconRegistry.Reinitialize();
        AssetDoubleClickRegistry.Reinitialize();
        GUI.ScriptTemplateRegistry.Reinitialize();

        // Re-register Window menu items for any new panels from user assemblies
        foreach (var (type, path) in _registeredPanels)
        {
            var capturedType = type;
            MenuRegistry.Register($"Window/{path}", () => OpenPanel(capturedType),
                isChecked: () => IsPanelOpen(capturedType));
        }

        // Re-register GameObject menu items for any new creators from user assemblies
        CreateGameObjectMenuRegistry.RegisterMenuBarItems();
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

    /// <summary>Save layout and settings for the current project.</summary>
    public void SaveProjectState()
    {
        if (Project.Current == null) return;
        SaveEditorWindowState();

        // Skip layout persistence while the game is running panel state snapshots
        // (selection, scene-view camera) would capture the transient playmode state
        // and overwrite authoring state on next open.
        if (!Application.IsPlaying)
            Docking.LayoutSerializer.Save(_dockSpace);

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

        // Load with full lifecycle (Enable → OnEnable/Start will fire)
        Runtime.Resources.Scene.Load(playScene);
        Undo.Clear();

        // Apply physics settings to the new scene
        try { ProjectSettingsRegistry.Get<PhysicsSettings>().Apply(); } catch { }

        // Focus the Game View tab
        FocusPanel(typeof(Panels.GameViewPanel));

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
        _savedActiveTabNode = FindNodeContainingPanel(_dockSpace.Root, typeof(Panels.GameViewPanel));
        if (_savedActiveTabNode == null)
        {
            foreach (var fw in _dockSpace.FloatingWindows)
            {
                _savedActiveTabNode = FindNodeContainingPanel(fw.Node, typeof(Panels.GameViewPanel));
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

        // Always allow gizmos in editor (even when not playing)
        if (scene != null)
            scene.DrawGizmos();

        if(Selection.Count > 0)
        {
            // Draw selection gizmo
            var selectedGOs = Selection.GetSelected<GameObject>();
            foreach (var comp in selectedGOs.SelectMany(e => e.GetComponents()))
            {
                comp.DrawGizmosSelected();
            }
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
        // Right: Inspector | Left: everything else
        return DockNode.Split(SplitDirection.Horizontal, 0.78f,
            // Left side: top (Hierarchy + Scene) | bottom (Project + Console)
            DockNode.Split(SplitDirection.Vertical, 0.65f,
                // Top: Hierarchy | Scene View
                DockNode.Split(SplitDirection.Horizontal, 0.25f,
                    DockNode.Leaf(new HierarchyPanel()),
                    DockNode.Leaf(new SceneViewPanel(), new GameViewPanel())
                ),
                // Bottom: Project (65%) | Console (35%)
                DockNode.Split(SplitDirection.Horizontal, 0.65f,
                    DockNode.Leaf(new ProjectPanel()),
                    DockNode.Leaf(new ConsolePanel())
                )
            ),
            // Right: Inspector
            DockNode.Leaf(new InspectorPanel())
        );
    }
}
