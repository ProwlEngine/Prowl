using System;
using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Editor.GUI;

/// <summary>
/// Panel maximizing for the editor's dock space. Double-clicking a docked panel's tab fills the
/// whole dock area with that panel alone, and double-clicking its tab again brings the layout back.
/// This class acts as a manager of a DockSpace, so it's meant to be used alongside it.
/// </summary>
/// <remarks>
/// This behaviour is not handled in Origami's <see cref="DockSpace"/>, but rather on the Prowl Editor side.
/// Maximizing a panel just makes it the dock's only root, and the real layout is set aside untouched.
/// Whatever the user does to the stand-in through the dock itself (drags the tab out, closes
/// it, docks another panel onto it) ends the maximize, and the change is carried over into the real layout.
/// </remarks>
internal sealed class PanelMaximizer
{
    // Origami right-aligns a panel's header controls this far in from the leaf's edge.
    private const float HeaderInset = 4f;

    private readonly DockSpace _dock;

    // While maximized: the panel, the leaf of the real layout it lives in, the real layout itself, and the
    // stand-in leaf the dock shows in its place.
    private DockPanel? _panel;
    private DockNode? _host;
    private DockNode? _layout;
    private DockNode? _view;

    // Where this frame's leaves were drawn, for finding the tab under a double-click.
    private readonly List<(DockNode Leaf, float X, float Y, float W)> _leaves = new();
    private float _tabBarHeight;

    // The panel whose tab was double-clicked and the leaf it was in, acted on once the mouse button is up.
    private DockPanel? _toggle;
    private DockNode? _toggleLeaf;

    public PanelMaximizer(DockSpace dock) => _dock = dock;

    /// <summary>The panel currently filling the dock area, or null.</summary>
    public DockPanel? MaximizedPanel => _panel;

    /// <summary>
    /// The docked layout as the user arranged it. While a panel is maximized this is the tree set aside, which
    /// still holds every docked panel (the maximized one included); otherwise it is the dock's own root.
    /// </summary>
    public DockNode LayoutRoot => _layout ?? _dock.Root;

    /// <summary>Whether <paramref name="panel"/> is docked but out of sight behind the maximized panel.</summary>
    public bool IsHidden(DockPanel panel) => _layout != null && panel != _panel && Contains(_layout, panel);

    /// <summary>
    /// Draws the dock space into the given rect, in place of <see cref="DockSpace.Draw"/>, with tab
    /// double-clicks maximizing and restoring panels.
    /// </summary>
    public void Draw(Paper paper, float x, float y, float w, float h)
    {
        ApplyDoubleClick(paper);
        Sync();

        // Origami's tabs handle clicks and drags but not double-clicks, so a double-click on one bubbles up
        // to this host. It takes no input of its own and sits at the origin, so the dock lays out and hit
        // tests exactly as it would without it.
        var screen = paper.ScreenRect.Size;
        using (paper.Box("dock_maximize_host")
            .PositionType(PositionType.SelfDirected).Position(0, 0).Size(screen.X, screen.Y)
            .IsNotInteractable()
            .OnDoubleClick(paper, (p, _) => OnDoubleClick(p))
            .Enter())
        {
            _dock.Draw(paper, x, y, w, h);
        }

        // Taken after Draw, which applies a pending drop and prunes empty leaves first, so this matches what
        // was drawn when Paper dispatches the double-click at the end of the frame.
        var metrics = Origami.Current.Metrics;
        _tabBarHeight = metrics.TabBarHeight;
        _leaves.Clear();
        CollectLeaves(_dock.Root, x, y, w, h, metrics.SplitterSize);
    }

    /// <summary>
    /// Fill the dock area with <paramref name="panel"/>. Only panels docked in the main layout can be
    /// maximized; returns false for anything else (a floating window's panel, or one not open at all).
    /// </summary>
    public bool Maximize(DockPanel panel)
    {
        Restore();
        var host = FindLeaf(_dock.Root, panel);
        if (host == null) return false;

        // The panel is what the user is looking at, so it is the one in front when the layout comes back.
        host.ActiveTabIndex = host.Tabs!.IndexOf(panel);
        _panel = panel;
        _host = host;
        _layout = _dock.Root;
        _view = new StandIn(host, panel);
        _dock.Root = _view;
        return true;
    }

