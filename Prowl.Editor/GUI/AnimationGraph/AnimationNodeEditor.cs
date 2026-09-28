// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using AnimationCurve = Prowl.Vector.AnimationCurve;
using HumanBodyBone = Prowl.Motion.HumanBodyBone;
using MotionAvatar = Prowl.Motion.Avatar;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Editor.Inspector;

/// <summary>Marks an <see cref="AnimationNodeEditor"/> as the one for a node type, and for the types deriving from it.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AnimationNodeEditorAttribute(Type nodeType) : Attribute
{
    public Type NodeType { get; } = nodeType;
}

/// <summary>How the graph editor shows one kind of node. The default draws any node from its pins and settings.</summary>
public class AnimationNodeEditor
{
    private static readonly Dictionary<AnimationGraphNode, AnimationNodeEditor> s_editors = new(ReferenceEqualityComparer.Instance);

    public AnimationGraphNode Node { get; private set; } = null!;

    /// <summary>The editor for a node type: its own, or the default.</summary>
    public static AnimationNodeEditor For(AnimationGraphNode node)
    {
        if (s_editors.TryGetValue(node, out AnimationNodeEditor? editor)) return editor;

        editor = EditorRegistries.CreateAnimationNodeEditor(node.GetType()) ?? new AnimationNodeEditor();
        editor.Node = node;
        s_editors[node] = editor;
        return editor;
    }

    internal static void ClearCache() => s_editors.Clear();

