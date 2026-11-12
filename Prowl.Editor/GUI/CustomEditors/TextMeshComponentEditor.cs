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

        // ── Text Input ────────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_text", "Text Input").Show();

        Origami.TextArea(paper, $"{id}_text", text.Text, v => Edit(text, "Change Text", () => text.Text = v ?? string.Empty), rows: 6)
            .Placeholder("Enter text...")
            .Show();

        paper.Box($"{id}_sp0").Height(6);

        // ── Main Settings ─────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_main", "Main Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_font", "Font Asset", typeof(FontAsset), text.Font,
            v => Edit(text, "Change Font", () => text.Font = v as FontAsset), 0);

        paper.Box($"{id}_sp0.1").Height(6);

        EditorGUI.Row(paper, $"{id}_size", "Font Size", () =>
            Origami.NumericField<int>(paper, $"{id}_size_v", text.Size, v => Edit(text, "Change Font Size", () => text.Size = v)).Show());

        paper.Box($"{id}_sp0.15").Height(6);

        EditorGUI.Row(paper, $"{id}_quality", "Quality", () =>
            Origami.EnumDropdown<Prowl.Scribe.FontQuality>(paper, $"{id}_quality_v", text.Quality, v => Edit(text, "Change Quality", () => text.Quality = v)).Show());

        paper.Box($"{id}_sp0.18").Height(6);

        paper.Box($"{id}_sp0.2").Height(6);

        EditorGUI.Row(paper, $"{id}_color", "Text Color", () =>
            Origami.ColorField(paper, $"{id}_color_f", text.TextColor, v => Edit(text, "Change Text Color", () => text.TextColor = v)).Show());

        paper.Box($"{id}_sp0.3").Height(6);

        EditorGUI.TextAlignmentRow(paper, $"{id}_anchor", "Anchor", text.Anchor, v => Edit(text, "Change Anchor", () => text.Anchor = v));

        paper.Box($"{id}_sp1").Height(6);

        // ── World Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_world", "World Settings").Show();

        EditorGUI.Row(paper, $"{id}_ppu", "Pixels Per Unit", () =>
            Origami.NumericField<float>(paper, $"{id}_ppu_v", text.PixelsPerUnit, v => Edit(text, "Change Pixels Per Unit", () => text.PixelsPerUnit = v)).Show());

        paper.Box($"{id}_sp1.1").Height(6);

        EditorGUI.Row(paper, $"{id}_mw", "Max Width", () =>
            Origami.NumericField<float>(paper, $"{id}_mw_v", text.MaxWidth, v => Edit(text, "Change Max Width", () => text.MaxWidth = v)).Show());

        paper.Box($"{id}_sp2").Height(6);

        // ── Extra Settings ────────────────────────────────────────
        Origami.Header(paper, $"{id}_h_extra", "Extra Settings").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(Runtime.Resources.Material), text.Material,
            v => Edit(text, "Change Material", () => text.Material = v as Runtime.Resources.Material), 0);
        PropertyGridUtils.DrawField(paper, $"{id}_shadows", "Shadow Casting", typeof(ShadowCasting), text.ShadowCasting,
            v => Edit(text, "Change Shadow Casting", () => text.ShadowCasting = (ShadowCasting)v!), 0);
    }

    private static void Edit(TextMeshComponent text, string name, System.Action change)
    {
        Undo.RecordObject(text, name);
        change();
    }
}
