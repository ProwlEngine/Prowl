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

    // --- Drop preview animation ---
    // Keeps a single animated preview rect. When nothing is hovered, the preview fades out;
    // once it's fully hidden, the next appearance snaps instantly to the new target. While
    // visible, target changes interpolate smoothly (one rect moves between zones).
    private Rect _previewRect;
    private float _previewAlpha;
    private bool _previewVisible;


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
            .Hovered.BackgroundColor(EditorTheme.Purple500).End()
            .Active.BackgroundColor(EditorTheme.Purple400).End()
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

        if (fw == null)
            _leafRects[node] = new Rect(new Float2(x, y), new Float2(x + w, y + h));

        // Single container for the whole leaf we draw the merged tab+panel shape via canvas
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
                tabWidths[i] = (hasIcon ? iconW : 0) + textW + EditorTheme.TabPadding * 3 + EditorTheme.TabCloseSize + 3;
            }

            // Draw merged background via canvas (inactive tab bgs + active tab shape + panel body)
            int activeIdx = node.ActiveTabIndex;
            paper.Box($"leaf_bg_{node.GetHashCode()}")
                .PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    DrawMergedTabShape(canvas, r, tabWidths, activeIdx, tabH);
                }));

            // Outline drawn via DrawForeground on the leaf container (renders after all children)
            {
                var leafParent = paper.CurrentParent;
                paper.DrawForeground(ref leafParent, (canvas, r) =>
                {
                    StrokeMergedTabShape(canvas, r, tabWidths, activeIdx, tabH);
                });
            }

            // Compute tab X positions
            float[] tabPositions = new float[node.Tabs.Count];
            float tabX = 0;
            for (int i = 0; i < node.Tabs.Count; i++)
            {
                tabPositions[i] = tabX;
                tabX += tabWidths[i] + EditorTheme.TabGap;
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
                    .RoundedTop(EditorTheme.Roundness)
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
                float contentX = tx + EditorTheme.TabPadding;

                // Icon
                if (hasIcon && font != null)
                {
                    paper.Box($"t_ico_{node.GetHashCode()}_{i}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(contentX, inactiveOffset).Size(iconW, inactiveH)
                        .IsNotInteractable()
                        .Text(tab.Icon, font)
                        .TextColor(isActive ? EditorTheme.Blue400 : EditorTheme.Ink300)
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
                        .Width(tw - EditorTheme.TabPadding * 2 - EditorTheme.TabCloseSize - (hasIcon ? iconW : 0) + 4)
                        .IsNotInteractable()
                        .Text(tab.Title, font)
                        .TextColor(isActive ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                // Close button (X)
                paper.Box($"t_close_{node.GetHashCode()}_{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(tx + tw - EditorTheme.TabCloseSize - EditorTheme.TabPadding + 2, inactiveOffset + (inactiveH - EditorTheme.TabCloseSize) / 2)
                    .Size(EditorTheme.TabCloseSize, EditorTheme.TabCloseSize)
                    .Rounded(EditorTheme.TabCloseSize / 2)
                    .StopEventPropagation()
                    .OnClick((node, ci, fw), (cap, e) =>
                    {
                        cap.Item1.RemoveTab(cap.Item2);
                    })
                    .Text(EditorIcons.Xmark, EditorTheme.DefaultFont)
                    .TextColor(isActive ? EditorTheme.Ink400 : EditorTheme.Ink300)
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

    private static void DrawMergedTabShape(Prowl.Quill.Canvas canvas, Rect r,
        float[] tabWidths, int activeIdx, float tabH)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float rad = EditorTheme.Roundness;
        float ir = EditorTheme.Roundness + 2;

        var panelColor = EditorTheme.Neutral400;
        float panelTop = y + tabH;

        if (activeIdx < 0 || activeIdx >= tabWidths.Length)
        {
            // No active tab just draw panel body
            canvas.RoundedRectFilled(x, panelTop, w, h - tabH, 0, rad, rad, rad, panelColor);
            return;
        }

        // Draw inactive tab backgrounds FIRST (behind the active tab shape)
        float inactiveOffset = 6f;
        float itx = x;
        for (int i = 0; i < tabWidths.Length; i++)
        {
            if (i != activeIdx)
            {
                canvas.RoundedRectFilled(itx, y + inactiveOffset, tabWidths[i], tabH - inactiveOffset,
                    rad, rad, 0, 0, EditorTheme.Neutral300);
            }
            itx += tabWidths[i] + EditorTheme.TabGap;
        }

        // Compute active tab X position
        float tabX = x;
        for (int i = 0; i < activeIdx; i++)
            tabX += tabWidths[i] + EditorTheme.TabGap;
        float tw = tabWidths[activeIdx];

        bool isFirst = activeIdx == 0;
        float k = 0.5522847498f; // Kappa cubic bezier approximation of quarter circle

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

        // Panel top edge right of tab to right scoop
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

            // Panel top edge left of tab to left side
            canvas.LineTo(x, panelTop);

            // Left edge down to start
            canvas.LineTo(x, bottom - rad);
        }

        canvas.ClosePath();
        canvas.FillComplexAA();
    }

    /// <summary>
    /// Draw the outline of the merged tab+panel shape. Called via DrawForeground so it renders on top of all content.
    /// </summary>
    private static void StrokeMergedTabShape(Prowl.Quill.Canvas canvas, Rect r,
        float[] tabWidths, int activeIdx, float tabH)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float rad = EditorTheme.Roundness;
        float ir = EditorTheme.Roundness + 2;
        float panelTop = y + tabH;

        if (activeIdx < 0 || activeIdx >= tabWidths.Length) return;

        float tabX = x;
        for (int i = 0; i < activeIdx; i++)
            tabX += tabWidths[i] + EditorTheme.TabGap;
        float tw = tabWidths[activeIdx];

        bool isFirst = activeIdx == 0;
        float k = 0.5522847498f;
        float bottom = y + h;
        float right = x + w;

        canvas.SetStrokeColor(EditorTheme.Ink200);
        canvas.SetStrokeWidth(1f);
        canvas.BeginPath();

        if (isFirst)
        {
            canvas.MoveTo(x, bottom - rad);
            canvas.BezierCurveTo(x, bottom - rad * (1 - k), x + rad * (1 - k), bottom, x + rad, bottom);
        }
        else
        {
            canvas.MoveTo(x, bottom - rad);
            canvas.BezierCurveTo(x, bottom - rad * (1 - k), x + rad * (1 - k), bottom, x + rad, bottom);
        }

        canvas.LineTo(right - rad, bottom);
        canvas.BezierCurveTo(right - rad * (1 - k), bottom, right, bottom - rad * (1 - k), right, bottom - rad);
        canvas.LineTo(right, panelTop + rad);
        canvas.BezierCurveTo(right, panelTop + rad * (1 - k), right - rad * (1 - k), panelTop, right - rad, panelTop);
        canvas.LineTo(tabX + tw + ir, panelTop);

        canvas.BezierCurveTo(tabX + tw + ir * (1 - k), panelTop,
                              tabX + tw, panelTop - ir * (1 - k),
                              tabX + tw, panelTop - ir);

        canvas.LineTo(tabX + tw, y + rad);
        canvas.BezierCurveTo(tabX + tw, y + rad * (1 - k), tabX + tw - rad * (1 - k), y, tabX + tw - rad, y);
        canvas.LineTo(tabX + rad, y);
        canvas.BezierCurveTo(tabX + rad * (1 - k), y, tabX, y + rad * (1 - k), tabX, y + rad);

        if (isFirst)
        {
            canvas.LineTo(tabX, bottom - rad);
        }
        else
        {
            canvas.LineTo(tabX, panelTop - ir);
            canvas.BezierCurveTo(tabX, panelTop - ir * (1 - k),
                                  tabX - ir * (1 - k), panelTop,
                                  tabX - ir, panelTop);
            canvas.LineTo(x, panelTop);
            canvas.LineTo(x, bottom - rad);
        }

        canvas.ClosePath();
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
            .OnClick(index, (idx, e) => BringToFront(idx))
            .Enter())
        {
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
        float s = EditorTheme.IndicatorSize, g = EditorTheme.IndicatorGap, hs = s / 2;
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
        // Animated drop preview runs every frame so fade-out + snap-on-reappear works.
        DrawDropPreview(paper);

        if (_hoveredLeaf == null) return;
        var rect = _hoveredLeafRect;
        float cx = rect.Min.X + rect.Size.X / 2, cy = rect.Min.Y + rect.Size.Y / 2;
        float s = EditorTheme.IndicatorSize, g = EditorTheme.IndicatorGap, hs = s / 2;

        var bg = Color.FromArgb(85, EditorTheme.Blue400);
        var hi = Color.FromArgb(85, EditorTheme.Blue500);
        var bd = Color.FromArgb(85, EditorTheme.Blue600);

        Ind(paper, "di_c", cx - hs, cy - hs, s, _hoveredZone == DockZone.Center ? hi : bg, bd, EditorIcons.Clone);
        Ind(paper, "di_t", cx - hs, cy - hs - g - s, s, _hoveredZone == DockZone.Top ? hi : bg, bd, EditorIcons.ArrowUp);
        Ind(paper, "di_b", cx - hs, cy + hs + g, s, _hoveredZone == DockZone.Bottom ? hi : bg, bd, EditorIcons.ArrowDown);
        Ind(paper, "di_l", cx - hs - g - s, cy - hs, s, _hoveredZone == DockZone.Left ? hi : bg, bd, EditorIcons.ArrowLeft);
        Ind(paper, "di_r", cx + hs + g, cy - hs, s, _hoveredZone == DockZone.Right ? hi : bg, bd, EditorIcons.ArrowRight);
    }

    private void Ind(Paper paper, string id, float x, float y, float s, Color bg, Color bd, string icon)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).PositionType(PositionType.SelfDirected).Position(x, y).Size(s, s)
            .BackgroundColor(bg).BorderColor(bd).BorderWidth(1).Rounded(4)
            .Text(icon, font!)
            .TextColor(Color.FromArgb(230, 235, 240, 255))
            .FontSize(s * 0.45f)
            .Alignment(TextAlignment.MiddleCenter);
    }

    private void DrawDropPreview(Paper paper)
    {
        // Compute the target rect this frame, or null when no zone is hovered.
        Rect? target = null;
        if (_hoveredLeaf != null && _hoveredZone != DockZone.None)
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
        float dt = MathF.Max(0f, (float)Runtime.Time.DeltaTime);
        float tMove  = 1f - MathF.Exp(-dt * 18f);
        float tAlpha = 1f - MathF.Exp(-dt * 20f);

        if (target.HasValue)
        {
            if (!_previewVisible)
            {
                // Fully hidden → snap to target on first appearance. The user sees the preview
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
