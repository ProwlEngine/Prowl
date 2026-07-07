// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.OrigamiUI;

public class DockSpace
{
    public DockNode Root { get; set; }
    public List<FloatingWindow> FloatingWindows { get; } = new();

// --- Drag state (one unified mode: dragging a tab) ---
    private bool _isDragging;
    private DockPanel? _draggedPanel;
    private DockNode? _dragSourceNode;
    private FloatingWindow? _dragSourceWindow; // non-null if tab came from a floating window
    private Float2 _dragPos;
    private Float2 _prevDragPos;

    // --- Dock zone hover ---
    private DockNode? _hoveredLeaf;
    private DockZone _hoveredZone;
    private Rect _hoveredLeafRect;
    private int _hoveredTabIndex;         // insertion index when _hoveredZone == DockZone.Tab
    private float _hoveredTabCaretX;      // absolute X of the insertion caret

    // --- Root docking ---
    private DockZone _rootHoveredZone;
    private Rect _dockSpaceBounds;

    // --- Splitter ---
    private DockNode? _splitterDragNode;

    // --- Layout cache ---
    private readonly Dictionary<DockNode, Rect> _leafRects = new();
    // Per-leaf tab-bar geometry (absolute), for computing tab-insertion drops.
    private readonly Dictionary<DockNode, (Rect Bar, float[] Edges)> _tabBars = new();
    // Which floating window a leaf belongs to (null = it's in the root tree). For z-ordered hit testing.
    private readonly Dictionary<DockNode, FloatingWindow?> _leafOwner = new();

    // --- Drop preview animation ---
    private Rect _previewRect;
    private float _previewAlpha;
    private bool _previewVisible;

    // --- Cached delta time for methods without paper access ---
    private float _lastDeltaTime;


    public DockSpace(DockNode root) { Root = root; }

    public void Draw(Paper paper, float x, float y, float w, float h)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var icons = theme.Icons;
        var font = theme.Font;
        _lastDeltaTime = paper.DeltaTime;

        _dockSpaceBounds = new Rect(new Float2(x, y), new Float2(x + w, y + h));

        bool mouseUp = !paper.IsPointerDown(PaperMouseBtn.Left);

        // Handle drag end
        if (_isDragging && mouseUp)
        {
            ExecuteDrop(m);
            _isDragging = false;
            _draggedPanel = null;
            _dragSourceNode = null;
            _dragSourceWindow = null;
        }

        // Clean up any empty leaves from closed tabs
        CleanupTree();

        // Clear per-frame state
        _leafRects.Clear();
        _tabBars.Clear();
        _leafOwner.Clear();
        _hoveredLeaf = null;
        _hoveredZone = DockZone.None;
        _rootHoveredZone = DockZone.None;

        // 1. Root dock space
        DrawNodeTree(paper, Root, null, x, y, w, h, null, theme, m, icons, font);

        // 2. Floating windows (z-ordered)
        for (int i = 0; i < FloatingWindows.Count; i++)
            DrawFloatingWindow(paper, FloatingWindows[i], i, theme, m, icons, font);

