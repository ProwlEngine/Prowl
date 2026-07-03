using System;
using System.IO;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace OrigamiSample
{
    // Shared Nebula palette (nebula.css), used by the sample's custom chrome and the playground cards.
    // The docked panels themselves are styled by Origami's default theme (also the Nebula palette now).
    internal static class Palette
    {
        public static Color C(int r, int g, int b, float a = 1f) => Color32.FromArgb((int)Math.Round(a * 255), r, g, b);

        // Sample-only chrome: the void backdrop, panel root, and card overlays (not part of the widget theme).
        public static readonly Color Void = C(6, 4, 9);
        public static readonly Color RootBg = C(10, 8, 18, 0.4f);
        public static readonly Color CardBg = C(255, 255, 255, 0.022f);
        public static readonly Color CardBgHover = C(255, 255, 255, 0.035f);
        public static readonly Color Transparent = C(0, 0, 0, 0f);
        public static readonly Color White = C(255, 255, 255);
        public const float TS = 1.4f;

        // Everything else is the SAME colour the widgets use, so read it straight from Origami's default
        // (Nebula) theme instead of duplicating literals — one source of truth.
        private static OrigamiTheme T => Origami.Root;
        public static Color GlassIn => T.Glass;
        public static Color BdSoft => T.BorderSoft;
        public static Color Bd => T.Neutral.C200;
        public static Color Acc => T.Primary.C500;
        public static Color Acc300 => T.Primary.C700;
        public static Color Hover => T.Hover;
        public static Color THi => T.Ink.C500;
        public static Color TMid => T.Ink.C300;
        public static Color TLo => T.Ink.C200;
    }

    public class NebulaEditor
    {
        private readonly Paper P;
        private readonly FontFile _geist, _geistMed, _geistSemi, _geistBold, _grotesk, _mono;

        private float _time;
        private float _fps;
        private readonly Random _rng = new(20240630);

        private readonly DockSpace _dock;
        private DockNode _centerLeaf;
        private WidgetPlaygroundPanel _widgets;

        /// <summary>Select the Widget Playground category (used by the --cat screenshot flag).</summary>
        public void SetPlaygroundCategory(int cat) => _widgets?.SetCategory(cat);

        // ---- nebula background ----
        private struct Cloud { public float cx, cy, ang, targetAng, timer, rf, phase; public Color color; }
        private Cloud[] _clouds;
        private const int StarTexSize = 1024;
        private object _starTex;
        private struct Comet { public bool active; public float x, y, dx, dy, speed, len, life, dur; }
        private readonly Comet[] _comets = new Comet[3];
        private float _cometTimer = 2.5f;

        public NebulaEditor(Paper paper)
        {
            P = paper;
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            FontFile Load(string f) { using var s = File.OpenRead(Path.Combine(dir, f)); return new FontFile(s); }
            _geist = Load("Geist-Regular.ttf");
            _geistMed = Load("Geist-Medium.ttf");
            _geistSemi = Load("Geist-SemiBold.ttf");
            _geistBold = Load("Geist-Bold.ttf");
            _grotesk = Load("SpaceGrotesk-Bold.ttf");
            _mono = Load("JetBrainsMono-Regular.ttf");

            // Origami's default theme is now the Nebula palette; it only needs fonts supplied.
            Origami.Root.Font = _geist;
            Origami.Root.FontMedium = _geistMed;
            Origami.Root.FontSemiBold = _geistSemi;
            Origami.Root.FontBold = _geistBold;
            Origami.Root.FontMono = _mono;

            var cc = new[] { Palette.C(168, 85, 247, 0.40f), Palette.C(217, 107, 216, 0.26f), Palette.C(80, 90, 220, 0.24f), Palette.C(52, 211, 238, 0.14f) };
            var cr = new[] { 0.42f, 0.40f, 0.40f, 0.30f };
            var sx = new[] { 0.43f, 0.67f, 0.52f, 0.14f };
            var sy = new[] { 0.37f, 0.55f, 0.92f, 0.60f };
            _clouds = new Cloud[4];
            for (int i = 0; i < 4; i++)
                _clouds[i] = new Cloud { cx = sx[i], cy = sy[i], ang = (float)(_rng.NextDouble() * MathF.Tau), rf = cr[i], color = cc[i], phase = (float)(_rng.NextDouble() * 6.28f), timer = 0f };

            _dock = BuildDock();
        }

        // Editor layout: hierarchy | (Scene/Shader/Visual/Widgets tabs) | inspector, with an
        // asset browser + console strip beneath the left+center columns. A real Origami dock tree,
        // so every divider is a live splitter and every tab drags out into a floating window.
        private DockSpace BuildDock()
        {
            var scene = new EmptyPanel("Scene", "Scene viewport", _geistSemi, _geist);
            var shader = new EmptyPanel("Shader Graph", "Node canvas", _geistSemi, _geist);
            var visual = new EmptyPanel("Visual Scripts", "Block canvas", _geistSemi, _geist);
            _widgets = new WidgetPlaygroundPanel(_geist, _geistMed, _geistSemi, _mono);
            var widgets = _widgets;

            var hierarchy = new EmptyPanel("Hierarchy", "Scene tree", _geistSemi, _geist);
            var inspector = new EmptyPanel("Inspector", "Component properties", _geistSemi, _geist);
            var assets = new EmptyPanel("Assets", "Project browser", _geistSemi, _geist);
            var console = new EmptyPanel("Console", "Log output", _geistSemi, _geist);

            _centerLeaf = DockNode.Leaf(scene, shader, visual, widgets);
            _centerLeaf.ActiveTabIndex = 3; // show the Widget Playground

            var mainRow = DockNode.Split(SplitDirection.Horizontal, 0.23f, DockNode.Leaf(hierarchy), _centerLeaf);
            var bottomRow = DockNode.Split(SplitDirection.Horizontal, 0.5f, DockNode.Leaf(assets), DockNode.Leaf(console));
            var leftGroup = DockNode.Split(SplitDirection.Vertical, 0.70f, mainRow, bottomRow);
            var root = DockNode.Split(SplitDirection.Horizontal, 0.77f, leftGroup, DockNode.Leaf(inspector));

            return new DockSpace(root);
        }

        public void Render(int w, int h)
        {
            _time += P.DeltaTime;
            if (_starTex == null) BuildStarTexture();
            UpdateClouds(P.DeltaTime);
            UpdateComets(P.DeltaTime);

            Origami.BeginFrame(P, P.DeltaTime);

            using (P.Column("Root").BackgroundColor(Palette.Void).Enter())
            {
                using (P.Box("Nebula").PositionType(PositionType.SelfDirected)
                    .Left(0).Top(0).Width(P.Percent(100)).Height(P.Percent(100)).Enter())
                {
                    P.Draw(DrawNebula);
                }

                const float gap = 8f, top = 44f;
                _dock.Draw(P, gap, top, w - gap * 2, h - top - gap);

                Origami.EndFrame(P);

                TopStrip(w);
            }
        }

        // ====================================================================
        // Top strip: wordmark on the left, drag handle, traffic lights on the right.
        // ====================================================================
        private void TopStrip(int w)
        {
            using (P.Row("Wordmark").PositionType(PositionType.SelfDirected).Left(16).Top(0).Width(P.Auto).Height(44).Enter())
            {
                using (P.Box("WmLogo").Size(20).Rounded(6).Margin(0, 0, P.Stretch(), P.Stretch())
                    .BackgroundLinearGradient(0, 0, 1, 1, Palette.Acc, Palette.C(189, 107, 255))
                    .Enter())
                    P.Draw((vg, r) => SvgIcon.Draw(vg, SvgIcon.Mark, (float)r.Min.X + 4, (float)r.Min.Y + 4, 12, Palette.White, 1.2f));

                P.Box("WmText").Width(P.Auto).Height(P.Auto).Margin(9, 0, P.Stretch(), P.Stretch())
                    .Text("PROWL", _grotesk).FontSize(13 * Palette.TS).LetterSpacing(3f).TextColor(Palette.THi).Alignment(TextAlignment.MiddleLeft);
                P.Box("WmSub").Width(P.Auto).Height(P.Auto).Margin(9, 0, P.Stretch(), P.Stretch())
                    .Text("Origami Editor", _mono).FontSize(10.5f * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleLeft);
            }

            // FPS counter, right side. Exponential moving average so it reads steady, not jittery.
            float dt = P.DeltaTime;
            if (dt > 0.0001f) _fps = _fps <= 0f ? 1f / dt : _fps * 0.92f + (1f / dt) * 0.08f;
            using (P.Row("FpsBar").PositionType(PositionType.SelfDirected).Left(w - 116).Top(0).Width(100).Height(44).Enter())
                P.Box("FpsText").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, P.Stretch(), P.Stretch()).IsNotInteractable()
                    .Text($"{_fps:F0} FPS", _mono).FontSize(11 * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleRight);
        }

        // ====================================================================
        // Nebula background (matches the launcher sample).
        // ====================================================================
        private const float CloudSpeed = 0.045f;
        private const float CloudTurn = 2.5f;

        private void UpdateClouds(float dt)
        {
            if (_clouds == null) return;
            dt = Math.Min(dt, 0.1f);
            float turn = 1f - MathF.Exp(-dt / CloudTurn);
            for (int i = 0; i < _clouds.Length; i++)
            {
                ref Cloud c = ref _clouds[i];
                if (c.cx < -0.2f || c.cx > 1.2f || c.cy < -0.2f || c.cy > 1.2f)
                    c.targetAng = MathF.Atan2(0.5f - c.cy, 0.5f - c.cx);
                else if ((c.timer -= dt) <= 0f)
                {
                    c.targetAng = (float)(_rng.NextDouble() * MathF.Tau);
                    c.timer = (float)(3.0 + _rng.NextDouble() * 5.0);
                }
                float da = (float)MathF.IEEERemainder(c.targetAng - c.ang, MathF.Tau);
                c.ang += da * turn;
                c.cx += MathF.Cos(c.ang) * CloudSpeed * dt;
                c.cy += MathF.Sin(c.ang) * CloudSpeed * dt;
            }
        }

        private void BuildStarTexture()
        {
            const int T = StarTexSize;
            var data = new byte[T * T * 4];
            for (int s = 0; s < 170; s++)
            {
                float sx = (float)(_rng.NextDouble() * T), sy = (float)(_rng.NextDouble() * T);
                float bright = (float)(_rng.NextDouble() * 0.55 + 0.30);
                float rad = (float)(_rng.NextDouble() * 0.6 + 0.35);
                int rr = (int)MathF.Ceiling(rad * 2f) + 1;
                for (int oy = -rr; oy <= rr; oy++)
                    for (int ox = -rr; ox <= rr; ox++)
                    {
                        float a = bright * MathF.Max(0f, 1f - MathF.Sqrt(ox * ox + oy * oy) / (rad * 1.05f));
                        if (a <= 0f) continue;
                        int px = (((int)sx + ox) % T + T) % T, py = (((int)sy + oy) % T + T) % T;
                        int idx = (py * T + px) * 4;
                        byte va = (byte)(a * 255f);
                        if (va > data[idx + 3]) { data[idx] = va; data[idx + 1] = va; data[idx + 2] = va; data[idx + 3] = va; }
                    }
            }
            _starTex = P.Renderer.CreateTexture((uint)T, (uint)T);
            P.Renderer.SetTextureData(_starTex, new IntRect(0, 0, T, T), data);
        }

        private void UpdateComets(float dt)
        {
            dt = Math.Min(dt, 0.1f);
            if ((_cometTimer -= dt) <= 0f)
            {
                for (int i = 0; i < _comets.Length; i++)
                {
                    if (_comets[i].active) continue;
                    float a = (float)(_rng.NextDouble() * MathF.Tau);
                    float cdx = MathF.Cos(a), cdy = MathF.Sin(a);
                    float cx0 = (float)(0.1 + _rng.NextDouble() * 0.8);
                    float cy0 = (float)(_rng.NextDouble() * 0.6);
                    if ((cx0 < 0.28f && cdx < 0f) || (cx0 > 0.72f && cdx > 0f)) cdx = -cdx;
                    if ((cy0 < 0.28f && cdy < 0f) || (cy0 > 0.72f && cdy > 0f)) cdy = -cdy;
                    _comets[i] = new Comet
                    {
                        active = true,
                        x = cx0, y = cy0, dx = cdx, dy = cdy,
                        speed = (float)(0.09 + _rng.NextDouble() * 0.07),
                        len = (float)(0.08 + _rng.NextDouble() * 0.06),
                        dur = (float)(2.8 + _rng.NextDouble() * 1.8)
                    };
                    break;
                }
                _cometTimer = (float)(4.0 + _rng.NextDouble() * 6.0);
            }
            for (int i = 0; i < _comets.Length; i++)
            {
                ref Comet c = ref _comets[i];
                if (!c.active) continue;
                c.life += dt / c.dur;
                if (c.life >= 1f) { c.active = false; continue; }
                c.x += c.dx * c.speed * dt;
                c.y += c.dy * c.speed * dt;
            }
        }

        private void DrawNebula(Canvas vg, Rect rect)
        {
            float x = (float)rect.Min.X, y = (float)rect.Min.Y, w = (float)rect.Size.X, h = (float)rect.Size.Y;
            float big = Math.Max(w, h);

            vg.BeginPath(); vg.Rect(x, y, w, h); vg.SetFillColor(Palette.Void); vg.Fill();

            RadialFill(vg, x, y, w, h, x + w * 0.7f, y + h * 0.2f, 0, big * 0.95f, Palette.C(26, 15, 46, 1f), Palette.C(5, 3, 12, 0f));

            float t = _time;
            foreach (var c in _clouds)
            {
                float rad = w * c.rf * (1f + 0.10f * MathF.Sin(t * 0.09f + c.phase));
                RadialFill(vg, x, y, w, h, x + c.cx * w, y + c.cy * h, 0, rad, c.color, Color32.FromArgb(0, c.color));
            }
            RadialFill(vg, x, y, w, h,
                x + w * 0.5f + MathF.Sin(t * 0.06f) * 22f,
                y + h * 0.40f + MathF.Sin(t * 0.05f + 2.0f) * 16f,
                0, w * 0.46f, Palette.C(140, 92, 240, 0.38f), Palette.C(140, 92, 240, 0f));

            float pcx = x + w - w * 0.12f - 110f;
            float pcy = y + h * 0.14f + 110f + MathF.Sin(t * 0.32f) * 16f;
            vg.SaveState();
            vg.SetRadialBrush(pcx, pcy, 90, 210, Palette.C(168, 85, 247, 0.45f), Palette.C(168, 85, 247, 0f));
            vg.BeginPath(); vg.Circle(pcx, pcy, 210); vg.Fill();
            vg.RestoreState();
            vg.SaveState();
            vg.SetRadialBrush(pcx - 35, pcy - 44, 0, 200, Palette.C(176, 124, 232, 1f), Palette.C(26, 15, 51, 1f));
            vg.BeginPath(); vg.Circle(pcx, pcy, 110); vg.Fill();
            vg.RestoreState();

            if (_starTex != null)
                for (float ty = y; ty < y + h; ty += StarTexSize)
                    for (float tx = x; tx < x + w; tx += StarTexSize)
                        vg.DrawImage(_starTex, tx, ty, StarTexSize, StarTexSize);

            foreach (var c in _comets)
            {
                if (!c.active) continue;
                float env = MathF.Sin(c.life * MathF.PI);
                if (env <= 0.01f) continue;
                float hx = x + c.x * w, hy = y + c.y * h;
                float vx = c.dx * w, vy = c.dy * h;
                float vlen = MathF.Max(1e-4f, MathF.Sqrt(vx * vx + vy * vy));
                float ux = vx / vlen, uy = vy / vlen;
                float L = c.len * w;
                float tlx = hx - ux * L, tly = hy - uy * L;
                float perpx = -uy, perpy = ux, hw = 1.7f;
                vg.SaveState();
                vg.SetLinearBrush(hx, hy, tlx, tly, Palette.C(216, 190, 255, env * 0.85f), Palette.C(216, 190, 255, 0f));
                vg.BeginPath();
                vg.MoveTo(hx + perpx * hw, hy + perpy * hw);
                vg.LineTo(hx - perpx * hw, hy - perpy * hw);
                vg.LineTo(tlx, tly);
                vg.ClosePath();
                vg.FillComplex();
                vg.RestoreState();
                vg.BeginPath(); vg.Circle(hx, hy, 2.2f); vg.SetFillColor(Palette.C(255, 255, 255, env)); vg.Fill();
            }

            RadialFill(vg, x, y, w, h, x + w * 0.5f, y + h * 0.4f, big * 0.55f, big * 0.9f, Palette.C(0, 0, 0, 0f), Palette.C(0, 0, 0, 0.55f));
        }

        private static void RadialFill(Canvas vg, float x, float y, float w, float h, float cx, float cy, float ir, float or, Color inner, Color outer)
        {
            vg.SaveState();
            vg.SetRadialBrush(cx, cy, ir, or, inner, outer);
            vg.BeginPath(); vg.Rect(x, y, w, h); vg.Fill();
            vg.RestoreState();
        }
    }
}
