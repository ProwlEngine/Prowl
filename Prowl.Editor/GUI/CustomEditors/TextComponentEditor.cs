// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

// ================================================================
//  Text Component Custom Editor
// ================================================================

[CustomEditor(typeof(TextComponent))]
public class TextComponentEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var text = (TextComponent)target;

        Undo.Snapshot(text);

        // ── Text Input ────────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_text", "Text Input").Show();

        int rows = 6;

        using (paper.Box($"{id}_text_box_container").Padding(10).Height(in UnitValue.Auto).Enter())
        {
            // Full-width multi-line area
            // Sits outside InspectorRow so it isn't clamped to a single row's height.
            Origami.TextArea(paper, $"{id}_text", text.Text, v => text.Text = v ?? string.Empty, rows: rows)
                .Placeholder("Enter text...")
                .Show();
        }

        paper.Box($"{id}_sp0").Height(6);

        // ── Main Settings ─────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_main", "Main Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_font", "Font Asset", typeof(AssetRef<FontAsset>), text.Font,
            v => text.Font = (AssetRef<FontAsset>)v!, 0);

        //paper.Box($"{id}_sp0.1").Height(6);

        using (paper.Box($"{id}_text_properties").Height(in UnitValue.Auto).Gap(EditorTheme.Spacing * 2f).Enter())
        {

            PropertyGridUtils.DrawField(paper, $"{id}_font", "Font Asset", typeof(int), text.Size,
                v => text.Size = (int)v!, 0);

            PropertyGridUtils.DrawField(paper, $"{id}_size", "Font Size", typeof(Prowl.Scribe.FontQuality),
                text.Quality,
                v => text.Quality = (Prowl.Scribe.FontQuality)v!, 0);

            PropertyGridUtils.DrawField(paper, $"{id}_quality", "Quality", typeof(Prowl.Scribe.FontQuality),
                text.Quality,
                v => text.Quality = (Prowl.Scribe.FontQuality)v!, 0);

            PropertyGridUtils.DrawField(paper, $"{id}_richtext", "Rich Text", typeof(bool), text.RichTextEnabled,
                v => text.RichTextEnabled = (bool)v!, 0);


            PropertyGridUtils.DrawField(paper, $"{id}_color", "Color", typeof(Color), text.Color,
                v => text.Color = (Color)v!, 0);

            EditorGUI.TextAlignmentRow(paper, $"{id}_align", "Alignment", text.Alignment, v => text.Alignment = v);
        }


        // ── Extra Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_extra", "Extra Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Runtime.Resources.Material>), text.Material,
            v => text.Material = (AssetRef<Runtime.Resources.Material>)v!, 0);
    }

}
