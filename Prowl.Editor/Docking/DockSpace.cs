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

        // Clean up any empty leaves from closed tabs
        CleanupTree();

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
            .BackgroundColor(Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.SplitterHovered).End()
            .Active.BackgroundColor(EditorTheme.SplitterHovered).End()
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

    private const float TabPadding = 12f;
    private const float TabCloseSize = 14f;
    private const float TabRadius = 8f;
    private const float TabInsetRadius = 10f;
    private const float TabGap = 0f;

    private void DrawLeaf(Paper paper, DockNode node, float x, float y, float w, float h, FloatingWindow? fw)
    {
        if (node.Tabs == null || node.Tabs.Count == 0) return;
        float tabH = EditorTheme.TabBarHeight;

        if (fw == null)
            _leafRects[node] = new Rect(new Float2(x, y), new Float2(x + w, y + h));

        // Single container for the whole leaf — we draw the merged tab+panel shape via canvas
        using (paper.Box($"leaf_{node.GetHashCode()}")
            .PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .Enter())
        {
            var font = EditorTheme.DefaultFont;

            // Compute tab widths based on text
            float iconW = 10f; // Space for icon
            float[] tabWidths = new float[node.Tabs.Count];
            for (int i = 0; i < node.Tabs.Count; i++)
            {
                float textW = 60;
                if (font != null)
                {
                    var measured = paper.MeasureText(node.Tabs[i].Title, EditorTheme.FontSize, font, 0);
                    textW = (float)measured.X;
                }
                bool hasIcon = !string.IsNullOrEmpty(node.Tabs[i].Icon);
                tabWidths[i] = (hasIcon ? iconW : 0) + textW + TabPadding * 3 + TabCloseSize + 3;
            }

            // Draw merged background via canvas (inactive tab bgs + active tab shape + panel body)
            int activeIdx = node.ActiveTabIndex;
            paper.Box($"leaf_bg_{node.GetHashCode()}")
                .PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.AddActionElement(ref handle, (canvas, r) =>
                {
                    DrawMergedTabShape(canvas, r, tabWidths, activeIdx, tabH);
                }));

            // Compute tab X positions
            float[] tabPositions = new float[node.Tabs.Count];
            float tabX = 0;
            for (int i = 0; i < node.Tabs.Count; i++)
            {
                tabPositions[i] = tabX;
                tabX += tabWidths[i] + TabGap;
            }

            // Draw inactive tabs FIRST (behind), then active tab ON TOP
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < node.Tabs.Count; i++)
            {
                bool isActive = i == node.ActiveTabIndex;
                if (pass == 0 && isActive) continue;  // skip active on first pass
                if (pass == 1 && !isActive) continue;  // skip inactive on second pass

                var tab = node.Tabs[i];
                int ci = i;
                float tw = tabWidths[i];
                float tx = tabPositions[i];

                float inactiveOffset = isActive ? 0 : 6f;
                float inactiveH = isActive ? tabH : tabH - inactiveOffset;


                var tabEl = paper.Box($"t_{node.GetHashCode()}_{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(tx, inactiveOffset).Size(tw, inactiveH)
                    .BackgroundColor(Color.Transparent)
                    .RoundedTop(TabRadius)
                    .Hovered.BackgroundColor(isActive ? Color.Transparent : Color.FromArgb(30, 255, 255, 255)).End()
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
                            _dragSourceNode = srcNode;
                            _dragSourceWindow = srcFw;
                            int fwIdx = FloatingWindows.IndexOf(srcFw);
                            if (fwIdx >= 0) BringToFront(fwIdx);
                        }
                        else
                        {
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

                bool hasIcon = !string.IsNullOrEmpty(tab.Icon);
                float contentX = tx + TabPadding;

                // Icon
                if (hasIcon && font != null)
                {
                    paper.Box($"t_ico_{node.GetHashCode()}_{i}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(contentX, inactiveOffset).Size(iconW, inactiveH)
                        .IsNotInteractable()
                        .Text(tab.Icon, font)
                        .TextColor(isActive ? EditorTheme.Accent : EditorTheme.TextDim)
                        .FontSize((EditorTheme.FontSize - 1) * 0.75f)
                        .Alignment(TextAlignment.MiddleCenter);
                    contentX += iconW;
                }

                // Title label
                if (font != null)
                {
                    paper.Box($"t_lbl_{node.GetHashCode()}_{i}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(contentX, inactiveOffset)
                        .Height(inactiveH)
                        .Width(tw - TabPadding * 2 - TabCloseSize - (hasIcon ? iconW : 0) + 4)
                        .IsNotInteractable()
                        .Text(tab.Title, font)
                        .TextColor(isActive ? EditorTheme.Text : EditorTheme.TextDim)
                        .FontSize(EditorTheme.FontSize)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                // Close button (X)
                paper.Box($"t_close_{node.GetHashCode()}_{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(tx + tw - TabCloseSize - TabPadding + 2, inactiveOffset + (inactiveH - TabCloseSize) / 2)
                    .Size(TabCloseSize, TabCloseSize)
                    .Rounded(TabCloseSize / 2)
                    .Hovered.BackgroundColor(Color.FromArgb(255, 180, 60, 60)).End()
                    .StopEventPropagation()
                    .OnClick((node, ci, fw), (cap, e) =>
                    {
                        cap.Item1.RemoveTab(cap.Item2);
                    })
                    .Text(EditorIcons.Xmark, EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.TextDim)
                    .FontSize(15f)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            // Content area
            float cy = tabH, ch = h - tabH;
            if (ch > 0)
            {
                using (paper.Box($"c_{node.GetHashCode()}")
                    .PositionType(PositionType.SelfDirected).Position(0, cy).Size(w, ch)
                    .Enter())
                {
                    if (node.ActiveTabIndex < node.Tabs.Count)
                        node.Tabs[node.ActiveTabIndex].OnGUI(paper, w, ch);
                }
            }
        }
    }

    /// <summary>
    /// Draw the merged tab + panel shape via Quill.
    /// The active tab has rounded top corners and merges into the panel body below.
    /// Inactive tabs have a subtle separator.
    /// </summary>
    private static Prowl.Vector.Color32 ToC32(Color c)
        => Prowl.Vector.Color32.FromArgb(c.A, c.R, c.G, c.B);

    private static void DrawMergedTabShape(Prowl.Quill.Canvas canvas, Rect r,
        float[] tabWidths, int activeIdx, float tabH)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float rad = TabRadius;
        float ir = TabInsetRadius;

        var panelColor = ToC32(EditorTheme.PanelBackground);
        float panelTop = y + tabH;

        if (activeIdx < 0 || activeIdx >= tabWidths.Length)
        {
            // No active tab — just draw panel body
            canvas.RoundedRectFilled(x, panelTop, w, h - tabH, 0, rad, rad, rad, panelColor);
            return;
        }

        // Draw inactive tab backgrounds FIRST (behind the active tab shape)
        var inactiveColor = ToC32(EditorTheme.Normal);
        float inactiveOffset = 6f;
        float itx = x;
        for (int i = 0; i < tabWidths.Length; i++)
        {
            if (i != activeIdx)
            {
                canvas.RoundedRectFilled(itx, y + inactiveOffset, tabWidths[i], tabH - inactiveOffset,
                    rad, rad, 0, 0, inactiveColor);
            }
            itx += tabWidths[i] + TabGap;
        }

        // Compute active tab X position
        float tabX = x;
        for (int i = 0; i < activeIdx; i++)
            tabX += tabWidths[i] + TabGap;
        float tw = tabWidths[activeIdx];

        bool isFirst = activeIdx == 0;
        float k = 0.5522847498f; // Kappa — cubic bezier approximation of quarter circle

        float bottom = y + h;
        float right = x + w;

        // Draw the entire merged shape (tab + panel body) as one path
        canvas.SetFillColor(panelColor);
        canvas.BeginPath();

        if (isFirst)
        {
            // Start at bottom-left with rounded corner
            canvas.MoveTo(x, bottom - rad);
            canvas.BezierCurveTo(x, bottom - rad * (1 - k), x + rad * (1 - k), bottom, x + rad, bottom);
        }
        else
        {
            // Start at bottom-left with rounded corner
            canvas.MoveTo(x, bottom - rad);
            canvas.BezierCurveTo(x, bottom - rad * (1 - k), x + rad * (1 - k), bottom, x + rad, bottom);
        }

        // Bottom edge
        canvas.LineTo(right - rad, bottom);

        // Bottom-right rounded corner
        canvas.BezierCurveTo(right - rad * (1 - k), bottom, right, bottom - rad * (1 - k), right, bottom - rad);

        // Right edge up to panel top
        canvas.LineTo(right, panelTop + rad);

        // Top-right of panel body rounded corner
        canvas.BezierCurveTo(right, panelTop + rad * (1 - k), right - rad * (1 - k), panelTop, right - rad, panelTop);

        // Panel top edge — right of tab to right scoop
        canvas.LineTo(tabX + tw + ir, panelTop);

        // Right inverse scoop (down from panel surface into tab right side)
        canvas.BezierCurveTo(tabX + tw + ir * (1 - k), panelTop,
                              tabX + tw, panelTop - ir * (1 - k),
                              tabX + tw, panelTop - ir);

        // Tab right side going up
        canvas.LineTo(tabX + tw, y + rad);

        // Tab top-right rounded corner
        canvas.BezierCurveTo(tabX + tw, y + rad * (1 - k), tabX + tw - rad * (1 - k), y, tabX + tw - rad, y);

        // Tab top edge
        canvas.LineTo(tabX + rad, y);

        // Tab top-left rounded corner
        canvas.BezierCurveTo(tabX + rad * (1 - k), y, tabX, y + rad * (1 - k), tabX, y + rad);

        if (isFirst)
        {
            // Tab left side goes straight down to bottom-left start
            canvas.LineTo(tabX, bottom - rad);
        }
        else
        {
            // Tab left side down to scoop
            canvas.LineTo(tabX, panelTop - ir);

            // Left inverse scoop (from tab left side out to panel surface)
            canvas.BezierCurveTo(tabX, panelTop - ir * (1 - k),
                                  tabX - ir * (1 - k), panelTop,
                                  tabX - ir, panelTop);

            // Panel top edge — left of tab to left side
            canvas.LineTo(x, panelTop);

            // Left edge down to start
            canvas.LineTo(x, bottom - rad);
        }

        canvas.ClosePath();
        canvas.FillComplexAA();
        canvas.SetStrokeColor(ToC32(EditorTheme.Border));
        canvas.Stroke();
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
