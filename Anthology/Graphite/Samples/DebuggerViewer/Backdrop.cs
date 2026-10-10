using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace DebuggerViewer;

internal sealed class Backdrop
{
    private const float CloudSpeed = 0.045f;
    private const float CloudTurn = 2.5f;
    private const int StarTexSize = 1024;

    private struct Cloud
    {
        public float X, Y, Angle, TargetAngle, Timer, Radius, Phase;
        public Color Color;
    }

    private struct Comet
    {
        public bool Active;
        public float X, Y, DirX, DirY, Speed, Length, Life, Duration;
    }

    public static readonly Color Void = Rgba(6, 4, 9);

    private readonly Random _rng = new(20240630);
    private readonly Cloud[] _clouds = new Cloud[4];
    private readonly Comet[] _comets = new Comet[3];
    private float _cometTimer = 2.5f;
    private float _time;
    private object? _stars;

    public Backdrop()
    {
        Color[] colors = { Rgba(168, 85, 247, 0.40f), Rgba(217, 107, 216, 0.26f), Rgba(80, 90, 220, 0.24f), Rgba(52, 211, 238, 0.14f) };
        float[] radii = { 0.42f, 0.40f, 0.40f, 0.30f };
        float[] xs = { 0.43f, 0.67f, 0.52f, 0.14f };
        float[] ys = { 0.37f, 0.55f, 0.92f, 0.60f };
        for (int i = 0; i < _clouds.Length; i++)
            _clouds[i] = new Cloud { X = xs[i], Y = ys[i], Angle = (float)(_rng.NextDouble() * MathF.Tau), Radius = radii[i], Color = colors[i], Phase = (float)(_rng.NextDouble() * 6.28f) };
    }

    private static Color Rgba(int r, int g, int b, float a = 1f) => Color.FromArgb((int)MathF.Round(a * 255), r, g, b);

    public void Update(Paper paper, float dt)
    {
        dt = MathF.Min(dt, 0.1f);
        _time += dt;
        if (_stars == null)
            BuildStars(paper);
        UpdateClouds(dt);
        UpdateComets(dt);
    }

    private void UpdateClouds(float dt)
    {
        float turn = 1f - MathF.Exp(-dt / CloudTurn);
        for (int i = 0; i < _clouds.Length; i++)
        {
            ref Cloud c = ref _clouds[i];
            if (c.X < -0.2f || c.X > 1.2f || c.Y < -0.2f || c.Y > 1.2f)
                c.TargetAngle = MathF.Atan2(0.5f - c.Y, 0.5f - c.X);
            else if ((c.Timer -= dt) <= 0f)
            {
                c.TargetAngle = (float)(_rng.NextDouble() * MathF.Tau);
                c.Timer = (float)(3.0 + _rng.NextDouble() * 5.0);
            }
            c.Angle += MathF.IEEERemainder(c.TargetAngle - c.Angle, MathF.Tau) * turn;
            c.X += MathF.Cos(c.Angle) * CloudSpeed * dt;
            c.Y += MathF.Sin(c.Angle) * CloudSpeed * dt;
        }
    }

    private void BuildStars(Paper paper)
    {
        const int size = StarTexSize;
        byte[] data = new byte[size * size * 4];
        for (int s = 0; s < 170; s++)
        {
            float sx = (float)(_rng.NextDouble() * size), sy = (float)(_rng.NextDouble() * size);
            float bright = (float)(_rng.NextDouble() * 0.55 + 0.30);
            float radius = (float)(_rng.NextDouble() * 0.6 + 0.35);
            int reach = (int)MathF.Ceiling(radius * 2f) + 1;
            for (int oy = -reach; oy <= reach; oy++)
                for (int ox = -reach; ox <= reach; ox++)
                {
                    float a = bright * MathF.Max(0f, 1f - MathF.Sqrt(ox * ox + oy * oy) / (radius * 1.05f));
                    if (a <= 0f)
                        continue;
                    int px = (((int)sx + ox) % size + size) % size, py = (((int)sy + oy) % size + size) % size;
                    int idx = (py * size + px) * 4;
                    byte value = (byte)(a * 255f);
                    if (value > data[idx + 3])
                        data[idx] = data[idx + 1] = data[idx + 2] = data[idx + 3] = value;
                }
        }
        _stars = paper.Renderer.CreateTexture(size, size);
        paper.Renderer.SetTextureData(_stars, new IntRect(0, 0, size, size), data);
    }

