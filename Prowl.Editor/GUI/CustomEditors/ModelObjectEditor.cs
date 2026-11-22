// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI.SceneView.Editors;
using Prowl.Editor.Modeling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

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

/// <summary>
/// The Inspector in the model world, in tabs: the object as usual, its modifier stack, the edit tool's options and the
/// last operator's settings, and the edit selection as numbers.
/// </summary>
internal static class ModelInspector
{
    private enum Tab { Object, Modifiers, Tool, Selection }

    private static Tab s_tab;

    public static void Draw(Paper paper, GameObject go, Action drawDefault)
    {
        ModelObject? model = go.GetComponent<ModelObject>();
        if (model.IsNotValid())
        {
            drawDefault();
            return;
        }

        Origami.Tabs(paper, "model_tabs", (int)s_tab, i => s_tab = (Tab)i)
            .Underline()
            .Tab(Loc.Get("model.tab_object"))
            .Tab(Loc.Get("model.tab_modifiers"), null, model!.Modifiers.Count > 0 ? model.Modifiers.Count.ToString() : null)
            .Tab(Loc.Get("model.tab_tool"))
            .Tab(Loc.Get("model.tab_selection"))
            .Show();
        paper.Box("model_tabs_gap").Height(6).IsNotInteractable();

        ModelMeshTool? tool = SceneToolManager.Get<ModelMeshTool>();
        switch (s_tab)
        {
            case Tab.Object: drawDefault(); break;
            case Tab.Modifiers: DrawModifiers(paper, model); break;
            case Tab.Tool: DrawTool(paper, tool); break;
            case Tab.Selection: DrawSelection(paper, tool); break;
        }
    }

    private static void Change(ModelObject model, string name, Action change)
    {
        Undo.RecordObject(model, name);
        change();
        model.RefreshPreview();
    }

    private static void DrawModifiers(Paper paper, ModelObject model)
    {
        string[] kinds = [Loc.Get("model.add_modifier"), .. ModelModifier.Kinds.Select(k => k().Name)];
        Origami.Dropdown(paper, "model_mod_add", 0, i =>
        {
            if (i > 0) Change(model, "Add Modifier", () => model.Modifiers.Add(ModelModifier.Kinds[i - 1]()));
        }, kinds).Show();

        for (int i = 0; i < model.Modifiers.Count; i++)
        {
            int index = i;
            ModelModifier modifier = model.Modifiers[i];
            string id = $"model_mod{i}";
            paper.Box($"{id}_gap").Height(6).IsNotInteractable();
            using (paper.Row($"{id}_head").Height(24).Gap(4).Enter())
            {
                Origami.Label(paper, $"{id}_name", modifier.Name).Show();
                Origami.Button(paper, $"{id}_up", Loc.Get("model.move_up"), () => Change(model, "Move Modifier", () => Swap(model.Modifiers, index, index - 1)))
                    .Disabled(index == 0).Show();
                Origami.Button(paper, $"{id}_down", Loc.Get("model.move_down"), () => Change(model, "Move Modifier", () => Swap(model.Modifiers, index, index + 1)))
                    .Disabled(index == model.Modifiers.Count - 1).Show();
                Origami.Button(paper, $"{id}_apply", Loc.Get("model.apply_modifier"), () => Change(model, "Apply Modifier", () =>
                {
                    var copy = new GeometryData();
                    copy.Restore(model.Mesh.Capture());
                    model.Mesh = modifier.Apply(copy);
                    model.Mesh.MarkChanged();
                    model.Modifiers.RemoveAt(index);
                })).Tooltip(Loc.Get("model.apply_modifier_tip")).Show();
                Origami.Button(paper, $"{id}_remove", Loc.Get("model.remove_modifier"), () => Change(model, "Remove Modifier", () => model.Modifiers.RemoveAt(index))).Show();
            }

            // Edits go to a copy, so the object is recorded for undo before the real one changes
            ModelModifier edited = modifier.Clone();
            PropertyGridUtils.Draw(paper, $"{id}_grid", edited, _ => Change(model, "Edit Modifier", () => model.Modifiers[index] = edited.Clone()));
        }
    }

    private static void Swap(List<ModelModifier> list, int a, int b)
    {
        if (b < 0 || b >= list.Count) return;
        (list[a], list[b]) = (list[b], list[a]);
    }

