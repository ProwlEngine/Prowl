using Prowl.Editor.Docking;
using Prowl.PaperUI;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Project")]
public class ProjectPanel : DockPanel
{
    public override string Title => "Project";

    public override void OnGUI(Paper paper, float width, float height)
    {
        var el = paper.Box("project_content").Size(width, height);
        if (EditorTheme.DefaultFont != null)
            el.Text("Project", EditorTheme.DefaultFont).TextColor(EditorTheme.TextDim).FontSize(18f);
    }
}