    /// <summary>The icon on the node's header.</summary>
    public virtual IOrigamiIcon Icon => Node.Id switch
    {
        AnimationNodeIds.Clip => EditorIcons.Film_I,
        AnimationNodeIds.AnimationPose => EditorIcons.Image_I,
        AnimationNodeIds.ReferencePose => EditorIcons.Person_I,
        AnimationNodeIds.ZeroPose => EditorIcons.Circle_I,
        AnimationNodeIds.Passthrough => EditorIcons.ArrowRight_I,
        AnimationNodeIds.Mirror => EditorIcons.LeftRight_I,
        AnimationNodeIds.SpeedScale => EditorIcons.Gauge_I,
        AnimationNodeIds.PoseSnapshot => EditorIcons.Camera_I,
        AnimationNodeIds.ExternalPose => EditorIcons.ArrowRightToBracket_I,
        AnimationNodeIds.SubGraph => EditorIcons.DiagramProject_I,
        AnimationNodeIds.ExternalGraphSlot => EditorIcons.Plug_I,

        AnimationNodeIds.Blend1D => EditorIcons.ChartLine_I,
        AnimationNodeIds.Blend2D => EditorIcons.BorderAll_I,
        AnimationNodeIds.VelocityBlend => EditorIcons.GaugeHigh_I,
        AnimationNodeIds.WeightedBlend => EditorIcons.ScaleBalanced_I,
        AnimationNodeIds.Layer or AnimationNodeIds.LayerBlend => EditorIcons.LayerGroup_I,
        AnimationNodeIds.MakeAdditive or AnimationNodeIds.Crossfade => EditorIcons.Plus_I,
        AnimationNodeIds.MuscleLayer => EditorIcons.Dumbbell_I,
        AnimationNodeIds.InertialBlend => EditorIcons.Wind_I,
        AnimationNodeIds.PoseSmoothing => EditorIcons.Water_I,

        AnimationNodeIds.Selector or AnimationNodeIds.ConditionSelector => EditorIcons.CodeBranch_I,
        AnimationNodeIds.Sequence => EditorIcons.ListOl_I,
        AnimationNodeIds.RandomSelector => EditorIcons.Shuffle_I,
        AnimationNodeIds.StateMachine => EditorIcons.CircleNodes_I,

        AnimationNodeIds.OrientationWarp => EditorIcons.Compass_I,
        AnimationNodeIds.TargetWarp => EditorIcons.Crosshairs_I,
        AnimationNodeIds.StrideWarp => EditorIcons.PersonRunning_I,
        AnimationNodeIds.RootMotionFilter => EditorIcons.Filter_I,
        AnimationNodeIds.CharacterMotion => EditorIcons.Route_I,
        AnimationNodeIds.FootGrounding or AnimationNodeIds.FootLock => EditorIcons.ShoePrints_I,

        AnimationNodeIds.LookAt => EditorIcons.Eye_I,
        AnimationNodeIds.AimConstraint => EditorIcons.Crosshairs_I,
        AnimationNodeIds.CopyConstraint => EditorIcons.Link_I,
        AnimationNodeIds.TwistDistribution => EditorIcons.Rotate_I,
        AnimationNodeIds.SpringBones => EditorIcons.Wind_I,
        AnimationNodeIds.Ragdoll => EditorIcons.PersonFalling_I,

        AnimationNodeIds.PoseChannel or AnimationNodeIds.ChannelLayer or AnimationNodeIds.DrivenChannel => EditorIcons.FaceSmile_I,

        AnimationNodeIds.Parameter or AnimationNodeIds.VirtualParameter => EditorIcons.Sliders_I,
        AnimationNodeIds.ConstFloat or AnimationNodeIds.ConstInt => EditorIcons.Hashtag_I,
        AnimationNodeIds.ConstBool => EditorIcons.ToggleOn_I,
        AnimationNodeIds.ConstId or AnimationNodeIds.IdComparison or AnimationNodeIds.IdToFloat => EditorIcons.Tag_I,
        AnimationNodeIds.ConstVector or AnimationNodeIds.VectorCreate or AnimationNodeIds.VectorInfo or AnimationNodeIds.VectorNegate => EditorIcons.ArrowsUpDownLeftRight_I,
        AnimationNodeIds.FloatMath or AnimationNodeIds.FloatAngle => EditorIcons.Calculator_I,
        AnimationNodeIds.FloatCompare or AnimationNodeIds.FloatRange or AnimationNodeIds.Logic => EditorIcons.Equals_I,
        AnimationNodeIds.Curve or AnimationNodeIds.FloatEase or AnimationNodeIds.FloatRemap => EditorIcons.ChartLine_I,
        AnimationNodeIds.Timer => EditorIcons.Stopwatch_I,
        AnimationNodeIds.Noise => EditorIcons.WaveSquare_I,
        AnimationNodeIds.FloatSpring => EditorIcons.Magnet_I,
        AnimationNodeIds.CachedValue => EditorIcons.Anchor_I,
        AnimationNodeIds.TargetInfo or AnimationNodeIds.TargetOffset => EditorIcons.Crosshairs_I,

        AnimationNodeIds.StateQuery or AnimationNodeIds.StateFinished => EditorIcons.Hourglass_I,
        AnimationNodeIds.FootEvent => EditorIcons.ShoePrints_I,

        _ => Node.Category switch
        {
            "Sources" => EditorIcons.Person_I,
            "Modify" => EditorIcons.Sliders_I,
            "Blending" => EditorIcons.LayerGroup_I,
            "Logic" => EditorIcons.CodeBranch_I,
            "Locomotion" => EditorIcons.PersonWalking_I,
            "Rig" => EditorIcons.Bone_I,
            "Channels" => EditorIcons.FaceSmile_I,
            "Masks" => EditorIcons.Mask_I,
            "Events" => EditorIcons.Bolt_I,
            _ => EditorIcons.Cube_I,
        },
    };

    /// <summary>What hovering the node's header says.</summary>
    public virtual string Tooltip => Node.Description;

    /// <summary>The name a node takes from how it is set, such as the clip it plays, or empty for none.</summary>
    public virtual string AutoName(GraphNodeRecord record)
    {
        foreach (NodeSetting property in Node.Properties)
            if (record.Properties.TryGetValue(property.Key, out NodeValue? value) && AssetName(value) is { Length: > 0 } name)
                return name;
        return string.Empty;
    }

    /// <summary>The line on a card that has no settings of its own to show.</summary>
    public virtual string Summary(GraphNodeRecord record)
    {
        foreach (NodeSetting property in Node.Properties)
        {
            if (!record.Properties.TryGetValue(property.Key, out NodeValue? value)) continue;
            switch (value.Kind)
            {
                case NodeValueKind.Clip: return AssetName(value) is { Length: > 0 } clip ? clip : "no clip";
                case NodeValueKind.Graph: return AssetName(value) is { Length: > 0 } graph ? graph : "no graph";
                case NodeValueKind.Mask when AssetName(value) is { Length: > 0 } mask: return mask;
                case NodeValueKind.Text or NodeValueKind.Id when value.Text.Length > 0: return value.Text;
            }
        }
        return string.Empty;
    }

