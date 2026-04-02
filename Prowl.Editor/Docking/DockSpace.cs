using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Docking;

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

    // --- Splitter ---
    private DockNode? _splitterDragNode;

    // --- Layout cache ---
    private readonly Dictionary<DockNode, Rect> _leafRects = new();

    private const float IndicatorSize = 28f;
    private const float IndicatorGap = 4f;

    public DockSpace(DockNode root) { Root = root; }

    public void Draw(Paper paper, float x, float y, float w, float h)
    {
        bool mouseUp = !paper.IsPointerDown(PaperMouseBtn.Left);

        // Handle drag end
        if (_isDragging && mouseUp)
        {
            ExecuteDrop();
            _isDragging = false;
            _draggedPanel = null;
            _dragSourceNode = null;
            _dragSourceWindow = null;
        }

        // Clear per-frame state
        _leafRects.Clear();
        _hoveredLeaf = null;
        _hoveredZone = DockZone.None;

        // 1. Root dock space
        DrawNodeTree(paper, Root, null, x, y, w, h, null);

        // 2. Floating windows (z-ordered)
        for (int i = 0; i < FloatingWindows.Count; i++)
            DrawFloatingWindow(paper, FloatingWindows[i], i);

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

            ComputeHoveredZone(paper);
            if (_hoveredLeaf != null)
                DrawDockIndicators(paper);
        }
    }

    // ================================================================
    //  NODE TREE
    // ================================================================

    private void DrawNodeTree(Paper paper, DockNode node, DockNode? parent,
                               float x, float y, float w, float h, FloatingWindow? fw)
    {
        if (node == null || w <= 0 || h <= 0) return;
        node.Parent = parent;

        if (node.IsLeaf)
        {
            DrawLeaf(paper, node, x, y, w, h, fw);
            return;
        }

        float sp = EditorTheme.SplitterSize;
        if (node.Direction == SplitDirection.Horizontal)
        {
            float aw = (w - sp) * node.SplitRatio;
            float bw = w - aw - sp;
            DrawNodeTree(paper, node.ChildA!, node, x, y, aw, h, fw);
            DrawSplitter(paper, node, x + aw, y, sp, h, true);
            DrawNodeTree(paper, node.ChildB!, node, x + aw + sp, y, bw, h, fw);
        }
        else
        {
            float ah = (h - sp) * node.SplitRatio;
            float bh = h - ah - sp;
            DrawNodeTree(paper, node.ChildA!, node, x, y, w, ah, fw);
            DrawSplitter(paper, node, x, y + ah, w, sp, false);
            DrawNodeTree(paper, node.ChildB!, node, x, y + ah + sp, w, bh, fw);
        }
    }

    // ================================================================
    //  SPLITTER
    // ================================================================

    private void DrawSplitter(Paper paper, DockNode node, float x, float y, float w, float h, bool horiz)
    {
        bool active = _splitterDragNode == node;
        paper.Box($"spl_{node.GetHashCode()}")
            .PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .BackgroundColor(active ? EditorTheme.SplitterHovered : EditorTheme.Splitter)
            .Hovered.BackgroundColor(EditorTheme.SplitterHovered).End()
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

    private void DrawLeaf(Paper paper, DockNode node, float x, float y, float w, float h, FloatingWindow? fw)
    {
        if (node.Tabs == null || node.Tabs.Count == 0) return;
        float tabH = EditorTheme.TabBarHeight;

        // Only root-docked panels are valid dock targets (not floating windows)
        if (fw == null)
            _leafRects[node] = new Rect(new Float2(x, y), new Float2(x + w, y + h));

        DrawTabBar(paper, node, x, y, w, tabH, fw);

        float cy = y + tabH, ch = h - tabH;
        if (ch <= 0) return;
        using (paper.Box($"c_{node.GetHashCode()}")
            .PositionType(PositionType.SelfDirected).Position(x, cy).Size(w, ch)
            .BackgroundColor(EditorTheme.PanelBackground).Enter())
        {
            if (node.ActiveTabIndex < node.Tabs.Count)
                node.Tabs[node.ActiveTabIndex].OnGUI(paper, w, ch);
        }
    }

    private void DrawTabBar(Paper paper, DockNode node, float x, float y, float w, float tabH, FloatingWindow? fw)
    {
        using (paper.Row($"tb_{node.GetHashCode()}")
            .PositionType(PositionType.SelfDirected).Position(x, y).Size(w, tabH)
            .BackgroundColor(EditorTheme.HeaderBackground)
            .Enter())
        {
            for (int i = 0; i < node.Tabs!.Count; i++)
            {
                var tab = node.Tabs[i];
                bool isActive = i == node.ActiveTabIndex;
                int ci = i;

                var el = paper.Box($"t_{node.GetHashCode()}_{i}")
                    .Height(tabH).ChildLeft(10).ChildRight(10)
                    .BackgroundColor(isActive ? EditorTheme.TabActive : EditorTheme.TabInactive)
                    .Hovered.BackgroundColor(EditorTheme.TabHovered).End()
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

                        if (srcFw != null)
                        {
                            // Already in a floating window — just drag it
                            _dragSourceNode = srcNode;
                            _dragSourceWindow = srcFw;
                            int fwIdx = FloatingWindows.IndexOf(srcFw);
                            if (fwIdx >= 0) BringToFront(fwIdx);
                        }
                        else
                        {
                            // Docked tab — immediately undock into a new floating window
                            srcNode.RemoveTab(srcIdx);
                            var newNode = DockNode.Leaf(_draggedPanel);
                            var newFw = new FloatingWindow(newNode,
                                e.PointerPosition - new Float2(100, 15),
                                new Float2(400, 300));
                            FloatingWindows.Add(newFw);
                            _dragSourceNode = newNode;
                            _dragSourceWindow = newFw;
                            CleanupTree();
                        }
                    });

                if (EditorTheme.DefaultFont != null)
                    el.Text(tab.Title, EditorTheme.DefaultFont)
                        .TextColor(isActive ? EditorTheme.Text : EditorTheme.TextDim)
                        .FontSize(EditorTheme.FontSize);
            }
        }
    }

    // ================================================================
    //  FLOATING WINDOWS
    // ================================================================

    private const float ResizeHandleSize = 3f;
    private const float ResizeCornerSize = 8f;
    private const float MinWindowWidth = 150f;
    private const float MinWindowHeight = 80f;

    private void DrawFloatingWindow(Paper paper, FloatingWindow fw, int index)
    {
        using (paper.Box($"fw_{index}")
            .PositionType(PositionType.SelfDirected)
            .Position(fw.Position.X, fw.Position.Y)
            .Size(fw.Size.X, fw.Size.Y)
            .BackgroundColor(EditorTheme.WindowBackground)
            .BorderColor(EditorTheme.Border).BorderWidth(1).Rounded(4)
            .OnClick(index, (idx, e) => BringToFront(idx))
            .Enter())
        {
            // Close button in top-right corner
            float closeSize = 18f;
            var closeBtn = paper.Box($"fw_close_{index}")
                .PositionType(PositionType.SelfDirected)
                .Position(fw.Size.X - closeSize - 4, 4)
                .Size(closeSize, closeSize)
                .Rounded(9)
                .BackgroundColor(Color.Transparent)
                .Hovered.BackgroundColor(Color.FromArgb(255, 200, 60, 60)).End()
                .StopEventPropagation()
                .OnClick(index, (idx, e) =>
                {
                    // Dock the panel back or remove the window
                    if (idx >= 0 && idx < FloatingWindows.Count)
                        FloatingWindows.RemoveAt(idx);
                });

            if (EditorTheme.DefaultFont != null)
                closeBtn.Text("\u2715", EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.TextDim).FontSize(10f);

            DrawNodeTree(paper, fw.Node, null, 0, 0, fw.Size.X, fw.Size.Y, fw);

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

    private void ComputeHoveredZone(Paper paper)
    {
        Float2 mouse = paper.PointerPos;
        foreach (var (node, rect) in _leafRects)
        {
            // Don't show dock indicators on the panel we're dragging from
            // (if it's a single-tab floating window being moved)
            if (_dragSourceWindow != null && _dragSourceNode == node) continue;

            if (mouse.X < rect.Min.X || mouse.X > rect.Max.X ||
                mouse.Y < rect.Min.Y || mouse.Y > rect.Max.Y) continue;

            _hoveredLeaf = node;
            _hoveredLeafRect = rect;
            float cx = rect.Min.X + rect.Size.X / 2;
            float cy = rect.Min.Y + rect.Size.Y / 2;
            _hoveredZone = GetZoneFromIndicator(mouse, cx, cy);
            return;
        }
    }

    private DockZone GetZoneFromIndicator(Float2 m, float cx, float cy)
    {
        float s = IndicatorSize, g = IndicatorGap, hs = s / 2;
        if (Hit(m, cx - hs, cy - hs, s, s)) return DockZone.Center;
        if (Hit(m, cx - hs, cy - hs - g - s, s, s)) return DockZone.Top;
        if (Hit(m, cx - hs, cy + hs + g, s, s)) return DockZone.Bottom;
        if (Hit(m, cx - hs - g - s, cy - hs, s, s)) return DockZone.Left;
        if (Hit(m, cx + hs + g, cy - hs, s, s)) return DockZone.Right;
        return DockZone.None;
    }

    private static bool Hit(Float2 p, float x, float y, float w, float h)
        => p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;

    // ================================================================
    //  INDICATORS + PREVIEW
    // ================================================================

    private void DrawDockIndicators(Paper paper)
    {
        if (_hoveredLeaf == null) return;
        var rect = _hoveredLeafRect;
        float cx = rect.Min.X + rect.Size.X / 2, cy = rect.Min.Y + rect.Size.Y / 2;
        float s = IndicatorSize, g = IndicatorGap, hs = s / 2;

        var bg = Color.FromArgb(200, 50, 50, 55);
        var hi = EditorTheme.Accent;
        var bd = Color.FromArgb(255, 80, 80, 85);

        if (_hoveredZone != DockZone.None) DrawDropPreview(paper);

        Ind(paper, "di_c", cx - hs, cy - hs, s, _hoveredZone == DockZone.Center ? hi : bg, bd);
        Ind(paper, "di_t", cx - hs, cy - hs - g - s, s, _hoveredZone == DockZone.Top ? hi : bg, bd);
        Ind(paper, "di_b", cx - hs, cy + hs + g, s, _hoveredZone == DockZone.Bottom ? hi : bg, bd);
        Ind(paper, "di_l", cx - hs - g - s, cy - hs, s, _hoveredZone == DockZone.Left ? hi : bg, bd);
        Ind(paper, "di_r", cx + hs + g, cy - hs, s, _hoveredZone == DockZone.Right ? hi : bg, bd);
    }

    private void Ind(Paper paper, string id, float x, float y, float s, Color bg, Color bd)
    {
        paper.Box(id).PositionType(PositionType.SelfDirected).Position(x, y).Size(s, s)
            .BackgroundColor(bg).BorderColor(bd).BorderWidth(1).Rounded(4);
    }

    private void DrawDropPreview(Paper paper)
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
        paper.Box("drop_preview").PositionType(PositionType.SelfDirected).Position(hx, hy).Size(hw, hh)
            .BackgroundColor(Color.FromArgb(40, 51, 122, 183))
            .BorderColor(Color.FromArgb(120, 51, 122, 183)).BorderWidth(2);
    }

    // ================================================================
    //  DROP
    // ================================================================

    private void ExecuteDrop()
    {
        if (_draggedPanel == null || _dragSourceNode == null) return;

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
            else
                SplitLeaf(_hoveredLeaf, _draggedPanel, _hoveredZone);

            CleanupTree();
        }
        // Otherwise: dropped on nothing → floating window stays where it is
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