    /// <summary>
    /// Put the docked layout back, carrying over anything done to the maximized panel's stand-in meanwhile.
    /// Does nothing when no panel is maximized.
    /// </summary>
    public void Restore()
    {
        if (_view == null) return;
        var (panel, host, layout, view) = (_panel!, _host!, _layout!, _view);
        Discard();

        // The leaf keeps whichever tab it had in front (the panel, unless something like play mode focused
        // another meanwhile), except that a panel just docked into the stand-in comes to the front.
        var tabs = host.Tabs!;
        var viewTabs = view.Tabs!;
        var front = tabs.Count > 0 ? tabs[Math.Clamp(host.ActiveTabIndex, 0, tabs.Count - 1)] : null;
        if (viewTabs.Exists(t => t != panel))
            front = viewTabs[Math.Clamp(view.ActiveTabIndex, 0, viewTabs.Count - 1)];

        // The stand-in's tabs take the panel's place in its leaf: normally just the panel again, none if it
        // was dragged out or closed, more if another panel was docked in beside it.
        int slot = tabs.IndexOf(panel);
        if (slot >= 0) tabs.RemoveAt(slot);
        else slot = tabs.Count;
        tabs.InsertRange(slot, viewTabs);

        // If the tab in front was the panel and it left, its neighbour takes over, as when Origami closes a tab.
        int active = front != null ? tabs.IndexOf(front) : -1;
        host.ActiveTabIndex = active >= 0 ? active : Math.Min(slot, Math.Max(0, tabs.Count - 1));

        // A split made around the stand-in (a panel docked beside it, or on the dock's outer edge) stands
        // where the leaf stood in the layout. The stand-in can only leave the tree by being pruned once empty,
        // which Sync catches first; should it have anyway, both halves are kept.
        var current = _dock.Root;
        var replacement = Contains(current, view)
            ? Replace(current, view, host)
            : DockNode.Split(SplitDirection.Horizontal, 0.5f, host, current);
        _dock.Root = Replace(layout, host, replacement);
    }

    /// <summary>Forget the maximized panel without restoring anything, for when the whole layout is being replaced.</summary>
    public void Discard()
    {
        _panel = null;
        _host = null;
        _layout = null;
        _view = null;
    }

    /// <summary>
    /// Ends the maximize if the dock changed the stand-in since the last frame: its drops happen inside Draw,
    /// and a tab dragged out or closed changes it in Paper's event pass after that.
    /// </summary>
    internal void Sync()
    {
        if (_view == null) return;
        bool intact = _dock.Root == _view && _view.Tabs is { Count: 1 } tabs && tabs[0] == _panel;
        if (!intact) Restore();
    }

    // A double-click registers on the second press, but that click's release still has to reach the tab
    // (Paper handles it at the end of the frame the button comes up), so the toggle waits until after it.
    // Swapping the layout any sooner would hand the release to whichever tab took the clicked one's place.
    private void ApplyDoubleClick(Paper paper)
    {
        if (_toggle == null) return;

        // Origami pulls a tab out of its leaf as soon as a drag starts. If it has gone, the second press
        // turned into a drag and this was no double-click.
        if (_toggleLeaf!.Tabs?.Contains(_toggle) != true)
        {
            _toggle = null;
            return;
        }

        if (paper.IsPointerDown(PaperMouseBtn.Left) || paper.IsPointerReleased(PaperMouseBtn.Left)) return;

        var panel = _toggle;
        _toggle = null;
        if (_view != null) Restore();
        else Maximize(panel);
    }

    private void OnDoubleClick(Paper paper)
    {
        // The event arrives here from whatever was double-clicked, and Paper still knows which element that was.
        var hit = paper.FindElementByID(paper.HoveredElementId);
        if (!hit.IsValid) return;
        Rect element = hit.Data.LayoutRect;

        // Floating windows draw over the docked layout, and are not maximized.
        Float2 p = paper.PointerPos;
        foreach (var fw in _dock.FloatingWindows)
        {
            if (p.X >= fw.Position.X && p.X <= fw.Position.X + fw.Size.X &&
                p.Y >= fw.Position.Y && p.Y <= fw.Position.Y + fw.Size.Y)
                return;
        }

        foreach (var (leaf, x, y, w) in _leaves)
        {
            if (!IsTab(element, leaf, x, y, w)) continue;

            // The first click of the pair selected the tab, so the leaf's active panel is the one double-clicked.
            _toggle = leaf.Tabs![leaf.ActiveTabIndex];
            _toggleLeaf = leaf;
            return;
        }
    }

