using Prowl.Editor.Docking;
using Prowl.PaperUI;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Console")]
public class ConsolePanel : DockPanel
{
    public override string Title => "Console";

    public override void OnGUI(Paper paper, float width, float height)
    {
        var el = paper.Box("console_content").Size(width, height);
        if (EditorTheme.DefaultFont != null)
            el.Text("Console", EditorTheme.DefaultFont).TextColor(EditorTheme.TextDim).FontSize(18f);
    }
}