    /// <summary>What the node's live badge shows while the game runs, given what the running graph reports.</summary>
    public virtual string LiveText(AnimationGraphEditing editing, GraphNodeRecord record, string reported) => reported;

    /// <summary>Called once a node has just been added to a graph.</summary>
    public virtual void OnCreated(AnimationGraph graph, GraphNodeRecord record) { }

    /// <summary>Opens what the node holds, on a double click or from its menu. Returns false when there is nothing to open.</summary>
    public virtual bool Open(AnimationGraphEditing editing, GraphNodeRecord record) => false;

    /// <summary>Adds the node's own items to its right click menu.</summary>
    public virtual void AddMenuItems(AnimationGraphEditing editing, GraphNodeRecord record, ContextBuilder menu) { }

    /// <summary>Reports what is wrong with a node beyond unwired pins and mismatched wires, which are checked for every node.</summary>
    public virtual void Validate(AnimationGraph graph, GraphNodeRecord record, NodeProblems problems) { }

    /// <summary>Fills in the node's card. By default its settings, the entries of its repeating pins, then its toggles.</summary>
    public virtual void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        card.AddSettings();
        card.AddEntries();
        card.AddToggles();
    }

    /// <summary>Draws the node in the inspector. By default its description, its settings, then the entries of its repeating pins.</summary>
    public virtual void DrawInspector(AnimationNodeInspector inspector, GraphNodeRecord record)
    {
        inspector.Description();
        inspector.Settings();
        inspector.Entries();
    }

    /// <summary>Whether each repetition of the node's repeating pins has settings of its own, drawn by <see cref="DrawEntry"/>.</summary>
    public virtual bool HasEntries => false;

    /// <summary>The height of one entry on the card.</summary>
    public virtual float EntryHeight => Origami.Current.Metrics.RowHeight;

    /// <summary>The settings kept on one repetition of the repeating pins, such as a layer's weight, beside its label.</summary>
    public virtual void DrawEntry(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, int pin) { }

    private static string AssetName(NodeValue value) => value.Kind switch
    {
        NodeValueKind.Clip when value.Clip.Res is { } clip && clip.IsValid() => clip.Name,
        NodeValueKind.Graph when value.Graph.Res is { } graph && graph.IsValid() => graph.Name,
        NodeValueKind.Mask when value.Mask.Res is { } mask && mask.IsValid() => mask.Name,
        _ => string.Empty,
    };
}

/// <summary>What is wrong with one node, as an editor reports it.</summary>
public sealed class NodeProblems
{
    private readonly List<GraphProblem> _found;
    private readonly string _nodeId;

    internal NodeProblems(List<GraphProblem> found, string nodeId)
    {
        _found = found;
        _nodeId = nodeId;
    }

    /// <summary>Something that stops the graph running.</summary>
    public void Blocking(string message) => _found.Add(new GraphProblem(_nodeId, message, true));

    /// <summary>Something the graph runs with, but that is likely not finished.</summary>
    public void Unfinished(string message) => _found.Add(new GraphProblem(_nodeId, message, false));
}

/// <summary>The graph a node editor works on, and what it can do to it through the window showing it.</summary>
public sealed class AnimationGraphEditing
{
    private readonly AnimationGraphView _view;

    internal AnimationGraphEditing(AnimationGraphView view) => _view = view;

    internal Action<string, Action, bool>? EditHandler;
    internal Action<GraphNodeRecord>? EnterStatesHandler;
    internal Action<GraphNodeRecord>? OpenSubGraphHandler;
    internal Action<string, string, Action<AssetRef<AnimationGraph>>>? ExtractHandler;

    internal AnimationGraphView View => _view;

    public AnimationGraph? Graph => _view.Graph;

    /// <summary>The rig the editor picks bones and clips from.</summary>
    public Avatar? Rig => _view.Graph.IsValid() ? _view.Graph!.Rig.Res : null;

