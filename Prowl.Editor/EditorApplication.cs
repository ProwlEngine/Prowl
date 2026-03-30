using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Panels;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

public class EditorApplication : Game
{
    public static EditorApplication? Instance { get; private set; }

    private DockSpace _dockSpace = null!;

    public override void Initialize()
    {
        Instance = this;
        Application.IsEditor = true;
        Application.IsPlaying = false;

        // Load a system font for the editor UI
        EditorTheme.DefaultFont = PaperInstance.EnumerateSystemFonts().FirstOrDefault();

        _dockSpace = new DockSpace(CreateDefaultLayout());
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

    private static DockNode CreateDefaultLayout()
    {
        // Left: Hierarchy (20%)
        // Center-top: Scene (70% height)
        // Center-bottom: Project + Console (30% height, split 50/50)
        // Right: Inspector (25%)
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
