// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GraphEditor;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using AnimationCurve = Prowl.Vector.AnimationCurve;
using AnimationNodeCard = Prowl.Editor.GraphEditor.NodeCard<Prowl.Runtime.GraphNodeRecord, Prowl.Editor.Inspector.AnimationGraphEditing>;
using AnimationNodeInspector = Prowl.Editor.GraphEditor.NodeInspector<Prowl.Runtime.GraphNodeRecord, Prowl.Editor.Inspector.AnimationGraphEditing>;
using HumanBodyBone = Prowl.Motion.HumanBodyBone;

namespace Prowl.Editor.Inspector;

/// <summary>
/// How the graph editor shows one kind of animation node. The default draws any node from its pins and
/// settings. Mark a subclass with <see cref="NodeEditorAttribute"/> to make it the one for a node type.
/// </summary>
public class AnimationNodeEditor : NodeEditor<GraphNodeRecord, AnimationGraphEditing>
{
    private static readonly NodeEditorCache<AnimationGraphNode, AnimationNodeEditor> s_editors
        = new(() => new AnimationNodeEditor(), (editor, node) => editor.Node = node);

    public AnimationGraphNode Node { get; private set; } = null!;

    /// <summary>The editor for a node type: its own, or the default.</summary>
    public static AnimationNodeEditor For(AnimationGraphNode node) => s_editors.For(node);

    public override IOrigamiIcon? Icon => Node.Id switch
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

    public override string Tooltip => Node.Description;

    public override string AutoName(GraphNodeRecord record)
    {
        foreach (NodeSetting property in Node.Properties)
            if (record.Properties.TryGetValue(property.Key, out NodeValue? value) && AssetName(value) is { Length: > 0 } name)
                return name;
        return string.Empty;
    }

    public override string Summary(GraphNodeRecord record)
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

    /// <summary>Called once a node has just been added to a graph, by the editor or the CLI.</summary>
    public virtual void OnCreated(AnimationGraphDomain domain, GraphNodeRecord record) { }

    /// <summary>Fills in the node's card. By default its settings, the entries of its repeating pins, then its toggles.</summary>
    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        card.AddSettings();
        card.AddEntries();
        card.AddToggles();
    }

    /// <summary>Draws the node in the inspector. By default its description, its settings, then the entries of its repeating pins.</summary>
    public override void DrawInspector(AnimationNodeInspector inspector, GraphNodeRecord record)
    {
        inspector.Description();
        inspector.Settings();
        inspector.Entries();
    }

    private static string AssetName(NodeValue value) => value.Kind switch
    {
        NodeValueKind.Clip when value.Clip is { } clip && clip.IsValid() => clip.Name,
        NodeValueKind.Graph when value.Graph is { } graph && graph.IsValid() => graph.Name,
        NodeValueKind.Mask when value.Mask is { } mask && mask.IsValid() => mask.Name,
        _ => string.Empty,
    };
}

/// <summary>The animation graph a node editor works on, and what it can do to it through the window showing it.</summary>
public sealed class AnimationGraphEditing : GraphEditing<GraphNodeRecord>
{
    internal AnimationGraphEditing(AnimationGraphView view) : base(view) => View = view;

    internal new AnimationGraphView View { get; }

    internal Action<GraphNodeRecord>? EnterStatesHandler;
    internal Action<GraphNodeRecord>? OpenSubGraphHandler;
    internal Action<string, string, Action<AnimationGraph>>? ExtractHandler;
    internal Action<Paper, GraphNodeRecord, NodeSetting>? PropertyHandler;

    public AnimationGraph? Graph => View.Graph;

    public AnimationGraphDomain? Animation => View.Animation;

    private AnimationGraphProbe? Probe => View.Probe as AnimationGraphProbe;

    /// <summary>What a repeating input is called: the node feeding it, or the pin and its place in the list.</summary>
    public string InputLabel(GraphNodeRecord record, int pin)
        => AnimationNodeRegistry.Get(record.Type) is { } type && Animation is { } domain ? domain.InputLabel(record, type, pin) : string.Empty;

    /// <summary>What a node is doing in the running game, or empty when nothing runs.</summary>
    public string LiveValue(GraphNodeRecord record) => View.Probe?.Read(record.Id)?.Value ?? string.Empty;

