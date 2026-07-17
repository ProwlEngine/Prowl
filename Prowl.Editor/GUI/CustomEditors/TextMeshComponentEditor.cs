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
//  Text Mesh Component Custom Editor
// ================================================================
//  Routes every edit through the component's property setters so the
//  cached mesh is marked dirty and rebuilt (the default property grid
//  writes fields directly, which would skip the rebuild).

[CustomEditor(typeof(TextMeshComponent))]
public class TextMeshComponentEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var text = (TextMeshComponent)target;

        Undo.Snapshot(text);

        // ── Text Input ────────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_text", "Text Input").Show();

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

        AnchorRow(paper, $"{id}_anchor", text);

        paper.Box($"{id}_sp1").Height(6);

        // ── World Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_world", "World Settings").Show();

        EditorGUI.Row(paper, $"{id}_ppu", "Pixels Per Unit", () =>
            Origami.NumericField<float>(paper, $"{id}_ppu_v", text.PixelsPerUnit, v => text.PixelsPerUnit = v).Show());

        paper.Box($"{id}_sp1.1").Height(6);

        EditorGUI.Row(paper, $"{id}_mw", "Max Width", () =>
            Origami.NumericField<float>(paper, $"{id}_mw_v", text.MaxWidth, v => text.MaxWidth = v).Show());

        paper.Box($"{id}_sp2").Height(6);

        // ── Extra Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_extra", "Extra Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Runtime.Resources.Material>), text.Material,
            v => text.Material = (AssetRef<Runtime.Resources.Material>)v!, 0);
    }

    // ================================================================
    //  Anchor setter - two segmented controls on one row (9-point grid)
    // ================================================================

    private static void AnchorRow(Paper paper, string id, TextMeshComponent text)
    {
        var font = EditorTheme.DefaultFont;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(6).Enter())
        {
            if (font != null)
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight)
                    .ChildLeft(4)
                    .IsNotInteractable()
                    .Text("Anchor", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            Origami.ButtonGroup(paper, $"{id}_h", HIndex(text.Anchor),
                    idx => text.Anchor = VFlag(VIndex(text.Anchor)) | HFlag(idx))
                .Height(EditorTheme.RowHeight)
                .FullWidth()
                .Item("", EditorIcons.AlignLeft, "Left")
                .Item("", EditorIcons.AlignCenter, "Center")
                .Item("", EditorIcons.AlignRight, "Right")
                .Show();

            Origami.ButtonGroup(paper, $"{id}_v", VIndex(text.Anchor),
                    idx => text.Anchor = VFlag(idx) | HFlag(HIndex(text.Anchor)))
                .Height(EditorTheme.RowHeight)
                .FullWidth()
                .Item("Top", tooltip: "Top")
                .Item("Mid", tooltip: "Middle")
                .Item("Bot", tooltip: "Bottom")
                .Show();
        }
    }

    private static int HIndex(TAlignment a)
        => (a & TAlignment.Right) != 0 ? 2 : (a & TAlignment.Middle) != 0 ? 1 : 0;

    private static int VIndex(TAlignment a)
        => (a & TAlignment.Bottom) != 0 ? 2 : (a & TAlignment.Center) != 0 ? 1 : 0;

    private static TAlignment HFlag(int i)
        => i == 2 ? TAlignment.Right : i == 1 ? TAlignment.Middle : TAlignment.Left;

    private static TAlignment VFlag(int i)
        => i == 2 ? TAlignment.Bottom : i == 1 ? TAlignment.Center : TAlignment.Top;
}
