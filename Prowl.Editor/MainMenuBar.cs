using Prowl.PaperUI;

namespace Prowl.Editor;

public static class MainMenuBar
{
    private static readonly string[] MenuLabels = { "File", "Edit", "Assets", "GameObject", "Component", "Window", "Help" };

    public static void Draw(Paper paper)
    {
        using (paper.Row("menubar")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(paper.Percent(100), EditorTheme.MenuBarHeight)
            .BackgroundColor(EditorTheme.MenuBarBackground)
            .ChildLeft(4)
            .Enter())
        {
            foreach (var label in MenuLabels)
            {
                var el = paper.Box($"menu_{label}")
                    .Height(EditorTheme.MenuBarHeight)
                    .ChildLeft(8).ChildRight(8)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Active.BackgroundColor(EditorTheme.ButtonActive).End();

                if (EditorTheme.DefaultFont != null)
                    el.Text(label, EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.Text)
                        .FontSize(EditorTheme.FontSize);
            }
        }
    }
}