    /// <summary>Makes a change undoable. A rebuild redraws every card after, for a change to what cards show.</summary>
    public void Edit(string description, Action change, bool rebuild = true)
    {
        if (EditHandler != null) EditHandler(description, change, rebuild);
        else change();
    }

    public GraphNodeRecord? RecordOf(string nodeId) => _view.RecordOf(nodeId);

    /// <summary>What a repeating input is called: the node feeding it, or the pin and its place in the list.</summary>
    public string InputLabel(GraphNodeRecord record, int pin)
        => AnimationNodeRegistry.Get(record.Type) is { } type ? _view.InputLabel(record, type, pin) : string.Empty;

    /// <summary>What a node is doing in the running game, or empty when nothing runs.</summary>
    public string LiveValue(GraphNodeRecord record)
        => _view.Probe?.Read(_view.Graph, record)?.Value ?? string.Empty;

    /// <summary>A declared parameter's value in the running game, or empty when nothing runs or none is declared by that name.</summary>
    public string LiveParameter(string name)
    {
        if (_view.Probe == null || _view.Graph.IsNotValid()) return string.Empty;
        GraphParameterRecord? declared = _view.Graph!.Parameters.Find(p => p.Name == name);
        return declared == null ? string.Empty : _view.Probe.ReadParameter(declared);
    }

    /// <summary>A number node's value in the running game, or null when nothing runs.</summary>
    public float? LiveNumber(GraphNodeRecord record) => _view.Probe?.ReadNumber(_view.Graph, record);

    /// <summary>Opens a state machine node's states.</summary>
    public void EnterStates(GraphNodeRecord machine) => EnterStatesHandler?.Invoke(machine);

    /// <summary>Opens the graph a sub graph node runs.</summary>
    public void OpenSubGraph(GraphNodeRecord record) => OpenSubGraphHandler?.Invoke(record);

    /// <summary>Moves the nodes an owner holds into a new graph asset, then hands the asset over to use in their place.</summary>
    public void ExtractToAsset(string owner, string name, Action<AssetRef<AnimationGraph>> use) => ExtractHandler?.Invoke(owner, name, use);
}

/// <summary>A node's card as its editor fills it in: fixed height rows of widgets under the header.</summary>
public sealed class AnimationNodeCard
{
    private const float LabelWidth = 64f;
    private const float Gap = 4f;
    private const float Pad = 8f;

    internal readonly record struct Row(float Height, Action<Paper, string> Draw);

    internal readonly List<Row> Rows = new();

    internal AnimationNodeCard(GraphNode widget, GraphNodeRecord record, AnimationNodeEditor editor, AnimationGraphEditing editing)
    {
        Widget = widget;
        Record = record;
        Editor = editor;
        Editing = editing;
    }

    /// <summary>The card as the graph widget draws it, for its title, width or ports.</summary>
    public GraphNode Widget { get; }

    public GraphNodeRecord Record { get; }
    public AnimationNodeEditor Editor { get; }
    public AnimationGraphNode Node => Editor.Node;
    public AnimationGraphEditing Editing { get; }

    /// <summary>The card's width while it has rows, or null for the usual width.</summary>
    public float? Width { get; set; }

    public static float RowHeight => Origami.Current.Metrics.RowHeight;

    /// <summary>Adds a row, drawn with the paper and an id unique to it.</summary>
    public void Add(float height, Action<Paper, string> draw) => Rows.Add(new Row(height, draw));

    /// <summary>Adds a row for each setting that is shown and not exposed as a port, other than the toggles.</summary>
    public void AddSettings()
    {
        foreach (NodeSetting property in Node.Properties)
            if (property.Kind != NodeValueKind.Flag && ShowsOnCard(property)) AddSetting(property);
    }

    /// <summary>Adds a row editing one setting, with the widget its kind calls for.</summary>
    public void AddSetting(NodeSetting property)
    {
        float height = property.Kind == NodeValueKind.Curve ? 50f : RowHeight;
        Add(height, (paper, id) => DrawProperty(Editing, paper, id, Record, property));
    }

    /// <summary>Adds one row of chips for the on and off settings.</summary>
    public void AddToggles()
    {
        var flags = new List<NodeSetting>();
        foreach (NodeSetting property in Node.Properties)
            if (property.Kind == NodeValueKind.Flag && ShowsOnCard(property)) flags.Add(property);
        if (flags.Count > 0) Add(22f, (paper, id) => DrawToggles(Editing, paper, id, Record, flags));
    }

