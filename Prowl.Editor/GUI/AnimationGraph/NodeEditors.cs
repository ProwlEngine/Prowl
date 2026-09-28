// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using Canvas = Prowl.Quill.Canvas;
using Color = System.Drawing.Color;
using Color32 = Prowl.Vector.Color32;

namespace Prowl.Editor.Inspector;

/// <summary>A parameter node is its parameter's name and a port, nothing else, so it is just a header.</summary>
[AnimationNodeEditor(typeof(ParameterNode))]
internal sealed class ParameterNodeEditor : AnimationNodeEditor
{
    public override string AutoName(GraphNodeRecord record) => record.Get(ParameterNode.NameSetting);

    // Read straight from the parameter, since the node reports nothing until the graph ticks after a change.
    public override string LiveText(AnimationGraphEditing editing, GraphNodeRecord record, string reported)
    {
        string live = editing.LiveParameter(record.Get(ParameterNode.NameSetting));
        return live.Length > 0 ? live : reported;
    }

    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        card.Widget.Title = record.Name.Length > 0 ? record.Name : Node.DisplayName;
        card.Widget.HeaderOnly = true;
        card.Widget.Collapsible = false;
        card.Widget.Width = 168f;
    }

    public override void Validate(AnimationGraph graph, GraphNodeRecord record, NodeProblems problems)
    {
        string name = record.Get(ParameterNode.NameSetting);
        if (name.Length == 0) problems.Blocking("This reads a parameter but has no name.");
        else if (!graph.Parameters.Exists(p => p.Name == name) && !AnimationGraphView.VirtualParameters(graph).Contains(name))
            problems.Blocking($"No parameter called '{name}' is declared.");
    }

    public override void DrawInspector(AnimationNodeInspector inspector, GraphNodeRecord record)
    {
        inspector.Description();
        inspector.Window.DrawParameterPicker(inspector.Paper, "ag_p_Name", record.Get(ParameterNode.NameSetting));
    }
}

/// <summary>An external pose is known by the name the game finds it by.</summary>
[AnimationNodeEditor(typeof(ExternalPoseNode))]
internal sealed class ExternalPoseNodeEditor : AnimationNodeEditor
{
    public override string AutoName(GraphNodeRecord record) => record.Get(ExternalPoseNode.NameSetting);
}

/// <summary>A slot is known by the name the game fills it by.</summary>
[AnimationNodeEditor(typeof(ExternalGraphSlotNode))]
internal sealed class ExternalGraphSlotNodeEditor : AnimationNodeEditor
{
    public override string AutoName(GraphNodeRecord record) => record.Get(ExternalGraphSlotNode.Slot);
}

/// <summary>What an embedded sub graph plays. It feeds its owner rather than another node.</summary>
[AnimationNodeEditor(typeof(GraphOutputNode))]
internal sealed class GraphOutputNodeEditor : AnimationNodeEditor
{
    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record) => card.Widget.Width = 232f;
}

/// <summary>What a state plays, with a toggle beside its Enter and Exit for when nothing is wired to them.</summary>
[AnimationNodeEditor(typeof(StateOutputNode))]
internal sealed class StateOutputNodeEditor : AnimationNodeEditor
{
    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        card.Widget.Width = 232f;
        for (int pin = 1; pin <= 2 && pin < card.Widget.Inputs.Count; pin++)
            card.Widget.Inputs[pin].Inline = Gate(card.Editing, record, pin);
    }

    private static Action<NodeBodyContext> Gate(AnimationGraphEditing editing, GraphNodeRecord record, int pin) => ctx =>
    {
        while (record.Inputs.Count <= pin) record.Inputs.Add(new GraphInputRecord { Flag = pin == 2 });
        GraphInputRecord input = record.Inputs[pin];
        string name = pin == 1 ? "Enter" : "Exit";

        EditorGUI.Chip(ctx.Paper, "gate" + pin, input.Flag ? "Always" : "Never", input.Flag,
            () => editing.Edit("Toggle " + name, () => input.Flag = !input.Flag, false), 16f);
    };
}

