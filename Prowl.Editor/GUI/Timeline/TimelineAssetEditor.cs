// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Drawing;

using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>The inspector for a graph asset: a summary and a button to open the graph window.</summary>
[CustomAssetEditor(typeof(Timeline))]
public class TimelineAssetEditor : AssetImporterEditor
{
    [AssetDoubleClickHandler(".timeline")]
    private static bool OnDoubleClick(string relativePath, Guid guid)
    {
        TimelinePanel.OpenFor(guid);
        return true;
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        var timeline = asset as Timeline;
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row($"{id}_open").Width(UnitValue.Stretch()).Height(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingMedium).Enter())
        {
            paper.Box($"{id}_openBtn")
                .Width(UnitValue.Stretch())
                .Height(30)
                .Rounded(m.SmallRounding)
                .BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text("Open Timeline", font)
                .TextColor(Color.White)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => TimelinePanel.OpenFor(entry.Guid));
        }

        if (timeline.IsNotValid()) return;

        //EditorGUI.SectionHeader(paper, $"{id}_h", "Summary", first: true);
    }
}