    /// <summary>Adds a row for each repetition of the repeating pins, when the editor has entries to draw.</summary>
    public void AddEntries()
    {
        int stride = Node.VariadicStride;
        if (!Editor.HasEntries || stride <= 0) return;

        for (int pin = Node.VariadicStart; pin < Record.Inputs.Count; pin += stride)
        {
            int entry = pin;
            Add(Editor.EntryHeight, (paper, id) =>
            {
                Label(paper, id + "_l", Editing.InputLabel(Record, entry));
                Editor.DrawEntry(Editing, paper, id, Record, entry);
            });
        }
    }

    /// <summary>Adds a button that opens what the node holds.</summary>
    public void AddOpenButton(string label)
        => Add(24f, (paper, id) => Origami.Button(paper, id, label, () => Editor.Open(Editing, Record))
            .Subtle().Width(UnitValue.Stretch()).Height(24).Show());

    // An exposed setting is a port now, and the port is where it is set.
    private bool ShowsOnCard(NodeSetting property)
        => !Record.PropertyInputs.ContainsKey(property.Key) && Node.ShowsProperty(Record, property.Key);

    /// <summary>Gives the widget the card's rows, height and width. Returns false for a card with nothing on it.</summary>
    internal bool Apply()
    {
        if (Rows.Count == 0) return false;

        float height = Pad * 0.5f;
        foreach (Row row in Rows) height += row.Height + Gap;

        Widget.Width = Width ?? 212f;
        Widget.BodyHeight = height;
        List<Row> rows = Rows;
        Widget.Body = ctx => Draw(ctx, rows);
        return true;
    }

    private static void Draw(NodeBodyContext ctx, List<Row> rows)
    {
        Paper paper = ctx.Paper;
        using (paper.Column(ctx.Id("rows")).Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .Padding(Pad, Pad, Pad * 0.5f, 0).Enter())
        {
            for (int i = 0; i < rows.Count; i++)
            {
                // Every row keeps its drags to itself, so working a control never drags the node,
                // while the gaps between rows still pick the card up.
                var line = ctx.Control(paper.Row(ctx.Id("row" + i)).Width(UnitValue.Stretch()).Height(rows[i].Height)
                    .Margin(0, 0, 0, Gap).AlignItems(LayoutAlignment.Center));

                using (line.Enter())
                    rows[i].Draw(paper, ctx.Id("c" + i));
            }
        }
    }

    /// <summary>Gives a setting its own port, or takes it away and leaves the value that was typed.</summary>
    public static void Expose(GraphNodeRecord record, NodeSetting property, bool exposed)
    {
        if (exposed) record.PropertyInputs[property.Key] = string.Empty;
        else record.PropertyInputs.Remove(property.Key);
    }

