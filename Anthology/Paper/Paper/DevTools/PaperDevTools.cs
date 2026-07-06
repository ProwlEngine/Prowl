using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Built-in developer tools for Paper — a browser-inspector-style overlay (console, element
    /// tree, profiler, render/input/atlas panels). Off by default; set <see cref="Enabled"/> and
    /// press F12 to toggle. Lives inside the Paper assembly so it can read internal frame state
    /// directly; Paper only calls a handful of hooks (OnBeginFrame / OnEndFrameStart / Phase /
    /// OnEndFrameEnd). All of its own UI is built through the normal ElementBuilder API.
    /// </summary>
    public sealed partial class PaperDevTools
    {
        private readonly Paper _p;

        /// <summary>Opt-in switch. When false F12 does nothing and there is zero per-frame overhead.</summary>
        public bool Enabled = false;

        /// <summary>Font used to render the DevTools UI. If null, the first system font is loaded lazily.</summary>
        public FontFile Font;

        private bool _open;
        private float _panelH = 440f;
        private int _tab;
        private static readonly string[] Tabs = { "Console", "Elements", "Profiler", "Render", "Input", "Atlas" };

        // True while the panel is up and we should time EndFrame phases.
        internal bool Timing => Enabled && _open;

        // Our own element ids, so the snapshot can skip the DevTools subtree (no infinite mirror).
        private int _panelId, _overlayId;

        // Scroll offsets + measured content heights, keyed by region name (kept here rather than in
        // Paper storage so DevTools is fully self-contained).
        private readonly Dictionary<string, float> _scroll = new();
        private readonly Dictionary<string, float> _scrollContent = new();

        public PaperDevTools(Paper paper) => _p = paper;

        // ---------------------------------------------------------------- Frame hooks

        internal void OnBeginFrame()
        {
            if (!Enabled) return;

            if (_p.IsKeyPressed(PaperKey.F12))
            {
                _open = !_open;
                Log(_open ? "DevTools opened" : "DevTools closed", LogLevel.Info);
            }
            // The app is confined to the top region by reparenting it into a clipped viewport in
            // BuildShell (after it has finished building), which works regardless of how the app sizes.
        }

        internal void OnEndFrameStart()
        {
            _phaseAccum.Clear();
            _renderTimes.Clear();
            _layoutTimes.Clear();
            if (!Enabled || !_open) return;
            EnsureFont();
            if (_font == null) return; // no font available -> cannot draw text, bail gracefully
            BuildShell();
        }

        // Records the elapsed time of one EndFrame phase and returns the timestamp to measure the next from.
        internal long Phase(string name, long since)
        {
            if (!Timing) return since;
            long now = Stopwatch.GetTimestamp();
            _phaseAccum.Add((name, (now - since) * 1000.0 / Stopwatch.Frequency));
            return now;
        }

        internal void OnEndFrameEnd()
        {
            if (!Enabled) return;

            // Frame-time history (rolling ring buffer).
            _frameHist[_frameHead] = _p.MillisecondsSpent;
            _frameHead = (_frameHead + 1) % _frameHist.Length;

            // Geometry stats from the frame we just rendered (canvas is filled now, cleared next BeginFrame).
            var canvas = _p.Canvas;
            if (canvas != null)
            {
                var dcs = canvas.DrawCalls;
                _statDrawCalls = dcs.Count;
                _statVerts = canvas.Vertices.Count;
                _statTris = canvas.Indices.Count / 3;
                CaptureDrawCalls(dcs);
            }

            // Publish this frame's phase timings for the profiler to display next frame.
            _phaseDisplay.Clear();
            _phaseDisplay.AddRange(_phaseAccum);

            // Publish this frame's render + layout samples (frozen on the last captured frame when paused).
            if (DeepProfiling)
            {
                _renderDisplay.Clear();
                _renderDisplay.AddRange(_renderTimes);
                PublishLayout();
            }

            if (_open) CaptureSnapshot();
        }

        // ---------------------------------------------------------------- Font

        private FontFile _font;
        private bool _fontTried;

        private void EnsureFont()
        {
            if (Font != null) { _font = Font; return; }
            if (_font != null || _fontTried) return;
            _fontTried = true;
            try { _font = _p.EnumerateSystemFonts().FirstOrDefault(); }
            catch { _font = null; }
            if (_font == null) Log("DevTools: no font available (set PaperDevTools.Font).", LogLevel.Warning);
        }

        // ---------------------------------------------------------------- Shell

        private const int PanelLayer = 1_000_000;

        private void BuildShell()
        {
            float w = _p.Width;
            float appTop = MathF.Max(60f, _p.Height - _panelH);

            // Split the screen: move the app (already built this frame as root's children) into a
            // clipped viewport occupying the top region, so it shrinks/clips above the panel instead
            // of being covered by it. Works no matter how the app sizes itself.
            var root = _p.RootElement;
            var appKids = new List<int>(root.Data.ChildIndices);
            root.Data.ChildIndices.Clear();

            using (_p.Box("__dt_appviewport").PositionType(PositionType.SelfDirected).Position(0, 0)
                .Size(w, appTop).Clip().Enter())
            {
                int vpIndex = _p.CurrentParent.Index;
                var vpKids = _p.CurrentParent.Data.ChildIndices;
                for (int i = 0; i < appKids.Count; i++)
                {
                    vpKids.Add(appKids[i]);
                    _p.GetElementData(appKids[i]).ParentIndex = vpIndex;
                }
            }

            // Full-screen overlay for element highlights (below the panel, above the app).
            using (_p.Box("__dt_overlay").PositionType(PositionType.SelfDirected).Position(0, 0)
                .Size(w, _p.Height).Layer(PanelLayer - 1).IsNotInteractable().Enter())
            {
                _overlayId = _p.CurrentParent.Data.ID;
                _p.Draw((cv, _) => DrawOverlay(cv));
            }

            // Pick mode: hover-highlight the app element under the cursor, click to select it.
            UpdatePick(appTop);

            // The docked panel.
            using (_p.Column("__dt_panel").PositionType(PositionType.SelfDirected).Position(0, appTop)
                .Size(w, _panelH).Layer(PanelLayer).BackgroundColor(BG).Clip().StopEventPropagation().Enter())
            {
                _panelId = _p.CurrentParent.Data.ID;

                // Drag strip to resize.
                _p.Box("__dt_resize").Height(5).Width(_p.Percent(100)).BackgroundColor(Line)
                    .Hovered.BackgroundColor(Accent).End()
                    .OnDragging(e => _panelH = Math.Clamp(_panelH - (float)e.Delta.Y, 140f, _p.Height - 80f));

                BuildHeader(w);

                // Content area.
                float contentH = _panelH - 5 - HeaderH;
                using (_p.Box("__dt_content").Width(_p.Percent(100)).Height(contentH).Clip().Enter())
                {
                    switch (_tab)
                    {
                        case 0: BuildConsole(w, contentH); break;
                        case 1: BuildInspector(w, contentH); break;
                        case 2: BuildProfiler(w, contentH); break;
                        case 3: BuildRender(w, contentH); break;
                        case 4: BuildInput(w, contentH); break;
                        case 5: BuildAtlas(w, contentH); break;
                    }
                }
            }
        }

        private const float HeaderH = 36f;

        private void BuildHeader(float w)
        {
            using (_p.Row("__dt_header").Width(_p.Percent(100)).Height(HeaderH).BackgroundColor(BG2)
                .ChildTop().ChildBottom().Enter())
            {
                for (int i = 0; i < Tabs.Length; i++)
                {
                    bool sel = _tab == i;
                    int idx = i;
                    _p.Box("tab", i).Width(_p.Auto).Height(_p.Percent(100)).Padding(14, 14, 0, 0)
                        .Text(Tabs[i], _font).FontSize(16)
                        .TextColor(sel ? Text : TextDim).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                        .BackgroundColor(sel ? Panel : Transparent)
                        .Hovered.BackgroundColor(Hover).End()
                        .OnClick(idx, (t, _) => _tab = t);
                }

                // Spacer.
                _p.Box("__dt_hspace").Width(_p.Stretch()).Height(1);

                // Toggles.
                ToggleBtn("Pick", _pickMode, () => _pickMode = !_pickMode);
                ToggleBtn("Overlay", _layoutOverlay, () => _layoutOverlay = !_layoutOverlay);
                _p.Box("__dt_close").Width(40).Height(_p.Percent(100))
                    .Text("X", _font).FontSize(16).TextColor(TextDim).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                    .Hovered.TextColor(Danger).End()
                    .OnClick(e => _open = false);
            }
        }

        private void ToggleBtn(string label, bool on, Action toggle)
        {
            _p.Box("tgl_" + label).Width(_p.Auto).Height(_p.Percent(100)).Padding(12, 12, 0, 0)
                .Text(label, _font).FontSize(15)
                .TextColor(on ? Accent : TextDim).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter)
                .BackgroundColor(on ? Hover : Transparent)
                .Hovered.BackgroundColor(Hover).End()
                .OnClick(e => toggle());
        }

        // ---------------------------------------------------------------- Small UI helpers

        // Colours (dark inspector theme).
        static Color Col(int r, int g, int b, int a = 255) => System.Drawing.Color.FromArgb(a, r, g, b);
        static readonly Color BG = Col(22, 22, 26, 250);
        static readonly Color BG2 = Col(30, 30, 36);
        static readonly Color Panel = Col(38, 38, 46);
        static readonly Color Line = Col(58, 58, 68);
        static readonly Color Text = Col(222, 222, 230);
        static readonly Color TextDim = Col(140, 142, 154);
        static readonly Color Accent = Col(88, 154, 250);
        static readonly Color Accent2 = Col(120, 200, 140);
        static readonly Color Warn = Col(240, 190, 90);
        static readonly Color Danger = Col(240, 110, 110);
        static readonly Color Hover = Col(60, 62, 74);
        static readonly Color SelBg = Col(48, 82, 150, 200);
        static readonly Color Transparent = Col(0, 0, 0, 0);

        // A single-line label element.
        private void Label(string id, string text, Color color, float size = 15f, float height = 22f,
            Prowl.PaperUI.TextAlignment align = Prowl.PaperUI.TextAlignment.MiddleLeft)
        {
            _p.Box(id).Width(_p.Percent(100)).Height(height).IsNotInteractable()
                .Text(text ?? "", _font).FontSize(size).TextColor(color).Alignment(align);
        }

        // A "key : value" row.
        private void KeyVal(string id, string key, string val, Color valColor)
        {
            using (_p.Row(id).Width(_p.Percent(100)).Height(22).IsNotInteractable().Enter())
            {
                _p.Box("k").Width(155).Height(22).Text(key, _font).FontSize(14)
                    .TextColor(TextDim).Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft);
                _p.Box("v").Width(_p.Stretch()).Height(22).Text(val ?? "", _font).FontSize(14)
                    .TextColor(valColor).Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft);
            }
        }

        private void SectionHeader(string id, string text)
        {
            _p.Box(id).Width(_p.Percent(100)).Height(26).Padding(8, 0, 6, 0).BackgroundColor(BG2)
                .Text(text, _font).FontSize(14).TextColor(Accent).Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft);
        }

        // ---------------------------------------------------------------- Scroll region

        private sealed class ScrollScope : IDisposable
        {
            private readonly IDisposable _inner, _outer;
            public ScrollScope(IDisposable inner, IDisposable outer) { _inner = inner; _outer = outer; }
            public void Dispose() { _inner.Dispose(); _outer.Dispose(); }
        }

        // A vertically scrollable region. Children go inside the returned scope. Content column is
        // self-directed and offset by the stored scroll amount; wheel + measured height drive it.
        private IDisposable BeginScroll(string key, float w, float h)
        {
            float sy = _scroll.GetValueOrDefault(key);
            float maxScroll = MathF.Max(0f, _scrollContent.GetValueOrDefault(key) - h);
            sy = Math.Clamp(sy, 0f, maxScroll);
            _scroll[key] = sy;

            var outer = _p.Box("scr_" + key).Width(w).Height(h).Clip()
                .OnScroll(e =>
                {
                    float m = MathF.Max(0f, _scrollContent.GetValueOrDefault(key) - h);
                    _scroll[key] = Math.Clamp(_scroll.GetValueOrDefault(key) - (float)e.Delta * 30f, 0f, m);
                });
            var oScope = outer.Enter();

            var inner = _p.Column("sci_" + key).PositionType(PositionType.SelfDirected).Position(0, -sy)
                .Width(_p.Percent(100)).Height(_p.Auto)
                .OnPostLayout((h2, rect) => _scrollContent[key] = (float)rect.Size.Y);
            var iScope = inner.Enter();

            return new ScrollScope(iScope, oScope);
        }
    }
}