    private static void DrawTool(Paper paper, ModelMeshTool? tool)
    {
        if (tool is not { Editing: true })
        {
            Origami.Label(paper, "model_tool_off", Loc.Get("model.not_editing")).Show();
            return;
        }

        using (paper.Row("model_tool_modes").Height(24).Gap(4).Enter())
        {
            Origami.Button(paper, "model_mode_v", Loc.Get("csg.mode_vertex"), () => tool.SetMode(SelectMode.Vertex)).Disabled(tool.Mode == SelectMode.Vertex).Show();
            Origami.Button(paper, "model_mode_e", Loc.Get("csg.mode_edge"), () => tool.SetMode(SelectMode.Edge)).Disabled(tool.Mode == SelectMode.Edge).Show();
            Origami.Button(paper, "model_mode_f", Loc.Get("csg.mode_face"), () => tool.SetMode(SelectMode.Face)).Disabled(tool.Mode == SelectMode.Face).Show();
        }
        Toggle(paper, "model_xray", Loc.Get("csg.xray"), tool.XRay, v => tool.XRay = v);
        Toggle(paper, "model_snap", Loc.Get("csg.snap_brushes"), tool.SnapToObjects, v => tool.SnapToObjects = v);
        Toggle(paper, "model_prop", Loc.Get("csg.proportional"), tool.Proportional, v => tool.Proportional = v);
        if (tool.Proportional)
            Origami.Slider(paper, "model_prop_radius", tool.ProportionalRadius, v => tool.ProportionalRadius = v, 0.01f, 10f).Format("0.##").Show();

        if (tool.Last is { } last)
        {
            paper.Box("model_last_gap").Height(8).IsNotInteractable();
            Origami.Header(paper, "model_last", last.Name).Underline().Show();
            PropertyGridUtils.DrawField(paper, "model_last_amount", Loc.Get("model.amount"), typeof(float), last.Amount,
                v => tool.AdjustLast(v is float amount ? amount : last.Amount, last.Count));
            if (last.MaxCount > 1)
                PropertyGridUtils.DrawField(paper, "model_last_count", Loc.Get("model.count"), typeof(int), last.Count,
                    v => tool.AdjustLast(last.Amount, v is int count ? count : last.Count));
        }

        paper.Box("model_ops_gap").Height(8).IsNotInteractable();
        Origami.Header(paper, "model_ops", Loc.Get("model.operations")).Underline().Show();
        List<(string Label, Action Run)> operations = tool.PanelOperations();
        for (int i = 0; i < operations.Count; i++)
            Origami.Button(paper, $"model_op{i}", operations[i].Label, operations[i].Run).Width(UnitValue.Stretch()).Show();
    }

    private static void Toggle(Paper paper, string id, string label, bool value, Action<bool> set)
    {
        using (paper.Row(id).Height(22).Gap(6).Enter())
        {
            Origami.Checkbox(paper, $"{id}_box", value, set).Show();
            Origami.Label(paper, $"{id}_label", label).Show();
        }
    }

    private static void DrawSelection(Paper paper, ModelMeshTool? tool)
    {
        if (tool is not { Editing: true })
        {
            Origami.Label(paper, "model_sel_off", Loc.Get("model.not_editing")).Show();
            return;
        }
        if (tool.SelectionMedian is not { } median)
        {
            Origami.Label(paper, "model_sel_none", Loc.Get("model.nothing_selected")).Show();
            return;
        }

        PropertyGridUtils.DrawField(paper, "model_sel_median", Loc.Get("model.median"), typeof(Float3), median,
            v => { if (v is Float3 to) tool.MoveSelectionTo(to); });

        if (tool.SelectedFaceSlot is not { } slot) return;
        string[] slots = [.. Enumerable.Range(0, tool.SlotCount).Select(i => Loc.Get("model.slot", new { index = i }))];
        if (slot < 0) Origami.Label(paper, "model_sel_mixed", Loc.Get("model.mixed_slots")).Show();
        Origami.Label(paper, "model_sel_slot_label", Loc.Get("model.face_slot")).Show();
        Origami.Dropdown(paper, "model_sel_slot", Math.Max(0, slot), tool.SetSelectedFaceSlot, slots).Show();
    }
}
