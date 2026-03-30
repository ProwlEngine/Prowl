using Prowl.Editor.Docking;
using Prowl.PaperUI;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Hierarchy")]
public class HierarchyPanel : DockPanel
{
    public override string Title => "Hierarchy";

    public override void OnGUI(Paper paper, float width, float height)
    {
        var el = paper.Box("hierarchy_content").Size(width, height);
        if (EditorTheme.DefaultFont != null)
            el.Text("Hierarchy", EditorTheme.DefaultFont).TextColor(EditorTheme.TextDim).FontSize(18f);
    }
}
