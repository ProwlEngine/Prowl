// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView.Editors;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

/// <summary>Inspector for <see cref="CSGShape"/>: the size and settings the shape was made with, which build it again when changed.</summary>
[CustomEditor(typeof(CSGShape))]
public class CSGShapeEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var shape = (CSGShape)target;
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.Label(paper, $"{id}_kind", CSGSections.ShapeName(shape.Kind)).Show();

        using (paper.Column($"{id}_body").Height(UnitValue.Auto).Gap(4).Margin(0, 0, 4, 6).Enter())
        {
            if (shape.Kind == CSGGeneratorKind.Polygon)
            {
                CSGSections.Line(paper, font, $"{id}_height", Loc.Get("csg.height"), () =>
                    Origami.NumericField(paper, $"{id}_height_v", shape.Height, v => Rebuild(shape, () => shape.Height = v)).Show());
            }
            else
            {
                CSGSections.Line(paper, font, $"{id}_size", Loc.Get("csg.size"), () =>
                    Origami.Float3Field(paper, $"{id}_size_v", shape.Size, v => Rebuild(shape, () => shape.Size = Maths.Max(v, new Float3(0.01f)))).Show());
                CSGSections.DrawGeneratorSettings(paper, font, id, shape.Kind, shape.Settings, v => Rebuild(shape, () => shape.Settings = v));
            }
        }

        Origami.Button(paper, $"{id}_convert", Loc.Get("csg.convert_to_brush"), () => ConvertToBrush(shape))
            .Tooltip(Loc.Get("csg.convert_to_brush_tip")).Show();
    }

    private static void Rebuild(CSGShape shape, Action change)
    {
        Undo.RecordObject(shape, "Edit CSG Shape");
        change();
        CSGSelection.Regenerate(shape, "Edit CSG Shape");
    }

    /// <summary>Forgets how the shape was made, leaving plain brushes whose points can be edited one by one.</summary>
    private static void ConvertToBrush(CSGShape shape)
    {
        CSGGroup group = shape.GetComponent<CSGGroup>();
        if (group.IsValid())
        {
            Undo.RecordObject(group, "Convert to Brush");
            group.HoldsShape = false;
        }
        Undo.DestroyObjectImmediate(shape);
    }
}
