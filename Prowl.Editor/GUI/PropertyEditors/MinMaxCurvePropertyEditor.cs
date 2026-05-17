using System;

using Prowl.Editor.GUI.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
namespace Prowl.Editor.Inspector;

// ================================================================
//  MinMaxCurve Property Editor
// ================================================================

[CustomPropertyEditor(typeof(MinMaxCurve))]
public class MinMaxCurvePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var curve = value as MinMaxCurve ?? new MinMaxCurve();

        using (paper.Column(id).Height(UnitValue.Auto).Enter())
        {
            InspectorRow.Draw(paper, $"{id}_mode", label, () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", curve.Mode,
                    v => { curve.Mode = v; onChange(curve); }).Show());

            switch (curve.Mode)
            {
                case MinMaxCurveMode.Constant:
                    InspectorRow.Draw(paper, $"{id}_val", "Value", () =>
                        Origami.NumericField<float>(paper, $"{id}_val_v", curve.ConstantValue,
                            v => { curve.ConstantValue = v; onChange(curve); }).Show());
                    break;

                case MinMaxCurveMode.Curve:
                    InspectorRow.Draw(paper, $"{id}_curve", "Curve", () =>
                        CurveField.Create(paper, $"{id}_curve_cf", curve.Curve,
                            v => { curve.Curve = v; onChange(curve); }).Show());
                    break;

                case MinMaxCurveMode.Random:
                    InspectorRow.Draw(paper, $"{id}_min", "Min", () =>
                        Origami.NumericField<float>(paper, $"{id}_min_v", curve.MinValue,
                            v => { curve.MinValue = v; onChange(curve); }).Show());
                    InspectorRow.Draw(paper, $"{id}_max", "Max", () =>
                        Origami.NumericField<float>(paper, $"{id}_max_v", curve.MaxValue,
                            v => { curve.MaxValue = v; onChange(curve); }).Show());
                    break;
            }
        }
    }
}
