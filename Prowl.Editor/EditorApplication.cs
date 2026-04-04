using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using Prowl.Editor.Docking;
using Prowl.Editor.Panels;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

public class EditorApplication : Game
{
    public static EditorApplication? Instance { get; private set; }

    private DockSpace _dockSpace = null!;
    private double _time;
    private double _introTime = double.MaxValue;
    private const double IntroCloseDuration = 2.0; // bars close over launcher
    private const double IntroOpenDuration = 3.0;  // bars open revealing editor
    private const double IntroDuration = 5.0;      // total
    private bool _introClosing; // true = closing phase (bars sliding in)
    private bool _launcherWasOpen = true;

    // All registered panel types (from [EditorWindow] attribute scan)
    private readonly List<(Type type, string path)> _registeredPanels = new();

    public override void Initialize()
    {
        Instance = this;
        Application.IsEditor = true;
        Application.IsPlaying = false;

        // Pick a good system font — prefer Segoe UI (Windows), then Arial, then any Regular font
        EditorTheme.DefaultFont = PaperInstance.EnumerateSystemFonts()
            .FirstOrDefault(f => f.FamilyName == "segoe ui" && f.Style == Prowl.Scribe.FontStyle.Regular)
            ?? PaperInstance.EnumerateSystemFonts()
            .FirstOrDefault(f => f.FamilyName == "arial" && f.Style == Prowl.Scribe.FontStyle.Regular)
            ?? PaperInstance.EnumerateSystemFonts()
            .FirstOrDefault(f => f.Style == Prowl.Scribe.FontStyle.Regular)
            ?? PaperInstance.EnumerateSystemFonts().FirstOrDefault();
        EditorTheme.DefaultBoldFont = PaperInstance.EnumerateSystemFonts()
            .FirstOrDefault(f => f.FamilyName == "segoe ui" && f.Style == Prowl.Scribe.FontStyle.Bold)
            ?? PaperInstance.EnumerateSystemFonts()
            .FirstOrDefault(f => f.FamilyName == "arial" && f.Style == Prowl.Scribe.FontStyle.Bold)
            ?? PaperInstance.EnumerateSystemFonts()
            .FirstOrDefault(f => f.Style == Prowl.Scribe.FontStyle.Bold)
            ?? PaperInstance.EnumerateSystemFonts().FirstOrDefault();
        PaperInstance.TextMode = Prowl.Quill.TextRenderMode.Bitmap;

        // Load Font Awesome as fallback fonts for icons
        LoadFallbackFont("Prowl.Editor.Resources.fa-regular-400.ttf");
        LoadFallbackFont("Prowl.Editor.Resources.fa-solid-900.ttf");

        _dockSpace = new DockSpace(CreateDefaultLayout());

        ScanAndRegisterPanels();
        RegisterMenus();

        // Start with the project launcher
        ProjectLauncher.Initialize();

        // Set Windows title bar to match Darkest theme color
        ApplyDarkTitleBar();
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

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
        int w = Window.InternalWindow.Size.X;
        int h = Window.InternalWindow.Size.Y;

        _time += Time.UnscaledDeltaTime;

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
            }
        }

        // Process file changes each frame (after project is loaded)
        EditorAssetDatabase.Instance?.ProcessFileChanges();

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

        MainMenuBar.Draw(paper);
        DrawTitleFlap(paper, w, h);

        float pad = EditorTheme.DockPadding;
        float dockY = EditorTheme.MenuBarHeight + pad;
        float dockH = h - dockY - pad;
        _dockSpace.Draw(paper, pad, dockY, w - pad * 2, dockH);
    }

    private void DrawTitleFlap(Paper paper, int w, int h)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Version label — goes to the right side of the menu bar
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
            .TextColor(EditorTheme.TextDim)
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

                var nc = EditorTheme.Normal;
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
                var bc = EditorTheme.Bright;
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

        // Interactive content inside flap — centered row: Project | Buttons | FPS
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
            .TextColor(EditorTheme.Text)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);

        bx += projW + 2f;

        // Separator |
        paper.Box("flap_sep1")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY + 4).Size(1, EditorTheme.MenuBarHeight - 8)
            .IsNotInteractable()
            .BackgroundColor(EditorTheme.Border);

        bx += 10f;

        // Play button
        paper.Box("btn_play")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, btnY).Size(btnSize, btnSize)
            .Rounded(4)
            .BackgroundColor(Application.IsPlaying ? System.Drawing.Color.FromArgb(255, 60, 160, 60) : System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(80, 255, 255, 255)).End()
            .Text(EditorIcons.Play, font).TextColor(EditorTheme.Text).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, e) => Application.IsPlaying = !Application.IsPlaying);

        bx += btnSize + 4f;

        // Pause button
        paper.Box("btn_pause")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, btnY).Size(btnSize, btnSize)
            .Rounded(4)
            .BackgroundColor(System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(80, 255, 255, 255)).End()
            .Text(EditorIcons.Pause, font).TextColor(EditorTheme.Text).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter);

        bx += btnSize + 4f;

        // Step button
        paper.Box("btn_step")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, btnY).Size(btnSize, btnSize)
            .Rounded(4)
            .BackgroundColor(System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(80, 255, 255, 255)).End()
            .Text(EditorIcons.ForwardStep, font).TextColor(EditorTheme.Text).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter);

        bx += btnSize + 8f;

        // Separator |
        paper.Box("flap_sep2")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY + 4).Size(1, EditorTheme.MenuBarHeight - 8)
            .IsNotInteractable()
            .BackgroundColor(EditorTheme.Border);

        bx += 10f;

        // FPS (fixed width)
        paper.Box("flap_fps")
            .PositionType(PositionType.SelfDirected)
            .Position(bx, flapContentY).Size(fpsW, EditorTheme.MenuBarHeight)
            .IsNotInteractable()
            .Text(fpsText, font)
            .TextColor(EditorTheme.Text)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);
    }

    public override void EndGui(Paper paper)
    {
        // Systems drawn on top (Overlay/Topmost layers)
        Widgets.FileDialog.Draw(paper);
        Widgets.ModalDialog.Draw(paper);
        Widgets.Toasts.Draw(paper, Time.UnscaledDeltaTime);
        Widgets.Tooltip.Draw(paper);

        // Intro animation overlay
        if (_introTime < IntroDuration)
        {
            _introTime += Time.UnscaledDeltaTime;
            if (_introTime < 0.1) Runtime.Debug.Log($"Intro started! time={_introTime:F3}");
            DrawIntro(paper);
        }
    }

    private const int BarCount = 10;

    private void DrawIntro(Paper paper)
    {
        int w = Window.InternalWindow.Size.X;
        int h = Window.InternalWindow.Size.Y;

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
            // TODO: focus the tab (select it as active)
            return;
        }

        // Create new instance
        if (Activator.CreateInstance(panelType) is not DockPanel panel) return;

        // Add as a floating window
        var node = DockNode.Leaf(panel);
        _dockSpace.FloatingWindows.Add(new FloatingWindow(node,
            new Prowl.Vector.Float2(200, 200),
            new Prowl.Vector.Float2(400, 300)));
    }

    /// <summary>
    /// Check if a panel of the given type is currently open.
    /// </summary>
    public bool IsPanelOpen(Type panelType) => FindOpenPanel(panelType) != null;

    // ================================================================
    //  Menu Registration
    // ================================================================

    private void RegisterMenus()
    {
        // File menu
        MenuRegistry.Register("File/New Scene", () => { /* TODO */ });
        MenuRegistry.Register("File/Open Scene", () => { /* TODO */ });
        MenuRegistry.RegisterSeparator("File");
        MenuRegistry.Register("File/Save Scene", () => { /* TODO */ });
        MenuRegistry.Register("File/Save Scene As...", () => { /* TODO */ });
        MenuRegistry.RegisterSeparator("File");
        MenuRegistry.Register("File/Open Project...", () => ReturnToLauncher());
        MenuRegistry.RegisterSeparator("File");
        MenuRegistry.Register("File/Exit", () => Game.Quit());

        // Edit menu
        MenuRegistry.Register("Edit/Undo", () => { /* TODO */ }, enabled: false);
        MenuRegistry.Register("Edit/Redo", () => { /* TODO */ }, enabled: false);
        MenuRegistry.RegisterSeparator("Edit");
        MenuRegistry.Register("Edit/Preferences...", () => { /* TODO */ });

        // Assets menu
        AssetCreateMenu.RegisterMenus();

        // Window menu — auto-populated from [EditorWindow] attributes
        foreach (var (type, path) in _registeredPanels)
        {
            var capturedType = type;
            MenuRegistry.Register($"Window/{path}", () => OpenPanel(capturedType),
                isChecked: () => IsPanelOpen(capturedType));
        }
    }

    // ================================================================
    //  Project Switching
    // ================================================================

    public void ReturnToLauncher()
    {
        // TODO: Check for unsaved changes and show confirmation dialog
        // For now, go straight back to launcher

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
    //  Default Layout
    // ================================================================

    private static DockNode CreateDefaultLayout()
    {
        return DockNode.Split(SplitDirection.Horizontal, 0.20f,
            DockNode.Leaf(new HierarchyPanel()),
            DockNode.Split(SplitDirection.Horizontal, 0.75f,
                DockNode.Split(SplitDirection.Vertical, 0.70f,
                    DockNode.Leaf(new SceneViewPanel()),
                    DockNode.Split(SplitDirection.Horizontal, 0.50f,
                        DockNode.Leaf(new ProjectPanel()),
                        DockNode.Leaf(new ConsolePanel())
                    )
                ),
                DockNode.Leaf(new InspectorPanel())
            )
        );
    }
}
