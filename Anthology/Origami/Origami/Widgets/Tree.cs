// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

// ================================================================
//  Data types
// ================================================================

/// <summary>
/// Describes one node in an Origami tree. Built by the caller for each visible node;
/// the tree widget handles layout, selection, expand/collapse, checkboxes, drag-drop, etc.
/// </summary>
public sealed class TreeNode
{
    /// <summary>Stable unique ID for this node (used for expand state, selection, etc.).</summary>
    public string Id = "";

    /// <summary>Display label.</summary>
    public string Label = "";

    /// <summary>Icon glyph (FontAwesome etc.) shown before the label. Empty = no icon.</summary>
    public string Icon = "";

    /// <summary>Icon color override. Null = use default ink color.</summary>
    public Color? IconColor;

    /// <summary>Label color override. Null = use default ink color.</summary>
    public Color? LabelColor;

    /// <summary>Optional right-aligned badge text (counts, status, etc.).</summary>
    public string? Badge;

    /// <summary>Badge color override.</summary>
    public Color? BadgeColor;

    /// <summary>Whether this node has children (shows expand arrow even if children aren't provided yet - for lazy loading).</summary>
    public bool HasChildren;

    /// <summary>Whether this node starts expanded on first appearance. Ignored after first frame (state persists).</summary>
    public bool DefaultExpanded;

    /// <summary>Whether this node is a leaf that cannot be expanded (no arrow shown regardless of HasChildren).</summary>
    public bool IsLeaf;

    /// <summary>Depth in the tree (0 = root). Set by the caller; controls indentation.</summary>
    public int Depth;

    /// <summary>Arbitrary user data attached to this node.</summary>
    public object? UserData;

    /// <summary>If true, the node's checkbox is checked (only used when the tree has checkboxes enabled).</summary>
    public bool Checked;

    /// <summary>If true, the node is in an indeterminate/partial check state (visual only - drawn as a dash).</summary>
    public bool Indeterminate;

    /// <summary>If true, this node cannot be interacted with (dimmed, no click/drag).</summary>
    public bool Disabled;

    /// <summary>If true, this node is currently being renamed (draws a text field instead of a label).</summary>
    public bool IsRenaming;

    /// <summary>Optional secondary icon on the right side of the row (e.g., visibility eye).</summary>
    public string? TrailingIcon;

    /// <summary>Color for the trailing icon.</summary>
    public Color? TrailingIconColor;

    /// <summary>
    /// If set, overrides the internal expand state this frame AND persists it.
    /// Use to force-expand parents of pinged/searched nodes.
    /// </summary>
    public bool? OverrideExpanded;

    /// <summary>
    /// If set, the tree draws a drop indicator (above/below bar or into-tint) on this node.
    /// Set by the caller each frame based on drag state.
    /// </summary>
    public TreeDropPosition? DropIndicator;
}

/// <summary>Where a drag payload was dropped relative to a tree node.</summary>
public enum TreeDropPosition
{
    Above,
    Into,
    Below
}

/// <summary>Event args for tree node interactions.</summary>
public readonly struct TreeNodeEvent
{
    public readonly TreeNode Node;
    public readonly int Index; // Flat index in the visible list
    public TreeNodeEvent(TreeNode node, int index) { Node = node; Index = index; }
}

/// <summary>Event args for tree drag-drop.</summary>
public readonly struct TreeDropEvent
{
    public readonly TreeNode TargetNode;
    public readonly TreeDropPosition Position;
    public readonly float NormalizedY; // 0..1 within the row
    public TreeDropEvent(TreeNode target, TreeDropPosition pos, float normY) { TargetNode = target; Position = pos; NormalizedY = normY; }
}

// ================================================================
//  Builder
// ================================================================