    /// <summary>A declared parameter's value in the running game, or empty when nothing runs or none is declared by that name.</summary>
    public string LiveParameter(string name)
    {
        if (Probe == null || Graph.IsNotValid()) return string.Empty;
        GraphParameterRecord? declared = Graph!.Parameters.Find(p => p.Name == name);
        return declared == null ? string.Empty : Probe.ReadParameter(declared);
    }

    /// <summary>A number node's value in the running game, or null when nothing runs.</summary>
    public float? LiveNumber(GraphNodeRecord record) => Probe?.ReadNumber(record);

    /// <summary>The state a machine is in while the game runs, as an index into its states, or -1.</summary>
    public int ActiveState(GraphNodeRecord machine) => Probe?.ActiveState(Graph, machine) ?? -1;

    /// <summary>Opens a state machine node's states.</summary>
    public void EnterStates(GraphNodeRecord machine) => EnterStatesHandler?.Invoke(machine);

    /// <summary>Opens the graph a sub graph node runs.</summary>
    public void OpenSubGraph(GraphNodeRecord record) => OpenSubGraphHandler?.Invoke(record);

    /// <summary>Moves the nodes an owner holds into a new graph asset, then hands the asset over to use in their place.</summary>
    public void ExtractToAsset(string owner, string name, Action<AnimationGraph> use) => ExtractHandler?.Invoke(owner, name, use);

    /// <summary>One setting of a node in the inspector, with the widget its kind calls for.</summary>
    public void DrawProperty(Paper paper, GraphNodeRecord record, NodeSetting setting) => PropertyHandler?.Invoke(paper, record, setting);

    /// <summary>A plan of a graph a sub graph node runs, for drawing as a thumbnail.</summary>
    internal AnimationGraphView InnerView(AnimationGraph inner, string scope = "") => View.InnerView(inner, scope);

    /// <summary>The parameter a parameter node reads. Fixed when the node is made, so it is shown, not edited.</summary>
    public void DrawParameterPicker(Paper paper, string id, string name)
    {
        if (Graph.IsNotValid()) return;

        Scribe.FontFile font = EditorTheme.DefaultFont!;
        GraphParameterRecord? declared = Graph!.Parameters.Find(p => p.Name == name);
        bool isVirtual = declared == null && AnimationGraphDomain.VirtualParameters(Graph).Contains(name);

        string text = declared != null ? $"{name}  ({AnimationGraphWindow.KindName(declared)})"
            : isVirtual ? $"{name}  (Virtual)"
            : $"{name} (missing)";

        EditorGUI.Row(paper, id, "Parameter", () =>
            paper.Box($"{id}_v").Width(UnitValue.Stretch()).Height(Origami.Current.Metrics.RowHeight).IsNotInteractable()
                .Text(text, font)
                .TextColor(declared == null && !isVirtual ? EditorTheme.Red400 : EditorTheme.Ink600)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));
    }
}

/// <summary>The rows an animation node's card and inspector are built from: its settings, its toggles and its repeating entries.</summary>
public static class AnimationNodeRows
{
    private static AnimationGraphNode NodeOf(NodeEditor<GraphNodeRecord, AnimationGraphEditing> editor)
        => ((AnimationNodeEditor)editor).Node;

    /// <summary>Adds a row for each setting that is shown and not exposed as a port, other than the toggles.</summary>
    public static void AddSettings(this AnimationNodeCard card)
    {
        foreach (NodeSetting property in NodeOf(card.Editor).Properties)
            if (property.Kind != NodeValueKind.Flag && ShowsOnCard(card, property)) card.AddSetting(property);
    }

    /// <summary>Adds a row editing one setting, with the widget its kind calls for.</summary>
    public static void AddSetting(this AnimationNodeCard card, NodeSetting property)
    {
        float height = property.Kind == NodeValueKind.Curve ? 50f : AnimationNodeCard.RowHeight;
        card.Add(height, (paper, id) => AnimationNodeFields.DrawProperty(card.Editing, paper, id, card.Record, property));
    }

    /// <summary>Adds one row of chips for the on and off settings.</summary>
    public static void AddToggles(this AnimationNodeCard card)
    {
        var flags = new List<NodeSetting>();
        foreach (NodeSetting property in NodeOf(card.Editor).Properties)
            if (property.Kind == NodeValueKind.Flag && ShowsOnCard(card, property)) flags.Add(property);
        if (flags.Count > 0) card.Add(22f, (paper, id) => AnimationNodeFields.DrawToggles(card.Editing, paper, id, card.Record, flags));
    }

