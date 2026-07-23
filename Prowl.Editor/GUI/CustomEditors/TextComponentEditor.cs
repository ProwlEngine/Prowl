// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

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

        // Full-width multi-line area
        // Sits outside InspectorRow so it isn't clamped to a single row's height.
        Origami.TextArea(paper, $"{id}_text", text.Text, v => text.Text = v ?? string.Empty, rows: 6)
            .Placeholder("Enter text...")
            .Show();

        paper.Box($"{id}_sp0").Height(6);

        // ── Main Settings ─────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_main", "Main Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_font", "Font Asset", typeof(AssetRef<FontAsset>), text.Font,
            v => text.Font = (AssetRef<FontAsset>)v!, 0);

        paper.Box($"{id}_sp0.1").Height(6);

        EditorGUI.Row(paper, $"{id}_size", "Font Size", () =>
            Origami.NumericField<int>(paper, $"{id}_size_v", text.Size, v => text.Size = v).Show());

        paper.Box($"{id}_sp0.15").Height(6);

        EditorGUI.Row(paper, $"{id}_quality", "Quality", () =>
            Origami.EnumDropdown<Prowl.Scribe.FontQuality>(paper, $"{id}_quality_v", text.Quality, v => text.Quality = v).Show());

        paper.Box($"{id}_sp0.18").Height(6);

        Origami.Checkbox(paper, $"{id}_rich", text.RichTextEnabled, v => text.RichTextEnabled = v)
            .LabelRight("Rich Text").Show();

        paper.Box($"{id}_sp0.2").Height(6);

        EditorGUI.Row(paper, $"{id}_color", "Text Color", () =>
        Origami.ColorField(paper, $"{id}_color_f", text.TextColor, v => text.TextColor = v).Show());

        paper.Box($"{id}_sp0.3").Height(6);

        EditorGUI.TextAlignmentRow(paper, $"{id}_align", "Alignment", text.Alignment, v => text.Alignment = v);

        paper.Box($"{id}_sp1").Height(6);

        // ── Extra Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_extra", "Extra Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Runtime.Resources.Material>), text.Material,
            v => text.Material = (AssetRef<Runtime.Resources.Material>)v!, 0);
    }

}
