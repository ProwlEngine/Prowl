using System;
using System.Collections.Generic;

using Prowl.Editor.Inspector;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

[ProjectSettings("Tags & Layers", EditorIcons.Tags, order: 10)]
public class TagsAndLayersSettings : ProjectSettingsBase
{
    public List<string> Tags = new(TagLayerManager.tags);
    public string[] Layers = (string[])TagLayerManager.layers.Clone();

    public override void Apply()
    {
        TagLayerManager.tags = new List<string>(Tags);
        Array.Copy(Layers, TagLayerManager.layers, Math.Min(Layers.Length, TagLayerManager.layers.Length));
    }

    public override void ResetToDefaults()
    {
        TagLayerManager.ResetDefault();
        Tags = new List<string>(TagLayerManager.tags);
        Layers = (string[])TagLayerManager.layers.Clone();
    }

    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Tags section
        Origami.Header(paper, "tl_tags_header", $"{EditorIcons.Tags}  Tags").Underline().Show();

        for (int i = 0; i < Tags.Count; i++)
        {
            int idx = i;
            bool isBuiltin = i < 7;

            using (paper.Row($"tl_tag_{i}").Height(24).RowBetween(4).ChildLeft(8).ChildRight(4).Enter())
            {
                paper.Box($"tl_tag_name_{i}")
                    .Height(22).ChildLeft(4)
                    .Text(Tags[i], font)
                    .TextColor(isBuiltin ? EditorTheme.Ink400 : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);

                if (!isBuiltin)
                {
                    paper.Box($"tl_tag_del_{i}")
                        .Width(20).Height(22).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (id, _) =>
                        {
                            Tags.RemoveAt(id);
                            Apply();
                            ProjectSettingsRegistry.SaveAll();
                        });
                }
            }
        }

        // Add tag row
        Origami.TextField(paper, "tl_new_tag", "", v =>
            {
                if (!string.IsNullOrWhiteSpace(v) && !Tags.Contains(v))
                {
                    Tags.Add(v);
                    Apply();
                    ProjectSettingsRegistry.SaveAll();
                }
            }).Placeholder("Add new tag...").Width(UnitValue.Stretch()).Show();

        paper.Box("tl_spacer1").Height(16);

        // Layers section
        Origami.Header(paper, "tl_layers_header", $"{EditorIcons.LayerGroup}  Layers").Underline().Show();

        for (int i = 0; i < Layers.Length; i++)
        {
            int idx = i;
            bool isBuiltin = i < 4;

            if (isBuiltin)
            {
                using (paper.Row($"tl_layer_{i}").Height(24).RowBetween(4).ChildLeft(8).Enter())
                {
                    paper.Box($"tl_layer_idx_{i}")
                        .Width(24).Height(22)
                        .Text(i.ToString(), font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleRight);

                    paper.Box($"tl_layer_name_{i}")
                        .Height(22).ChildLeft(4)
                        .Text(Layers[i], font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 1)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
            else
            {
                InspectorRow.Draw(paper, $"tl_layer_{i}", $"Layer {i}", () =>
                    Origami.TextField(paper, $"tl_layer_{i}_v", Layers[i], v =>
                    {
                        Layers[idx] = v;
                        Apply();
                        ProjectSettingsRegistry.SaveAll();
                    }).Show());
            }
        }
    }
}
