// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.SceneView;
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
        DrawModifiers(paper, id, [model]);
    }

    /// <summary>Several model objects: the smoothing angle and the modifiers, each change made to every one of them.</summary>
    public override bool OnGUIMulti(Paper paper, string id, IReadOnlyList<object> targets)
    {
        List<ModelObject> models = [.. targets.OfType<ModelObject>()];
        if (models.Count == 0) return false;

        Origami.Label(paper, $"{id}_smooth_label", Loc.Get("model.smoothing")).Tooltip(Loc.Get("model.smoothing_tip")).Show();
        Origami.Slider(paper, $"{id}_smooth", models[0].SmoothingAngle, value => Change(models, "Set Smoothing Angle", m => m.SmoothingAngle = value), 0f, 180f)
            .Format("F0").Show();

        paper.Box($"{id}_sp").Height(6);
        Origami.Label(paper, $"{id}_mods", Loc.Get("model.tab_modifiers")).Show();
        DrawModifiers(paper, id, models);
        return true;
    }

    private static void Change(IReadOnlyList<ModelObject> models, string name, Action<ModelObject> change)
    {
        Undo.RecordObjects([.. models], name);
        foreach (ModelObject model in models)
        {
            change(model);
            model.RefreshPreview();
        }
    }

    /// <summary>The modifier stack the objects share, as far as theirs hold the same kinds in the same order. Every change goes to all of them.</summary>
    private static void DrawModifiers(Paper paper, string id, List<ModelObject> models)
    {
        string[] kinds = [Loc.Get("model.add_modifier"), .. ModelModifier.Kinds.Select(k => k().Name)];
        Origami.Dropdown(paper, $"{id}_mod_add", 0, i =>
        {
            if (i > 0) Change(models, "Add Modifier", m => m.Modifiers.Add(ModelModifier.Kinds[i - 1]()));
        }, kinds).Show();

        ModelObject first = models[0];
        int shared = models.Min(m => m.Modifiers.Count);
        for (int i = 0; i < shared; i++)
            if (models.Any(m => m.Modifiers[i].GetType() != first.Modifiers[i].GetType()))
            {
                shared = i;
                break;
            }

        for (int i = 0; i < shared; i++)
        {
            int index = i;
            ModelModifier modifier = first.Modifiers[i];
            string row = $"{id}_mod{i}";
            paper.Box($"{row}_gap").Height(6).IsNotInteractable();
            using (paper.Row($"{row}_head").Height(24).Gap(4).Enter())
            {
                Origami.Label(paper, $"{row}_name", modifier.Name).Show();
                Origami.Button(paper, $"{row}_up", Loc.Get("model.move_up"), () => Change(models, "Move Modifier", m => Swap(m.Modifiers, index, index - 1)))
                    .Disabled(index == 0).Show();
                Origami.Button(paper, $"{row}_down", Loc.Get("model.move_down"), () => Change(models, "Move Modifier", m => Swap(m.Modifiers, index, index + 1)))
                    .Disabled(index == shared - 1).Show();
                Origami.Button(paper, $"{row}_apply", Loc.Get("model.apply_modifier"), () => Change(models, "Apply Modifier", m =>
                {
                    ModelModifier applied = m.Modifiers[index].Clone();
                    applied.Enabled = true;
                    m.Mesh = ModelModifier.Run(m.Mesh, [applied], m.ModifierContext());
                    m.Mesh.MarkChanged();
                    m.Modifiers.RemoveAt(index);
                })).Tooltip(Loc.Get("model.apply_modifier_tip")).Show();
                Origami.Button(paper, $"{row}_remove", Loc.Get("model.remove_modifier"), () => Change(models, "Remove Modifier", m => m.Modifiers.RemoveAt(index))).Show();
            }

            if (modifier is BooleanModifier boolean)
            {
                List<ModelObject> others = OtherModels(models);
                int current = others.FindIndex(o => o.Id == boolean.Target);
                Origami.Dropdown(paper, $"{row}_target", current + 1,
                    i => Change(models, "Set Boolean Object", m => ((BooleanModifier)m.Modifiers[index]).Target = i > 0 ? others[i - 1].Id : Guid.Empty),
                    [Loc.Get("model.boolean_none"), .. others.Select(o => o.GameObject.Name)]).Show();
            }

            // Edits go to a copy, so the objects are recorded for undo before the real ones change
            ModelModifier edited = modifier.Clone();
            PropertyGridUtils.Draw(paper, $"{row}_grid", edited, _ => Change(models, "Edit Modifier", m => m.Modifiers[index] = edited.Clone()));
        }
        if (models.Any(m => m.Modifiers.Count > shared))
            Origami.Label(paper, $"{id}_mods_differ", Loc.Get("model.modifiers_differ")).Show();
    }

    // The model objects of the open model other than the ones being edited
    private static List<ModelObject> OtherModels(IReadOnlyList<ModelObject> models)
    {
        var others = new List<ModelObject>();
        if (ModelEditingMode.World is not { } world) return others;
        foreach (GameObject go in world.Objects)
            if (go.GetComponent<ModelObject>() is { } model && model.IsValid() && !models.Contains(model)) others.Add(model);
        return others;
    }

    private static void Swap(List<ModelModifier> list, int a, int b)
    {
        if (b < 0 || b >= list.Count) return;
        (list[a], list[b]) = (list[b], list[a]);
    }
}
