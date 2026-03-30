using Prowl.Editor.Docking;
using Prowl.PaperUI;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Scene")]
public class SceneViewPanel : DockPanel
{
    public override string Title => "Scene";

    public override void OnGUI(Paper paper, float width, float height)
    {
        var el = paper.Box("scene_view_content").Size(width, height);
        if (EditorTheme.DefaultFont != null)
            el.Text("Scene View", EditorTheme.DefaultFont).TextColor(EditorTheme.TextDim).FontSize(18f);
    }
}