        // 3. While dragging: move source floating window, compute hover, draw indicators
        if (_isDragging && _draggedPanel != null)
        {
            Float2 newPos = paper.PointerPos;
            Float2 delta = newPos - _prevDragPos;
            _prevDragPos = newPos;
            _dragPos = newPos;

            // If the dragged tab's source is a floating window with only this one tab,
            // move the whole window to follow the cursor
            if (_dragSourceWindow != null &&
                _dragSourceWindow.Node.IsLeaf &&
                _dragSourceWindow.Node.Tabs != null &&
                _dragSourceWindow.Node.Tabs.Count <= 1)
            {
                _dragSourceWindow.Position += delta;
            }

            ComputeHoveredZone(paper, m);
            if (_hoveredZone == DockZone.Tab)
                DrawTabInsertionCaret(paper, theme);
            else if (_hoveredLeaf != null)
                DrawDockIndicators(paper, m, icons, font);
            DrawRootDockIndicators(paper, m, theme);
        }
    }

    // ================================================================
    //  NODE TREE
    // ================================================================

    private void DrawNodeTree(Paper paper, DockNode node, DockNode? parent,
                               float x, float y, float w, float h, FloatingWindow? fw,
                               OrigamiTheme theme, OrigamiMetrics m, OrigamiIcons icons, FontFile? font)
    {
        if (node == null || w <= 0 || h <= 0) return;
        node.Parent = parent;

        if (node.IsLeaf)
        {
            DrawLeaf(paper, node, x, y, w, h, fw, theme, m, icons, font);
            return;
        }

        float sp = m.SplitterSize;
        if (node.Direction == SplitDirection.Horizontal)
        {
            float aw = (w - sp) * node.SplitRatio;
            float bw = w - aw - sp;
            DrawNodeTree(paper, node.ChildA!, node, x, y, aw, h, fw, theme, m, icons, font);
            DrawSplitter(paper, node, x + aw, y, sp, h, true, theme);
            DrawNodeTree(paper, node.ChildB!, node, x + aw + sp, y, bw, h, fw, theme, m, icons, font);
        }
        else
        {
            float ah = (h - sp) * node.SplitRatio;
            float bh = h - ah - sp;
            DrawNodeTree(paper, node.ChildA!, node, x, y, w, ah, fw, theme, m, icons, font);
            DrawSplitter(paper, node, x, y + ah, w, sp, false, theme);
            DrawNodeTree(paper, node.ChildB!, node, x, y + ah + sp, w, bh, fw, theme, m, icons, font);
        }
    }

    // ================================================================
    //  SPLITTER
    // ================================================================

    private void DrawSplitter(Paper paper, DockNode node, float x, float y, float w, float h, bool horiz,
                               OrigamiTheme theme)
    {
        bool active = _splitterDragNode == node;
        paper.Box($"spl_{node.GetHashCode()}")
            .PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .Hovered.BackgroundColor(theme.Primary.C500).End()
            .Active.BackgroundColor(theme.Primary.C400).End()
            .OnDragStart(node, (n, e) => _splitterDragNode = n)
            .OnDragging(node, (n, e) =>
            {
                if (_splitterDragNode != n) return;
                float total = EstimateSplitSize(n, horiz);
                if (total > 0)
                    n.SplitRatio = Math.Clamp(n.SplitRatio + (horiz ? e.Delta.X : e.Delta.Y) / total, 0.1f, 0.9f);
            })
            .OnDragEnd(e => _splitterDragNode = null);
    }

    private float EstimateSplitSize(DockNode n, bool horiz)
    {
        var a = FindLeafRect(n.ChildA);
        var b = FindLeafRect(n.ChildB);
        if (a == null || b == null) return 600f;
        return horiz ? b.Value.Max.X - a.Value.Min.X : b.Value.Max.Y - a.Value.Min.Y;
    }

    private Rect? FindLeafRect(DockNode? n)
    {
        if (n == null) return null;
        if (n.IsLeaf && _leafRects.TryGetValue(n, out var r)) return r;
        return FindLeafRect(n.ChildA) ?? FindLeafRect(n.ChildB);
    }

    // ================================================================
    //  LEAF
    // ================================================================

    private void DrawLeaf(Paper paper, DockNode node, float x, float y, float w, float h, FloatingWindow? fw,
                           OrigamiTheme theme, OrigamiMetrics m, OrigamiIcons icons, FontFile? font)
    {
        if (node.Tabs == null || node.Tabs.Count == 0) return;
        float tabH = m.TabBarHeight;
        float cr = m.ContainerRounding;

        // Floating-window content is drawn in the window's local space (0,0), so offset the cached
        // hit rects by the window position to get absolute (screen) coords. Registering floating leaves
        // makes a floating window a valid drop target too — it behaves like a second root.
        float ox = fw?.Position.X ?? 0f, oy = fw?.Position.Y ?? 0f;
        _leafRects[node] = new Rect(new Float2(x + ox, y + oy), new Float2(x + w + ox, y + h + oy));
        _leafOwner[node] = fw;

        var bodyColor = theme.Neutral.C400;
        var tabBarColor = theme.Neutral.C300;
        var borderColor = theme.Neutral.C200;
        int id = node.GetHashCode();

        // Frosted glass: with backdrop blur on, thin the fill slightly so the blurred content behind
        // reads through as frost — but keep the panels dark and solid (they should be a deep glass,
        // not a light purple wash). Without blur the surfaces stay their normal tint.
        float winBlur = m.WindowBackdropBlur;
        if (winBlur > 0f)
        {
            bodyColor = Color.FromArgb(199, bodyColor.R, bodyColor.G, bodyColor.B);
            tabBarColor = Color.FromArgb(217, tabBarColor.R, tabBarColor.G, tabBarColor.B);
        }

        using (paper.Box($"leaf_{id}")
            .PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .Enter())
        {
            // Frosted-glass window: body fill + tab-bar strip drawn as two non-overlapping
            // translucent fills, so the alpha never doubles up where they meet. Each blurs the
            // content behind it (backdrop blur), so the translucent fill reads as a tint over frost.
            paper.Box($"leaf_body_{id}")
                .PositionType(PositionType.SelfDirected).Position(0, tabH).Size(w, h - tabH)
                .IsNotInteractable()
                .BackdropBlur(winBlur)
                .BackgroundColor(bodyColor).RoundedBottom(cr);

            var tabbar = paper.Box($"leaf_tabbar_{id}")
                .PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, tabH)
                .BackdropBlur(winBlur)
                .BackgroundColor(tabBarColor).RoundedTop(cr);
            // In a floating window, dragging the tab-bar background (behind the tabs) moves the whole
            // window — the tabs sit on top with StopEventPropagation, so a tab drag still detaches it.
            if (fw != null)
                tabbar.OnDragging(fw, (cap, e) => cap.Position += e.Delta);
            else
                tabbar.IsNotInteractable();

            // Tab widths from measured label text (+ optional icon + close affordance).
            float iconW = 15f;              // icon slot
            float iconGap = 6f;             // gap between the icon and the label
            float tabIconSize = m.FontSize * 0.85f;   // glyph a touch smaller than the label text
            float[] tabWidths = new float[node.Tabs.Count];
            for (int i = 0; i < node.Tabs.Count; i++)
            {
                float textW = 60;
                if (font != null)
                    textW = (float)paper.MeasureText(node.Tabs[i].Title, m.FontSize, font, 0).X;
                bool hasIco = !string.IsNullOrEmpty(node.Tabs[i].Icon);
                bool showsClose = i == node.ActiveTabIndex;  // active tab expands to fit its close button
                tabWidths[i] = m.TabPadding * 2 + (hasIco ? iconW + iconGap : 0) + textW + (showsClose ? m.TabCloseSize + 4 : 0);
            }
            float[] tabPositions = new float[node.Tabs.Count];
            float acc = 0;
            for (int i = 0; i < node.Tabs.Count; i++) { tabPositions[i] = acc; acc += tabWidths[i] + m.TabGap; }

            // Cache the tab-bar geometry (absolute) so a drag can compute a between-tabs insertion index.
            if (node.Tabs.Count > 0)
            {
                var edges = new float[node.Tabs.Count + 1];
                for (int i = 0; i < node.Tabs.Count; i++) edges[i] = x + ox + tabPositions[i];
                edges[node.Tabs.Count] = x + ox + tabPositions[node.Tabs.Count - 1] + tabWidths[node.Tabs.Count - 1];
                _tabBars[node] = (new Rect(new Float2(x + ox, y + oy), new Float2(x + w + ox, y + tabH + oy)), edges);
            }

            for (int i = 0; i < node.Tabs.Count; i++)
            {
                bool isActive = i == node.ActiveTabIndex;
                var tab = node.Tabs[i];
                int ci = i;
                float tw = tabWidths[i];
                float tx = tabPositions[i];

                var tabEl = paper.Box($"t_{id}_{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(tx, 0).Size(tw, tabH)
                    .Rounded(i == 0 ? cr : 0f, 0f, 0f, 0f) // clip the first tab to the window's rounded top-left
                    .BackgroundColor(isActive ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(46, 0, 0, 0))
                    .Hovered.BackgroundColor(isActive ? Color.FromArgb(0, 0, 0, 0) : OrigamiTheme.WithAlpha(theme.Primary.C500, 70)).End()
                    .StopEventPropagation()
                    .OnClick(ci, (idx, e) => { if (!_isDragging) node.ActiveTabIndex = idx; })
                    .OnDragStart((node, ci, fw), (cap, e) =>
                    {
                        var srcNode = cap.Item1;
                        var srcIdx = cap.Item2;
                        var srcFw = cap.Item3;

                        _draggedPanel = srcNode.Tabs![srcIdx];
                        _isDragging = true;
                        _dragPos = e.PointerPosition;
                        _prevDragPos = e.PointerPosition;

                        // Detach the tab immediately into a new floating window that follows the cursor —
                        // uniform whether it came from the root or another floating window. This makes
                        // pulling a tab out remove it right away, and lets it be dropped back onto its own
                        // former leaf (it's no longer the drag source).
                        srcNode.RemoveTab(srcIdx);
                        var newNode = DockNode.Leaf(_draggedPanel);
                        var newFw = new FloatingWindow(newNode,
                            e.PointerPosition - new Float2(100, 15),
                            srcFw?.Size ?? new Float2(400, 300));
                        FloatingWindows.Add(newFw);
                        _dragSourceNode = newNode;
                        _dragSourceWindow = newFw;
                        CleanupTree();
                    });

                // Full-height separator on the left edge of every tab after the first.
                if (i > 0)
                    paper.Box($"t_div_{id}_{i}")
                        .PositionType(PositionType.SelfDirected).Position(tx, 0).Size(1, tabH)
                        .IsNotInteractable().BackgroundColor(borderColor);

                bool hasIcon = !string.IsNullOrEmpty(tab.Icon);
                float contentX = tx + m.TabPadding;

                if (hasIcon && font != null)
                {
                    paper.Box($"t_ico_{id}_{i}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(contentX, 0).Size(iconW, tabH)
                        .IsNotInteractable()
                        .Text(tab.Icon, font)
                        .TextColor(isActive ? theme.Primary.C700 : theme.Ink.C300)
                        .FontSize(tabIconSize)
                        .Alignment(TextAlignment.MiddleCenter);
                    contentX += iconW + iconGap;
                }

                bool showClose = isActive;
                if (font != null)
                {
                    float lblW = tw - (contentX - tx) - m.TabPadding - (showClose ? m.TabCloseSize + 4 : 0);
                    paper.Box($"t_lbl_{id}_{i}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(contentX, 0).Height(tabH).Width(lblW)
                        .IsNotInteractable()
                        .Text(tab.Title, font)
                        .TextColor(isActive ? theme.Ink.C500 : theme.Ink.C300)
                        .FontSize(m.FontSize)
                        .Alignment(TextAlignment.MiddleLeft);
                }

                if (showClose)
                {
                    // The icon font's glyphs are empty, so stroke the "x" onto the canvas.
                    var closeCol = theme.Ink.C400;
                    float closeSz = m.TabCloseSize;
                    paper.Box($"t_close_{id}_{i}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(tx + tw - closeSz - m.TabPadding + 2, (tabH - closeSz) / 2)
                        .Size(closeSz, closeSz)
                        .Rounded(closeSz / 2)
                        .Hovered.BackgroundColor(OrigamiTheme.WithAlpha(theme.Primary.C500, 60)).End()
                        .StopEventPropagation()
                        .OnClick((node, ci, fw), (cap, e) => cap.Item1.RemoveTab(cap.Item2))
                        .OnPostLayout((h2, r2) => paper.Draw(ref h2, (canvas, rr) =>
                        {
                            float ccx = (float)(rr.Min.X + rr.Size.X / 2);
                            float ccy = (float)(rr.Min.Y + rr.Size.Y / 2);
                            float q = closeSz * 0.22f;
                            canvas.SaveState();
                            canvas.SetStrokeColor(closeCol);
                            canvas.SetStrokeWidth(1.3f);
                            canvas.SetStrokeCap(EndCapStyle.Round);
                            canvas.BeginPath();
                            canvas.MoveTo(ccx - q, ccy - q); canvas.LineTo(ccx + q, ccy + q);
                            canvas.MoveTo(ccx + q, ccy - q); canvas.LineTo(ccx - q, ccy + q);
                            canvas.Stroke();
                            canvas.RestoreState();
                        }));
                }

                if (isActive)
                    paper.Box($"t_ul_{id}")
                        .PositionType(PositionType.SelfDirected).Position(tx, tabH - 2).Size(tw, 2)
                        .IsNotInteractable()
                        .BackgroundColor(theme.Primary.C500)
                        .BoxShadow(0, 0, 8, 0, OrigamiTheme.WithAlpha(theme.Primary.C500, 140));
            }

            // Trailing separator on the right edge of the last tab, so the final tab (and a lone tab)
            // is delimited from the empty bar / header controls to its right.
            {
                int last = node.Tabs.Count - 1;
                float rx = tabPositions[last] + tabWidths[last];
                paper.Box($"t_div_{id}_end")
                    .PositionType(PositionType.SelfDirected).Position(rx, 0).Size(1, tabH)
                    .IsNotInteractable().BackgroundColor(borderColor);
            }

            // Panel-supplied header controls, right-aligned in the tab bar (e.g. a refresh button).
            var activePanel = node.ActiveTabIndex < node.Tabs.Count ? node.Tabs[node.ActiveTabIndex] : null;
            float hdrW = activePanel?.HeaderWidth ?? 0f;
            if (activePanel != null && hdrW > 0f)
            {
                using (paper.Row($"leaf_hdr_{id}")
                    .PositionType(PositionType.SelfDirected).Position(w - hdrW - 4, 0).Size(hdrW, tabH)
                    .Enter())
                    activePanel.OnHeaderContent(paper, hdrW, tabH);
            }

            // Hairline under the tab bar.
            paper.Box($"leaf_tabdiv_{id}")
                .PositionType(PositionType.SelfDirected).Position(0, tabH).Size(w, 1)
                .IsNotInteractable().BackgroundColor(borderColor);

            // Content area (clipped to the panel body).
            float ch = h - tabH;
            if (ch > 0)
            {
                using (paper.Box($"c_{id}")
                    .PositionType(PositionType.SelfDirected).Position(0, tabH).Size(w, ch)
                    .RoundedBottom(cr).Clip() // round the content to the window's bottom corners
                    .Enter())
                {
                    if (node.ActiveTabIndex < node.Tabs.Count)
                        node.Tabs[node.ActiveTabIndex].OnGUI(paper, w, ch);
                }
            }

            // Outer glass border, drawn last so it sits above the body and content edges.
            paper.Box($"leaf_border_{id}")
                .PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h)
                .IsNotInteractable()
                .BorderColor(borderColor).BorderWidth(1).Rounded(cr);
        }
    }

    // ================================================================
    //  FLOATING WINDOWS
    // ================================================================

    private const float ResizeHandleSize = 3f;
    private const float ResizeCornerSize = 8f;
    private const float MinWindowWidth = 150f;
    private const float MinWindowHeight = 80f;

    private void DrawFloatingWindow(Paper paper, FloatingWindow fw, int index,
                                     OrigamiTheme theme, OrigamiMetrics m, OrigamiIcons icons, FontFile? font)
    {
        using (paper.Box($"fw_{index}")
            .PositionType(PositionType.SelfDirected)
            .Position(fw.Position.X, fw.Position.Y)
            .Size(fw.Size.X, fw.Size.Y)
            .OnClick(index, (idx, e) => BringToFront(idx))
            .Enter())
        {
            DrawNodeTree(paper, fw.Node, null, 0, 0, fw.Size.X, fw.Size.Y, fw, theme, m, icons, font);

            // Resize handles
            float w = fw.Size.X, h = fw.Size.Y;
            float s = ResizeHandleSize;
            float cs = ResizeCornerSize;

            // Edges
            ResizeHandle(paper, $"fw_r_{index}", fw, w - s, cs, s, h - cs * 2, true, false, false, false);
            ResizeHandle(paper, $"fw_b_{index}", fw, cs, h - s, w - cs * 2, s, false, true, false, false);
            ResizeHandle(paper, $"fw_l_{index}", fw, 0, cs, s, h - cs * 2, false, false, true, false);
            ResizeHandle(paper, $"fw_t_{index}", fw, cs, 0, w - cs * 2, s, false, false, false, true);

            // Corners (slightly larger hit area)
            ResizeHandle(paper, $"fw_br_{index}", fw, w - cs, h - cs, cs, cs, true, true, false, false);
            ResizeHandle(paper, $"fw_bl_{index}", fw, 0, h - cs, cs, cs, false, true, true, false);
            ResizeHandle(paper, $"fw_tr_{index}", fw, w - cs, 0, cs, cs, true, false, false, true);
            ResizeHandle(paper, $"fw_tl_{index}", fw, 0, 0, cs, cs, false, false, true, true);
        }
    }

    private void ResizeHandle(Paper paper, string id, FloatingWindow fw,
                               float x, float y, float w, float h,
                               bool right, bool bottom, bool left, bool top)
    {
        paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y).Size(w, h)
            .Hovered.BackgroundColor(Color.FromArgb(60, 51, 122, 183)).End()
            .OnDragging(fw, (captured, e) =>
            {
                Float2 delta = e.Delta;

                if (right)
                    captured.Size = new Float2(Math.Max(MinWindowWidth, captured.Size.X + delta.X), captured.Size.Y);

                if (bottom)
                    captured.Size = new Float2(captured.Size.X, Math.Max(MinWindowHeight, captured.Size.Y + delta.Y));

                if (left)
                {
                    float newW = Math.Max(MinWindowWidth, captured.Size.X - delta.X);
                    float actualDelta = captured.Size.X - newW;
                    captured.Position += new Float2(actualDelta, 0);
                    captured.Size = new Float2(newW, captured.Size.Y);
                }

                if (top)
                {
                    float newH = Math.Max(MinWindowHeight, captured.Size.Y - delta.Y);
                    float actualDelta = captured.Size.Y - newH;
                    captured.Position += new Float2(0, actualDelta);
                    captured.Size = new Float2(captured.Size.X, newH);
                }
            });
    }

    private void BringToFront(int index)
    {
        if (index < 0 || index >= FloatingWindows.Count) return;
        var fw = FloatingWindows[index];
        FloatingWindows.RemoveAt(index);
        FloatingWindows.Add(fw);
    }

    // ================================================================
    //  DOCK ZONE
    // ================================================================

    private void ComputeHoveredZone(Paper paper, OrigamiMetrics m)
    {
        Float2 mouse = paper.PointerPos;

        // Floating windows are drawn on top -> check them front-to-back first. A window under the cursor
        // captures the hover, so the root (and any window behind it) is ignored.
        for (int i = FloatingWindows.Count - 1; i >= 0; i--)
        {
            var fw = FloatingWindows[i];
            if (fw == _dragSourceWindow) continue;   // the window we're dragging follows the cursor; ignore it
            if (!Hit(mouse, fw.Position.X, fw.Position.Y, fw.Size.X, fw.Size.Y)) continue;
            HoverLeaves(mouse, m, fw);
            return;   // block fall-through even if the cursor sits on a splitter gap inside the window
        }

        // Root edge zones (dock to the outer edges of the whole space).
        float edgeW = m.IndicatorSize + m.IndicatorGap;
        float bx = _dockSpaceBounds.Min.X, by = _dockSpaceBounds.Min.Y;
        float bw = _dockSpaceBounds.Size.X, bh = _dockSpaceBounds.Size.Y;
        if (mouse.X >= bx && mouse.X <= bx + bw && mouse.Y >= by && mouse.Y <= by + bh)
        {
            if (mouse.Y >= by && mouse.Y <= by + edgeW) { _rootHoveredZone = DockZone.RootTop; return; }
            if (mouse.Y >= by + bh - edgeW && mouse.Y <= by + bh) { _rootHoveredZone = DockZone.RootBottom; return; }
            if (mouse.X >= bx && mouse.X <= bx + edgeW) { _rootHoveredZone = DockZone.RootLeft; return; }
            if (mouse.X >= bx + bw - edgeW && mouse.X <= bx + bw) { _rootHoveredZone = DockZone.RootRight; return; }
        }
        _rootHoveredZone = DockZone.None;

        // Root leaves.
        HoverLeaves(mouse, m, null);
    }

    // Resolve the hovered leaf + zone among the leaves owned by <paramref name="owner"/> (null = root tree):
    // tab-bar insertion takes priority, then the center/edge dock indicators.
    private void HoverLeaves(Float2 mouse, OrigamiMetrics m, FloatingWindow? owner)
    {
        foreach (var (node, tb) in _tabBars)
        {
            if (_leafOwner.GetValueOrDefault(node) != owner || node == _dragSourceNode) continue;
            if (!Hit(mouse, (float)tb.Bar.Min.X, (float)tb.Bar.Min.Y, (float)tb.Bar.Size.X, (float)tb.Bar.Size.Y)) continue;
            var edges = tb.Edges;
            int idx = edges.Length - 1;
            float caret = edges[edges.Length - 1];
            for (int t = 0; t < edges.Length - 1; t++)
            {
                float mid = (edges[t] + edges[t + 1]) / 2f;
                if (mouse.X < mid) { idx = t; caret = edges[t]; break; }
            }
            _hoveredLeaf = node;
            _hoveredLeafRect = _leafRects.TryGetValue(node, out var lr) ? lr : tb.Bar;
            _hoveredZone = DockZone.Tab;
            _hoveredTabIndex = idx;
            _hoveredTabCaretX = caret;
            return;
        }

        foreach (var (node, rect) in _leafRects)
        {
            if (_leafOwner.GetValueOrDefault(node) != owner) continue;
            if (_dragSourceWindow != null && _dragSourceNode == node) continue;
            if (mouse.X < rect.Min.X || mouse.X > rect.Max.X || mouse.Y < rect.Min.Y || mouse.Y > rect.Max.Y) continue;
            _hoveredLeaf = node;
            _hoveredLeafRect = rect;
            float cx = rect.Min.X + rect.Size.X / 2;
            float cy = rect.Min.Y + rect.Size.Y / 2;
            // Floating windows stay single-leaf (tabs only) — offer just the center (add-as-tab), no split.
            _hoveredZone = GetZoneFromIndicator(mouse, cx, cy, m, centerOnly: owner != null);
            return;
        }
    }

    private DockZone GetZoneFromIndicator(Float2 mouse, float cx, float cy, OrigamiMetrics m, bool centerOnly = false)
    {
        float s = m.IndicatorSize, g = m.IndicatorGap, hs = s / 2;
        if (Hit(mouse, cx - hs, cy - hs, s, s)) return DockZone.Center;
        if (centerOnly) return DockZone.None;
        if (Hit(mouse, cx - hs, cy - hs - g - s, s, s)) return DockZone.Top;
        if (Hit(mouse, cx - hs, cy + hs + g, s, s)) return DockZone.Bottom;
        if (Hit(mouse, cx - hs - g - s, cy - hs, s, s)) return DockZone.Left;
        if (Hit(mouse, cx + hs + g, cy - hs, s, s)) return DockZone.Right;
        return DockZone.None;
    }

    private static bool Hit(Float2 p, float x, float y, float w, float h)
        => p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;

    // A vertical accent caret marking where a dragged tab will be inserted in the hovered tab bar.
    private void DrawTabInsertionCaret(Paper paper, OrigamiTheme theme)
    {
        if (_hoveredLeaf == null || !_tabBars.TryGetValue(_hoveredLeaf, out var tb)) return;
        float barTop = (float)tb.Bar.Min.Y;
        float barH = (float)tb.Bar.Size.Y;
        paper.Box("tab_caret")
            .PositionType(PositionType.SelfDirected)
            .Position(_hoveredTabCaretX - 1.5f, barTop + 2)
            .Size(3, barH - 4)
            .Layer(Layer.Topmost)
            .IsNotInteractable()
            .Rounded(1.5f)
            .BackgroundColor(theme.Primary.C500)
            .BoxShadow(0, 0, 8, 0, OrigamiTheme.WithAlpha(theme.Primary.C500, 140));
    }

    // ================================================================
    //  INDICATORS + PREVIEW
    // ================================================================

    private void DrawDockIndicators(Paper paper, OrigamiMetrics m, OrigamiIcons icons, FontFile? font)
    {
        // Animated drop preview runs every frame so fade-out + snap-on-reappear works.
        DrawDropPreview(paper, m);

        if (_hoveredLeaf == null) return;
        var rect = _hoveredLeafRect;
        float cx = rect.Min.X + rect.Size.X / 2, cy = rect.Min.Y + rect.Size.Y / 2;
        float s = m.IndicatorSize, g = m.IndicatorGap, hs = s / 2;

        var theme = Origami.Current;
        var bg = Color.FromArgb(85, theme.Blue.C400);
        var hi = Color.FromArgb(85, theme.Blue.C500);
        var bd = Color.FromArgb(85, theme.Blue.C600);

        Ind(paper, "di_c", cx - hs, cy - hs, s, _hoveredZone == DockZone.Center ? hi : bg, bd, icons.Duplicate, font);

        // Floating windows stay single-leaf (add-as-tab only), so don't offer the split arrows.
        if (_leafOwner.GetValueOrDefault(_hoveredLeaf) != null) return;

        Ind(paper, "di_t", cx - hs, cy - hs - g - s, s, _hoveredZone == DockZone.Top ? hi : bg, bd, icons.ArrowUp, font);
        Ind(paper, "di_b", cx - hs, cy + hs + g, s, _hoveredZone == DockZone.Bottom ? hi : bg, bd, icons.ArrowDown, font);
        Ind(paper, "di_l", cx - hs - g - s, cy - hs, s, _hoveredZone == DockZone.Left ? hi : bg, bd, icons.ArrowLeft, font);
        Ind(paper, "di_r", cx + hs + g, cy - hs, s, _hoveredZone == DockZone.Right ? hi : bg, bd, icons.ArrowRight, font);
    }

    private void DrawRootDockIndicators(Paper paper, OrigamiMetrics m, OrigamiTheme theme)
    {
        if (_rootHoveredZone == DockZone.None) return;

        float bx = _dockSpaceBounds.Min.X, by = _dockSpaceBounds.Min.Y;
        float bw = _dockSpaceBounds.Size.X, bh = _dockSpaceBounds.Size.Y;
        float stripSize = m.IndicatorSize + m.IndicatorGap;

        float hx = bx, hy = by, hw = bw, hh = bh;
        switch (_rootHoveredZone)
        {
            case DockZone.RootTop:    hh = stripSize; break;
            case DockZone.RootBottom: hy = by + bh - stripSize; hh = stripSize; break;
            case DockZone.RootLeft:   hw = stripSize; break;
            case DockZone.RootRight:  hx = bx + bw - stripSize; hw = stripSize; break;
        }

        paper.Box("root_dock_highlight")
            .PositionType(PositionType.SelfDirected)
            .Position(hx, hy).Size(hw, hh)
            .IsNotInteractable()
            .BackgroundColor(Color.FromArgb(60, theme.Blue.C400))
            .BorderColor(Color.FromArgb(120, theme.Blue.C500)).BorderWidth(2);

        // Also run the drop preview animation for root zones
        DrawDropPreview(paper, m);
    }

    private void Ind(Paper paper, string id, float x, float y, float s, Color bg, Color bd, IOrigamiIcon? icon, FontFile? font)
    {
        var inkNearWhite = Origami.Current.Ink.C500;
        var iconCol = Color.FromArgb(230, inkNearWhite.R, inkNearWhite.G, inkNearWhite.B);
        paper.Box(id).PositionType(PositionType.SelfDirected).Position(x, y).Size(s, s)
            .BackgroundColor(bg).BorderColor(bd).BorderWidth(1).Rounded(4)
            .OnPostLayout((h, r) => paper.Draw(ref h, (canvas, rr) =>
            {
                float inset = (float)rr.Size.X * 0.28f;
                var inner = new Rect(new Float2(rr.Min.X + inset, rr.Min.Y + inset), new Float2(rr.Max.X - inset, rr.Max.Y - inset));
                icon?.Draw(canvas, inner, iconCol);
            }));
    }

    private void DrawDropPreview(Paper paper, OrigamiMetrics m)
    {
        // Compute the target rect this frame, or null when no zone is hovered.
        Rect? target = null;

        // Check root zones first
        if (_rootHoveredZone != DockZone.None)
        {
            float bx = _dockSpaceBounds.Min.X, by = _dockSpaceBounds.Min.Y;
            float bw = _dockSpaceBounds.Size.X, bh = _dockSpaceBounds.Size.Y;
            float hx = bx, hy = by, hw = bw, hh = bh;
            switch (_rootHoveredZone)
            {
                case DockZone.RootTop:    hh *= 0.25f; break;
                case DockZone.RootBottom: hy += hh * 0.75f; hh *= 0.25f; break;
                case DockZone.RootLeft:   hw *= 0.25f; break;
                case DockZone.RootRight:  hx += hw * 0.75f; hw *= 0.25f; break;
            }
            target = new Rect(new Float2(hx, hy), new Float2(hx + hw, hy + hh));
        }
        else if (_hoveredLeaf != null && _hoveredZone != DockZone.None)
        {
            var r = _hoveredLeafRect;
            float hx = r.Min.X, hy = r.Min.Y, hw = r.Size.X, hh = r.Size.Y;
            switch (_hoveredZone)
            {
                case DockZone.Top:    hh *= 0.5f; break;
                case DockZone.Bottom: hy += hh * 0.5f; hh *= 0.5f; break;
                case DockZone.Left:   hw *= 0.5f; break;
                case DockZone.Right:  hx += hw * 0.5f; hw *= 0.5f; break;
            }
            target = new Rect(new Float2(hx, hy), new Float2(hx + hw, hy + hh));
        }

        // Exponential smoothing frame-rate independent. `tMove` follows the target rect;
        // `tAlpha` fades the preview in/out. The chosen rates feel snappy (~120ms to settle)
        // without the perceptible lag of a slower interpolation.
        float dt = MathF.Max(0f, _lastDeltaTime);
        float tMove  = 1f - MathF.Exp(-dt * 18f);
        float tAlpha = 1f - MathF.Exp(-dt * 20f);

        if (target.HasValue)
        {
            if (!_previewVisible)
            {
                // Fully hidden -> snap to target on first appearance. The user sees the preview
                // arrive at the right zone immediately; subsequent zone changes animate.
                _previewRect = target.Value;
                _previewVisible = true;
            }
            else
            {
                _previewRect = LerpRect(_previewRect, target.Value, tMove);
            }
            _previewAlpha += (1f - _previewAlpha) * tAlpha;
        }
        else
        {
            _previewAlpha += (0f - _previewAlpha) * tAlpha;
            if (_previewAlpha < 0.01f)
            {
                _previewAlpha = 0f;
                _previewVisible = false;
            }
        }

        if (_previewAlpha <= 0.01f) return;

        int fillA = (int)(40f * _previewAlpha);
        int borderA = (int)(120f * _previewAlpha);
        paper.Box("drop_preview")
            .PositionType(PositionType.SelfDirected)
            .Position((float)_previewRect.Min.X, (float)_previewRect.Min.Y)
            .Size((float)_previewRect.Size.X, (float)_previewRect.Size.Y)
            .IsNotInteractable()
            .BackgroundColor(Color.FromArgb(fillA, 51, 122, 183))
            .BorderColor(Color.FromArgb(borderA, 51, 122, 183)).BorderWidth(2);
    }

    private static Rect LerpRect(Rect a, Rect b, float t)
    {
        float minX = (float)(a.Min.X + (b.Min.X - a.Min.X) * t);
        float minY = (float)(a.Min.Y + (b.Min.Y - a.Min.Y) * t);
        float maxX = (float)(a.Max.X + (b.Max.X - a.Max.X) * t);
        float maxY = (float)(a.Max.Y + (b.Max.Y - a.Max.Y) * t);
        return new Rect(new Float2(minX, minY), new Float2(maxX, maxY));
    }

    // ================================================================
    //  DROP
    // ================================================================

    private void ExecuteDrop(OrigamiMetrics m)
    {
        if (_draggedPanel == null || _dragSourceNode == null) return;

        // Handle root-level docking
        if (_rootHoveredZone != DockZone.None)
        {
            // Remove tab from source
            int srcIdx = _dragSourceNode.Tabs!.IndexOf(_draggedPanel);
            if (srcIdx < 0) return;
            _dragSourceNode.RemoveTab(srcIdx);

            // Create new leaf with the panel
            var newLeaf = DockNode.Leaf(_draggedPanel);

            // Split at root level with 25/75 ratio
            SplitDirection dir;
            bool newFirst;
            switch (_rootHoveredZone)
            {
                case DockZone.RootLeft:
                    dir = SplitDirection.Horizontal;
                    newFirst = true;
                    break;
                case DockZone.RootRight:
                    dir = SplitDirection.Horizontal;
                    newFirst = false;
                    break;
                case DockZone.RootTop:
                    dir = SplitDirection.Vertical;
                    newFirst = true;
                    break;
                case DockZone.RootBottom:
                    dir = SplitDirection.Vertical;
                    newFirst = false;
                    break;
                default:
                    return;
            }

            Root = DockNode.Split(dir, newFirst ? 0.25f : 0.75f,
                newFirst ? newLeaf : Root,
                newFirst ? Root : newLeaf);

            CleanupTree();
            return;
        }

        if (_hoveredLeaf != null && _hoveredZone != DockZone.None)
        {
            // Don't dock onto self
            if (_hoveredLeaf == _dragSourceNode) return;

            // Remove from source (which is always a floating window now)
            int srcIdx = _dragSourceNode.Tabs!.IndexOf(_draggedPanel);
            if (srcIdx < 0) return;
            _dragSourceNode.RemoveTab(srcIdx);

            // Dock it
            if (_hoveredZone == DockZone.Center)
                _hoveredLeaf.InsertTab(_draggedPanel);
            else if (_hoveredZone == DockZone.Tab)
                _hoveredLeaf.InsertTab(_draggedPanel, _hoveredTabIndex);   // between-tabs insert / reorder
            else
                SplitLeaf(_hoveredLeaf, _draggedPanel, _hoveredZone);

            CleanupTree();
        }
        // Otherwise: dropped on nothing -> floating window stays where it is
    }

    private void SplitLeaf(DockNode target, DockPanel panel, DockZone zone)
    {
        var newLeaf = DockNode.Leaf(panel);
        var dir = (zone == DockZone.Left || zone == DockZone.Right) ? SplitDirection.Horizontal : SplitDirection.Vertical;
        bool newFirst = zone == DockZone.Left || zone == DockZone.Top;
        var split = DockNode.Split(dir, 0.5f, newFirst ? newLeaf : target, newFirst ? target : newLeaf);
        ReplaceInTree(target, split);
    }

    // ================================================================
    //  TREE
    // ================================================================

    private void ReplaceInTree(DockNode target, DockNode replacement)
    {
        if (target.Parent != null)
        {
            target.Parent.ReplaceChild(target, replacement);
            replacement.Parent = target.Parent;
            return;
        }
        if (Root == target) { Root = replacement; return; }
        for (int i = 0; i < FloatingWindows.Count; i++)
            if (FloatingWindows[i].Node == target) { FloatingWindows[i].Node = replacement; return; }
    }

    private void CleanupTree()
    {
        Root = Cleanup(Root);
        for (int i = FloatingWindows.Count - 1; i >= 0; i--)
        {
            FloatingWindows[i].Node = Cleanup(FloatingWindows[i].Node);
            if (FloatingWindows[i].Node.IsLeaf && (FloatingWindows[i].Node.Tabs == null || FloatingWindows[i].Node.Tabs.Count == 0))
                FloatingWindows.RemoveAt(i);
        }
    }

    private DockNode Cleanup(DockNode node)
    {
        if (node.IsLeaf) return node;
        node.ChildA = Cleanup(node.ChildA!);
        node.ChildB = Cleanup(node.ChildB!);
        bool ae = node.ChildA.IsLeaf && (node.ChildA.Tabs == null || node.ChildA.Tabs.Count == 0);
        bool be = node.ChildB.IsLeaf && (node.ChildB.Tabs == null || node.ChildB.Tabs.Count == 0);
        if (ae && be) return DockNode.Leaf();
        if (ae) return node.ChildB;
        if (be) return node.ChildA;
        return node;
    }
}
