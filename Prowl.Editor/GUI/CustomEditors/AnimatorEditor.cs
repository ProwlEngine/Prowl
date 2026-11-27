// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(Animator))]
public class AnimatorEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var animator = (Animator)target;
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        DrawDefaultInspector(paper, id, animator);

        OrigamiMetrics m = Origami.Current.Metrics;
        using (paper.Row($"{id}_actions").Height(32)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.Spacing).Gap(m.SpacingMedium).Enter())
        {
            Origami.Button(paper, $"{id}_open_anim", $"{EditorIcons.Film}  Open Animation Window", () =>
            {
                AnimationPanel.OpenFor(animator);
            }).Width(UnitValue.Stretch()).Height(28).Show();
        }
    }
}