    /// <summary>A label at the start of a row.</summary>
    public static void Label(Paper paper, string id, string text)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        paper.Box(id).Width(LabelWidth).Height(UnitValue.Stretch()).IsNotInteractable()
            .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate();
    }

    /// <summary>One setting on a card, as the widget its kind calls for.</summary>
    public static void DrawProperty(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, NodeSetting setting)
    {
        switch (setting.Kind)
        {
            case NodeValueKind.Clip or NodeValueKind.Mask or NodeValueKind.Avatar or NodeValueKind.Graph:
                using (paper.Box(id + "_host").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Margin(-Origami.Current.Metrics.PaddingLarge, -Origami.Current.Metrics.PaddingLarge, 0, 0).Enter())
                    AssetField(editing, paper, id, "", record, setting);
                return;
            case NodeValueKind.Number or NodeValueKind.Integer:
                ValueField(editing, paper, id, record, setting, dragLabel: setting.Label);
                return;
            default:
                Label(paper, id + "_l", setting.Label);
                ValueField(editing, paper, id, record, setting);
                return;
        }
    }

    /// <summary>The widget editing a setting's value, without its label. A number given a drag label is dragged by it.</summary>
    public static void ValueField(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, NodeSetting setting,
        string? dragLabel = null, float curveHeight = 40f)
    {
        NodeValue value = AnimationGraphView.ValueOf(record, setting);
        string edit = "Set " + setting.Label;

        if (setting is TextSetting { Bone: true, List: false })
        {
            BoneField(paper, id, editing.Rig, value.Text, v => editing.Edit(edit, () => value.Text = v));
            return;
        }

        switch (setting.Kind)
        {
            case NodeValueKind.Number:
                var number = Origami.NumericField<float>(paper, id, value.Number, v => editing.Edit(edit, () => value.Number = v, false));
                if (dragLabel != null) number.DraggableLabel(dragLabel, EditorTheme.Ink400);
                number.Width(UnitValue.Stretch()).Height(RowHeight).Show();
                return;

            case NodeValueKind.Integer:
                var integer = Origami.NumericField<int>(paper, id, value.Integer, v => editing.Edit(edit, () => value.Integer = v, false));
                if (dragLabel != null) integer.DraggableLabel(dragLabel, EditorTheme.Ink400);
                integer.Width(UnitValue.Stretch()).Height(RowHeight).Show();
                return;

            case NodeValueKind.Vector:
                Origami.Float3Field(paper, id, value.Vector, v => editing.Edit(edit, () => value.Vector = v, false)).Show();
                return;

            // The curve widget changes the curve it is handed, so it gets a copy and the edit lands through undo.
            case NodeValueKind.Curve:
                CurveField.Create(paper, id, (value.Curve ?? new AnimationCurve()).Clone(),
                    v => editing.Edit(edit, () => value.Curve = v.Clone(), false)).PreviewHeight(curveHeight).Show();
                return;

            default:
                if (setting.Choices is { } options)
                {
                    int selected = Math.Max(0, Array.IndexOf(options, value.Text));
                    Origami.Dropdown(paper, id, selected, v => editing.Edit(edit, () => value.Text = options[v]), options)
                        .Width(UnitValue.Stretch()).Show();
                }
                else
                {
                    Origami.TextField(paper, id, value.Text, v => editing.Edit(edit, () => value.Text = v))
                        .Width(UnitValue.Stretch()).Show();
                }
                return;
        }
    }

    /// <summary>An asset setting's picker, with its own label.</summary>
    public static void AssetField(AnimationGraphEditing editing, Paper paper, string id, string label, GraphNodeRecord record, NodeSetting setting)
    {
        NodeValue value = AnimationGraphView.ValueOf(record, setting);
        switch (setting.Kind)
        {
            case NodeValueKind.Clip: AssetField(paper, id, label, value.Clip, v => editing.Edit("Set Clip", () => value.Clip = v)); break;
            case NodeValueKind.Mask: AssetField(paper, id, label, value.Mask, v => editing.Edit("Set Mask", () => value.Mask = v)); break;
            case NodeValueKind.Avatar: AssetField(paper, id, label, value.Avatar, v => editing.Edit("Set Avatar", () => value.Avatar = v)); break;
            case NodeValueKind.Graph: AssetField(paper, id, label, value.Graph, v => editing.Edit("Set Graph", () => value.Graph = v)); break;
        }
    }

    public static void AssetField<T>(Paper paper, string id, string label, AssetRef<T> value, Action<AssetRef<T>> setter) where T : EngineObject
    {
        object boxed = value;
        new AssetRefPropertyEditor().OnGUI(paper, id, label, boxed, _ => setter((AssetRef<T>)boxed), 0);
    }

    /// <summary>The on and off settings of a node, as chips: the Loop of a clip, the Mirror of a blend.</summary>
    public static void DrawToggles(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, IReadOnlyList<NodeSetting> flags)
    {
        for (int i = 0; i < flags.Count; i++)
        {
            NodeValue value = AnimationGraphView.ValueOf(record, flags[i]);
            string name = flags[i].Key;
            EditorGUI.Chip(paper, $"{id}_{i}", name, value.Flag, () => editing.Edit("Toggle " + name, () => value.Flag = !value.Flag, false));
        }
    }

    /// <summary>The bones a rig offers: its humanoid bones first, then the bones it actually has.</summary>
    public static List<string> BoneOptions(Avatar? rig)
    {
        var options = new List<string>();
        if (rig.IsNotValid()) return options;

        MotionAvatar? runtime = rig!.Runtime;
        if (runtime?.Humanoid is { } humanoid)
            foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>())
                if (humanoid.HasBone(bone)) options.Add(GraphCompileContext.HumanoidBonePrefix + bone.ToString());

        MotionSkeleton? skeleton = rig.Skeleton;
        if (skeleton != null)
            for (int i = 0; i < skeleton.BoneCount; i++)
                if (skeleton.GetBoneID(i).DebugName is { Length: > 0 } name) options.Add(name);

        return options;
    }

    /// <summary>One bone, picked from the rig, or typed when there is no rig.</summary>
    public static void BoneField(Paper paper, string id, Avatar? rig, string value, Action<string> setter)
    {
        List<string> options = BoneOptions(rig);
        if (options.Count == 0)
        {
            Origami.TextField(paper, id, value, setter).Placeholder("bone").Width(UnitValue.Stretch()).Show();
            return;
        }

        // A bone the rig does not have is still what the graph says, so it is offered rather than lost.
        int selected = options.IndexOf(value);
        if (selected < 0 && value.Length > 0)
        {
            options.Insert(0, value);
            selected = 0;
        }

        string[] items = options.ToArray();
        Origami.Dropdown(paper, id, Math.Max(0, selected), v => setter(items[v]), items)
            .Searchable("Search bones...").Width(UnitValue.Stretch()).Show();
    }
}