/// <summary>
/// Fluent builder for an Origami tree view. Construct via <see cref="Origami.Tree"/>;
/// configure callbacks and options; call <see cref="Show"/> to render.
///
/// The tree is virtualised for large lists: only visible rows are laid out. Nodes are
/// provided as a flat list (pre-ordered depth-first) with a Depth field for indentation;
/// expand/collapse state is managed internally via Paper element storage.
/// </summary>
public sealed class TreeBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    // Node data
    private List<TreeNode>? _nodes;

    // Layout - default to 0 meaning "use theme HeaderHeight"
    private float _rowHeight;
    private float _indentSize = 16f;
    private float _width;
    private float _height;
    private float _padding = 4f;

    // Features
    private bool _checkboxes;
    private bool _multiSelect;
    private bool _reorderable; // show above/below drop indicators

    // Selection (external state)
    private Func<TreeNode, bool>? _isSelected;
    private Action<TreeNodeEvent>? _onSelect;
    private Action<TreeNodeEvent, bool, bool>? _onSelectModified; // (node, ctrl, shift)

    // Expand/collapse
    private Action<TreeNode, bool>? _onExpandChanged;

    // Checkbox
    private Action<TreeNode, bool>? _onCheckedChanged;

    // Clicks
    private Action<TreeNodeEvent>? _onDoubleClick;
    private Action<TreeNodeEvent>? _onRightClick;

    // Trailing icon click
    private Action<TreeNode>? _onTrailingIconClick;

    // Context menu
    private Action<TreeNode>? _onContextMenu;

    // Drag-drop
    private Func<TreeNode, bool>? _canDrag;
    private Action<TreeNode>? _onDragStart;
    private Func<TreeNode, bool>? _canDrop;
    private Action<TreeDropEvent>? _onDrop;

    // Hover
    private Action<TreeNode, float>? _onHover; // (node, normalizedY)

    // Rename
    private Action<TreeNode, string>? _onRenamed;

    // Custom row rendering
    private Action<Paper, TreeNode, bool, bool>? _customRowContent; // (paper, node, isSelected, isExpanded)

    // Ping highlight
    private Func<TreeNode, bool>? _isPinged;
    private Func<float>? _pingAlpha;

    // Optional sink: each Show() fills this with the resolved expanded state per node id, so callers
    // (e.g. a hierarchy panel doing drag-drop) can know whether a node is currently expanded.
    private Dictionary<string, bool>? _expandStateSink;

    // Empty state
    private string? _emptyMessage;

    internal TreeBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    // ── Data ──────────────────────────────────────────────────────────

    /// <summary>
    /// Provide the flat, depth-first-ordered node list. Each node's Depth field
    /// controls indentation. Children of a collapsed parent should be omitted by
    /// the caller (or the tree will skip them based on expand state).
    /// </summary>
    public TreeBuilder Nodes(List<TreeNode> nodes) { _nodes = nodes; return this; }

    // ── Layout ────────────────────────────────────────────────────────

    public TreeBuilder RowHeight(float h) { _rowHeight = MathF.Max(16, h); return this; }
    public TreeBuilder IndentSize(float s) { _indentSize = MathF.Max(0, s); return this; }
    public TreeBuilder Size(float w, float h) { _width = w; _height = h; return this; }
    public TreeBuilder Padding(float p) { _padding = p; return this; }

    // ── Features ──────────────────────────────────────────────────────

    /// <summary>Show checkboxes on each node.</summary>
    public TreeBuilder Checkboxes(bool show = true) { _checkboxes = show; return this; }

    /// <summary>Enable multi-select (Ctrl+Click, Shift+Click). Requires OnSelectModified callback.</summary>
    public TreeBuilder MultiSelect(bool enable = true) { _multiSelect = enable; return this; }

    /// <summary>Show above/below drop indicators during drag (for reorder trees like Hierarchy).</summary>
    public TreeBuilder Reorderable(bool enable = true) { _reorderable = enable; return this; }

    // ── Selection ─────────────────────────────────────────────────────

    /// <summary>Predicate to determine if a node is currently selected (for highlighting).</summary>
    public TreeBuilder IsSelected(Func<TreeNode, bool> predicate) { _isSelected = predicate; return this; }

    /// <summary>Called on plain click (no modifiers). Single-select behavior.</summary>
    public TreeBuilder OnSelect(Action<TreeNodeEvent> handler) { _onSelect = handler; return this; }

    /// <summary>Called on click with modifier key info. For multi-select trees.</summary>
    public TreeBuilder OnSelectModified(Action<TreeNodeEvent, bool, bool> handler) { _onSelectModified = handler; return this; }

    // ── Expand / Collapse ─────────────────────────────────────────────

    /// <summary>Called when a node's expand state changes. Use to trigger lazy child loading.</summary>
    public TreeBuilder OnExpandChanged(Action<TreeNode, bool> handler) { _onExpandChanged = handler; return this; }

    // ── Checkbox ──────────────────────────────────────────────────────

    /// <summary>Called when a node's checkbox is toggled.</summary>
    public TreeBuilder OnCheckedChanged(Action<TreeNode, bool> handler) { _onCheckedChanged = handler; return this; }

    // ── Clicks ────────────────────────────────────────────────────────

    public TreeBuilder OnDoubleClick(Action<TreeNodeEvent> handler) { _onDoubleClick = handler; return this; }
    public TreeBuilder OnRightClick(Action<TreeNodeEvent> handler) { _onRightClick = handler; return this; }
    public TreeBuilder OnTrailingIconClick(Action<TreeNode> handler) { _onTrailingIconClick = handler; return this; }

    // ── Context Menu ──────────────────────────────────────────────────

    /// <summary>Called to build a context menu for a node. Use with ContextMenuHelper inside the callback.</summary>
    public TreeBuilder OnContextMenu(Action<TreeNode> handler) { _onContextMenu = handler; return this; }

    // ── Drag & Drop ───────────────────────────────────────────────────

    public TreeBuilder CanDrag(Func<TreeNode, bool> predicate) { _canDrag = predicate; return this; }
    public TreeBuilder OnDragStart(Action<TreeNode> handler) { _onDragStart = handler; return this; }
    public TreeBuilder CanDrop(Func<TreeNode, bool> predicate) { _canDrop = predicate; return this; }
    public TreeBuilder OnDrop(Action<TreeDropEvent> handler) { _onDrop = handler; return this; }

    // ── Hover ─────────────────────────────────────────────────────────

    /// <summary>Called when hovering a node during drag. Receives normalizedY (0..1) within the row.</summary>
    public TreeBuilder OnHover(Action<TreeNode, float> handler) { _onHover = handler; return this; }

    // ── Rename ────────────────────────────────────────────────────────

    /// <summary>Called when a rename is committed. Node.IsRenaming must be set by the caller.</summary>
    public TreeBuilder OnRenamed(Action<TreeNode, string> handler) { _onRenamed = handler; return this; }

    // ── Custom Content ────────────────────────────────────────────────

    /// <summary>
    /// Override the default row content rendering. The callback receives the paper,
    /// the node, whether it's selected, and whether it's expanded. The callback draws
    /// inside the row's layout scope. Arrow and checkbox are still drawn by the tree.
    /// </summary>
    public TreeBuilder CustomRowContent(Action<Paper, TreeNode, bool, bool> renderer) { _customRowContent = renderer; return this; }

    // ── Ping ──────────────────────────────────────────────────────────

    /// <summary>Predicate to determine if a node should show a ping highlight.</summary>
    public TreeBuilder IsPinged(Func<TreeNode, bool> predicate) { _isPinged = predicate; return this; }

    /// <summary>Returns the current ping alpha (0..1) for fade animation.</summary>
    public TreeBuilder PingAlpha(Func<float> getter) { _pingAlpha = getter; return this; }

    /// <summary>Provide a dictionary that Show() fills with each node's resolved expanded state (by node id).</summary>
    public TreeBuilder ExpandStateSink(Dictionary<string, bool> sink) { _expandStateSink = sink; return this; }

    // ── Empty state ───────────────────────────────────────────────────

    /// <summary>Message shown when the node list is empty.</summary>
    public TreeBuilder EmptyMessage(string msg) { _emptyMessage = msg; return this; }

    // ── Terminator ────────────────────────────────────────────────────

    public void Show()
    {
        var nodes = _nodes;
        if (nodes == null) return;

        var font = _theme.Font;
        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        float rounding = metrics.Rounding;

        // Resolve row height from theme if not explicitly set
        if (_rowHeight <= 0)
            _rowHeight = metrics.HeaderHeight;

        // Expand state storage key prefix
        string expandPrefix = $"{_id}_exp_";

        // Capture a stable handle for expand state storage. We use a hidden box
        // that lives outside the scroll so its handle doesn't shift with scroll content.
        var stateBox = _paper.Box($"{_id}_state").Height(0).Width(0);
        var stateHandle = stateBox._handle;

        Origami.ScrollView(_paper, $"{_id}_scroll", _width, _height)
            .Padding(_padding, _padding, _padding, _padding)
            .Body(() =>
            {
                if (nodes.Count == 0 && !string.IsNullOrEmpty(_emptyMessage) && font != null)
                {
                    _paper.Box($"{_id}_empty").Height(40)
                        .Text(_emptyMessage, font)
                        .TextColor(ink.C300)
                        .FontSize(metrics.FontSize - 1)
                        .Alignment(TextAlignment.MiddleCenter);
                    return;
                }

                // We walk the flat list in depth-first order. Expandable parents get an
                // animated wrapper around their children that lerps height 0..Auto.
                // A stack tracks open wrappers so we close them when depth decreases.

                // Stack of (parentDepth, IDisposable wrapper scope)
                var wrapperStack = new Stack<(int depth, IDisposable scope)>();

                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];

                    // Close any wrappers for parents we've moved past
                    while (wrapperStack.Count > 0 && node.Depth <= wrapperStack.Peek().depth)
                    {
                        var (_, scope) = wrapperStack.Pop();
                        scope.Dispose();
                    }

                    bool isExpandable = node.HasChildren && !node.IsLeaf;
                    string expKey = expandPrefix + node.Id;
                    bool isExpanded;
                    if (node.OverrideExpanded.HasValue)
                    {
                        isExpanded = isExpandable && node.OverrideExpanded.Value;
                        _paper.SetElementStorage(stateHandle, expKey, node.OverrideExpanded.Value);
                    }
                    else
                    {
                        isExpanded = isExpandable && _paper.GetElementStorage(
                            stateHandle, expKey, node.DefaultExpanded);
                    }

                    if (_expandStateSink != null)
                        _expandStateSink[node.Id] = isExpanded;

                    bool isSelected = _isSelected?.Invoke(node) ?? false;
                    bool isPinged = _isPinged?.Invoke(node) ?? false;

                    DrawNode(font, ink, metrics, rounding, stateHandle, expandPrefix, nodes,
                        node, i, isSelected, isExpanded, isExpandable, isPinged);

                    // If this node is expandable, open an animated wrapper for its children
                    if (isExpandable)
                    {
                        string animId = $"{_id}_anim_{node.Id}";
                        float anim = _paper.AnimateBool(isExpanded, 0.15f, id: animId);

                        if (!isExpanded && anim <= float.Epsilon)
                        {
                            // Fully collapsed and animation done - skip all children
                            int parentDepth = node.Depth;
                            while (i + 1 < nodes.Count && nodes[i + 1].Depth > parentDepth)
                                i++;
                        }
                        else
                        {
                            // Animating or expanded - wrap children in a height-lerped container
                            var wrapper = _paper.Column($"{_id}_cw_{node.Id}")
                                .Width(UnitValue.Stretch())
                                .Height(UnitValue.Lerp(0, UnitValue.Auto, anim));

                            // Only clip during animation (not at rest) since Clip isn't cheap
                            bool animating = anim > float.Epsilon && anim < 1f - float.Epsilon;
                            if (animating)
                                wrapper.Clip();

                            wrapperStack.Push((node.Depth, wrapper.Enter()));
                        }
                    }
                }

                // Close any remaining open wrappers
                while (wrapperStack.Count > 0)
                {
                    var (_, scope) = wrapperStack.Pop();
                    scope.Dispose();
                }
            });
    }

    private void DrawNode(Prowl.Scribe.FontFile? font, OrigamiRamp ink, OrigamiMetrics metrics,
        float rounding, ElementHandle stateHandle, string expandPrefix, List<TreeNode> allNodes,
        TreeNode node, int index, bool isSelected, bool isExpanded,
        bool isExpandable, bool isPinged)
    {
        float indent = node.Depth * _indentSize;
        string rowId = $"{_id}_r_{node.Id}";
        string expKey = $"{_id}_exp_{node.Id}";
        int capturedIndex = index;
        var capturedNode = node;
        bool disabled = node.Disabled;

        var accentColor = _theme.Primary.C400;

        // Drop indicator above
        if (node.DropIndicator == TreeDropPosition.Above)
        {
            _paper.Box($"{rowId}_drop_above")
                .Height(3).Margin(indent + _theme.Metrics.SpacingLarge, _theme.Metrics.Spacing, 0, 0)
                .Rounded(_theme.Metrics.SmallRounding)
                .BackgroundColor(accentColor);
        }

        bool isDropInto = node.DropIndicator == TreeDropPosition.Into;
        Color rowBg = isSelected ? accentColor
            : isDropInto ? Color.FromArgb(60, accentColor.R, accentColor.G, accentColor.B)
            : Color.Transparent;
        Color rowHover = isSelected ? accentColor : _theme.Ink.C200;

        // Build the row element
        var row = _paper.Row(rowId)
            .Height(_rowHeight)
            .BackgroundColor(rowBg)
            .Hovered.BackgroundColor(rowHover).End()
            .Rounded(_theme.Metrics.Rounding)
            .ChildLeft(indent);

        // Click handling
        if (!disabled)
        {
            if (_onSelectModified != null && _multiSelect)
            {
                row.OnClick(e =>
                {
                    e.StopPropagation();
                    bool ctrl = _paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl);
                    bool shift = _paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift);
                    _onSelectModified(new TreeNodeEvent(capturedNode, capturedIndex), ctrl, shift);
                });
            }
            else if (_onSelect != null)
            {
                row.OnClick(e =>
                {
                    e.StopPropagation();
                    _onSelect(new TreeNodeEvent(capturedNode, capturedIndex));
                });
            }

            if (_onDoubleClick != null)
                row.OnDoubleClick(e =>
                {
                    e.StopPropagation();
                    _onDoubleClick(new TreeNodeEvent(capturedNode, capturedIndex));
                });

            if (_onRightClick != null)
                row.OnRightClick(e =>
                {
                    e.StopPropagation();
                    _onRightClick(new TreeNodeEvent(capturedNode, capturedIndex));
                });

            // Drag
            if (_onDragStart != null && (_canDrag == null || _canDrag(node)))
                row.OnDragStart(_ => _onDragStart(capturedNode));

            // Hover (for drag-drop position tracking)
            if (_onHover != null)
            {
                row.OnHover(capturedNode, (n, e) =>
                {
                    _onHover(n, (float)e.NormalizedPosition.Y);
                });
            }
        }

        // Ping highlight
        if (isPinged)
        {
            row.OnPostLayout((handle, rect) =>
            {
                float alpha = _pingAlpha?.Invoke() ?? 1f;
                if (alpha <= 0f) return;
                _paper.Draw(ref handle, (canvas, r) =>
                {
                    int fillA = (int)(alpha * 60);
                    int borderA = (int)(alpha * 200);
                    var fillColor = Color.FromArgb(fillA, 255, 220, 50);
                    var borderColor = Color.FromArgb(borderA, 255, 200, 0);
                    float x = (float)r.Min.X, y = (float)r.Min.Y;
                    float w = (float)r.Size.X, h = (float)r.Size.Y;
                    canvas.RoundedRectFilled(x, y, w, h, 4, 4, 4, 4, fillColor);
                    canvas.SetStrokeColor(borderColor);
                    canvas.SetStrokeWidth(2f);
                    canvas.BeginPath();
                    canvas.RoundedRect(x + 1, y + 1, w - 2, h - 2, 3, 3, 3, 3);
                    canvas.Stroke();
                });
            });
        }

        using (row.Enter())
        {
            // ---- Expand arrow ----
            if (isExpandable)
            {
                _paper.Box($"{rowId}_arr")
                    .Width(14).Height(_rowHeight)
                    .Text(isExpanded ? _theme.Icons.ChevronDown : _theme.Icons.ChevronRight, font)
                    .TextColor(ink.C400)
                    .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                    .StopEventPropagation()
                    .OnClick(_ =>
                    {
                        bool newState = !isExpanded;
                        bool alt = _paper.IsKeyDown(PaperKey.LeftAlt) || _paper.IsKeyDown(PaperKey.RightAlt);

                        _paper.SetElementStorage(stateHandle, expKey, newState);
                        _onExpandChanged?.Invoke(capturedNode, newState);

                        // Alt+Click: recursively expand/collapse all descendants
                        if (alt)
                        {
                            int parentDepth = capturedNode.Depth;
                            for (int di = capturedIndex + 1; di < allNodes.Count; di++)
                            {
                                var desc = allNodes[di];
                                if (desc.Depth <= parentDepth) break;
                                if (desc.HasChildren && !desc.IsLeaf)
                                {
                                    string descKey = expandPrefix + desc.Id;
                                    _paper.SetElementStorage(stateHandle, descKey, newState);
                                    _onExpandChanged?.Invoke(desc, newState);
                                }
                            }
                        }
                    });
            }
            else
            {
                _paper.Box($"{rowId}_arr").Width(14).Height(_rowHeight);
            }

            // ---- Checkbox ----
            if (_checkboxes && !disabled)
            {
                Origami.Checkbox(_paper, $"{rowId}_chk", node.Checked, v =>
                {
                    _onCheckedChanged?.Invoke(capturedNode, v);
                }).Show();
            }
            else if (_checkboxes && disabled)
            {
                Origami.Checkbox(_paper, $"{rowId}_chk", node.Checked, _ => { }).Disabled().Show();
            }

            // ---- Custom or default content ----
            if (_customRowContent != null)
            {
                _customRowContent(_paper, node, isSelected, isExpanded);
            }
            else
            {
                DrawDefaultContent(font, ink, metrics, node, rowId, isSelected, disabled);
            }
        }

        // Drop indicator below
        if (node.DropIndicator == TreeDropPosition.Below)
        {
            _paper.Box($"{rowId}_drop_below")
                .Height(3).Margin(indent + _theme.Metrics.SpacingLarge, _theme.Metrics.Spacing, 0, 0)
                .Rounded(_theme.Metrics.SmallRounding)
                .BackgroundColor(accentColor);
        }
    }

    private void DrawDefaultContent(Prowl.Scribe.FontFile? font, OrigamiRamp ink,
        OrigamiMetrics metrics, TreeNode node, string rowId, bool isSelected, bool disabled)
    {
        if (font == null) return;

        // Icon
        if (!string.IsNullOrEmpty(node.Icon))
        {
            Color iconColor = node.IconColor ?? (disabled ? ink.C200 : ink.C400);
            _paper.Box($"{rowId}_ico")
                .Width(16).Height(_rowHeight)
                .Text(node.Icon, font)
                .TextColor(iconColor)
                .FontSize(metrics.FontSize - 1).Alignment(TextAlignment.MiddleCenter);
        }

        // Label or rename field
        if (node.IsRenaming && _onRenamed != null)
        {
            string renameKey = $"{_id}_rename_{node.Id}";
            string currentName = _paper.GetElementStorage(_paper.CurrentParent, renameKey, node.Label);

            Origami.TextField(_paper, $"{rowId}_rename", currentName, v =>
            {
                _paper.SetElementStorage(_paper.CurrentParent, renameKey, v);
            }).Show();

            // Commit on Enter
            if (_paper.IsKeyPressed(PaperKey.Enter) || _paper.IsKeyPressed(PaperKey.KeypadEnter))
            {
                string finalName = _paper.GetElementStorage(_paper.CurrentParent, renameKey, node.Label);
                _onRenamed(node, finalName);
            }
        }
        else
        {
            Color labelColor = node.LabelColor ?? (disabled ? ink.C200 : ink.C500);
            _paper.Box($"{rowId}_lbl")
                .Height(_rowHeight).ChildLeft(4)
                .Text(node.Label, font)
                .TextColor(labelColor)
                .FontSize(metrics.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);
        }

        // Badge
        if (!string.IsNullOrEmpty(node.Badge))
        {
            _paper.Box($"{rowId}_badge")
                .Width(UnitValue.Stretch()).Height(_rowHeight)
                .Text(node.Badge, font)
                .TextColor(node.BadgeColor ?? ink.C300)
                .FontSize(metrics.FontSize - 2)
                .Alignment(TextAlignment.MiddleRight)
                .ChildRight(4);
        }

        // Trailing icon (e.g., visibility eye)
        if (!string.IsNullOrEmpty(node.TrailingIcon))
        {
            Color trailColor = node.TrailingIconColor ?? ink.C400;
            var trailBox = _paper.Box($"{rowId}_trail")
                .Width(18).Height(_rowHeight)
                .Text(node.TrailingIcon, font)
                .TextColor(trailColor)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .StopEventPropagation();

            if (_onTrailingIconClick != null && !disabled)
                trailBox.OnClick(_ => _onTrailingIconClick(node));
        }
    }
}