    /// <summary>Adds a row for each repetition of the repeating pins, when the editor has entries to draw.</summary>
    public static void AddEntries(this AnimationNodeCard card)
    {
        AnimationGraphNode node = NodeOf(card.Editor);
        int stride = node.VariadicStride;
        if (!card.Editor.HasEntries || stride <= 0) return;

        for (int pin = node.VariadicStart; pin < card.Record.Inputs.Count; pin += stride)
        {
            int entry = pin;
            card.Add(card.Editor.EntryHeight, (paper, id) =>
            {
                AnimationNodeCard.Label(paper, id + "_l", card.Editing.InputLabel(card.Record, entry));
                card.Editor.DrawEntry(card.Editing, paper, id, card.Record, entry);
            });
        }
    }

    // An exposed setting is a port now, and the port is where it is set.
    private static bool ShowsOnCard(AnimationNodeCard card, NodeSetting property)
        => !card.Record.PropertyInputs.ContainsKey(property.Key) && NodeOf(card.Editor).ShowsProperty(card.Record, property.Key);

    /// <summary>The node type's description, as a note.</summary>
    public static void Description(this AnimationNodeInspector inspector)
    {
        AnimationGraphNode node = NodeOf(inspector.Editor);
        if (node.Description.Length > 0) inspector.Note("ag_desc", node.Description);
    }

    /// <summary>A row for each setting that is shown, other than a list another list draws beside its own.</summary>
    public static void Settings(this AnimationNodeInspector inspector)
    {
        AnimationGraphNode node = NodeOf(inspector.Editor);
        foreach (NodeSetting property in node.Properties)
            if (!IsPairedInto(node, property.Key) && node.ShowsProperty(inspector.Record, property.Key)) inspector.Setting(property);
    }

    /// <summary>A row editing one setting, with the widget its kind calls for.</summary>
    public static void Setting(this AnimationNodeInspector inspector, NodeSetting property)
        => inspector.Editing.DrawProperty(inspector.Paper, inspector.Record, property);

    /// <summary>A row for each repetition of the repeating pins, when the editor has entries to draw.</summary>
    public static void Entries(this AnimationNodeInspector inspector, string? header = "Inputs")
    {
        AnimationGraphNode node = NodeOf(inspector.Editor);
        int stride = node.VariadicStride;
        if (!inspector.Editor.HasEntries || stride <= 0 || inspector.Record.Inputs.Count <= node.VariadicStart) return;

        if (header != null) inspector.Header("ag_h_inputs", header);
        for (int pin = node.VariadicStart, entry = 0; pin < inspector.Record.Inputs.Count; pin += stride, entry++)
        {
            int at = pin;
            string id = $"ag_in_{entry}";
            inspector.Row(id, inspector.Editing.InputLabel(inspector.Record, at),
                () => inspector.Editor.DrawEntry(inspector.Editing, inspector.Paper, id + "_v", inspector.Record, at));
        }
    }

    private static bool IsPairedInto(AnimationGraphNode node, string name)
    {
        foreach (NodeSetting property in node.Properties)
            if (property is TextSetting { PairedWith: { } paired } && paired.Key == name) return true;
        return false;
    }
}

/// <summary>The widgets that edit an animation node's settings, on its card and in the inspector.</summary>
public static class AnimationNodeFields
{
    /// <summary>Gives a setting its own port, or takes it away and leaves the value that was typed.</summary>
    public static void Expose(GraphNodeRecord record, NodeSetting property, bool exposed)
    {
        if (exposed) record.PropertyInputs[property.Key] = string.Empty;
        else record.PropertyInputs.Remove(property.Key);
    }

    /// <summary>A label at the start of a row.</summary>
    public static void Label(Paper paper, string id, string text) => AnimationNodeCard.Label(paper, id, text);

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
        NodeValue value = AnimationGraphDomain.ValueOf(record, setting);
        string edit = "Set " + setting.Label;

        if (setting is TextSetting { Bone: true, List: false })
        {
            BoneField(paper, id, value.Text, v => editing.Edit(edit, () => value.Text = v));
            return;
        }