/// <summary>A selected node in the inspector, as its editor draws it.</summary>
public sealed class AnimationNodeInspector
{
    private readonly AnimationGraphWindow _window;

    internal AnimationNodeInspector(AnimationGraphWindow window, Paper paper, GraphNodeRecord record, AnimationNodeEditor editor, AnimationGraphEditing editing)
    {
        _window = window;
        Paper = paper;
        Record = record;
        Editor = editor;
        Editing = editing;
    }

    internal AnimationGraphWindow Window => _window;

    public Paper Paper { get; }
    public GraphNodeRecord Record { get; }
    public AnimationNodeEditor Editor { get; }
    public AnimationGraphNode Node => Editor.Node;
    public AnimationGraphEditing Editing { get; }

    /// <summary>The node type's description, as a note.</summary>
    public void Description()
    {
        if (Node.Description.Length > 0) Note("ag_desc", Node.Description);
    }

    /// <summary>A row for each setting that is shown, other than a list another list draws beside its own.</summary>
    public void Settings()
    {
        foreach (NodeSetting property in Node.Properties)
            if (!IsPairedInto(property.Key) && Node.ShowsProperty(Record, property.Key)) Setting(property);
    }

    /// <summary>A row editing one setting, with the widget its kind calls for.</summary>
    public void Setting(NodeSetting property) => _window.DrawNodeProperty(Paper, Record, property);

    /// <summary>A row for each repetition of the repeating pins, when the editor has entries to draw.</summary>
    public void Entries(string? header = "Inputs")
    {
        int stride = Node.VariadicStride;
        if (!Editor.HasEntries || stride <= 0 || Record.Inputs.Count <= Node.VariadicStart) return;

        if (header != null) Header("ag_h_inputs", header);
        for (int pin = Node.VariadicStart, entry = 0; pin < Record.Inputs.Count; pin += stride, entry++)
        {
            int at = pin;
            string id = $"ag_in_{entry}";
            Row(id, Editing.InputLabel(Record, at), () => Editor.DrawEntry(Editing, Paper, id + "_v", Record, at));
        }
    }

    public void Header(string id, string title) => EditorGUI.SectionHeader(Paper, id, title);

    public void Row(string id, string label, Action draw) => EditorGUI.Row(Paper, id, label, draw);

    public void Note(string id, string text) => EditorGUI.Note(Paper, id, text);

    public void Button(string id, string label, Action onClick)
        => EditorGUI.PillButton(Paper, id, label, active: true, onClick).Margin(0, 0, Origami.Current.Metrics.Spacing, Origami.Current.Metrics.Spacing);

    private bool IsPairedInto(string name)
    {
        foreach (NodeSetting property in Node.Properties)
            if (property is TextSetting { PairedWith: { } paired } && paired.Key == name) return true;
        return false;
    }
}