    private void UpdateComets(float dt)
    {
        if ((_cometTimer -= dt) <= 0f)
        {
            for (int i = 0; i < _comets.Length; i++)
            {
                if (_comets[i].Active)
                    continue;
                float a = (float)(_rng.NextDouble() * MathF.Tau);
                float dx = MathF.Cos(a), dy = MathF.Sin(a);
                float x = (float)(0.1 + _rng.NextDouble() * 0.8), y = (float)(_rng.NextDouble() * 0.6);
                if ((x < 0.28f && dx < 0f) || (x > 0.72f && dx > 0f)) dx = -dx;
                if ((y < 0.28f && dy < 0f) || (y > 0.72f && dy > 0f)) dy = -dy;
                _comets[i] = new Comet
                {
                    Active = true, X = x, Y = y, DirX = dx, DirY = dy,
                    Speed = (float)(0.09 + _rng.NextDouble() * 0.07),
                    Length = (float)(0.08 + _rng.NextDouble() * 0.06),
                    Duration = (float)(2.8 + _rng.NextDouble() * 1.8),
                };
                break;
            }
            _cometTimer = (float)(4.0 + _rng.NextDouble() * 6.0);
        }

        for (int i = 0; i < _comets.Length; i++)
        {
            ref Comet c = ref _comets[i];
            if (!c.Active)
                continue;
            c.Life += dt / c.Duration;
            if (c.Life >= 1f)
            {
                c.Active = false;
                continue;
            }
            c.X += c.DirX * c.Speed * dt;
            c.Y += c.DirY * c.Speed * dt;
        }
    }

    public void Draw(Canvas vg, Rect rect)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y, w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float big = MathF.Max(w, h);

        vg.BeginPath();
        vg.Rect(x, y, w, h);
        vg.SetFillColor(Void);
        vg.Fill();

        Radial(vg, x, y, w, h, x + w * 0.7f, y + h * 0.2f, 0, big * 0.95f, Rgba(26, 15, 46), Rgba(5, 3, 12, 0f));

        foreach (Cloud c in _clouds)
        {
            float radius = w * c.Radius * (1f + 0.10f * MathF.Sin(_time * 0.09f + c.Phase));
            Radial(vg, x, y, w, h, x + c.X * w, y + c.Y * h, 0, radius, c.Color, Color.FromArgb(0, c.Color));
        }
        Radial(vg, x, y, w, h,
            x + w * 0.5f + MathF.Sin(_time * 0.06f) * 22f,
            y + h * 0.40f + MathF.Sin(_time * 0.05f + 2.0f) * 16f,
            0, w * 0.46f, Rgba(140, 92, 240, 0.38f), Rgba(140, 92, 240, 0f));

        float px = x + w - w * 0.12f - 110f;
        float py = y + h * 0.14f + 110f + MathF.Sin(_time * 0.32f) * 16f;
        vg.SaveState();
        vg.SetRadialBrush(px, py, 90, 210, Rgba(168, 85, 247, 0.45f), Rgba(168, 85, 247, 0f));
        vg.BeginPath();
        vg.Circle(px, py, 210);
        vg.Fill();
        vg.RestoreState();
        vg.SaveState();
        vg.SetRadialBrush(px - 35, py - 44, 0, 200, Rgba(176, 124, 232), Rgba(26, 15, 51));
        vg.BeginPath();
        vg.Circle(px, py, 110);
        vg.Fill();
        vg.RestoreState();

        if (_stars != null)
            for (float ty = y; ty < y + h; ty += StarTexSize)
                for (float tx = x; tx < x + w; tx += StarTexSize)
                    vg.DrawImage(_stars, tx, ty, StarTexSize, StarTexSize);

        foreach (Comet c in _comets)
        {
            if (!c.Active)
                continue;
            float envelope = MathF.Sin(c.Life * MathF.PI);
            if (envelope <= 0.01f)
                continue;
            float hx = x + c.X * w, hy = y + c.Y * h;
            float vx = c.DirX * w, vy = c.DirY * h;
            float length = MathF.Max(1e-4f, MathF.Sqrt(vx * vx + vy * vy));
            float ux = vx / length, uy = vy / length;
            float tail = c.Length * w;
            float tx = hx - ux * tail, ty = hy - uy * tail;
            float perpX = -uy, perpY = ux, half = 1.7f;
            vg.SaveState();
            vg.SetLinearBrush(hx, hy, tx, ty, Rgba(216, 190, 255, envelope * 0.85f), Rgba(216, 190, 255, 0f));
            vg.BeginPath();
            vg.MoveTo(hx + perpX * half, hy + perpY * half);
            vg.LineTo(hx - perpX * half, hy - perpY * half);
            vg.LineTo(tx, ty);
            vg.ClosePath();
            vg.FillComplex();
            vg.RestoreState();
            vg.BeginPath();
            vg.Circle(hx, hy, 2.2f);
            vg.SetFillColor(Rgba(255, 255, 255, envelope));
            vg.Fill();
        }

        Radial(vg, x, y, w, h, x + w * 0.5f, y + h * 0.4f, big * 0.55f, big * 0.9f, Rgba(0, 0, 0, 0f), Rgba(0, 0, 0, 0.55f));
    }

    private static void Radial(Canvas vg, float x, float y, float w, float h, float cx, float cy, float inner, float outer, Color from, Color to)
    {
        vg.SaveState();
        vg.SetRadialBrush(cx, cy, inner, outer, from, to);
        vg.BeginPath();
        vg.Rect(x, y, w, h);
        vg.Fill();
        vg.RestoreState();
    }
}