    // A tab is the only element in a leaf's tab bar that takes input and spans the bar's full height: the bar
    // and the labels on it take none, and a tab's close button is smaller. The panel's header controls sit at
    // the right end of the bar and are ruled out by position.
    private bool IsTab(Rect element, DockNode leaf, float x, float y, float w)
    {
        if (leaf.Tabs is not { Count: > 0 } tabs || leaf.ActiveTabIndex < 0 || leaf.ActiveTabIndex >= tabs.Count)
            return false;
        if (MathF.Abs(element.Min.Y - y) > 0.5f || MathF.Abs(element.Size.Y - _tabBarHeight) > 0.5f)
            return false;
        if (element.Min.X < x - 0.5f || element.Max.X > x + w + 0.5f)
            return false;

        float header = tabs[leaf.ActiveTabIndex].HeaderWidth;
        return header <= 0f || element.Min.X < x + w - header - HeaderInset - 0.5f;
    }

    // Mirrors DockSpace's layout: a split gives its first child SplitRatio of the space left after the splitter.
    private void CollectLeaves(DockNode? node, float x, float y, float w, float h, float splitter)
    {
        if (node == null || w <= 0 || h <= 0) return;

        if (node.IsLeaf)
        {
            _leaves.Add((node, x, y, w));
            return;
        }

        if (node.Direction == SplitDirection.Horizontal)
        {
            float aw = (w - splitter) * node.SplitRatio;
            CollectLeaves(node.ChildA, x, y, aw, h, splitter);
            CollectLeaves(node.ChildB, x + aw + splitter, y, w - aw - splitter, h, splitter);
        }
        else
        {
            float ah = (h - splitter) * node.SplitRatio;
            CollectLeaves(node.ChildA, x, y, w, ah, splitter);
            CollectLeaves(node.ChildB, x, y + ah + splitter, w, h - ah - splitter, splitter);
        }
    }

    private static DockNode? FindLeaf(DockNode? node, DockPanel panel)
    {
        if (node == null) return null;
        if (node.IsLeaf) return node.Tabs!.Contains(panel) ? node : null;
        return FindLeaf(node.ChildA, panel) ?? FindLeaf(node.ChildB, panel);
    }

    private static bool Contains(DockNode? node, DockPanel panel) => FindLeaf(node, panel) != null;

    private static bool Contains(DockNode? node, DockNode target)
    {
        if (node == null) return false;
        if (node == target) return true;
        return !node.IsLeaf && (Contains(node.ChildA, target) || Contains(node.ChildB, target));
    }

    // Puts replacement where target sits under root (in place), returning the root, which is replacement
    // itself when target was the root.
    private static DockNode Replace(DockNode root, DockNode target, DockNode replacement)
    {
        if (root == target) return replacement;
        if (!root.IsLeaf)
        {
            root.ChildA = Replace(root.ChildA!, target, replacement);
            root.ChildB = Replace(root.ChildB!, target, replacement);
        }
        return root;
    }

    /// <summary>Holds the maximized panel in the leaf's place. It answers to the leaf's hash code because Origami
    /// builds a leaf's element IDs from it, so the panel's UI state kept in Paper's element storage (scroll
    /// offsets and the like) carries over instead of starting fresh.
    /// <remarks>
    /// <b>The two are never drawn in the same
    /// frame, which is what would make those IDs clash.</b></remarks></summary>
    private sealed class StandIn : DockNode
    {
        private readonly int _hash;

        public StandIn(DockNode leaf, DockPanel panel)
        {
            _hash = leaf.GetHashCode();
            Tabs = new List<DockPanel> { panel };
        }

        public override int GetHashCode() => _hash;
    }
}
