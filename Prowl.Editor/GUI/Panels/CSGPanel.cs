// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI.SceneView.Editors;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;

namespace Prowl.Editor.GUI.Panels;

/// <summary>The CSG controls as a dockable panel, showing the same sections as the scene view overlay.</summary>
public class CSGPanel : DockPanel
{
    [MenuItem("Window/General/CSG", priority: 9)]
    static void Open() => EditorApplication.Instance?.OpenPanel<CSGPanel>();

    public override string Title => Loc.Get("csg.title");
    public override string Icon => EditorIcons.Cube;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid())
        {
            CSGSections.EmptyState(paper, font, height);
            return;
        }

        // The same tabs and sections as the scene view's panel, with room to breathe
        CSGOverlayTool.DrawModeTabs(paper, font, "csg_dock");
        Origami.ScrollView(paper, "csg_scroll", width, height - 44).Body(() =>
        {
            using (paper.Column("csg_content").Height(UnitValue.Auto).Padding(0, 0, 0, 12).Enter())
                CSGOverlayTool.DrawModeBody(paper, font);
        });
    }
}