        switch (setting.Kind)
        {
            case NodeValueKind.Number:
                NumericFieldBuilder<float> number = Origami.NumericField<float>(paper, id, value.Number, v => editing.Edit(edit, () => value.Number = v, false));
                if (dragLabel != null) number.DraggableLabel(dragLabel, EditorTheme.Ink400);
                number.Width(UnitValue.Stretch()).Height(AnimationNodeCard.RowHeight).Show();
                return;

            case NodeValueKind.Integer:
                NumericFieldBuilder<int> integer = Origami.NumericField<int>(paper, id, value.Integer, v => editing.Edit(edit, () => value.Integer = v, false));
                if (dragLabel != null) integer.DraggableLabel(dragLabel, EditorTheme.Ink400);
                integer.Width(UnitValue.Stretch()).Height(AnimationNodeCard.RowHeight).Show();
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
                        .Width(UnitValue.Stretch()).CommitOnEnter().Show();
                }
                return;
        }
    }

    /// <summary>An asset setting's picker, with its own label.</summary>
    public static void AssetField(AnimationGraphEditing editing, Paper paper, string id, string label, GraphNodeRecord record, NodeSetting setting)
    {
        NodeValue value = AnimationGraphDomain.ValueOf(record, setting);
        switch (setting.Kind)
        {
            case NodeValueKind.Clip: AssetField(paper, id, label, value.Clip, v => editing.Edit("Set Clip", () => value.Clip = v)); break;
            case NodeValueKind.Mask: AssetField(paper, id, label, value.Mask, v => editing.Edit("Set Mask", () => value.Mask = v)); break;
            case NodeValueKind.Avatar: AssetField(paper, id, label, value.Avatar, v => editing.Edit("Set Avatar", () => value.Avatar = v)); break;
            case NodeValueKind.Graph: AssetField(paper, id, label, value.Graph, v => editing.Edit("Set Graph", () => value.Graph = v)); break;
        }
    }

    public static void AssetField<T>(Paper paper, string id, string label, T? value, Action<T?> setter) where T : Asset
    {
        EngineObjectPropertyEditor.SetFieldType(typeof(T));
        new EngineObjectPropertyEditor().OnGUI(paper, id, label, value, picked => setter(picked as T), 0);
    }

    /// <summary>The on and off settings of a node, as chips: the Loop of a clip, the Mirror of a blend.</summary>
    public static void DrawToggles(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, IReadOnlyList<NodeSetting> flags)
    {
        for (int i = 0; i < flags.Count; i++)
        {
            NodeValue value = AnimationGraphDomain.ValueOf(record, flags[i]);
            string name = flags[i].Key;
            EditorGUI.Chip(paper, $"{id}_{i}", name, value.Flag, () => editing.Edit("Toggle " + name, () => value.Flag = !value.Flag, false));
        }
    }

    // Every humanoid bone, then Custom, which names a bone of the rig directly.
    private static readonly string[] BoneChoices = BuildBoneChoices();
    private static readonly int CustomBone = BoneChoices.Length - 1;

    private static string[] BuildBoneChoices()
    {
        HumanBodyBone[] bones = Enum.GetValues<HumanBodyBone>();
        string[] choices = new string[bones.Length + 1];
        for (int i = 0; i < bones.Length; i++)
            choices[i] = System.Text.RegularExpressions.Regex.Replace(bones[i].ToString(), "(?<=[a-z])(?=[A-Z])", " ");
        choices[^1] = "Custom";
        return choices;
    }

    /// <summary>The bone a new entry starts on.</summary>
    public static string DefaultBone => GraphCompileContext.HumanoidBonePrefix + nameof(HumanBodyBone.Hips);

    /// <summary>
    /// One bone: a humanoid bone, found through whatever rig the animator plays on, or Custom, a bone
    /// named directly. Humanoid bones are stored as "@LeftFoot".
    /// </summary>
    public static void BoneField(Paper paper, string id, string value, Action<string> setter)
    {
        int selected = value.Length > 1 && value[0] == GraphCompileContext.HumanoidBonePrefix
            && Enum.TryParse(value[1..], true, out HumanBodyBone bone) ? (int)bone : CustomBone;

        using (paper.Row($"{id}_row").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
        {
            Origami.Dropdown(paper, id, selected, v => setter(v == CustomBone ? "" : GraphCompileContext.HumanoidBonePrefix + ((HumanBodyBone)v).ToString()), BoneChoices)
                .Searchable("Search bones...").Width(UnitValue.Stretch()).Show();

            if (selected == CustomBone)
                Origami.TextField(paper, $"{id}_name", value, setter).Placeholder("bone name").Width(UnitValue.Stretch()).CommitOnEnter().Show();
        }
    }
}
