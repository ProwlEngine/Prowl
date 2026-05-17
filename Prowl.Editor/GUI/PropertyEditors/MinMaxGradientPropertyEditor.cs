using System;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Gradient = Prowl.Runtime.Gradient;

using PropertyGrid = Prowl.Editor.GUI.PropertyGrid;
namespace Prowl.Editor.Inspector;

// ================================================================
//  MinMaxGradient Property Editor
// ================================================================

[CustomPropertyEditor(typeof(MinMaxGradient))]
public class MinMaxGradientPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var gradient = value as MinMaxGradient ?? new MinMaxGradient();

        using (paper.Column(id).Height(UnitValue.Auto).Enter())
        {
            InspectorRow.Draw(paper, $"{id}_mode", label, () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", gradient.Mode,
                    v => { gradient.Mode = v; onChange(gradient); }).Show());

            switch (gradient.Mode)
            {
                case MinMaxGradientMode.Color:
                    InspectorRow.Draw(paper, $"{id}_color", "Color", () =>
                        Origami.ColorField(paper, $"{id}_color_cf", gradient.ConstantColor, v => { gradient.ConstantColor = v; onChange(gradient); }).Show());
                    break;

                case MinMaxGradientMode.Gradient:
                    PropertyGrid.DrawField(paper, $"{id}_grad", "Gradient", typeof(Gradient), gradient.Gradient,
                        v => { gradient.Gradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    break;

                case MinMaxGradientMode.RandomBetweenTwoColors:
                    InspectorRow.Draw(paper, $"{id}_minc", "Min Color", () =>
                        Origami.ColorField(paper, $"{id}_minc_cf", gradient.MinColor, v => { gradient.MinColor = v; onChange(gradient); }).Show());
                    InspectorRow.Draw(paper, $"{id}_maxc", "Max Color", () =>
                        Origami.ColorField(paper, $"{id}_maxc_cf", gradient.MaxColor, v => { gradient.MaxColor = v; onChange(gradient); }).Show());
                    break;

                case MinMaxGradientMode.RandomBetweenTwoGradients:
                    PropertyGrid.DrawField(paper, $"{id}_ming", "Min Gradient", typeof(Gradient), gradient.MinGradient,
                        v => { gradient.MinGradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    PropertyGrid.DrawField(paper, $"{id}_maxg", "Max Gradient", typeof(Gradient), gradient.MaxGradient,
                        v => { gradient.MaxGradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    break;
            }
        }
    }
}
