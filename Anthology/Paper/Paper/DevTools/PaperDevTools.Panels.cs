using System;
using System.Collections;
using System.Collections.Generic;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI
{
    public sealed partial class PaperDevTools
    {
        // ================================================================= Logging

        public enum LogLevel { Info, Warning, Error }

        private struct LogEntry { public LogLevel Level; public string Msg; public int Count; }
        private readonly List<LogEntry> _log = new();
        private const int MaxLog = 500;

        /// <summary>Append a message to the DevTools console (also usable via <c>paper.Log(...)</c>).</summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            message ??= "";
            if (_log.Count > 0)
            {
                var last = _log[_log.Count - 1];
                if (last.Level == level && last.Msg == message) { last.Count++; _log[_log.Count - 1] = last; return; }
            }
            _log.Add(new LogEntry { Level = level, Msg = message, Count = 1 });
            if (_log.Count > MaxLog) _log.RemoveRange(0, _log.Count - MaxLog);
        }

        // ================================================================= Snapshot

        private sealed class Node
        {
            public int Id, Parent, Depth, Subtree;
            public readonly List<int> Kids = new();
            public float X, Y, W, H;
            public int Layer, TabIndex;
            public bool Visible, Scissor;
            public LayoutType Lay;
            public PositionType Pos;
            public string Text, Handlers, Draw;
        }

        private readonly List<Node> _snap = new();
        private readonly Dictionary<int, int> _idToSnap = new();

        private void CaptureSnapshot()
        {
            _snap.Clear();
            _idToSnap.Clear();
            var root = _p.RootElement;
            if (root.IsValid) Walk(root, 0, -1);

            // Subtree element counts (post-order so children are already counted).
            for (int i = _snap.Count - 1; i >= 0; i--)
            {
                var n = _snap[i];
                n.Subtree = 1;
                foreach (var k in n.Kids) n.Subtree += _snap[k].Subtree;
            }

            CaptureSelectedStyles();
            CaptureSelectedInfo();
        }

        private int Walk(ElementHandle h, int depth, int parentSnap)
        {
            ref var d = ref h.Data;
            if (d.ID == _panelId || d.ID == _overlayId) return -1; // skip DevTools' own subtree

            var n = new Node
            {
                Id = d.ID, Parent = parentSnap, Depth = depth,
                X = d.X, Y = d.Y, W = d.LayoutWidth, H = d.LayoutHeight,
                Layer = d.Layer, TabIndex = d.TabIndex, Visible = d.Visible, Scissor = d._scissorEnabled,
                Lay = d.LayoutType, Pos = d.PositionType,
                Text = TextPreview(ref d), Handlers = HandlerString(ref d), Draw = ComputeDrawKind(ref d),
            };
            int idx = _snap.Count;
            _snap.Add(n);
            _idToSnap[d.ID] = idx;

            var kids = d.ChildIndices;
            if (kids != null)
                for (int i = 0; i < kids.Count; i++)
                {
                    var child = new ElementHandle(_p, kids[i]);
                    if (!child.IsValid) continue;
                    int cs = Walk(child, depth + 1, idx);
                    if (cs >= 0) n.Kids.Add(cs);
                }
            return idx;
        }

        private static string TextPreview(ref ElementData d)
        {
            if (string.IsNullOrEmpty(d.Paragraph)) return "";
            string s = d.Paragraph.Replace("\n", " ");
            return s.Length > 42 ? s.Substring(0, 42) + "..." : s;
        }

        // Classifies what an element actually paints during the render pass (this is what the flame
        // measures). Order matters: the most expensive/identifying paint wins.
        private static string ComputeDrawKind(ref ElementData d)
        {
            if (!string.IsNullOrEmpty(d.Paragraph)) return d.IsMarkdown ? "markdown" : d.IsRichText ? "richtext" : "text";
            if (d._renderCommands != null || d._foregroundRenderCommands != null) return "custom";

            var st = d._elementStyle;
            if (st != null)
            {
                if (Convert.ToSingle(st.GetValue(GuiProp.BackdropBlur)) > 0f) return "blur";
                if (st.GetValue(GuiProp.BoxShadow) is BoxShadow sh && sh.IsVisible) return "shadow";
                if (st.GetValue(GuiProp.BackgroundGradient) is Gradient g && g.Type != GradientType.None) return "gradient";
                if (st.GetValue(GuiProp.BackgroundImage) != null) return "image";
                if (st.GetValue(GuiProp.BackgroundColor) is Color bg && bg.A > 0f) return "box";
                if (Convert.ToSingle(st.GetValue(GuiProp.BorderWidth)) > 0f) return "border";
            }
            return ""; // pure layout container, ~no render self-cost
        }

        private static string HandlerString(ref ElementData d)
        {
            var list = new List<string>(4);
            if (d.OnClick != null || d.OnPress != null || d.OnRelease != null) list.Add("click");
            if (d.OnDragStart != null || d.OnDragging != null) list.Add("drag");
            if (d.OnScroll != null) list.Add("scroll");
            if (d.OnHover != null || d.OnEnter != null || d.OnLeave != null) list.Add("hover");
            if (d.OnKeyPressed != null || d.OnTextInput != null) list.Add("key");
            if (d.OnFocusChange != null) list.Add("focus");
            return string.Join(" ", list);
        }

        // Curated set of style props shown in the inspector (the full GuiProp set is much larger).
        private static readonly GuiProp[] ShownProps =
        {
            GuiProp.Width, GuiProp.Height, GuiProp.MinWidth, GuiProp.MaxWidth, GuiProp.MinHeight, GuiProp.MaxHeight,
            GuiProp.PaddingLeft, GuiProp.PaddingRight, GuiProp.PaddingTop, GuiProp.PaddingBottom,
            GuiProp.Left, GuiProp.Right, GuiProp.Top, GuiProp.Bottom,
            GuiProp.ChildLeft, GuiProp.ChildRight, GuiProp.ChildTop, GuiProp.ChildBottom,
            GuiProp.RowBetween, GuiProp.ColBetween,
            GuiProp.BackgroundColor, GuiProp.BorderColor, GuiProp.BorderWidth, GuiProp.Rounded,
            GuiProp.TextColor, GuiProp.FontSize, GuiProp.AspectRatio,
            GuiProp.TranslateX, GuiProp.TranslateY, GuiProp.ScaleX, GuiProp.ScaleY, GuiProp.Rotate, GuiProp.BackdropBlur,
        };

        private readonly List<(string name, string val, bool anim)> _selStyles = new();

        private void CaptureSelectedStyles()
        {
            _selStyles.Clear();
            if (_selectedId == 0) return;
            var h = _p.FindElementByID(_selectedId);
            if (!h.IsValid) return;
            var style = h.Data._elementStyle;
            if (style == null) return;
            foreach (var p in ShownProps)
                _selStyles.Add((p.ToString(), FormatStyle(style.GetValue(p)), style.IsAnimating(p)));
        }

        private static string FormatStyle(object v)
        {
            if (v == null) return "null";
            if (v is Color c) return $"#{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2} a{(int)(c.A * 255)}";
            return v.ToString();
        }

        // ================================================================= Draw-call stats

        private struct DcInfo { public int Tris; public string Brush; public bool Text; }
        private readonly List<DcInfo> _drawCallList = new();
        private int _statDrawCalls, _statVerts, _statTris;

        private void CaptureDrawCalls(IReadOnlyList<DrawCall> dcs)
        {
            _drawCallList.Clear();
            for (int i = 0; i < dcs.Count && i < 400; i++)
            {
                var dc = dcs[i];
                _drawCallList.Add(new DcInfo { Tris = dc.ElementCount / 3, Brush = dc.Brush.Type.ToString(), Text = dc.FontAtlas != null });
            }
        }

        // ================================================================= Frame + phase timing data

        private readonly float[] _frameHist = new float[180];
        private int _frameHead;
        private readonly List<(string name, double ms)> _phaseAccum = new();
        private readonly List<(string name, double ms)> _phaseDisplay = new();

        // ================================================================= Pick + overlay

        private bool _pickMode, _layoutOverlay;
        private int _selectedId, _hoverId;

        private void UpdatePick(float appTop)
        {
            _hoverId = 0;
            if (!_pickMode) return;
            var pos = _p.PointerPos;
            if (pos.Y >= appTop) return; // pointer is over the panel, not the inspected app
            _hoverId = PickAt(pos.X, pos.Y);
            if (_hoverId != 0 && _p.IsPointerPressed(PaperMouseBtn.Left))
            {
                _selectedId = _hoverId;
                _pickMode = false;
                _tab = 1;
            }
        }

        // Topmost snapshot node whose rect contains the point (higher Layer, then later-drawn wins).
        private int PickAt(float x, float y)
        {
            int best = 0, bestLayer = int.MinValue, bestIdx = -1;
            for (int i = 0; i < _snap.Count; i++)
            {
                var n = _snap[i];
                if (!n.Visible || n.W <= 0 || n.H <= 0) continue;
                if (x < n.X || y < n.Y || x > n.X + n.W || y > n.Y + n.H) continue;
                if (n.Layer > bestLayer || (n.Layer == bestLayer && i > bestIdx))
                { best = n.Id; bestLayer = n.Layer; bestIdx = i; }
            }
            return best;
        }

        private static Color WithA(Color c, int a) =>
            System.Drawing.Color.FromArgb(a, (int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));

        private void DrawOverlay(Canvas cv)
        {
            if (_layoutOverlay)
                foreach (var n in _snap)
                {
                    if (!n.Visible || n.W <= 0 || n.H <= 0) continue;
                    StrokeRect(cv, n.X, n.Y, n.W, n.H, WithA(Accent, 40), 1f);
                }

            DrawHighlight(cv, _hoverId, Warn);
            DrawHighlight(cv, _selectedId, Accent);
        }

        private void DrawHighlight(Canvas cv, int id, Color color)
        {
            if (id == 0 || !_idToSnap.TryGetValue(id, out int idx)) return;
            var n = _snap[idx];
            if (n.W <= 0 || n.H <= 0) return;
            cv.RectFilled(n.X, n.Y, n.W, n.H, WithA(color, 36));
            StrokeRect(cv, n.X, n.Y, n.W, n.H, color, 1.5f);
            cv.DrawText($"{n.W:0}x{n.H:0}", n.X + 2, MathF.Max(0, n.Y - 14), Text, 12, _font);
        }

        private static void StrokeRect(Canvas cv, float x, float y, float w, float h, Color c, float width)
        {
            cv.BeginPath();
            cv.Rect(x, y, w, h);
            cv.SetStrokeColor(c);
            cv.SetStrokeWidth(width);
            cv.Stroke();
        }

        private void Btn(string id, string label, Action onClick)
        {
            _p.Box(id).Width(_p.Auto).Height(24).Padding(12, 12, 0, 0).Margin(5, 5, 3, 3).Rounded(4)
                .BackgroundColor(Panel).Hovered.BackgroundColor(Hover).End()
                .Text(label, _font).FontSize(14).TextColor(Text).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                .OnClick(e => onClick());
        }

        // ================================================================= Panel: Console

        private void BuildConsole(float w, float h)
        {
            using (_p.Column("con").Width(w).Height(h).Enter())
            {
                using (_p.Row("con_bar").Width(_p.Percent(100)).Height(24).BackgroundColor(BG2).Enter())
                {
                    Btn("con_clear", "Clear", () => _log.Clear());
                    Label("con_count", $"{_log.Count} messages", TextDim, 12, 24);
                }

                using (BeginScroll("console", w, h - 24))
                {
                    if (_log.Count == 0)
                        Label("con_empty", "No messages. Log via paper.Log(...) or paper.DevTools.Log(...).", TextDim, 12, 20);

                    for (int i = 0; i < _log.Count; i++)
                    {
                        var e = _log[i];
                        Color c = e.Level == LogLevel.Error ? Danger : e.Level == LogLevel.Warning ? Warn : Text;
                        string prefix = e.Level == LogLevel.Error ? "[error] " : e.Level == LogLevel.Warning ? "[warn] " : "";
                        string msg = e.Count > 1 ? $"{prefix}{e.Msg}  (x{e.Count})" : prefix + e.Msg;
                        _p.Box("log", i).Width(_p.Percent(100)).Height(_p.Auto).Padding(8, 8, 3, 3)
                            .Text(msg, _font).FontSize(14).TextColor(c).Wrap(TextWrapMode.Wrap)
                            .Hovered.BackgroundColor(Hover).End();
                    }
                }
            }
        }

        // ================================================================= Panel: Inspector

        private readonly HashSet<int> _collapsed = new();

        private void BuildInspector(float w, float h)
        {
            float treeW = MathF.Floor(w * 0.6f);
            float detW = w - treeW - 1;

            using (_p.Row("insp").Width(w).Height(h).Enter())
            {
                using (BeginScroll("tree", treeW, h))
                {
                    if (_snap.Count == 0) Label("tree_empty", "(building tree...)", TextDim, 12, 20);
                    else BuildTreeNode(0);
                }

                _p.Box("insp_sep").Width(1).Height(h).BackgroundColor(Line);

                using (BeginScroll("details", detW, h))
                    BuildDetails();
            }
        }

        private void BuildTreeNode(int idx)
        {
            var n = _snap[idx];
            bool hasKids = n.Kids.Count > 0;
            bool collapsed = _collapsed.Contains(n.Id);
            bool sel = n.Id == _selectedId;
            float indent = 2 + n.Depth * 12;

            string kind = n.Lay == LayoutType.Row ? "Row" : "Col";
            string extra = string.IsNullOrEmpty(n.Text) ? "" : "  \"" + n.Text + "\"";
            if (!string.IsNullOrEmpty(n.Handlers)) extra += "  {" + n.Handlers + "}";
            string label = $"{kind} #{n.Id}{extra}";

            using (_p.Row("tn", idx).Width(_p.Percent(100)).Height(22)
                .BackgroundColor(sel ? SelBg : Transparent)
                .Hovered.BackgroundColor(sel ? SelBg : Hover).End()
                .OnClick(n.Id, (id, _) => _selectedId = id)
                .OnHover(n.Id, (id, _) => _hoverId = id)
                .Enter())
            {
                _p.Box("pad", idx).Width(indent).Height(1).IsNotInteractable();

                if (hasKids)
                    _p.Box("arw", idx).Width(16).Height(22).Text(collapsed ? "+" : "-", _font).FontSize(16)
                        .TextColor(TextDim).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                        .OnClick(n.Id, (id, _) => { if (!_collapsed.Add(id)) _collapsed.Remove(id); });
                else
                    _p.Box("arw", idx).Width(16).Height(1).IsNotInteractable();

                _p.Box("lbl", idx).Width(_p.Stretch()).Height(22).IsNotInteractable()
                    .Text(label, _font).FontSize(14).TextColor(n.Visible ? (sel ? Text : TextDim) : Danger)
                    .Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft);
            }

            if (hasKids && !collapsed)
                foreach (var k in n.Kids) BuildTreeNode(k);
        }

        private void BuildDetails()
        {
            if (_selectedId == 0 || !_idToSnap.TryGetValue(_selectedId, out int si))
            {
                Label("d_none", "Select an element in the tree, or use Pick.", TextDim, 12, 20);
                return;
            }

            var n = _snap[si];

            SectionHeader("d_h1", "ELEMENT");
            KeyVal("d_id", "id", n.Id.ToString(), Text);
            KeyVal("d_kind", "layout", $"{n.Lay} / {n.Pos}", Text);
            KeyVal("d_layer", "layer", n.Layer.ToString(), Text);
            KeyVal("d_vis", "visible", n.Visible.ToString(), n.Visible ? Accent2 : Danger);
            KeyVal("d_clip", "clip", n.Scissor.ToString(), n.Scissor ? Accent2 : TextDim);
            KeyVal("d_tab", "tabIndex", n.TabIndex.ToString(), Text);
            if (!string.IsNullOrEmpty(n.Handlers)) KeyVal("d_hnd", "handlers", n.Handlers, Accent2);
            if (!string.IsNullOrEmpty(n.Text)) KeyVal("d_txt", "text", n.Text, Text);

            SectionHeader("d_h2", "LAYOUT (px)");
            KeyVal("d_x", "x", n.X.ToString("0.#"), Text);
            KeyVal("d_y", "y", n.Y.ToString("0.#"), Text);
            KeyVal("d_w", "width", n.W.ToString("0.#"), Text);
            KeyVal("d_hh", "height", n.H.ToString("0.#"), Text);

            SectionHeader("d_h3", "COMPUTED STYLES");
            for (int i = 0; i < _selStyles.Count; i++)
            {
                var s = _selStyles[i];
                KeyVal("st_" + i, s.name, s.anim ? s.val + "  (anim)" : s.val, s.anim ? Warn : Text);
            }

            SectionHeader("d_h4", "STORAGE");
            var store = _p.DebugGetStorage(_selectedId);
            if (store == null || store.Count == 0) Label("d_nostore", "(none)", TextDim, 12, 18);
            else
            {
                int i = 0;
                foreach (DictionaryEntry kv in store)
                {
                    string val = kv.Value?.GetType().Name ?? "null";
                    KeyVal("sto_" + i, kv.Key?.ToString() ?? "?", val, TextDim);
                    i++;
                }
            }
        }

        // ================================================================= Panel: Profiler

        private bool _deepProfile;
        /// <summary>True when Paper should record per-subtree render timings this frame.</summary>
        internal bool DeepProfiling => Enabled && _open && _deepProfile;

        private struct RTime { public int Id, Parent; public double Ms; }
        private readonly List<RTime> _renderTimes = new();   // this frame's raw render samples
        private readonly List<RTime> _renderDisplay = new();  // published render flame (live per frame)

        // Layout timing, summed per element (an element is laid out several times per frame).
        private readonly List<RTime> _layoutTimes = new();
        private readonly List<RTime> _layoutDisplay = new();
        private readonly Dictionary<int, double> _layoutSum = new();
        private readonly Dictionary<int, int> _layoutParent = new();

        private int _flameMode; // 0 = render, 1 = layout
        private List<RTime> ActiveDisplay => _flameMode == 1 ? _layoutDisplay : _renderDisplay;
        private string ActivePhaseName => _flameMode == 1 ? "LAYOUT" : "RENDER";

        internal void RecordLayout(int id, int parent, long ticks)
        {
            if (id == _panelId || id == _overlayId) return;
            _layoutTimes.Add(new RTime { Id = id, Parent = parent, Ms = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency });
        }

        internal void PublishLayout()
        {
            _layoutSum.Clear();
            _layoutParent.Clear();
            for (int i = 0; i < _layoutTimes.Count; i++)
            {
                var rt = _layoutTimes[i];
                _layoutSum[rt.Id] = (_layoutSum.TryGetValue(rt.Id, out var s) ? s : 0) + rt.Ms;
                _layoutParent[rt.Id] = rt.Parent;
            }
            _layoutDisplay.Clear();
            foreach (var kv in _layoutSum)
                _layoutDisplay.Add(new RTime { Id = kv.Key, Parent = _layoutParent[kv.Key], Ms = kv.Value });
        }

        /// <summary>Called from Paper's render pass (deep-profile only) with a subtree's inclusive time.</summary>
        internal void RecordRender(int id, int parent, long ticks)
        {
            if (id == _panelId || id == _overlayId) return; // don't profile DevTools' own chrome
            _renderTimes.Add(new RTime { Id = id, Parent = parent, Ms = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency });
        }

        private struct FNode { public double Value; public List<int> Kids; }
        private readonly List<(int id, float x, float y, float w, float h)> _flameRects = new();
        private readonly Dictionary<int, FNode> _flameNodes = new();
        private readonly Dictionary<int, int> _flameParent = new();
        private readonly List<int> _flameRoots = new();

        // Pan/zoom view state for the flame canvas.
        private float _flameScale = 1f, _flamePanX, _flamePanY;
        private Rect _flameArea;
        private int _flameMaxDepth;
        private const float FlameRowH = 20f;

        // Selected-entry details shown to the right of the flame.
        private readonly List<(string k, string v, Color c)> _selInfo = new();

        private void BuildProfiler(float w, float h)
        {
            ComputeFrameStats(out float avg, out float max, out float last);
            UpdateFlameHover(); // sets _hoverId from last frame's bars so the app element lights up

            bool timing = ActiveDisplay.Count > 0;
            float headH = 26 + 66 + 28 + 24 + 30; // stats + graph + phasebar + legend + flame toolbar
            float flameH = MathF.Max(90, h - headH);

            using (_p.Column("prof").Width(w).Height(h).Enter())
            {
                using (_p.Row("prof_stats").Width(_p.Percent(100)).Height(26).BackgroundColor(BG2).Enter())
                {
                    Label("ps1", $"frame {last:0.00}ms   avg {avg:0.00}   max {max:0.00}", Text, 14, 26);
                    _p.Box("ps_sp").Width(_p.Stretch()).Height(1).IsNotInteractable();
                    Label("ps2", $"elems {_p.CountOfAllElements}   draws {_statDrawCalls}   tris {_statTris}   verts {_statVerts}", TextDim, 14, 26);
                }

                using (_p.Box("prof_graph").Width(_p.Percent(100)).Height(66).BackgroundColor(BG2).Enter())
                    _p.Draw((cv, rect) => DrawFrameGraph(cv, rect));

                using (_p.Box("prof_phase").Width(_p.Percent(100)).Height(28).Enter())
                    _p.Draw((cv, rect) => DrawPhaseBar(cv, rect));

                Label("prof_legend", PhaseLegend(), TextDim, 13, 24);

                using (_p.Row("prof_fbar").Width(_p.Percent(100)).Height(30).BackgroundColor(BG2).ChildTop().ChildBottom().Enter())
                {
                    // Record button: red while capturing. Pause it to freeze a frame, then pan/zoom/inspect.
                    _p.Box("prof_rec").Width(_p.Auto).Height(24).Padding(12, 12, 0, 0).Margin(4, 4, 0, 0).Rounded(4)
                        .BackgroundColor(_deepProfile ? Col(140, 44, 44) : Panel)
                        .Hovered.BackgroundColor(_deepProfile ? Col(170, 54, 54) : Hover).End()
                        .Text(_deepProfile ? "REC  recording..." : "Record", _font).FontSize(14)
                        .TextColor(_deepProfile ? Col(255, 190, 190) : Text).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                        .OnClick(e => _deepProfile = !_deepProfile);
                    ModeBtn("Render", 0);
                    ModeBtn("Layout", 1);
                    Btn("prof_reset", "Reset view", ResetFlameView);
                    Label("prof_fmode", _flameMode == 1
                        ? "LAYOUT (CPU) time per subtree - includes text measurement/shaping.   wheel=zoom, drag=pan, click=select"
                        : "RENDER (CPU) time per subtree - geometry build, not layout or GPU.   wheel=zoom, drag=pan, click=select",
                        TextDim, 13, 24);
                }

                using (_p.Row("prof_frow").Width(_p.Percent(100)).Height(flameH).Enter())
                {
                    using (_p.Box("prof_flame").Width(_p.Stretch()).Height(flameH).Clip()
                        .OnScroll(e => FlameZoom(e.PointerPosition.X, e.Delta))
                        .OnDragging(e => { _flamePanX += (float)e.Delta.X; _flamePanY += (float)e.Delta.Y; ClampFlamePan(); })
                        .OnClick(e => FlameSelect(e.PointerPosition.X, e.PointerPosition.Y)).Enter())
                        _p.Draw((cv, rect) => DrawFlame(cv, rect, timing));

                    _p.Box("prof_fsep").Width(1).Height(flameH).BackgroundColor(Line);

                    const float infoW = 300f;
                    using (_p.Box("prof_finfo").Width(infoW).Height(flameH).BackgroundColor(BG).Enter())
                        using (BeginScroll("flameinfo", infoW, flameH))
                            BuildFlameInfo();
                }
            }
        }

        private void ResetFlameView() { _flameScale = 1f; _flamePanX = 0f; _flamePanY = 0f; }

        private void ModeBtn(string label, int mode)
        {
            bool on = _flameMode == mode;
            _p.Box("mode_" + label).Width(_p.Auto).Height(24).Padding(11, 11, 0, 0).Margin(2, 2, 0, 0).Rounded(4)
                .BackgroundColor(on ? Accent : Panel)
                .Hovered.BackgroundColor(on ? Accent : Hover).End()
                .Text(label, _font).FontSize(14).TextColor(on ? Col(15, 15, 20) : Text).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                .OnClick(mode, (m, _) => _flameMode = m);
        }

        private void ClampFlamePan()
        {
            float areaW = _flameArea.Size.X, areaH = _flameArea.Size.Y;
            _flamePanX = Math.Clamp(_flamePanX, MathF.Min(0f, areaW - areaW * _flameScale), 0f);
            float contentH = (_flameMaxDepth + 1) * FlameRowH;
            _flamePanY = Math.Clamp(_flamePanY, MathF.Min(0f, areaH - contentH), 0f);
        }

        private void FlameZoom(float cursorX, float delta)
        {
            float areaX = _flameArea.Min.X, areaW = _flameArea.Size.X;
            if (areaW <= 0 || delta == 0) return;
            float factor = delta > 0 ? 1.25f : 1f / 1.25f;
            float newScale = Math.Clamp(_flameScale * factor, 1f, 500000f);
            float pCursor = (cursorX - areaX - _flamePanX) / (areaW * _flameScale);
            _flamePanX = cursorX - areaX - pCursor * areaW * newScale;
            _flameScale = newScale;
            ClampFlamePan();
        }

        private void FlameSelect(float x, float y)
        {
            for (int i = 0; i < _flameRects.Count; i++)
            {
                var rr = _flameRects[i];
                if (x >= rr.x && x <= rr.x + rr.w && y >= rr.y && y <= rr.y + rr.h) { _selectedId = rr.id; return; }
            }
        }

        private void ComputeFrameStats(out float avg, out float max, out float last)
        {
            float sum = 0, mx = 0; int cnt = 0;
            for (int i = 0; i < _frameHist.Length; i++)
            {
                float v = _frameHist[i];
                if (v <= 0) continue;
                sum += v; cnt++; if (v > mx) mx = v;
            }
            avg = cnt > 0 ? sum / cnt : 0;
            max = mx;
            last = _frameHist[(_frameHead - 1 + _frameHist.Length) % _frameHist.Length];
        }

        private void DrawFrameGraph(Canvas cv, Rect r)
        {
            float x0 = r.Min.X, y0 = r.Min.Y, w = r.Size.X, h = r.Size.Y;
            float scale = 16.6f;
            for (int i = 0; i < _frameHist.Length; i++) if (_frameHist[i] > scale) scale = _frameHist[i];
            scale *= 1.1f;

            // 60fps (16.6ms) and 30fps (33.3ms) guide lines.
            GuideLine(cv, x0, y0, w, h, scale, 16.6f, Accent2);
            GuideLine(cv, x0, y0, w, h, scale, 33.3f, Warn);

            int count = _frameHist.Length;
            float bw = w / count;
            for (int i = 0; i < count; i++)
            {
                int idx = (_frameHead + i) % count; // oldest -> newest
                float v = _frameHist[idx];
                if (v <= 0) continue;
                float bh = MathF.Min(h, v / scale * h);
                Color c = v > 33.3f ? Danger : v > 16.6f ? Warn : Accent;
                cv.RectFilled(x0 + i * bw, y0 + h - bh, MathF.Max(1, bw - 0.5f), bh, c);
            }
        }

        private static void GuideLine(Canvas cv, float x0, float y0, float w, float h, float scale, float ms, Color c)
        {
            if (ms > scale) return;
            float y = y0 + h - ms / scale * h;
            cv.RectFilled(x0, y, w, 1, WithA(c, 90));
        }

        private void DrawPhaseBar(Canvas cv, Rect r)
        {
            double total = 0;
            foreach (var (_, ms) in _phaseDisplay) total += ms;
            if (total <= 0) { cv.DrawText("(no phase data - phases time only while open)", r.Min.X + 2, r.Min.Y + 7, TextDim, 13, _font); return; }

            float x = r.Min.X, y = r.Min.Y + 2, h = r.Size.Y - 6, w = r.Size.X;
            for (int i = 0; i < _phaseDisplay.Count; i++)
            {
                float seg = (float)(_phaseDisplay[i].ms / total * w);
                Color c = PhaseColor(i);
                cv.RectFilled(x, y, MathF.Max(0, seg - 1), h, c);
                if (seg > 52) cv.DrawText(_phaseDisplay[i].name, x + 4, y + 3, Col(15, 15, 18), 12, _font);
                x += seg;
            }
        }

        private string PhaseLegend()
        {
            if (_phaseDisplay.Count == 0) return "";
            var parts = new List<string>(_phaseDisplay.Count);
            foreach (var (name, ms) in _phaseDisplay) parts.Add($"{name} {ms:0.00}");
            return string.Join("   |   ", parts);
        }

        private static readonly Color[] PhasePalette =
        {
            Col(88,154,250), Col(120,200,140), Col(240,190,90), Col(230,130,200),
            Col(120,210,220), Col(240,150,110), Col(170,160,240), Col(200,200,120), Col(150,220,170),
        };
        private static Color PhaseColor(int i) => PhasePalette[i % PhasePalette.Length];

        // Colour a flame bar by the DOMINANT thing it renders, so expensive paints (blur/shadow) pop.
        private static Color DrawKindColor(string kind) => kind switch
        {
            "text" or "markdown" or "richtext" => Col(70, 130, 235),
            "custom"   => Col(220, 120, 200),
            "blur"     => Col(150, 110, 235),
            "shadow"   => Col(230, 150, 90),
            "gradient" => Col(90, 190, 200),
            "image"    => Col(110, 200, 130),
            "box"      => Col(96, 104, 138),
            "border"   => Col(120, 120, 140),
            _          => Col(58, 60, 72), // pure container (no paint)
        };

        // Builds a flame tree keyed by element id. Timing mode uses inclusive render ms captured from
        // Paper's render pass; otherwise it falls back to subtree element counts. DevTools' own
        // elements are excluded (they are absent from the snapshot).
        private void BuildFlameTree(bool timing)
        {
            _flameNodes.Clear();
            _flameParent.Clear();
            _flameRoots.Clear();

            int rootId = _p.RootElement.Data.ID;
            if (timing)
            {
                var disp = ActiveDisplay;
                for (int i = 0; i < disp.Count; i++)
                {
                    var rt = disp[i];
                    if (rt.Id == rootId) continue;            // skip the real root; the app viewport is the flame root
                    if (!_idToSnap.ContainsKey(rt.Id)) continue; // skip anything not in the app tree
                    _flameNodes[rt.Id] = new FNode { Value = rt.Ms, Kids = new List<int>() };
                    _flameParent[rt.Id] = rt.Parent;
                }
                foreach (var kv in _flameParent)
                {
                    if (kv.Value != 0 && _flameNodes.ContainsKey(kv.Value)) _flameNodes[kv.Value].Kids.Add(kv.Key);
                    else _flameRoots.Add(kv.Key);
                }
            }
            else
            {
                for (int i = 0; i < _snap.Count; i++)
                    _flameNodes[_snap[i].Id] = new FNode { Value = _snap[i].Subtree, Kids = new List<int>() };
                for (int i = 0; i < _snap.Count; i++)
                {
                    var n = _snap[i];
                    foreach (var k in n.Kids)
                    {
                        int cid = _snap[k].Id;
                        _flameNodes[n.Id].Kids.Add(cid);
                        _flameParent[cid] = n.Id;
                    }
                }
                if (_snap.Count > 0) _flameRoots.Add(_snap[0].Id);
            }
        }

        private void DrawFlame(Canvas cv, Rect r, bool timing)
        {
            _flameArea = r;
            _flameRects.Clear();
            _flameMaxDepth = 0;
            BuildFlameTree(timing);

            if (_flameNodes.Count == 0)
            {
                cv.DrawText(_deepProfile ? "Recording... draw a frame." : "Press Record to capture timings.",
                    r.Min.X + 6, r.Min.Y + 8, TextDim, 14, _font);
                return;
            }

            double total = 0;
            foreach (var id in _flameRoots) total += _flameNodes[id].Value;
            if (total <= 0) return;

            // Roots laid across [0,1] proportional to value; pan/zoom applied per node.
            double acc = 0;
            foreach (var id in _flameRoots)
            {
                double v = _flameNodes[id].Value;
                DrawNode(cv, r, id, acc / total, (acc + v) / total, 0, timing);
                acc += v;
            }

            DrawFlameTooltip(cv, timing);
        }

        // Draws one node given its normalized [n0,n1] slice of the whole tree; applies pan/zoom.
        private void DrawNode(Canvas cv, Rect area, int id, double n0, double n1, int depth, bool timing)
        {
            float areaX = area.Min.X, areaW = area.Size.X, areaY = area.Min.Y, areaBottom = area.Min.Y + area.Size.Y;
            float scaledW = areaW * _flameScale;
            float x = areaX + _flamePanX + (float)n0 * scaledW;
            float w = (float)(n1 - n0) * scaledW;

            if (x + w < areaX || x > areaX + areaW) return; // whole subtree off-screen horizontally
            if (depth > _flameMaxDepth) _flameMaxDepth = depth;

            float y = areaY + _flamePanY + depth * FlameRowH;
            var n = _flameNodes[id];

            if (y + FlameRowH > areaY && y < areaBottom && w >= 0.5f)
            {
                float bx = MathF.Max(x, areaX), bw = MathF.Min(x + w, areaX + areaW) - bx;
                string kind = _idToSnap.TryGetValue(id, out int si) ? _snap[si].Draw : "";
                cv.RectFilled(bx, y, MathF.Max(1, bw - 1), FlameRowH - 1, DrawKindColor(kind));
                _flameRects.Add((id, bx, y, MathF.Max(1, bw), FlameRowH));

                if (id == _selectedId) StrokeRect(cv, bx, y, MathF.Max(1, bw - 1), FlameRowH - 1, Text, 2f);
                else if (id == _hoverId) StrokeRect(cv, bx, y, MathF.Max(1, bw - 1), FlameRowH - 1, Warn, 1.5f);

                if (bw > 48)
                {
                    string label = string.IsNullOrEmpty(kind) ? "container" : kind;
                    string val = timing ? $"{n.Value:0.00}ms" : $"{(int)n.Value}";
                    cv.DrawText($"{label}  {val}", bx + 4, y + 3, Col(12, 12, 16), 12, _font);
                }
            }

            if (n.Value <= 0 || n.Kids == null || y > areaBottom) return;

            double span = n1 - n0, childAcc = n0;
            foreach (var k in n.Kids)
            {
                double c1 = Math.Min(childAcc + _flameNodes[k].Value / n.Value * span, n1);
                DrawNode(cv, area, k, childAcc, c1, depth + 1, timing);
                childAcc = c1;
                if (childAcc >= n1) break;
            }
        }

        // Hover highlight for the flame: sets the shared highlight id so the app element lights up too.
        // Uses last frame's bar rects (computed during draw) so it is ready before the overlay draws.
        private void UpdateFlameHover()
        {
            var pos = _p.PointerPos;
            if (pos.X < _flameArea.Min.X || pos.Y < _flameArea.Min.Y ||
                pos.X > _flameArea.Min.X + _flameArea.Size.X || pos.Y > _flameArea.Min.Y + _flameArea.Size.Y) return;
            for (int i = 0; i < _flameRects.Count; i++)
            {
                var rr = _flameRects[i];
                if (pos.X >= rr.x && pos.X <= rr.x + rr.w && pos.Y >= rr.y && pos.Y <= rr.y + rr.h) { _hoverId = rr.id; return; }
            }
        }

        private void DrawFlameTooltip(Canvas cv, bool timing)
        {
            if (_hoverId == 0 || !_idToSnap.TryGetValue(_hoverId, out int si) || !_flameNodes.TryGetValue(_hoverId, out var fn)) return;
            var sn = _snap[si];
            double kids = 0;
            foreach (var k in fn.Kids) kids += _flameNodes[k].Value;

            var lines = new List<string>();
            lines.Add($"{(string.IsNullOrEmpty(sn.Draw) ? "container" : sn.Draw)}  #{sn.Id}");
            if (!string.IsNullOrEmpty(sn.Text)) lines.Add("\"" + sn.Text + "\"");
            lines.Add($"{sn.W:0} x {sn.H:0} px");
            if (timing) { lines.Add($"inclusive {fn.Value:0.000} ms"); lines.Add($"self {MathF.Max(0f, (float)(fn.Value - kids)):0.000} ms"); }
            else lines.Add($"subtree {(int)fn.Value} elems");

            const float fs = 13f, pad = 6f, lh = 17f;
            float bw = 0;
            foreach (var l in lines) bw = MathF.Max(bw, l.Length * fs * 0.56f);
            bw += pad * 2;
            float bh = lines.Count * lh + pad * 2;
            var pos = _p.PointerPos;
            float tx = pos.X + 16, ty = pos.Y + 16;
            if (tx + bw > _flameArea.Min.X + _flameArea.Size.X) tx = pos.X - bw - 16;
            if (ty + bh > _flameArea.Min.Y + _flameArea.Size.Y) ty = pos.Y - bh - 16;

            cv.RectFilled(tx, ty, bw, bh, Col(14, 14, 18, 245));
            StrokeRect(cv, tx, ty, bw, bh, Accent, 1f);
            for (int i = 0; i < lines.Count; i++)
                cv.DrawText(lines[i], tx + pad, ty + pad + i * lh, i == 0 ? Text : TextDim, fs, _font);
        }

        private void BuildFlameInfo()
        {
            if (_selInfo.Count == 0)
            {
                Label("fi_none", "Click a flame bar to inspect the element.", TextDim, 14, 24);
                return;
            }
            for (int i = 0; i < _selInfo.Count; i++)
            {
                var (k, v, c) = _selInfo[i];
                if (k.Length == 0) SectionHeader("fih_" + i, v); // section header row
                else KeyVal("fi_" + i, k, v, c);
            }
            Btn("fi_elem", "Show in Elements", () => _tab = 1);
        }

        // Builds the right-hand info for the selected flame entry: identity, timing, and everything it paints.
        private void CaptureSelectedInfo()
        {
            _selInfo.Clear();
            if (_selectedId == 0) return;

            void Row(string k, string v, Color c) => _selInfo.Add((k, v, c));
            void Head(string t) => _selInfo.Add(("", t, Accent));

            bool inTree = _idToSnap.TryGetValue(_selectedId, out int si);
            Node n = inTree ? _snap[si] : null;

            Head("ELEMENT");
            Row("id", _selectedId.ToString(), Text);
            if (n != null)
            {
                Row("layout", $"{n.Lay} / {n.Pos}", Text);
                Row("size", $"{n.W:0} x {n.H:0} px", Text);
                Row("position", $"{n.X:0}, {n.Y:0}", Text);
                Row("layer", n.Layer.ToString(), Text);
                Row("visible", n.Visible.ToString(), n.Visible ? Accent2 : Danger);
                if (!string.IsNullOrEmpty(n.Handlers)) Row("handlers", n.Handlers, Accent2);
            }
            else Row("note", "not in the current tree", TextDim);

            // Timing from the active capture (render or layout), even if the element just left the tree.
            var disp = ActiveDisplay;
            double incl = -1, childSum = 0;
            for (int i = 0; i < disp.Count; i++)
            {
                if (disp[i].Id == _selectedId) incl = disp[i].Ms;
                if (disp[i].Parent == _selectedId) childSum += disp[i].Ms;
            }
            if (incl >= 0)
            {
                Head(ActivePhaseName + " TIME");
                Row("inclusive", $"{incl:0.000} ms", Warn);
                Row("self", $"{MathF.Max(0f, (float)(incl - childSum)):0.000} ms", Warn);
                if (_p.MillisecondsSpent > 0) Row("% of frame", $"{incl / _p.MillisecondsSpent * 100:0.0}%", Warn);
            }
            if (n != null)
            {
                Row("subtree elems", n.Subtree.ToString(), TextDim);
                Row("children", n.Kids.Count.ToString(), TextDim);
            }

            // What it renders - an element can paint several of these at once.
            Head("RENDERS");
            var h = _p.FindElementByID(_selectedId);
            var st = h.IsValid ? h.Data._elementStyle : null;
            if (st == null) { Row("(element not live)", "", TextDim); return; }

            if (!string.IsNullOrEmpty(h.Data.Paragraph)) Row("text", "\"" + (n?.Text ?? h.Data.Paragraph) + "\"", Col(70, 130, 235));
            if (h.Data.IsMarkdown) Row("markdown", "yes", Col(70, 130, 235));
            if (h.Data.IsRichText) Row("rich text", "yes", Col(70, 130, 235));
            if (st.GetValue(GuiProp.BackgroundColor) is Color bg && bg.A > 0f) Row("background", FormatStyle(bg), Text);
            if (st.GetValue(GuiProp.BackgroundGradient) is Gradient g && g.Type != GradientType.None) Row("gradient", g.Type.ToString(), Col(90, 190, 200));
            if (st.GetValue(GuiProp.BackgroundImage) != null) Row("image", "yes", Col(110, 200, 130));
            float bwid = Convert.ToSingle(st.GetValue(GuiProp.BorderWidth));
            if (bwid > 0f) Row("border", $"{bwid:0.#}px {FormatStyle(st.GetValue(GuiProp.BorderColor))}", Text);
            if (st.GetValue(GuiProp.Rounded) is Float4 rr && (rr.X > 0 || rr.Y > 0 || rr.Z > 0 || rr.W > 0)) Row("rounded", rr.ToString(), TextDim);
            if (st.GetValue(GuiProp.BoxShadow) is BoxShadow shdw && shdw.IsVisible) Row("box shadow", "yes", Warn);
            float blur = Convert.ToSingle(st.GetValue(GuiProp.BackdropBlur));
            if (blur > 0f) Row("backdrop blur", $"{blur:0.#}px", Danger);
            if (h.Data._renderCommands != null) Row("custom draw", h.Data._renderCommands.Count + " cmd", Accent2);
            if (h.Data._foregroundRenderCommands != null) Row("custom draw (fg)", h.Data._foregroundRenderCommands.Count + " cmd", Accent2);
            if (h.Data._scissorEnabled) Row("clip", "yes", TextDim);
        }

        // ================================================================= Panel: Render

        private void BuildRender(float w, float h)
        {
            using (_p.Column("rnd").Width(w).Height(h).Enter())
            {
                using (_p.Row("rnd_bar").Width(_p.Percent(100)).Height(26).BackgroundColor(BG2).Enter())
                    Label("rnd_stat", $"draw calls {_statDrawCalls}    triangles {_statTris}    vertices {_statVerts}", Text, 14, 26);

                using (BeginScroll("render", w, h - 22))
                {
                    if (_drawCallList.Count == 0) Label("rnd_empty", "(no draw calls captured yet)", TextDim, 12, 20);
                    for (int i = 0; i < _drawCallList.Count; i++)
                    {
                        var dc = _drawCallList[i];
                        string tag = dc.Text ? "  [text]" : dc.Brush != "None" ? "  [" + dc.Brush.ToLowerInvariant() + "]" : "";
                        KeyVal("dc_" + i, $"#{i}", $"{dc.Tris} tris{tag}", dc.Text ? Accent2 : Text);
                    }
                }
            }
        }

        // ================================================================= Panel: Input

        private void BuildInput(float w, float h)
        {
            using (BeginScroll("input", w, h))
            {
                var pos = _p.PointerPos;

                SectionHeader("in_h1", "POINTER");
                KeyVal("in_pos", "position", $"{pos.X:0}, {pos.Y:0}", Text);
                KeyVal("in_wheel", "wheel", _p.PointerWheel.ToString("0.##"), Text);
                KeyVal("in_move", "moving", _p.IsPointerMoving.ToString(), Text);
                KeyVal("in_lmb", "left", _p.IsPointerDown(PaperMouseBtn.Left).ToString(), _p.IsPointerDown(PaperMouseBtn.Left) ? Accent2 : TextDim);
                KeyVal("in_rmb", "right", _p.IsPointerDown(PaperMouseBtn.Right).ToString(), _p.IsPointerDown(PaperMouseBtn.Right) ? Accent2 : TextDim);

                SectionHeader("in_h2", "FOCUS / HOVER");
                KeyVal("in_hov", "hovered", IdLabel(_p.HoveredElementId), Text);
                KeyVal("in_act", "active", IdLabel(_p.ActiveElementId), Text);
                KeyVal("in_foc", "focused", IdLabel(_p.FocusedElementId), Text);

                SectionHeader("in_h3", "HIT STACK (under cursor)");
                var hits = new List<int>();
                for (int i = 0; i < _snap.Count; i++)
                {
                    var n = _snap[i];
                    if (!n.Visible || n.W <= 0 || n.H <= 0) continue;
                    if (pos.X < n.X || pos.Y < n.Y || pos.X > n.X + n.W || pos.Y > n.Y + n.H) continue;
                    hits.Add(i);
                }
                if (hits.Count == 0) Label("in_nohit", "(nothing)", TextDim, 12, 18);
                hits.Sort((a, b) => _snap[a].Layer != _snap[b].Layer ? _snap[b].Layer.CompareTo(_snap[a].Layer) : b.CompareTo(a));
                for (int i = 0; i < hits.Count && i < 24; i++)
                {
                    var n = _snap[hits[i]];
                    string kind = n.Lay == LayoutType.Row ? "Row" : "Col";
                    _p.Box("hit_" + i, i).Width(_p.Percent(100)).Height(22)
                        .Text($"{kind} #{n.Id}  ({n.W:0}x{n.H:0})", _font).FontSize(14).TextColor(TextDim)
                        .Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft)
                        .Hovered.BackgroundColor(Hover).End()
                        .OnClick(n.Id, (id, _) => { _selectedId = id; _tab = 1; });
                }
            }
        }

        private string IdLabel(int id)
        {
            if (id == 0) return "none";
            if (_idToSnap.TryGetValue(id, out int si))
            {
                var n = _snap[si];
                string kind = n.Lay == LayoutType.Row ? "Row" : "Col";
                return $"{kind} #{id}";
            }
            return "#" + id;
        }

        // ================================================================= Panel: Atlas

        private void BuildAtlas(float w, float h)
        {
            var fe = _p.Canvas?.Text?.FontEngine;

            using (_p.Column("atl").Width(w).Height(h).Enter())
            {
                using (_p.Row("atl_bar").Width(_p.Percent(100)).Height(22).BackgroundColor(BG2).Enter())
                {
                    if (fe == null) Label("atl_none", "(no font engine)", TextDim, 12, 22);
                    else Label("atl_stat", $"atlas {fe.Width}x{fe.Height}   version {fe.AtlasVersion}", Text, 12, 22);
                }

                float availW = w - 20, availH = h - 42;
                float dw = availW, dh = availH;
                if (fe != null && fe.Width > 0 && fe.Height > 0)
                {
                    float aspect = fe.Width / (float)fe.Height;
                    dh = MathF.Min(availH, availW / aspect);
                    dw = dh * aspect;
                }

                using (_p.Box("atl_img").Width(w).Height(h - 22).Enter())
                    _p.Draw((cv, rect) =>
                    {
                        cv.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, Col(12, 12, 16));
                        var tex = fe?.Texture;
                        if (tex != null)
                        {
                            float ix = rect.Min.X + 10, iy = rect.Min.Y + 10;
                            StrokeRect(cv, ix - 1, iy - 1, dw + 2, dh + 2, Line, 1f);
                            cv.DrawImage(tex, ix, iy, dw, dh);
                        }
                    });
            }
        }
    }
}
