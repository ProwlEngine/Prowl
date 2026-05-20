using System;

using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Widgets;
using Prowl.PaperUI;

using Gradient = Prowl.Runtime.Gradient;
namespace Prowl.Editor.Inspector;

// ================================================================
//  Gradient Property Editor
// ================================================================

[CustomPropertyEditor(typeof(Gradient))]
public class GradientPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var gradient = value as Gradient ?? new Gradient();

        InspectorRow.Draw(paper, id, label, () =>
            GradientField.Create(paper, $"{id}_gf", gradient,
                v => onChange(v)).Show());
    }
}
