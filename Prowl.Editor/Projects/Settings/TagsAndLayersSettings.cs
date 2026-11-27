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

/// <summary> Project settings for configuring tags and layers used in the project. </summary>
[ProjectSettings("Tags & Layers", EditorIcons.Tags, order: 10)]
public class TagsAndLayersSettings : ProjectSettingsBase
{
    /// <summary> The list of tags defined for the project. </summary>
    public List<string> Tags = new(TagLayerManager.Tags);
    /// <summary> The array of layers defined for the project. </summary>
    public string[] Layers = (string[])TagLayerManager.Layers.Clone();
    /// <summary> Sorting layers back to front. </summary>
    public List<SortingLayerInfo> SortingLayers = CopyOf(TagLayerManager.SortingLayers);

    /// <summary> Applies the current tag and layer settings to TagLayerManager. </summary>
    public override void Apply()
    {
        TagLayerManager.Tags = new List<string>(Tags);
        Array.Copy(Layers, TagLayerManager.Layers, Math.Min(Layers.Length, TagLayerManager.Layers.Length));
        TagLayerManager.SetSortingLayers(SortingLayers);
    }

    /// <summary> Resets tags and layers to their default values. </summary>
    public override void ResetToDefaults()
    {
        TagLayerManager.ResetDefault();
        Tags = new List<string>(TagLayerManager.Tags);
        Layers = (string[])TagLayerManager.Layers.Clone();
        SortingLayers = CopyOf(TagLayerManager.SortingLayers);
    }

    private static List<SortingLayerInfo> CopyOf(IReadOnlyList<SortingLayerInfo> layers)
    {
        List<SortingLayerInfo> copy = new(layers.Count);
        foreach (SortingLayerInfo layer in layers)
            copy.Add(new SortingLayerInfo { Id = layer.Id, Name = layer.Name });
        return copy;
    }

    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    public override void OnGUI(Paper paper, float width)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Tags section
        Origami.Header(paper, "tl_tags_header", $"{EditorIcons.Tags}  Tags").Underline().Show();

        const float TagDelW = 20;

        for (int i = 0; i < Tags.Count; i++)
        {
            int idx = i;
            bool isBuiltin = i < 7;

            using (paper.Row($"tl_tag_{i}").Height(24).Gap(4).PaddingLeft(8).PaddingRight(4).Enter())
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
                            }).CommitOnEnter().Show();
                    }
                    finally { dim?.Dispose(); }
                }

                if (!isBuiltin)
                {
                    Origami.IconButton(paper, $"tl_tag_del_{i}", EditorIcons.Xmark_I, () =>
                        {
                            Tags.RemoveAt(idx);
                            Changed();
                        })
                        .Small().Width(TagDelW).Height(22).Ghost().Show();
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
                using (paper.Row($"tl_layer_{i}").Height(24).Gap(4).PaddingLeft(8).Enter())
                {
                    paper.Box($"tl_layer_idx_{i}")
                        .Width(24).Height(22)
                        .Text(i.ToString(), font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleRight);

                    paper.Box($"tl_layer_name_{i}")
                        .Height(22).PaddingLeft(4)
                        .Text(Layers[i], font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
            else
            {
                EditorGUI.SettingsTextField(paper, $"tl_layer_{i}", $"Layer {i}", Layers[i],
                    v =>
                    {
                        // Like tags: trimmed, and a name another layer already has is refused. Empty marks an unused layer.
                        string trimmed = v.Trim();
                        int existing = trimmed.Length > 0 ? Array.IndexOf(Layers, trimmed) : -1;
                        if (existing >= 0 && existing != idx) return;
                        Layers[idx] = trimmed;
                        Apply();
                    });
            }
        }

        paper.Box("tl_spacer2").Height(16);
        SortingLayersGUI(paper);
    }

    private void SortingLayersGUI(Paper paper)
    {
        Origami.Header(paper, "tl_sorting_header", $"{EditorIcons.LayerGroup}  Sorting Layers").Underline().Show();

        const float ButtonW = 20;
        for (int i = 0; i < SortingLayers.Count; i++)
        {
            int idx = i;
            SortingLayerInfo layer = SortingLayers[i];
            bool isDefault = layer.Id == SortingLayer.DefaultId;

            using (paper.Row($"tl_sort_{layer.Id}").Height(24).Gap(4).PaddingLeft(8).PaddingRight(4).Enter())
            {
                using (paper.Box($"tl_sort_name_{layer.Id}").Width(UnitValue.Stretch()).Height(22).Enter())
                {
                    IDisposable? dim = isDefault ? EnableIfAttributeHandler.PushDisabledScope() : null;
                    try
                    {
                        Origami.TextField(paper, $"tl_sort_name_tf_{layer.Id}", layer.Name, v =>
                            {
                                string trimmed = v.Trim();
                                if (isDefault || trimmed.Length == 0 || SortingLayers.Exists(l => l != layer && l.Name == trimmed)) return;
                                layer.Name = trimmed;
                                Changed();
                            }).CommitOnEnter().Show();
                    }
                    finally { dim?.Dispose(); }
                }

                Origami.IconButton(paper, $"tl_sort_up_{layer.Id}", EditorIcons.ChevronUp_I, () => Move(idx, idx - 1))
                    .Small().Width(ButtonW).Height(22).Ghost().Show();
                Origami.IconButton(paper, $"tl_sort_down_{layer.Id}", EditorIcons.ChevronDown_I, () => Move(idx, idx + 1))
                    .Small().Width(ButtonW).Height(22).Ghost().Show();

                if (!isDefault)
                    Origami.IconButton(paper, $"tl_sort_del_{layer.Id}", EditorIcons.Xmark_I, () =>
                        {
                            SortingLayers.RemoveAt(idx);
                            Changed();
                        })
                        .Small().Width(ButtonW).Height(22).Ghost().Show();
                else
                    paper.Box($"tl_sort_del_{layer.Id}").Width(ButtonW).Height(22);
            }
        }

        paper.Box("tl_sort_sp").Height(4);

        Origami.Button(paper, "tl_add_sorting", $"{EditorIcons.Plus}  Add Sorting Layer", () =>
        {
            string name = "New Layer";
            for (int n = 1; SortingLayers.Exists(l => l.Name == name); n++)
                name = $"New Layer ({n})";
            // Random so a deleted layer's id is never handed out again to renderers still holding it
            int id;
            do id = Random.Shared.Next(1, int.MaxValue);
            while (SortingLayers.Exists(l => l.Id == id));
            SortingLayers.Add(new SortingLayerInfo { Id = id, Name = name });
            Changed();
        }).Show();
    }

    private void Move(int from, int to)
    {
        if (to < 0 || to >= SortingLayers.Count) return;
        (SortingLayers[from], SortingLayers[to]) = (SortingLayers[to], SortingLayers[from]);
        Changed();
    }
}

/// <summary>Draws a <see cref="SortingLayer"/> field as a dropdown of the project's sorting layers.</summary>
public sealed class SortingLayerDrawer : OrigamiUI.FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type type, Action<object?> onChange, int depth)
    {
        SortingLayer current = value is SortingLayer layer ? layer : SortingLayer.Default;
        List<SortingLayer> values = [];
        foreach (SortingLayerInfo info in TagLayerManager.SortingLayers)
            values.Add(new SortingLayer(info.Id));
        if (!values.Contains(current))
            current = SortingLayer.Default;

        Origami.Dropdown(paper, $"{id}_dd", current, v => onChange(v), values)
            .Display(v => v.Name)
            .Show();
    }
}
