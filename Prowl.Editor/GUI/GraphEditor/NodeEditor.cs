// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GraphEditor;

/// <summary>Marks a node editor as the one for a node type, and for the types deriving from it.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class NodeEditorAttribute(Type nodeType) : Attribute
{
    public Type NodeType { get; } = nodeType;
}

/// <summary>One editor instance per node type for a tool, created from the registry or the tool's default.</summary>
public sealed class NodeEditorCache<TNode, TEditor> where TNode : class where TEditor : class
{
    private readonly Dictionary<TNode, TEditor> _editors = new(ReferenceEqualityComparer.Instance);
    private readonly Func<TEditor> _fallback;
    private readonly Action<TEditor, TNode> _bind;

    public NodeEditorCache(Func<TEditor> fallback, Action<TEditor, TNode> bind)
    {
        _fallback = fallback;
        _bind = bind;
        NodeEditorCaches.Register(_editors.Clear);
    }

    public TEditor For(TNode node)
    {
        if (_editors.TryGetValue(node, out TEditor? editor)) return editor;

        editor = EditorRegistries.CreateNodeEditor<TEditor>(node.GetType()) ?? _fallback();
        _bind(editor, node);
        _editors[node] = editor;
        return editor;
    }
}

/// <summary>Every tool's node editor cache, emptied together so editors from a reloaded assembly are created fresh.</summary>
internal static class NodeEditorCaches
{
    private static readonly List<Action> s_clears = [];

    internal static void Register(Action clear) { lock (s_clears) s_clears.Add(clear); }

    internal static void ClearAll()
    {
        lock (s_clears)
            foreach (Action clear in s_clears) clear();
    }
}

/// <summary>The graph a node editor works on, and what it can do to it through the window showing it.</summary>
public class GraphEditing<TRecord> where TRecord : class
{
    public GraphEditing(GraphView<TRecord> view) => View = view;

    public GraphView<TRecord> View { get; }

    public GraphDomain<TRecord>? Domain => View.Records;

    internal Action<string, Action, bool>? EditHandler;

    /// <summary>Makes a change undoable. A rebuild redraws every card after, for a change to what cards show.</summary>
    public void Edit(string description, Action change, bool rebuild = true)
    {
        if (EditHandler != null) EditHandler(description, change, rebuild);
        else change();
    }

    public TRecord? RecordOf(string nodeId) => View.RecordOf(nodeId);
}

/// <summary>How a graph editor shows one kind of node. Each tool derives its own base from this.</summary>
public abstract class NodeEditor<TRecord, TEditing> where TRecord : class where TEditing : GraphEditing<TRecord>
{
    /// <summary>The icon on the node's header.</summary>
    public virtual IOrigamiIcon? Icon => null;

    /// <summary>What hovering the node's header says.</summary>
    public virtual string Tooltip => string.Empty;

    /// <summary>The name a node takes from how it is set, or empty for none.</summary>
    public virtual string AutoName(TRecord record) => string.Empty;

    /// <summary>The line on a card that has no settings of its own to show.</summary>
    public virtual string Summary(TRecord record) => string.Empty;

    /// <summary>What the node's live badge shows while the game runs, given what the running graph reports.</summary>
    public virtual string LiveText(TEditing editing, TRecord record, string reported) => reported;

    /// <summary>Opens what the node holds, on a double click or from its menu. Returns false when there is nothing to open.</summary>
    public virtual bool Open(TEditing editing, TRecord record) => false;

    /// <summary>Adds the node's own items to its right click menu.</summary>
    public virtual void AddMenuItems(TEditing editing, TRecord record, ContextBuilder menu) { }

    /// <summary>Reports what is wrong with a node beyond what every node is checked for.</summary>
    public virtual void Validate(TEditing editing, TRecord record, NodeProblems problems) { }

    /// <summary>Fills in the node's card.</summary>
    public virtual void BuildCard(NodeCard<TRecord, TEditing> card, TRecord record) { }

