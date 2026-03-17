using Raylib_cs;

namespace MobaGame;

/// <summary>
/// All world-space rendering via RLGL. Coordinates are in meters (1 unit = 1 meter).
/// Call SetCamera before drawing to enable world-to-screen text scaling.
/// </summary>
public static class Draw
{
    public const float PixelsPerMeter = 20f;

    private static float _zoom = PixelsPerMeter;

    public static void SetZoom(float cameraZoom)
    {
        _zoom = cameraZoom;
    }

    // ── Filled Circle ──

    public static void Circle(float cx, float cy, float radius, Color color, int segments = 24)
    {
        Rlgl.Begin(DrawMode.Triangles);
        for (int i = 0; i < segments; i++)
        {
            float a1 = i * 2f * MathF.PI / segments;
            float a2 = (i + 1) * 2f * MathF.PI / segments;
            Rlgl.Color4ub(color.R, color.G, color.B, color.A);
            Rlgl.Vertex2f(cx, cy);
            Rlgl.Color4ub(color.R, color.G, color.B, color.A);
            Rlgl.Vertex2f(cx + MathF.Cos(a2) * radius, cy + MathF.Sin(a2) * radius);
            Rlgl.Color4ub(color.R, color.G, color.B, color.A);
            Rlgl.Vertex2f(cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius);
        }
        Rlgl.End();
    }

    // ── Circle Outline ──

    public static void CircleOutline(float cx, float cy, float radius, Color color, int segments = 24)
    {
        Rlgl.Begin(DrawMode.Lines);
        for (int i = 0; i < segments; i++)
        {
            float a1 = i * 2f * MathF.PI / segments;
            float a2 = (i + 1) * 2f * MathF.PI / segments;
            Rlgl.Color4ub(color.R, color.G, color.B, color.A);
            Rlgl.Vertex2f(cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius);
            Rlgl.Color4ub(color.R, color.G, color.B, color.A);
            Rlgl.Vertex2f(cx + MathF.Cos(a2) * radius, cy + MathF.Sin(a2) * radius);
        }
        Rlgl.End();
    }

    // ── Filled Rect (x,y = center) ──

    public static void Rect(float cx, float cy, float hw, float hh, Color color)
    {
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx - hw, cy - hh);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx - hw, cy + hh);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx + hw, cy + hh);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx + hw, cy - hh);
        Rlgl.End();
    }

    // ── Filled Rect (corner-based) ──

    public static void RectCorner(float x, float y, float w, float h, Color color)
    {
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(x, y);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(x, y + h);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(x + w, y + h);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(x + w, y);
        Rlgl.End();
    }

    // ── Rect Outline ──

    public static void RectOutline(float cx, float cy, float hw, float hh, Color color)
    {
        Rlgl.Begin(DrawMode.Lines);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx - hw, cy - hh); Rlgl.Color4ub(color.R, color.G, color.B, color.A); Rlgl.Vertex2f(cx + hw, cy - hh);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx + hw, cy - hh); Rlgl.Color4ub(color.R, color.G, color.B, color.A); Rlgl.Vertex2f(cx + hw, cy + hh);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx + hw, cy + hh); Rlgl.Color4ub(color.R, color.G, color.B, color.A); Rlgl.Vertex2f(cx - hw, cy + hh);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(cx - hw, cy + hh); Rlgl.Color4ub(color.R, color.G, color.B, color.A); Rlgl.Vertex2f(cx - hw, cy - hh);
        Rlgl.End();
    }

    // ── Line ──

    public static void Line(float x1, float y1, float x2, float y2, Color color)
    {
        Rlgl.Begin(DrawMode.Lines);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(x1, y1);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex2f(x2, y2);
        Rlgl.End();
    }

    // ── World-space text (renders at consistent screen-pixel size) ──

    public static void Text(string text, float worldX, float worldY, float screenPxSize, Color color)
    {
        float scale = 1f / _zoom;
        Rlgl.PushMatrix();
        Rlgl.Translatef(worldX, worldY, 0);
        Rlgl.Scalef(scale, scale, 1);
        Raylib.DrawText(text, 0, 0, (int)screenPxSize, color);
        Rlgl.PopMatrix();
    }

    public static void TextCentered(string text, float worldX, float worldY, float screenPxSize, Color color)
    {
        float scale = 1f / _zoom;
        int tw = Raylib.MeasureText(text, (int)screenPxSize);
        Rlgl.PushMatrix();
        Rlgl.Translatef(worldX, worldY, 0);
        Rlgl.Scalef(scale, scale, 1);
        Raylib.DrawText(text, -tw / 2, 0, (int)screenPxSize, color);
        Rlgl.PopMatrix();
    }

    // ── Health Bar (world-space) ──

    public static void HealthBar(float cx, float cy, float halfW, float halfH, float current, float max, Color fill)
    {
        if (max <= 0) return;
        float ratio = Math.Clamp(current / max, 0, 1);
        RectCorner(cx - halfW, cy - halfH, halfW * 2, halfH * 2, Color.DarkGray);
        RectCorner(cx - halfW, cy - halfH, halfW * 2 * ratio, halfH * 2, fill);
    }
}
