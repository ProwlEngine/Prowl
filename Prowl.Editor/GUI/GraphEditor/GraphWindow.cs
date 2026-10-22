// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GraphEditor;

/// <summary>What the editor selection and the inspector need from a graph window, whatever its records are.</summary>
public interface IGraphWindow
{
    /// <summary>Draws a selected piece of the graph in the inspector, editing through the window's undo.</summary>
    void DrawInspector(Paper paper, object item);

    /// <summary>What a selected piece of the graph is called.</summary>
    string Describe(object item);
}

/// <summary>
/// One piece of a graph in the editor's selection: a node, or whatever else the tool selects, such as a
/// parameter. The inspector draws it through the window that selected it.
/// </summary>
public sealed class GraphEditorSelection(IGraphWindow window, object item) : IEquatable<GraphEditorSelection>
{
    public IGraphWindow Window { get; } = window;
    public object Item { get; } = item;

    public bool Equals(GraphEditorSelection? other)
        => other != null && ReferenceEquals(Window, other.Window) && Equals(Item, other.Item);

    public override bool Equals(object? obj) => obj is GraphEditorSelection other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Window, Item);

    public override string ToString() => Window.Describe(Item);
}

/// <summary>Shows a selected piece of a graph in the inspector, drawn by the window it came from.</summary>
[CustomEditor(typeof(GraphEditorSelection))]
internal sealed class GraphEditorSelectionEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        if (target is GraphEditorSelection selection) selection.Window.DrawInspector(paper, selection.Item);
    }
}

/// <summary>The open windows and clipboard of each graph tool, kept outside the generic window so a reload carries them over.</summary>
internal static class GraphWindowRegistry
{
    private static readonly Dictionary<Type, object> s_windows = [];
    private static readonly Dictionary<Type, object?> s_clipboards = [];

    public static List<TWindow> Windows<TWindow>()
    {
        if (s_windows.TryGetValue(typeof(TWindow), out object? list)) return (List<TWindow>)list;

        var windows = new List<TWindow>();
        s_windows[typeof(TWindow)] = windows;
        return windows;
    }

    public static object? Clipboard<TWindow>() => s_clipboards.TryGetValue(typeof(TWindow), out object? fragment) ? fragment : null;

    public static void SetClipboard<TWindow>(object? fragment) => s_clipboards[typeof(TWindow)] = fragment;
}

