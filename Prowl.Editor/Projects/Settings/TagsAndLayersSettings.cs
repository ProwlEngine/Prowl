using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
namespace Prowl.Editor.Projects.Settings;

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

    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Tags section
        Origami.Header(paper, "tl_tags_header", $"{EditorIcons.Tags}  Tags").Underline().Show();

        const float TagDelW = 20;

        for (int i = 0; i < Tags.Count; i++)
        {
            int idx = i;
            bool isBuiltin = i < 7;

            using (paper.Row($"tl_tag_{i}").Height(24).RowBetween(4).ChildLeft(8).ChildRight(4).Enter())
            {
                // Name: same control for every row; built-in tags are locked.
                using (paper.Box($"tl_tag_name_{i}").Width(UnitValue.Stretch()).Height(22).Enter())
                {
                    IDisposable? dim = isBuiltin ? EnableIfAttributeHandler.PushDisabledScope() : null;
                    try
                    {
                        Origami.TextField(paper, $"tl_tag_name_tf_{i}", Tags[i], v =>
                            {
                                if (isBuiltin || string.IsNullOrWhiteSpace(v)) return;
                                string trimmed = v.Trim();
                                // Duplicate names would make tag lookup ambiguous.
                                int existing = Tags.IndexOf(trimmed);
                                if (existing >= 0 && existing != idx) return;
                                Tags[idx] = trimmed;
                                Changed();
                            }).Show();
                    }
                    finally { dim?.Dispose(); }
                }

                if (!isBuiltin)
                {
                    paper.Box($"tl_tag_del_{i}")
                        .Width(TagDelW).Height(22).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (id, _) =>
                        {
                            Tags.RemoveAt(id);
                            Changed();
                        });
                }
                else
                {
                    paper.Box($"tl_tag_del_{i}").Width(TagDelW).Height(22); // column alignment spacer
                }
            }
        }

        paper.Box("tl_tags_sp").Height(4);

        // A text-entry "add" would fire per keystroke (typing "test" would add "t", "te",
        // "tes" and "test"); add a placeholder row instead and rename it in the row's own
        // name field above.
        Origami.Button(paper, "tl_add_tag", $"{EditorIcons.Plus}  Add Tag", () =>
        {
            string name = "New Tag";
            for (int n = 1; Tags.Contains(name); n++)
                name = $"New Tag ({n})";

            Tags.Add(name);
            Changed();
        }).Show();

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
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleRight);

                    paper.Box($"tl_layer_name_{i}")
                        .Height(22).ChildLeft(4)
                        .Text(Layers[i], font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
            else
            {
                EditorGUI.SettingsTextField(paper, $"tl_layer_{i}", $"Layer {i}", Layers[i],
                    v => { Layers[idx] = v; Apply(); });
            }
        }
    }
}
