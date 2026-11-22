// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Modeling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

/// <summary>Inspector for <see cref="ModelObject"/>: the size of its mesh, its smoothing angle and a material for each slot.</summary>
[CustomEditor(typeof(ModelObject))]
public class ModelObjectEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var model = (ModelObject)target;

        Origami.Label(paper, $"{id}_stats", Loc.Get("model.stats", new
        {
            vertices = model.Mesh.Vertices.Count,
            faces = model.Mesh.Faces.Count,
        })).Show();

        paper.Box($"{id}_sp").Height(4);
        Origami.Label(paper, $"{id}_smooth_label", Loc.Get("model.smoothing")).Tooltip(Loc.Get("model.smoothing_tip")).Show();
        Origami.Slider(paper, $"{id}_smooth", model.SmoothingAngle, value =>
        {
            Undo.RecordObject(model, "Set Smoothing Angle");
            model.SmoothingAngle = value;
            model.RefreshPreview();
        }, 0f, 180f).Format("F0").Show();

        paper.Box($"{id}_sp2").Height(6);
        Origami.Label(paper, $"{id}_slots", Loc.Get("model.material_slots")).Show();
        for (int i = 0; i < model.Materials.Count; i++)
        {
            int slot = i;
            Material? material = model.Materials[i] == Guid.Empty ? null : AssetDatabase.Get<Material>(model.Materials[i]);
            EngineObjectPropertyEditor.SetFieldType(typeof(Material));
            new EngineObjectPropertyEditor().OnGUI(paper, $"{id}_slot{i}", Loc.Get("model.slot", new { index = i }), material, picked =>
            {
                Undo.RecordObject(model, "Set Material");
                model.Materials[slot] = picked is Material chosen ? chosen.AssetID : Guid.Empty;
                model.RefreshPreview();
            }, 0);
        }

        using (paper.Row($"{id}_slot_buttons").Height(24).Gap(4).Enter())
        {
            Origami.Button(paper, $"{id}_add_slot", Loc.Get("model.add_slot"), () =>
            {
                Undo.RecordObject(model, "Add Material Slot");
                model.Materials.Add(Guid.Empty);
                model.RefreshPreview();
            }).Show();
            if (model.Materials.Count > 0)
                Origami.Button(paper, $"{id}_remove_slot", Loc.Get("model.remove_slot"), () =>
                {
                    Undo.RecordObject(model, "Remove Material Slot");
                    model.Materials.RemoveAt(model.Materials.Count - 1);
                    model.RefreshPreview();
                }).Show();
        }
    }
}