/// <summary>Nodes whose every input carries a number of its own: a layer's weight, a blend's share, a pick's value.</summary>
[AnimationNodeEditor(typeof(LayerBlendNode))]
[AnimationNodeEditor(typeof(WeightedBlendNode))]
[AnimationNodeEditor(typeof(RandomSelectorNode))]
[AnimationNodeEditor(typeof(PickNumberNode))]
internal sealed class WeightedInputsNodeEditor : AnimationNodeEditor
{
    public override bool HasEntries => true;

    public override float EntryHeight => 22f;

    public override void DrawEntry(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, int pin)
    {
        if (pin >= record.Inputs.Count) return;
        GraphInputRecord input = record.Inputs[pin];

        if (Node.Id == AnimationNodeIds.LayerBlend)
        {
            Origami.Slider(paper, id, input.Value, v => editing.Edit("Set Layer Weight", () => input.Value = v, false), 0f, 1f)
                .Width(UnitValue.Stretch()).Small().Show();
            EditorGUI.Chip(paper, id + "_add", "Add", input.Flag, () => editing.Edit("Toggle Additive", () => input.Flag = !input.Flag, false));
            return;
        }

        string what = Node.Id == AnimationNodeIds.PickNumber ? "Set Value" : "Set Weight";
        Origami.NumericField<float>(paper, id, input.Value, v => editing.Edit(what, () => input.Value = v, false))
            .Width(UnitValue.Stretch()).Height(20).Show();
    }
}

/// <summary>Nodes whose every input is named: the channel a layer writes, the effector a rig moves.</summary>
[AnimationNodeEditor(typeof(ChannelLayerNode))]
[AnimationNodeEditor(typeof(IKRigNode))]
internal sealed class NamedInputsNodeEditor : AnimationNodeEditor
{
    private bool IsRig => Node.Id == AnimationNodeIds.IKRig;

    public override bool HasEntries => true;

    public override void DrawEntry(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, int pin)
    {
        if (pin >= record.Inputs.Count) return;
        GraphInputRecord input = record.Inputs[pin];

        Origami.TextField(paper, id, input.Name, v => editing.Edit("Name Input", () => input.Name = v, false))
            .Placeholder(IsRig ? "effector" : "channel").Width(UnitValue.Stretch()).Show();
    }

    // A rig's effectors also name the chain each one moves, which only the inspector has room for.
    public override void DrawInspector(AnimationNodeInspector inspector, GraphNodeRecord record)
    {
        base.DrawInspector(inspector, record);
        if (!IsRig) return;

        int stride = Node.VariadicStride;
        for (int pin = Node.VariadicStart, entry = 0; pin < record.Inputs.Count; pin += stride, entry++)
        {
            GraphInputRecord input = record.Inputs[pin];
            string id = $"ag_chain_{entry}";
            inspector.Row(id, $"{inspector.Editing.InputLabel(record, pin)} chain", () =>
                Origami.TextField(inspector.Paper, id + "_v", input.Text, v => inspector.Editing.Edit("Set Chain", () => input.Text = v)).Show());
        }
    }
}

/// <summary>A state machine's card is a small map of its states; opening it edits them.</summary>
[AnimationNodeEditor(typeof(StateMachineNode))]
internal sealed class StateMachineNodeEditor : AnimationNodeEditor
{
    public override string Tooltip => "Double click to open its states";

    public override string Summary(GraphNodeRecord record)
        => record.States.Count == 1 ? "1 state" : $"{record.States.Count} states";

    public override bool Open(AnimationGraphEditing editing, GraphNodeRecord record)
    {
        editing.EnterStates(record);
        return true;
    }

    public override void AddMenuItems(AnimationGraphEditing editing, GraphNodeRecord record, ContextBuilder menu)
        => menu.Item("Edit States", () => editing.EnterStates(record));