    /// <summary>Draws the node in the inspector.</summary>
    public virtual void DrawInspector(NodeInspector<TRecord, TEditing> inspector, TRecord record) { }

    /// <summary>Whether each repetition of the node's repeating inputs has settings of its own, drawn by <see cref="DrawEntry"/>.</summary>
    public virtual bool HasEntries => false;

    /// <summary>The height of one entry on the card.</summary>
    public virtual float EntryHeight => Origami.Current.Metrics.RowHeight;

    /// <summary>The settings kept on one repetition of the repeating inputs, such as a layer's weight, beside its label.</summary>
    public virtual void DrawEntry(TEditing editing, Paper paper, string id, TRecord record, int pin) { }
}

/// <summary>A node's card as its editor fills it in: fixed height rows of widgets under the header.</summary>
public class NodeCard<TRecord, TEditing> where TRecord : class where TEditing : GraphEditing<TRecord>
{
    private const float LabelWidth = 64f;

    private readonly List<GraphNodeRow> _rows = [];

    public NodeCard(GraphNode widget, TRecord record, NodeEditor<TRecord, TEditing> editor, TEditing editing)
    {
        Widget = widget;
        Record = record;
        Editor = editor;
        Editing = editing;
    }

    /// <summary>The card as the graph widget draws it, for its title, width or ports.</summary>
    public GraphNode Widget { get; }

    public TRecord Record { get; }
    public NodeEditor<TRecord, TEditing> Editor { get; }
    public TEditing Editing { get; }

    /// <summary>The card's width while it has rows, or null for the usual width.</summary>
    public float? Width { get; set; }

    public static float RowHeight => Origami.Current.Metrics.RowHeight;

    /// <summary>Adds a row, drawn with the paper and an id unique to it.</summary>
    public void Add(float height, Action<Paper, string> draw) => _rows.Add(new GraphNodeRow(height, ctx => draw(ctx.Paper, "c")));

    /// <summary>Adds a button that opens what the node holds.</summary>
    public void AddOpenButton(string label)
        => Add(24f, (paper, id) => Origami.Button(paper, id, label, () => Editor.Open(Editing, Record))
            .Subtle().Width(UnitValue.Stretch()).Height(24).Show());

    /// <summary>Gives the widget the card's rows and width. Returns false for a card with nothing on it.</summary>
    public bool Apply()
    {
        if (_rows.Count == 0) return false;

        Widget.Width = Width ?? 212f;
        Widget.Rows.AddRange(_rows);
        return true;
    }

    /// <summary>A label at the start of a row.</summary>
    public static void Label(Paper paper, string id, string text)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        paper.Box(id).Width(LabelWidth).Height(UnitValue.Stretch()).IsNotInteractable()
            .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate();
    }
}

/// <summary>A selected node in the inspector, as its editor draws it.</summary>
public class NodeInspector<TRecord, TEditing> where TRecord : class where TEditing : GraphEditing<TRecord>
{
    public NodeInspector(Paper paper, TRecord record, NodeEditor<TRecord, TEditing> editor, TEditing editing)
    {
        Paper = paper;
        Record = record;
        Editor = editor;
        Editing = editing;
    }

    public Paper Paper { get; }
    public TRecord Record { get; }
    public NodeEditor<TRecord, TEditing> Editor { get; }
    public TEditing Editing { get; }

    public void Header(string id, string title) => EditorGUI.SectionHeader(Paper, id, title);

    public void Row(string id, string label, Action draw) => EditorGUI.Row(Paper, id, label, draw);

    public void Note(string id, string text) => EditorGUI.Note(Paper, id, text);

    public void Button(string id, string label, Action onClick)
        => EditorGUI.PillButton(Paper, id, label, active: true, onClick).Margin(0, 0, Origami.Current.Metrics.Spacing, Origami.Current.Metrics.Spacing);
}
