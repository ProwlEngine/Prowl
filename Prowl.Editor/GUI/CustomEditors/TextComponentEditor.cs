// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using TAlignment = Prowl.Runtime.UI.TextAlignment;

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
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Pre-snapshot: captures component state before any widget mutates it.
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

        PropertyGridUtils.DrawField(paper, $"{id}_font", "Font Asset", typeof(FontAsset), text.Font,
            v => text.Font = (FontAsset)v!, 0);

        paper.Box($"{id}_sp0.1").Height(6);

        InspectorRow.Draw(paper, $"{id}_size", "Font Size", () =>
            Origami.NumericField<int>(paper, $"{id}_size_v", text.Size, v => text.Size = v).Show());

        paper.Box($"{id}_sp0.2").Height(6);

        InspectorRow.Draw(paper, $"{id}_color", "Text Color", () =>
        Origami.ColorField(paper, $"{id}_color_f", text.TextColor, v => text.TextColor = v).Show());

        paper.Box($"{id}_sp0.3").Height(6);

        AlignmentRow(paper, $"{id}_align", text);

        paper.Box($"{id}_sp1").Height(6);

        // ── Extra Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_extra", "Extra Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(Runtime.Resources.Material), text.Material,
            v => text.Material = v as Runtime.Resources.Material, 0);
    }

    // ================================================================
    //  Alignment setter — two segmented controls on one row
    // ================================================================

    private static void AlignmentRow(Paper paper, string id, TextComponent text)
    {
        var font = EditorTheme.DefaultFont;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(6).Enter())
        {
            if (font != null)
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight)
                    .ChildLeft(4)
                    .IsNotInteractable()
                    .Text("Alignment", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            // Horizontal axis — icon segments, captures the current vertical flag on click.
            Origami.ButtonGroup(paper, $"{id}_h", HIndex(text.Alignment),
                    idx => text.Alignment = VFlag(VIndex(text.Alignment)) | HFlag(idx))
                .Height(EditorTheme.RowHeight)
                .FullWidth()
                .Item("", EditorIcons.AlignLeft, "Left")
                .Item("", EditorIcons.AlignCenter, "Center")
                .Item("", EditorIcons.AlignRight, "Right")
                .Show();

            // Vertical axis — labelled segments (no clean vertical-align glyphs in the icon font).
            Origami.ButtonGroup(paper, $"{id}_v", VIndex(text.Alignment),
                    idx => text.Alignment = VFlag(idx) | HFlag(HIndex(text.Alignment)))
                .Height(EditorTheme.RowHeight)
                .FullWidth()
                .Item("Top", tooltip: "Top")
                .Item("Mid", tooltip: "Middle")
                .Item("Bot", tooltip: "Bottom")
                .Show();
        }
    }

    // TextAlignment is a [Flags] enum split across two axes. These map each axis to/from
    // a 0..2 segment index for the button groups; recombining the two flags yields one of
    // the nine named combinations (e.g. Top | Left == TopLeft).

    private static int HIndex(TAlignment a)
        => (a & TAlignment.Right) != 0 ? 2 : (a & TAlignment.Middle) != 0 ? 1 : 0;

    private static int VIndex(TAlignment a)
        => (a & TAlignment.Bottom) != 0 ? 2 : (a & TAlignment.Center) != 0 ? 1 : 0;

    private static TAlignment HFlag(int i)
        => i == 2 ? TAlignment.Right : i == 1 ? TAlignment.Middle : TAlignment.Left;

    private static TAlignment VFlag(int i)
        => i == 2 ? TAlignment.Bottom : i == 1 ? TAlignment.Center : TAlignment.Top;
}