    public override void Validate(AnimationGraph graph, GraphNodeRecord record, NodeProblems problems)
    {
        if (record.States.Count == 0) problems.Unfinished("This state machine has no states.");
    }

    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        card.Width = 232f;
        card.Add(104f, (paper, id) => DrawStates(card.Editing, paper, id, record));
        card.AddOpenButton("Open");
    }

    public override void DrawInspector(AnimationNodeInspector inspector, GraphNodeRecord record)
    {
        inspector.Description();
        inspector.Settings();
        inspector.Button("ag_editStates", $"Edit States ({record.States.Count})", () => inspector.Editing.EnterStates(record));
    }

    /// <summary>A small map of a state machine's states and transitions, with the current state marked.</summary>
    private static void DrawStates(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord machine)
    {
        using (paper.Box(id).Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Rounded(8)
            .BackgroundColor(EditorTheme.Neutral300).Clip().IsNotInteractable().Enter())
            paper.Draw((canvas, rect) => PaintStates(editing, canvas, rect, machine));
    }

    private static void PaintStates(AnimationGraphEditing editing, Canvas canvas, Rect rect, GraphNodeRecord machine)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null || machine.States.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (GraphStateRecord state in machine.States)
        {
            minX = MathF.Min(minX, state.EditorPosition.X); maxX = MathF.Max(maxX, state.EditorPosition.X);
            minY = MathF.Min(minY, state.EditorPosition.Y); maxY = MathF.Max(maxY, state.EditorPosition.Y);
        }

        const float pillW = 52f, pillH = 18f, inset = 8f;
        float w = (float)rect.Size.X - inset * 2f - pillW, h = (float)rect.Size.Y - inset * 2f - pillH;
        float spanX = MathF.Max(1f, maxX - minX), spanY = MathF.Max(1f, maxY - minY);

        Float2 Place(GraphStateRecord state) => new(
            (float)rect.Min.X + inset + (maxX > minX ? (state.EditorPosition.X - minX) / spanX * w : w * 0.5f),
            (float)rect.Min.Y + inset + (maxY > minY ? (state.EditorPosition.Y - minY) / spanY * h : h * 0.5f));

        int active = editing.View.Probe?.ActiveState(editing.Graph, machine) ?? -1;

        canvas.SaveState();
        canvas.SetStrokeWidth(1.2f);
        canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.Purple400, 0.55f));
        foreach (GraphStateRecord state in machine.States)
            foreach (GraphTransitionRecord transition in state.Transitions)
            {
                GraphStateRecord? to = machine.States.Find(s => s.Name == transition.To);
                if (to == null) continue;

                Float2 a = Place(state) + new Float2(pillW * 0.5f, pillH * 0.5f);
                Float2 b = Place(to) + new Float2(pillW * 0.5f, pillH * 0.5f);
                canvas.BeginPath();
                canvas.MoveTo(a.X, a.Y);
                canvas.LineTo(b.X, b.Y);
                canvas.Stroke();
            }
        canvas.RestoreState();

        for (int i = 0; i < machine.States.Count; i++)
        {
            GraphStateRecord state = machine.States[i];
            Float2 at = Place(state);
            Color fill = i == active ? EditorTheme.Green400 : state.IsDefault ? EditorTheme.Purple400 : EditorTheme.WithAlpha(EditorTheme.Purple400, 90);

            canvas.RoundedRectFilled(at.X, at.Y, pillW, pillH, 5f, EditorTheme.ToColor32(fill));

            string text = state.Name.Length > 7 ? state.Name[..7] : state.Name;
            float tw = canvas.MeasureText(text, 10f, font).X;
            canvas.DrawText(text, at.X + (pillW - tw) * 0.5f, at.Y + 3f, EditorTheme.ToColor32(Color.White), 10f, font);
        }
    }
}

/// <summary>A sub graph is either its own nodes or a graph asset; its card shows a plan of whichever it runs.</summary>
[AnimationNodeEditor(typeof(SubGraphNode))]
internal sealed class SubGraphNodeEditor : AnimationNodeEditor
{
    public override string Tooltip => "Double click to open the graph it runs";

    // A sub graph starts as its own nodes, ready to fill in, and becomes an asset only when asked.
    public override void OnCreated(AnimationGraph graph, GraphNodeRecord record) => SetEmbedded(graph, record, true);

