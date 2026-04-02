using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Docking;
using Prowl.Editor.Panels;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

public class EditorApplication : Game
{
    public static EditorApplication? Instance { get; private set; }

    private DockSpace _dockSpace = null!;

    // All registered panel types (from [EditorWindow] attribute scan)
    private readonly List<(Type type, string path)> _registeredPanels = new();

    public override void Initialize()
    {
        Instance = this;
        Application.IsEditor = true;
        Application.IsPlaying = false;

        EditorTheme.DefaultFont = PaperInstance.EnumerateSystemFonts().FirstOrDefault();
        PaperInstance.TextMode = Prowl.Quill.TextRenderMode.Bitmap;

        _dockSpace = new DockSpace(CreateDefaultLayout());

        ScanAndRegisterPanels();
        RegisterMenus();
    }

    public override void BeginGui(Paper paper)
    {
        int w = Window.InternalWindow.FramebufferSize.X;
        int h = Window.InternalWindow.FramebufferSize.Y;

        MainMenuBar.Draw(paper);

        float dockY = EditorTheme.MenuBarHeight;
        float dockH = h - dockY;
        _dockSpace.Draw(paper, 0, dockY, w, dockH);
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
        MenuRegistry.Register("File/Exit", () => Game.Quit());

        // Edit menu
        MenuRegistry.Register("Edit/Undo", () => { /* TODO */ }, enabled: false);
        MenuRegistry.Register("Edit/Redo", () => { /* TODO */ }, enabled: false);
        MenuRegistry.RegisterSeparator("Edit");
        MenuRegistry.Register("Edit/Preferences...", () => { /* TODO */ });

        // Window menu — auto-populated from [EditorWindow] attributes
        foreach (var (type, path) in _registeredPanels)
        {
            var capturedType = type;
            MenuRegistry.Register($"Window/{path}", () => OpenPanel(capturedType),
                isChecked: () => IsPanelOpen(capturedType));
        }
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
