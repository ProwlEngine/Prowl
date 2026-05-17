// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

[CustomPropertyEditor(typeof(LayerMask))]
public class LayerMaskPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var mask = value is LayerMask lm ? lm : LayerMask.Nothing;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        float fontSz = EditorTheme.FontSize;

        // Pull the named layers out of the tag/layer manager. Empty slots are skipped so
        // the popover only shows assignable layers (matches the legacy widget's behaviour).
        var layers = TagLayerManager.layers;
        var validIndices = new List<int>(layers.Length);
        for (int i = 0; i < layers.Length; i++)
            if (!string.IsNullOrEmpty(layers[i])) validIndices.Add(i);

        // Project the bitmask into the int-indexed selection set Origami expects.
        var selected = new List<int>();
        foreach (int i in validIndices)
            if (mask.HasLayer(i)) selected.Add(i);

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(6).Enter())
        {
            if (!string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(fontSz);
            }

            Origami.MultiDropdown<int>(paper, $"{id}_md", selected, picked =>
                {
                    var updated = LayerMask.Nothing;
                    foreach (int i in picked) updated.SetLayer(i);
                    onChange(updated);
                }, validIndices)
                .Display(i => layers[i])
                .Height(EditorTheme.RowHeight)
                .SummaryFormat("{0} layers")
                .Searchable()
                .Show();
        }
    }
}