/// <summary>
/// The window every graph tool shares: one asset per window, snapshot undo, saving, the editor selection,
/// shortcuts, the toolbar and breadcrumb, menus, the clipboard, the create picker and the side panel. A
/// tool derives it, hands it a domain for its asset and draws its own side panel section.
/// </summary>
public abstract class GraphWindow<TWindow, TRecord> : DockPanel, IGraphWindow
    where TWindow : GraphWindow<TWindow, TRecord>, new()
    where TRecord : class
{
    /// <summary>Every window of this tool currently open, so opening a graph can reuse one instead of stacking up.</summary>
    private static List<TWindow> OpenWindows => GraphWindowRegistry.Windows<TWindow>();

    /// <summary>Copied nodes, shared by every window of this tool so a copy in one graph pastes into another.</summary>
    private static GraphFragment<TRecord>? Clipboard
    {
        get => GraphWindowRegistry.Clipboard<TWindow>() as GraphFragment<TRecord>;
        set => GraphWindowRegistry.SetClipboard<TWindow>(value);
    }

    /// <summary>The graphs opened on the way here through nodes that run other assets, for the breadcrumb back.</summary>
    private readonly Stack<Guid> _parents = new();

    private Guid _assetGuid;
    private Asset? _asset;
    private GraphDomain<TRecord>? _domain;

    /// <summary>A locked window keeps its graph, so another one opens somewhere else.</summary>
    private bool _locked;

    /// <summary>The latest edits, so entering play mode does not quietly throw them away.</summary>
    private EchoObject? _unsaved;

    /// <summary>True while this window is writing the editor selection, so it does not answer itself.</summary>
    private bool _publishing;

    private bool _dirty;

    // The asset's content when the view was last built from it, so a refill (a reimport, a save, a revert) rebuilds it.
    private int _seenContent = -1;

    /// <summary>Where the graph canvas is on screen, for taking something dropped onto it.</summary>
    private Rect _graphRect;
    private float _rootX, _rootY, _rootW, _rootH;

    static GraphWindow()
    {
        SaveManager.OnSave += () =>
        {
            var saved = new List<string>();
            string tool = "";
            foreach (TWindow window in OpenWindows)
            {
                if (!window._dirty || window._asset.IsNotValid()) continue;
                string name = window._asset!.Name;
                tool = window.ToolName;
                window.Save();
                if (!window._dirty) saved.Add(name);
            }
            return saved.Count == 0 ? null : $"{tool}: " + string.Join(", ", saved);
        };
    }

    protected GraphWindow()
    {
        OpenWindows.Add((TWindow)this);
        Selection.OnSelectionChanged += OnEditorSelectionChanged;
    }

    protected readonly NodeGraphController Controller = new();

    protected Guid AssetGuid => _assetGuid;
    protected Asset? Asset => _asset;
    protected GraphDomain<TRecord>? Domain => _domain;
    protected bool Dirty => _dirty;

    /// <summary>Where the window's content sits on screen, as of the last frame.</summary>
    protected Rect RootRect => new(_rootX, _rootY, _rootX + _rootW, _rootY + _rootH);

    /// <summary>Which graph inside the asset is shown: empty for the root, or an owner's id.</summary>
    protected string Scope { get; set; } = string.Empty;

    /// <summary>The dataflow canvas.</summary>
    protected abstract GraphView<TRecord> View { get; }

    /// <summary>The canvas showing right now. The dataflow view unless the tool opens another.</summary>
    protected virtual GraphCanvas Canvas => View;

    /// <summary>Every canvas the window keeps in step with the records.</summary>
    protected virtual IEnumerable<GraphCanvas> Canvases => [View];

    protected virtual IGraphProbe? Probe => null;

    /// <summary>What the tool calls a graph, for the title and the save message.</summary>
    protected abstract string ToolName { get; }

    protected abstract string EmptyText { get; }

    protected virtual IOrigamiIcon AssetIcon => EditorIcons.DiagramProject_I;

    /// <summary>Loads the asset, for opening it.</summary>
    protected abstract Asset? LoadAsset(Guid guid);

    /// <summary>The asset if it is loaded, for an undo landing on a graph no window shows.</summary>
    protected abstract Asset? LoadedAsset(Guid guid);

    protected abstract GraphDomain<TRecord> CreateDomain(Asset asset);

    public override string Title => _asset.IsValid() ? $"{ToolName}: {_asset!.Name}{(_dirty ? " *" : "")}" : ToolName;

    public override void OnClosed()
    {
        OpenWindows.Remove((TWindow)this);
        Selection.OnSelectionChanged -= OnEditorSelectionChanged;
        ClearSelection();

        // Another window on the same graph carries the unsaved edits on, otherwise they are saved here.
        if (_dirty && !OpenWindows.Exists(other => other._assetGuid == _assetGuid)) Save();
    }

    /// <summary>Shows a graph in the first window not locked to something else.</summary>
    public static void OpenFor(Guid assetGuid)
    {
        foreach (TWindow window in OpenWindows)
        {
            if (window._locked) continue;

            window._parents.Clear();
            if (window._assetGuid != assetGuid) window.SwitchTo(assetGuid);
            EditorApplication.Instance?.FocusPanelInstance(window);
            return;
        }

        var panel = new TWindow();
        panel.Load(assetGuid);
        EditorApplication.Instance?.OpenPanelInstance(panel, 1240, 780);
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_assetGuid == Guid.Empty) return false;
        state["graph"] = _assetGuid.ToString();
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        if (state["graph"]?.GetValue<string>() is { } text && Guid.TryParse(text, out Guid guid)) Load(guid);
    }

    /// <summary>Moves the window to another graph, saving unsaved edits first.</summary>
    private void SwitchTo(Guid assetGuid)
    {
        if (_dirty) Save();
        Load(assetGuid);
    }

    private void Load(Guid assetGuid)
    {
        _assetGuid = assetGuid;
        _asset = null;
        _domain = null;
        _seenContent = -1;
        _dirty = false;
        _unsaved = null;
        Scope = string.Empty;
        Resolve(keepPlace: false);
    }

    /// <summary>Loads the graph, and rebuilds the view after its content was refilled, carrying unsaved edits onto it.</summary>
    private void Resolve(bool keepPlace = true)
    {
        if (_assetGuid == Guid.Empty) return;
        if (_asset is { IsLoaded: true } current && current.ContentVersion == _seenContent) return;

        List<string> selected = keepPlace ? new List<string>(Controller.SelectedNodes) : [];

        _asset = LoadAsset(_assetGuid);
        if (_asset is not { IsLoaded: true }) _asset = null;
        _seenContent = _asset is null ? -1 : _asset.ContentVersion;
        _domain = _asset is null ? null : CreateDomain(_asset);
        View.Records = _domain;
        ClearSelection();

        if (_domain != null && _dirty && _unsaved != null) _domain.Restore(_unsaved, compiles: true);
        Reattach(keepPlace);
        Reselect(selected);
    }

    /// <summary>Finds where the window was again after the records were replaced, or steps back to the root.</summary>
    protected virtual void Reattach(bool keepPlace)
    {
        if (!ScopeExists(Scope)) Scope = string.Empty;
        foreach (GraphCanvas canvas in Canvases) canvas.Invalidate();
    }

    /// <summary>Whether a scope still names something in the asset.</summary>
    protected virtual bool ScopeExists(string scope) => scope.Length == 0 || _domain?.Find(scope) != null;

    private void Reselect(List<string> ids)
    {
        if (ids.Count > 0) Controller.SelectNodes(ids);
        else Controller.ClearSelection();
    }

    /// <summary>Redraws other windows showing the same graph and shares the unsaved state with them.</summary>
    private void ShareWithSiblings()
    {
        foreach (TWindow other in OpenWindows)
        {
            if (ReferenceEquals(other, this) || other._assetGuid != _assetGuid) continue;
            other._dirty = _dirty;
            other._unsaved = _unsaved;
            other.Reattach(keepPlace: true);
        }
    }

    // Selection

    protected void ClearSelection()
    {
        if (OwnsSelection()) Selection.Clear();
    }

    private bool OwnsSelection()
    {
        foreach (object selected in Selection.Selected)
            if (selected is GraphEditorSelection mine && ReferenceEquals(mine.Window, this)) return true;
        return false;
    }

    protected bool IsSelected(object item) => Selection.IsSelected(new GraphEditorSelection(this, item));

    /// <summary>Puts what is selected in the graph into the editor selection, so the inspector shows it.</summary>
    protected void Publish(List<object> picked)
    {
        _publishing = true;
        try
        {
            if (picked.Count == 0)
            {
                ClearSelection();
                return;
            }

            Selection.Select(new GraphEditorSelection(this, picked[0]));
            for (int i = 1; i < picked.Count; i++) Selection.AddToSelection(new GraphEditorSelection(this, picked[i]));
        }
        finally
        {
            _publishing = false;
        }
    }

    /// <summary>Something else was selected in the editor, so the graph lets go of its own selection.</summary>
    private void OnEditorSelectionChanged()
    {
        if (_publishing || OwnsSelection()) return;
        Controller.ClearSelection();
    }

    /// <summary>What the widget selected, as items for the editor selection.</summary>
    protected virtual void PublishSelection(GraphCanvas canvas, GraphSelection selection)
    {
        var picked = new List<object>();
        foreach (GraphNode node in selection.Nodes)
            if (View.RecordOf(node.Id) is { } record) picked.Add(record);
        Publish(picked);
    }

    void IGraphWindow.DrawInspector(Paper paper, object item)
    {
        if (_asset.IsNotValid() || _domain == null || !StillInGraph(item))
        {
            EditorGUI.Note(paper, "agi_gone", "This is no longer part of the graph.");
            return;
        }

        DrawInspector(paper, item);
    }

    string IGraphWindow.Describe(object item) => Describe(item);

    protected virtual string Describe(object item) => item is TRecord record && _domain != null ? _domain.TitleOf(record) : ToolName;

    protected virtual bool StillInGraph(object item) => item is TRecord record && _domain!.Nodes.Contains(record);

    protected abstract void DrawInspector(Paper paper, object item);

    // Edits and undo

    /// <summary>
    /// Runs an edit made from outside the window, such as by the CLI, through a window showing that graph, so it is
    /// undoable, drawn, and kept with any unsaved edits there. Saves when asked. False when no window shows the graph.
    /// </summary>
    public static bool TryEditOpen(Guid asset, string description, Action change, bool save)
    {
        TWindow? window = OpenWindows.Find(w => w._assetGuid == asset && w._asset.IsValid());
        if (window == null) return false;

        window.Edit(description, change);
        if (save) window.Save();
        return true;
    }

    /// <summary>Runs one edit and makes it undoable by snapshotting the whole graph.</summary>
    protected void Edit(string description, Action change, bool rebuild = true, bool compiles = true)
    {
        if (_domain == null) return;

        EchoObject before = _domain.Snapshot();
        change();
        EditDone(description, before, coalesce: false, rebuild, compiles);
    }

    // Where things sit, groups and notes: nothing the graph compiles, so whatever runs it keeps going.
    protected void EditLayout(string description, Action change) => Edit(description, change, compiles: false);

    /// <summary>A one field edit, made undoable and folded into the one before it. Pass rebuild false when no card shows the value.</summary>
    protected void Set(string description, Action change, bool rebuild = true)
    {
        if (_domain == null) return;

        EchoObject before = _domain.Snapshot();
        change();
        EditDone(description, before, coalesce: true, rebuild);
    }

    private void EditDone(string description, EchoObject before, bool coalesce, bool rebuild = true, bool compiles = true)
    {
        if (_domain == null) return;

        EchoObject after = _domain.Snapshot();
        _unsaved = after;

        // Tied to the asset the edit was made on, which may no longer be the one showing.
        Guid asset = _assetGuid;
        Func<Guid, GraphDomain?> open = guid => LoadedAsset(guid) is { } loaded ? CreateDomain(loaded) : null;
        void undo() => RestoreSnapshot(asset, before, compiles, open);
        void redo() => RestoreSnapshot(asset, after, compiles, open);

        if (coalesce) Undo.RegisterCoalescableAction(description, undo, redo);
        else Undo.RegisterAction(description, undo, redo);
        Touch(rebuild, compiles);
    }

    private static void RestoreSnapshot(Guid asset, EchoObject snapshot, bool compiles, Func<Guid, GraphDomain?> open)
    {
        // The undo goes to whichever open window shows that graph.
        TWindow? showing = OpenWindows.Find(window => window._assetGuid == asset);
        if (showing == null)
        {
            // Nothing shows the graph any more, and it was saved when it was left, so the undo is saved too.
            open(asset)?.Restore(snapshot, compiles);
            WriteToDisk(asset, snapshot);
            return;
        }

        if (!showing.Apply(snapshot, compiles)) return;

        showing._unsaved = snapshot;
        showing._dirty = true;
        showing.ShareWithSiblings();
    }

    /// <summary>Writes a snapshot's records onto the live asset, leaving the asset itself in place.</summary>
    private bool Apply(EchoObject snapshot, bool compiles)
    {
        if (_domain == null) return false;

        var selected = new List<string>(Controller.SelectedNodes);
        if (!_domain.Restore(snapshot, compiles)) return false;

        ClearSelection();
        Reattach(keepPlace: true);
        Reselect(selected);
        return true;
    }

    /// <summary>After any edit: drops the compiled graph, when the edit changes it, so whatever runs it picks it up.</summary>
    private void Touch(bool rebuild = true, bool compiles = true)
    {
        _dirty = true;
        if (compiles) _domain?.Invalidate();
        if (!rebuild) return;

        foreach (GraphCanvas canvas in Canvases) canvas.Invalidate();
        ShareWithSiblings();
    }

    protected void Save()
    {
        if (!_dirty || _domain == null || _asset.IsNotValid()) return;
        if (!WriteToDisk(_assetGuid, _domain.Snapshot())) return;

        _dirty = false;
        _unsaved = null;
        _seenContent = _asset!.ContentVersion;
        ShareWithSiblings();
    }

    private static bool WriteToDisk(Guid asset, EchoObject snapshot)
        => EditorAssetBackend.Instance?.SaveAsset(asset, snapshot) ?? false;

    // Drawing

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        OrigamiMetrics m = Origami.Current.Metrics;

        Resolve();
        HandleShortcuts(paper);

        if (_asset.IsNotValid() || _domain == null)
        {
            paper.Box("gw_empty").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Text(EmptyText, font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        // A graph that was deleted while it was open leaves nowhere to be.
        if (!ScopeExists(Scope)) GoToRoot();
        BeforeDraw();

        IGraphProbe? probe = Probe;
        probe?.Refresh(_asset);
        View.Probe = probe;
        View.ShowLive = probe?.Live ?? false;
        View.Scope = Scope;

        foreach (GraphCanvas canvas in Canvases) canvas.Sync();
        foreach (GraphCanvas canvas in Canvases) canvas.Tint();

        using (paper.Column("gw_root").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .OnPostLayout((h, r) =>
            {
                _rootX = (float)r.Min.X; _rootY = (float)r.Min.Y;
                _rootW = (float)r.Size.X; _rootH = (float)r.Size.Y;
            }).Enter())
        {
            DrawToolbar(paper, font, m);

            using (paper.Row("gw_body").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float panelW = Math.Clamp(width * 0.14f, 150f, 210f);
                DrawCanvas(paper, Canvas, width - panelW, height - 44f);
                DrawPanel(paper, font, m, panelW, height - 44f);
            }

            DrawOverlays(paper, font, m);
        }
    }

    /// <summary>Runs every frame before the canvases are synced, for a tool to step out of somewhere that is gone.</summary>
    protected virtual void BeforeDraw() { }

    /// <summary>Popups the tool draws over the window.</summary>
    protected virtual void DrawOverlays(Paper paper, Scribe.FontFile font, OrigamiMetrics m) { }

    /// <summary>True while a popup of the tool's own wants the keyboard.</summary>
    protected virtual bool PopupOpen => false;

    private void DrawToolbar(Paper paper, Scribe.FontFile font, OrigamiMetrics m)
    {
        using (paper.Row("gw_toolbar").Width(UnitValue.Stretch()).Height(40)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.SpacingMedium)
            .BackgroundColor(EditorTheme.Neutral200)
            .AlignItems(LayoutAlignment.Center).Enter())
        {
            Origami.Breadcrumb(paper, "gw_path", Trail(), OnCrumb).ShowIcons().HighlightLast().Show();
            paper.Box("gw_gap").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            DrawWatch(paper, font);
            DrawToolButtons(paper);
            EditorGUI.PillButton(paper, "gw_add", "Add Node", false, () => OpenPicker(paper, NextFreeSpot(), null, null, frame: true));
            EditorGUI.PillButton(paper, "gw_frame", "Frame All", false, () => Controller.FrameAll());
            EditorGUI.PillButton(paper, "gw_lock", _locked ? "Locked" : "Lock", _locked, () => _locked = !_locked)
                .Tooltip("A locked window keeps this graph, so opening another one uses a new window");
            EditorGUI.PillButton(paper, "gw_save", _dirty ? "Save" : "Saved", _dirty, Save);
        }
    }

    /// <summary>Buttons the tool adds to the toolbar, before the shared ones.</summary>
    protected virtual void DrawToolButtons(Paper paper) { }

    /// <summary>Which runner the live readouts come from, when more than one runs this graph.</summary>
    private void DrawWatch(Paper paper, Scribe.FontFile font)
    {
        IGraphProbe? probe = Probe;
        if (probe == null || !Application.IsPlaying) return;

        if (probe.Candidates.Count == 0)
        {
            paper.Box("gw_watchNone").Width(UnitValue.Auto).Height(26)
                .Padding(10, 10, 0, 0).IsNotInteractable()
                .Text("nothing is running this graph", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        string[] names = [.. probe.Candidates];
        Origami.Dropdown(paper, "gw_watch", Math.Clamp(probe.Watched, 0, names.Length - 1), v => probe.Watched = v, names)
            .Width(160).Show();
    }

    /// <summary>The breadcrumb trail: the graph assets passed through, then each graph down to what is showing.</summary>
    private List<BreadcrumbItem> Trail()
    {
        var path = new List<BreadcrumbItem>();

        Guid[] parents = [.. _parents];
        for (int i = parents.Length - 1; i >= 0; i--)
        {
            Asset? graph = LoadedAsset(parents[i]);
            path.Add(new BreadcrumbItem(graph.IsValid() ? graph!.Name : ToolName, AssetIcon, parents[i]));
        }

        AddPlaces(path);
        return path;
    }

    /// <summary>The breadcrumb items inside the asset: its own graph, then each graph down to what is showing.</summary>
    protected virtual void AddPlaces(List<BreadcrumbItem> path)
        => path.Add(new BreadcrumbItem(_asset!.Name, AssetIcon, string.Empty));

    private void OnCrumb(BreadcrumbItem item)
    {
        if (item.UserData is Guid guid) { if (guid != _assetGuid) OpenParent(guid); }
        else if (item.UserData != null) GoTo(item.UserData);
    }

    /// <summary>Shows a place a breadcrumb item named. By default a scope.</summary>
    protected virtual void GoTo(object place)
    {
        if (place is string scope) Scope = ScopeExists(scope) ? scope : string.Empty;
        Controller.FrameAll();
    }

    protected virtual void GoToRoot() => GoTo(string.Empty);

    /// <summary>Opens another graph asset, with the way back on the breadcrumb.</summary>
    protected void OpenAsset(Guid guid)
    {
        if (guid == Guid.Empty || guid == _assetGuid) return;

        Guid from = _assetGuid;
        SwitchTo(guid);
        _parents.Push(from);
    }

    /// <summary>Steps back up the breadcrumb to a graph opened earlier.</summary>
    private void OpenParent(Guid guid)
    {
        while (_parents.Count > 0)
            if (_parents.Pop() == guid) break;
        SwitchTo(guid);
    }

    private void HandleShortcuts(Paper paper)
    {
        if (NodePicker.IsOpen || ContextMenu.IsOpen || PopupOpen || ShortcutManager.IsRebinding) return;

        float x = (float)paper.PointerPos.X, y = (float)paper.PointerPos.Y;
        if (x < _rootX || y < _rootY || x > _rootX + _rootW || y > _rootY + _rootH) return;

        if (ShortcutManager.IsPressed("GraphEditor/Delete")) Controller.DeleteSelection();
        else if (ShortcutManager.IsPressed("GraphEditor/SelectAll")) Controller.SelectAll();
        else if (ShortcutManager.IsPressed("GraphEditor/FrameSelection")) Controller.FrameSelection();
        else if (ShortcutManager.IsPressed("GraphEditor/Recenter")) Controller.FrameAll();
        else if (ShortcutManager.IsPressed("GraphEditor/GroupSelection")) GroupNodes(SelectedNodes());

        if (!Canvas.Copyable) return;

        if (ShortcutManager.IsPressed("GraphEditor/Copy")) CopyNodes(SelectedNodes());
        else if (ShortcutManager.IsPressed("GraphEditor/Paste")) PasteNodes();
        else if (ShortcutManager.IsPressed("GraphEditor/Duplicate")) DuplicateNodes(SelectedNodes());
    }

    protected static string Keys(string id)
        => ShortcutManager.GetBinding(id) is { } binding ? ShortcutManager.GetDisplayString(binding) : "";

    private void DrawCanvas(Paper paper, GraphCanvas canvas, float width, float height)
    {
        using (paper.Box("gw_graphHost").Width(width).Height(height).OnPostLayout((_, r) => _graphRect = r).Enter())
        {
            NodeGraphBuilder graph = Origami.NodeGraph(paper, "gw_" + canvas.GetType().Name, width, height)
                .Nodes(canvas.Nodes)
                .Connections(canvas.Wires)
                .Groups(canvas.Groups)
                .Stickies(canvas.Notes)
                .Controller(Controller)
                .Minimap()
                .SnapToGrid(12f)
                .HostShortcuts()
                .Arrows(canvas.Arrows)
                .OnSelectionChanged(selection => PublishSelection(canvas, selection))
                .OnNodesMoved((nodes, delta) => EditLayout(canvas.MoveText, () => canvas.Move(nodes, delta)))
                .OnValidateConnection(canvas.CanConnect)
                .OnConnect(request => Edit(canvas.ConnectText, () => canvas.Connect(request)))
                .OnDisconnect(wire => Edit(canvas.DisconnectText, () => canvas.Disconnect(wire)))
                .OnDeleteSelection(selection => Edit("Delete", () =>
                {
                    canvas.DeleteSelection(selection);
                    ClearSelection();
                }))
                .OnNodeToggleCollapsed(node => EditLayout("Collapse Node", () => canvas.ToggleCollapsed(node)))
                .OnNodeDoubleClick(node => NodeDoubleClick(canvas, node))
                .OnBackgroundContext(at => BackgroundMenu(paper, canvas, at))
                .OnNodeContext((node, _) => NodeMenu(paper, canvas, node))
                .OnGroupMoved((group, members, delta) => EditLayout("Move Group", () =>
                {
                    if (GraphCanvas.GroupOf(group) is { } record) record.Position += delta;
                    canvas.Move(members, delta);
                }))
                .OnGroupResized((group, position, size) => EditLayout("Resize Group", () =>
                {
                    if (GraphCanvas.GroupOf(group) is not { } record) return;
                    record.Position = position;
                    record.Size = size;
                }))
                .OnGroupRenamed((group, title) => EditLayout("Rename Group", () =>
                {
                    if (GraphCanvas.GroupOf(group) is { } record) record.Title = title;
                }))
                .OnGroupContext((group, _) => GroupMenu(paper, group))
                .OnStickyMoved((note, delta) => EditLayout("Move Note", () =>
                {
                    if (GraphCanvas.NoteOf(note) is { } record) record.Position += delta;
                }))
                .OnStickyResized((note, position, size) => EditLayout("Resize Note", () =>
                {
                    if (GraphCanvas.NoteOf(note) is not { } record) return;
                    record.Position = position;
                    record.Size = size;
                }))
                .OnStickyEdited((note, text) => EditLayout("Edit Note", () =>
                {
                    if (GraphCanvas.NoteOf(note) is { } record) record.Text = text;
                }))
                .OnStickyContext((note, _) => NoteMenu(paper, note));

            if (canvas.Copyable) graph.OnNodesContext((nodes, _) => NodesMenu(paper, nodes));
            if (ReferenceEquals(canvas, View)) graph.OnDropWireInEmpty((at, node, port, output) => DropWire(paper, at, node, port, output));
            graph.Show();

            AcceptDrop(paper);
        }
    }

    /// <summary>Something dragged in from elsewhere in the editor, let go over the canvas.</summary>
    private void AcceptDrop(Paper paper)
    {
        if (!DragDrop.IsDropFrame || DragDrop.Payload == null) return;

        Float2 pointer = new((float)paper.PointerPos.X, (float)paper.PointerPos.Y);
        if (pointer.X < _graphRect.Min.X || pointer.Y < _graphRect.Min.Y || pointer.X > _graphRect.Max.X || pointer.Y > _graphRect.Max.Y)
            return;

        Drop(DragDrop.Payload, Controller.ScreenToGraph(pointer));
    }

    /// <summary>A drag payload let go over the canvas at a graph position. Accept it with <c>DragDrop.AcceptDrop</c> to take it.</summary>
    protected virtual void Drop(DragPayload payload, Float2 at) { }

    protected virtual void NodeDoubleClick(GraphCanvas canvas, GraphNode node) { }

    // Menus

    protected virtual void BackgroundMenu(Paper paper, GraphCanvas canvas, Float2 at)
    {
        if (ReferenceEquals(canvas, View)) OpenPicker(paper, at, null, null);
    }

    protected virtual void NodeMenu(Paper paper, GraphCanvas canvas, GraphNode node)
    {
        if (!ReferenceEquals(canvas, View) || _domain == null || View.RecordOf(node.Id) is not { } record) return;
        GraphDomain<TRecord> domain = _domain;

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
        {
            b.Header(domain.TitleOf(record));
            bool copyable = domain.IsCopyable(record), removable = domain.IsRemovable(record);
            if (!copyable && !removable) return;

            AddNodeMenuItems(b, record);
            b.Separator();
            if (copyable)
            {
                b.Item("Copy", () => CopyNodes([node]), shortcut: Keys("GraphEditor/Copy"));
                b.Item("Duplicate", () => DuplicateNodes([node]), shortcut: Keys("GraphEditor/Duplicate"));
                b.Item("Group", () => GroupNodes([node]), shortcut: Keys("GraphEditor/GroupSelection"));
                b.Separator();
            }
            if (removable)
                b.Item("Delete", () => Edit("Delete Node", () =>
                {
                    View.Delete([node], []);
                    ClearSelection();
                }), danger: true);
        });
    }

    /// <summary>The tool's own items on a node's menu, above Copy, Duplicate, Group and Delete.</summary>
    protected virtual void AddNodeMenuItems(ContextBuilder menu, TRecord record) { }

    private void NodesMenu(Paper paper, IReadOnlyList<GraphNode> nodes)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Header($"{nodes.Count} nodes")
            .Item("Copy", () => CopyNodes(nodes), shortcut: Keys("GraphEditor/Copy"))
            .Item("Duplicate", () => DuplicateNodes(nodes), shortcut: Keys("GraphEditor/Duplicate"))
            .Item("Group", () => GroupNodes(nodes), shortcut: Keys("GraphEditor/GroupSelection"))
            .Separator()
            .Item("Delete", () => Edit("Delete Nodes", () =>
            {
                Canvas.Delete(nodes, []);
                ClearSelection();
            }), danger: true));
    }

    private void GroupMenu(Paper paper, GraphGroup group)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Header(group.Title)
            .Item("Delete Group", () => EditLayout("Delete Group", () =>
            {
                if (GraphCanvas.GroupOf(group) is { } record) _domain?.Groups.Remove(record);
            }), danger: true));
    }

    private void NoteMenu(Paper paper, GraphSticky note)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Header("Note")
            .Item("Delete Note", () => EditLayout("Delete Note", () =>
            {
                if (GraphCanvas.NoteOf(note) is { } record) _domain?.Notes.Remove(record);
            }), danger: true));
    }

    // Clipboard and grouping

    /// <summary>The selected cards of whichever canvas is showing.</summary>
    protected List<GraphNode> SelectedNodes()
    {
        var selected = new List<GraphNode>();
        foreach (GraphNode node in Canvas.Nodes)
            if (Controller.SelectedNodes.Contains(node.Id)) selected.Add(node);
        return selected;
    }

    private void CopyNodes(IReadOnlyList<GraphNode> nodes)
    {
        List<TRecord> records = View.RecordsOf(nodes);
        if (records.Count > 0 && _domain != null) Clipboard = _domain.Capture(records);
    }

    private void PasteNodes()
    {
        if (Clipboard == null || Clipboard.Nodes.Count == 0 || _domain == null) return;

        GraphDomain<TRecord> domain = _domain;
        List<TRecord> copies = [];
        Edit("Paste Nodes", () => copies = domain.Paste(Clipboard, new Float2(40f, 40f), Scope));
        SelectCopies(copies, frame: true);
    }

    private void DuplicateNodes(IReadOnlyList<GraphNode> nodes)
    {
        List<TRecord> records = View.RecordsOf(nodes);
        if (records.Count == 0 || _domain == null) return;

        GraphDomain<TRecord> domain = _domain;
        List<TRecord> copies = [];
        Edit(records.Count == 1 ? "Duplicate Node" : "Duplicate Nodes", () => copies = domain.Paste(domain.Capture(records), new Float2(28f, 28f), Scope));
        SelectCopies(copies, frame: false);
    }

    protected void GroupNodes(IReadOnlyList<GraphNode> nodes)
    {
        Float2 at = nodes.Count > 0 ? nodes[0].Position : NextFreeSpot();
        GraphCanvas canvas = Canvas;
        EditLayout("Group Nodes", () => canvas.AddGroup(nodes, at));
    }

    private void SelectCopies(List<TRecord> copies, bool frame)
    {
        if (copies.Count == 0 || _domain == null) return;

        var ids = new List<string>(copies.Count);
        foreach (TRecord copy in copies) ids.Add(_domain.IdOf(copy));

        Controller.SelectNodes(ids);
        if (frame) Controller.FrameSelection();
    }

    /// <summary>Where a node added from the toolbar goes: below everything already placed.</summary>
    protected Float2 NextFreeSpot()
    {
        if (View.Nodes.Count == 0) return new Float2(0f, 0f);

        float x = float.MaxValue, y = float.MinValue;
        foreach (GraphNode node in View.Nodes)
        {
            x = MathF.Min(x, node.Position.X);
            y = MathF.Max(y, node.Position.Y);
        }
        return new Float2(x, y + 140f);
    }

    // The create picker

    /// <summary>The end of a wire dropped in empty space: an output the new node takes in, or an input it feeds.</summary>
    private readonly record struct DroppedWire(string Node, string Port, bool FromOutput);

    private void DropWire(Paper paper, Float2 at, string fromNode, string fromPort, bool fromOutput)
    {
        if (_domain == null || View.RecordOf(fromNode) is not { } end) return;

        // Dropping a wire says what the new node has to fit, so the catalog is filtered to it.
        PortKind? kind = fromOutput ? _domain.OutputKind(end, fromPort) : _domain.InputPort(end, fromPort)?.Kind;
        OpenPicker(paper, at, kind, new DroppedWire(fromNode, fromPort, fromOutput));
    }

    private void OpenPicker(Paper paper, Float2 at, PortKind? kind, DroppedWire? wire, bool frame = false)
    {
        if (_domain == null || !ReferenceEquals(Canvas, View)) return;

        var types = new List<NodeTypeInfo>();
        foreach (NodeTypeInfo type in _domain.Catalog)
            if (!type.Hidden && (kind == null || Fits(type, kind, wire?.FromOutput ?? true))) types.Add(type);

        NodePicker.Show((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
        {
            b.Placeholder(kind != null ? $"{kind.Label} nodes..." : "Search nodes...");

            // The things that are not nodes, left out when a wire was dropped.
            if (wire == null && kind == null)
                b.Section("Organise")
                    .Item("Note", () => EditLayout("Add Note", () => View.AddNote(at)), "A note left on the canvas")
                    .Item("Group", () => Edit("Add Group", () => View.AddGroup(SelectedNodes(), at)), "A box around the selected nodes, or an empty one here");

            b.Items(types, t => t.Category, t => t.DisplayName, t => t.Description.Length > 0 ? t.Description : null, t => Create(t, at, wire, frame));
        });
    }

    /// <summary>Whether a node type can take the dropped wire.</summary>
    private bool Fits(NodeTypeInfo type, PortKind kind, bool fromOutput)
    {
        if (fromOutput)
        {
            foreach (PortKind input in type.InputKinds)
                if (_domain!.Accepts(input, kind)) return true;
            return false;
        }

        if (type.DynamicOutput) return true;
        foreach (PortKind output in type.OutputKinds)
            if (_domain!.Accepts(kind, output)) return true;
        return false;
    }

    /// <summary>Creates the picked node, wired straight into whatever the wire was dragged from.</summary>
    private void Create(NodeTypeInfo type, Float2 at, DroppedWire? wire, bool frame)
    {
        if (_domain == null) return;
        GraphDomain<TRecord> domain = _domain;

        Edit($"Add {type.DisplayName}", () =>
        {
            TRecord? record = View.Add(type.Id, at);
            if (record == null) return;

            string id = domain.IdOf(record);
            Controller.SelectNodes([id]);
            if (frame) Controller.FocusNode(id);

            if (wire is not { } dropped || domain.Find(dropped.Node) is not { } end) return;

            if (!dropped.FromOutput)
            {
                // Dragged from an input: the new node feeds it from its first output that fits.
                foreach (PortInfo output in domain.Outputs(record))
                {
                    string? port = dropped.Port == GraphView<TRecord>.PlaceholderPort ? domain.PlaceholderTarget(end, output.Kind) : dropped.Port;
                    if (port == null || domain.InputPort(end, port) is not { } input || !domain.Accepts(input.Kind, output.Kind)) continue;

                    domain.SetSource(end, port, new WireEnd(id, output.Id));
                    domain.PadRepeating(end);
                    return;
                }
                return;
            }

            // Dragged from an output: the new node takes it in its first input of that kind.
            if (domain.OutputKind(end, dropped.Port) is not { } kind) return;
            foreach (PortInfo input in domain.Inputs(record))
            {
                if (!domain.Accepts(input.Kind, kind)) continue;
                domain.SetSource(record, input.Id, new WireEnd(dropped.Node, dropped.Port));
                domain.PadRepeating(record);
                return;
            }
        });
    }

    // The side panel

    private void DrawPanel(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        List<GraphProblem> problems = GraphProblems();
        float footer = 1f + 26f + Math.Max(0, problems.Count - 1) * 20f;

        using (paper.Column("gw_panel").Width(width).Height(height)
            .BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            DrawPanelSection(paper, font, m, width, height - footer);
            DrawFooter(paper, font, m, problems, footer);
        }
    }

    /// <summary>The tool's part of the side panel, above the problems footer.</summary>
    protected virtual void DrawPanelSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height) { }

    /// <summary>The problems that belong to the graph as a whole rather than to one node, like having no output.</summary>
    private List<GraphProblem> GraphProblems()
    {
        var problems = new List<GraphProblem>();
        if (!ReferenceEquals(Canvas, View)) return problems;
        foreach (GraphProblem problem in View.Problems)
            if (problem.NodeId.Length == 0) problems.Add(problem);
        return problems;
    }

    /// <summary>The graph's problems that belong to no node, pinned to the bottom of the panel.</summary>
    private static void DrawFooter(Paper paper, Scribe.FontFile font, OrigamiMetrics m, List<GraphProblem> problems, float height)
    {
        EditorGUI.Divider(paper, "gw_footSep");

        using (paper.Column("gw_foot").Width(UnitValue.Stretch()).Height(height - 1f)
            .Padding(m.Padding, m.Padding, 5, 5).Gap(2).Enter())
        {
            if (problems.Count == 0)
            {
                FooterLine(paper, font, "gw_foot_ok", "Graph is ready", EditorTheme.Green400);
                return;
            }

            for (int i = 0; i < problems.Count; i++)
                FooterLine(paper, font, $"gw_foot_{i}", problems[i].Message,
                    problems[i].Blocking ? EditorTheme.Red400 : EditorTheme.Amber400);
        }
    }

    private static void FooterLine(Paper paper, Scribe.FontFile font, string id, string text, Color colour)
    {
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(16).Gap(6).AlignItems(LayoutAlignment.Center)
            .Tooltip(text).Enter())
        {
            paper.Box(id + "_dot").Width(7).Height(7).Rounded(4).BackgroundColor(colour).IsNotInteractable();
            paper.Box(id + "_text").Width(UnitValue.Stretch()).Height(16).IsNotInteractable()
                .Text(text, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall * 0.95f)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate();
        }
    }
}
