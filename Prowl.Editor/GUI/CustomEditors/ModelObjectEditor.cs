// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

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

/// <summary>Inspector for <see cref="ModelObject"/>: the size of its mesh, its smoothing angle, a material for each slot and its modifiers.</summary>
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

        paper.Box($"{id}_sp3").Height(6);
        Origami.Label(paper, $"{id}_mods", Loc.Get("model.tab_modifiers")).Show();
        DrawModifiers(paper, id, model);
    }

    private static void Change(ModelObject model, string name, Action change)
    {
        Undo.RecordObject(model, name);
        change();
        model.RefreshPreview();
    }

    private static void DrawModifiers(Paper paper, string id, ModelObject model)
    {
        string[] kinds = [Loc.Get("model.add_modifier"), .. ModelModifier.Kinds.Select(k => k().Name)];
        Origami.Dropdown(paper, $"{id}_mod_add", 0, i =>
        {
            if (i > 0) Change(model, "Add Modifier", () => model.Modifiers.Add(ModelModifier.Kinds[i - 1]()));
        }, kinds).Show();

        for (int i = 0; i < model.Modifiers.Count; i++)
        {
            int index = i;
            ModelModifier modifier = model.Modifiers[i];
            string row = $"{id}_mod{i}";
            paper.Box($"{row}_gap").Height(6).IsNotInteractable();
            using (paper.Row($"{row}_head").Height(24).Gap(4).Enter())
            {
                Origami.Label(paper, $"{row}_name", modifier.Name).Show();
                Origami.Button(paper, $"{row}_up", Loc.Get("model.move_up"), () => Change(model, "Move Modifier", () => Swap(model.Modifiers, index, index - 1)))
                    .Disabled(index == 0).Show();
                Origami.Button(paper, $"{row}_down", Loc.Get("model.move_down"), () => Change(model, "Move Modifier", () => Swap(model.Modifiers, index, index + 1)))
                    .Disabled(index == model.Modifiers.Count - 1).Show();
                Origami.Button(paper, $"{row}_apply", Loc.Get("model.apply_modifier"), () => Change(model, "Apply Modifier", () =>
                {
                    ModelModifier applied = modifier.Clone();
                    applied.Enabled = true;
                    model.Mesh = ModelModifier.Run(model.Mesh, [applied], false, [.. model.Bones.Select(b => b.Name)]);
                    model.Mesh.MarkChanged();
                    model.Modifiers.RemoveAt(index);
                })).Tooltip(Loc.Get("model.apply_modifier_tip")).Show();
                Origami.Button(paper, $"{row}_remove", Loc.Get("model.remove_modifier"), () => Change(model, "Remove Modifier", () => model.Modifiers.RemoveAt(index))).Show();
            }

            // Edits go to a copy, so the object is recorded for undo before the real one changes
            ModelModifier edited = modifier.Clone();
            PropertyGridUtils.Draw(paper, $"{row}_grid", edited, _ => Change(model, "Edit Modifier", () => model.Modifiers[index] = edited.Clone()));
        }
    }

    private static void Swap(List<ModelModifier> list, int a, int b)
    {
        if (b < 0 || b >= list.Count) return;
        (list[a], list[b]) = (list[b], list[a]);
    }
}