    public override bool Open(AnimationGraphEditing editing, GraphNodeRecord record)
    {
        editing.OpenSubGraph(record);
        return true;
    }

    public override void AddMenuItems(AnimationGraphEditing editing, GraphNodeRecord record, ContextBuilder menu)
    {
        menu.Item("Open Sub Graph", () => editing.OpenSubGraph(record));
        if (record.Get(SubGraphNode.Embedded))
            menu.Item("Extract To Asset", () => editing.ExtractToAsset(record.Id, "Sub Graph", asset =>
            {
                record.Properties[SubGraphNode.Embedded.Key] = NodeValue.FromFlag(false);
                record.Properties[SubGraphNode.GraphAssetSetting.Key] = NodeValue.FromGraph(asset);
            }));
    }

    public override void Validate(AnimationGraph graph, GraphNodeRecord record, NodeProblems problems)
    {
        if (record.Get(SubGraphNode.Embedded))
        {
            GraphNodeRecord? output = graph.OwnedNode(record.Id, AnimationNodeIds.GraphOutput);
            if (output == null || output.Inputs.Count == 0 || output.Inputs[0].Node.Length == 0)
                problems.Unfinished("Nothing is wired into its output, so this plays the reference pose.");
            return;
        }

        if (record.Get(SubGraphNode.GraphAssetSetting) is not { } value || value.Graph.AssetID == Guid.Empty)
            problems.Unfinished("No graph is set, so this plays the reference pose.");
        else if (value.Graph.Res is { } inner && inner.IsValid() && AnimationGraphView.LeadsBack(graph, inner))
            problems.Blocking($"'{inner.Name}' leads back to this graph, so it would never end.");
    }

    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        card.Width = 232f;
        card.Add(24f, (paper, id) => DrawSource(card.Editing, paper, id, record));
        if (!record.Get(SubGraphNode.Embedded))
            foreach (NodeSetting property in Node.Properties)
                if (property.Kind == NodeValueKind.Graph) card.AddSetting(property);
        card.Add(88f, (paper, id) => DrawPlan(card.Editing, paper, id, record));
        card.AddOpenButton("Open Graph");
    }

    /// <summary>Whether a sub graph is its own nodes or plays a graph asset.</summary>
    private static void DrawSource(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record)
    {
        bool embedded = record.Get(SubGraphNode.Embedded);
        Origami.ButtonGroup(paper, id, embedded ? 0 : 1, v =>
        {
            if ((v == 0) == embedded || editing.Graph.IsNotValid()) return;
            editing.Edit(v == 0 ? "Embed Sub Graph" : "Use Graph Asset", () => SetEmbedded(editing.Graph!, record, v == 0));
        })
            .Height(22).FullWidth()
            .Item("Embedded", tooltip: "Its own nodes, inside this graph")
            .Item("Asset", tooltip: "A graph asset")
            .Show();
    }

    // Its embedded nodes are kept either way.
    private static void SetEmbedded(AnimationGraph graph, GraphNodeRecord record, bool embedded)
    {
        record.Properties[SubGraphNode.Embedded.Key] = NodeValue.FromFlag(embedded);
        if (embedded) AnimationGraphView.EnsureOutput(graph, record.Id, AnimationNodeIds.GraphOutput);
    }

    /// <summary>A plan of the graph a sub graph node runs, so the card shows what is inside it.</summary>
    private static void DrawPlan(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record)
    {
        bool embedded = record.Get(SubGraphNode.Embedded);
        AnimationGraph? inner = embedded ? editing.Graph
            : record.Get(SubGraphNode.GraphAssetSetting)?.Graph.Res;

        using (paper.Box(id).Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Rounded(8)
            .BackgroundColor(EditorTheme.Neutral300).IsNotInteractable().Enter())
        {
            if (inner.IsNotValid()) return;

            AnimationGraphView view = editing.View.InnerView(inner!, embedded ? record.Id : "");
            paper.Draw((canvas, rect) => NodeGraphPreview.Paint(canvas, rect, view.Nodes, null, Origami.Current, 6f, groups: view.Groups));
        }
    }
}
