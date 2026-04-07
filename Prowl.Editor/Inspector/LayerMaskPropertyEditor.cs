using System;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

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
        string displayText = GetDisplayText(mask);

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(fontSz);

            ElementHandle btnHandle = default;

            using (paper.Box($"{id}_btn")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.Neutral200)
                .Rounded(3)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .Hovered.BorderColor(EditorTheme.Purple400).End()
                .ChildLeft(6).ChildRight(6)
                .OnClick(e =>
                {
                    bool cur = paper.GetElementStorage(btnHandle, "open", false);
                    paper.SetElementStorage(btnHandle, "open", !cur);
                })
                .Enter())
            {
                btnHandle = paper.CurrentParent;
                bool isOpen = paper.GetElementStorage(btnHandle, "open", false);

                if (isOpen && !paper.IsParentHovered)
                    paper.SetElementStorage(btnHandle, "open", false);

                using (paper.Row($"{id}_display")
                    .Height(EditorTheme.RowHeight)
                    .Width(UnitValue.Stretch())
                    .Enter())
                {
                    paper.Box($"{id}_txt")
                        .Width(UnitValue.Stretch())
                        .IsNotInteractable()
                        .Text(displayText, font).TextColor(EditorTheme.Ink500).FontSize(fontSz);

                    paper.Box($"{id}_arrow")
                        .Margin(EditorTheme.RowHeight / 4, 0, EditorTheme.RowHeight / 8, 0)
                        .Width(16).MaxWidth(16)
                        .Text(isOpen ? EditorIcons.ChevronUp : EditorIcons.ChevronDown, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(fontSz * 0.7f);
                }

                if (isOpen)
                {
                    using (paper.Column($"{id}_popup")
                        .PositionType(PositionType.SelfDirected)
                        .Position(0, EditorTheme.RowHeight - 1)
                        .Width(UnitValue.Stretch())
                        .Height(UnitValue.Auto)
                        .BackgroundColor(EditorTheme.Neutral300)
                        .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                        .Rounded(4)
                        .ChildTop(2).ChildBottom(2).ChildLeft(2).ChildRight(2)
                        .HookToParent()
                        .Layer(Layer.Topmost)
                        .ClampToScreen()
                        .Enter())
                    {
                        // Nothing
                        DrawMaskOption(paper, font, fontSz, $"{id}_nothing", "Nothing", mask.Equals(LayerMask.Nothing), () =>
                        {
                            onChange(LayerMask.Nothing);
                        });

                        // Everything
                        DrawMaskOption(paper, font, fontSz, $"{id}_everything", "Everything", mask.Equals(LayerMask.Everything), () =>
                        {
                            onChange(LayerMask.Everything);
                        });

                        // Separator
                        paper.Box($"{id}_sep").Height(1).BackgroundColor(EditorTheme.Ink200);

                        // Individual layers
                        var layers = TagLayerManager.layers;
                        for (int i = 0; i < layers.Length; i++)
                        {
                            if (string.IsNullOrEmpty(layers[i])) continue;

                            int layerIdx = i;
                            bool active = mask.HasLayer(i);

                            DrawMaskOption(paper, font, fontSz, $"{id}_l_{i}", layers[i], active, () =>
                            {
                                var updated = mask;
                                if (active)
                                    updated.RemoveLayer(layerIdx);
                                else
                                    updated.SetLayer(layerIdx);
                                onChange(updated);
                            });
                        }
                    }
                }
            }
        }
    }

    private static void DrawMaskOption(Paper paper, Prowl.Scribe.FontFile font, float fontSz,
        string id, string text, bool active, Action onClick)
    {
        paper.Box(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(6)
            .BackgroundColor(active ? EditorTheme.Purple300 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Purple400).End()
            .Rounded(3)
            .HookToParent()
            .Text($"{(active ? EditorIcons.Check + "  " : "     ")}{text}", font)
            .TextColor(EditorTheme.Ink500).FontSize(fontSz)
            .OnClick(e => onClick());
    }

    private static string GetDisplayText(LayerMask mask)
    {
        if (mask.Equals(LayerMask.Nothing)) return "Nothing";
        if (mask.Equals(LayerMask.Everything)) return "Everything";

        var layers = TagLayerManager.layers;
        int count = 0;
        string? firstName = null;

        for (int i = 0; i < layers.Length; i++)
        {
            if (string.IsNullOrEmpty(layers[i])) continue;
            if (!mask.HasLayer(i)) continue;

            count++;
            firstName ??= layers[i];
        }

        return count switch
        {
            0 => "Nothing",
            1 => firstName!,
            _ => $"Mixed ({count} layers)"
        };
    }
}
