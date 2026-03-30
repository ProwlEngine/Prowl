using Prowl.Editor.Docking;
using Prowl.PaperUI;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Inspector")]
public class InspectorPanel : DockPanel
{
    public override string Title => "Inspector";

    public override void OnGUI(Paper paper, float width, float height)
    {
        var el = paper.Box("inspector_content").Size(width, height);
        if (EditorTheme.DefaultFont != null)
            el.Text("Inspector", EditorTheme.DefaultFont).TextColor(EditorTheme.TextDim).FontSize(18f);
    }
}
