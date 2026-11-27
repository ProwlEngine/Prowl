using Prowl.Scribe;
using Raylib_cs;
using System.Diagnostics;
using Prowl.Vector;
using System.Runtime.InteropServices;

public class RaylibFontRenderer : IFontRenderer
{
    private const string SdfFragmentShader = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
const float pxRange = 4.0;
float screenPxRange() {
    vec2 unitRange = vec2(pxRange) / vec2(textureSize(texture0, 0));
    vec2 screenTexSize = vec2(1.0) / fwidth(fragTexCoord);
    // Per axis, then the smaller of the two. A glyph is scaled evenly so both agree, but a quad
    // stretched far along one axis and barely at all along the other, such as an underline, needs
    // the range of the axis carrying the gradient; averaging would harden that edge into a step.
    vec2 range = unitRange * screenTexSize;
    return max(min(range.x, range.y), 1.0);
}
void main() {
    // One distance field, written into every colour channel, so red is the value.
    float sd = texture(texture0, fragTexCoord).r;
    float d = screenPxRange() * (sd - 0.5);
    float coverage = clamp(d + 0.5, 0.0, 1.0);
    finalColor = vec4(fragColor.rgb, fragColor.a * coverage);
}";

    private Shader _sdfShader;
    private bool _shaderLoaded;

    private void EnsureShader()
    {
        if (_shaderLoaded) return;
        _sdfShader = Raylib_cs.Raylib.LoadShaderFromMemory(null, SdfFragmentShader);
        _shaderLoaded = true;
    }

    public object CreateTexture(int width, int height)
    {
        unsafe
        {
            var data = new byte[width * height * 4];
            fixed (byte* dataPtr = data)
            {
                Image image = new Image {
                    Data = (void*)dataPtr,
                    Width = width,
                    Height = height,
                    Format = PixelFormat.UncompressedR8G8B8A8,
                    Mipmaps = 1
                };
                var texture = Raylib_cs.Raylib.LoadTextureFromImage(image);
                // Bilinear so the distance field interpolates smoothly between texels (SDF needs it).
                Raylib_cs.Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                return texture;
            }
        }
    }

    public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) return;
        // Scribe supplies the region as RGBA (width*height*4: R=G=B=SDF, A=255), matching the
        // R8G8B8A8 atlas texture, so upload it directly.
        Rectangle updateRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Raylib_cs.Raylib.UpdateTextureRec(tex, updateRect, data);
    }

    public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
    {
        if (texture is not Texture2D tex) return;
        if (vertices.Length == 0 || indices.Length == 0) return;
        EnsureShader();

        // Flush any pending default-shader geometry, then draw the glyph quads through the SDF shader.
        Rlgl.DrawRenderBatchActive();
        Raylib_cs.Raylib.BeginShaderMode(_sdfShader);
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.DisableBackfaceCulling();
        Rlgl.DisableDepthTest();
        Rlgl.SetTexture(tex.Id);
        for (int i = 0; i < indices.Length; i++)
        {
            var vertex = vertices[indices[i]];
            Rlgl.Color4ub(vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A);
            Rlgl.TexCoord2f(vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y);
            Rlgl.Vertex2f(vertex.Position.X, vertex.Position.Y);
        }
        Rlgl.End();
        Rlgl.DrawRenderBatchActive(); // flush while the SDF shader is still active
        Rlgl.SetTexture(0);
        Raylib_cs.Raylib.EndShaderMode();
    }
}

internal class Program
{
    private enum DemoMode
    {
        BasicText,
        Wrapping,
        Alignment,
        Typography,
        Customizer
    }

    static FontFile font;
    static FontFile fontm;
    static FontFile fontb;
    static FontFile fonti;
    static FontFile fontbi;

    static void Main(string[] args)
    {
        const int screenWidth = 1400;
        const int screenHeight = 900;
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(screenWidth, screenHeight, "Prowl.Scribe Raylib Sample");

        // Create font atlas with Raylib renderer
        var renderer = new RaylibFontRenderer();
        var fontAtlas = new FontSystem(renderer, 1024, 1024);

        font = new FontFile(new FileInfo("Fonts/arial.ttf"));
        fontm = new FontFile(new FileInfo("Fonts/consola.ttf"));
        fontb = new FontFile(new FileInfo("Fonts/arialb.ttf"));
        fonti = new FontFile(new FileInfo("Fonts/ariali.ttf"));
        fontbi = new FontFile(new FileInfo("Fonts/arialbi.ttf"));

        // Demo state
        var demoMode = DemoMode.Customizer;
        var settings = TextLayoutSettings.Default;
        settings.PixelSize = 18;
        settings.Font = font;
        settings.LineHeight = 1.25f;
        settings.MaxWidth = 720;

        bool showAtlas = false;
        bool showMetrics = false;

        // Sample texts
        var sampleTexts = new Dictionary<DemoMode, string> {
            [DemoMode.BasicText] = "Hello World! This is a basic text rendering demonstration.\n\nUnicode: €£¥₹ ←↑→↓ ♠♣♥♦\nGreek: αβγδε Hebrew: אבגדה Arabic: ابجده Japanese: こんにちは Chinese: 你好",
            [DemoMode.Wrapping] = "This paragraph demonstrates wrapping capabilities across variable widths. Adjust with ← →.",
            [DemoMode.Alignment] = "This is a sample text showing alignment.",
            [DemoMode.Typography] = "Letter/word spacing and line height",
            [DemoMode.Customizer] = "Every glyph can be restyled while it is laid out, and moved while it is drawn. This line has a bold run, a bigger word, an underlined phrase and a struck one, and every letter sways on its own."
        };

        while (!Raylib.WindowShouldClose())
        {
            HandleInput(ref demoMode, ref settings, ref showAtlas, ref showMetrics, fontAtlas);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Raylib_cs.Color(245, 246, 250, 255));

            DrawDemo(demoMode, sampleTexts[demoMode], settings, fontAtlas, showMetrics);
            DrawUI(demoMode, settings, fontAtlas, showAtlas, showMetrics);

            if (showAtlas && fontAtlas.Texture is Texture2D atlasTexture)
                DrawAtlasView(atlasTexture, fontAtlas);

            Raylib.DrawFPS(10, 10);
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    static void HandleInput(ref DemoMode demoMode, ref TextLayoutSettings settings, ref bool showAtlas, ref bool showMetrics, FontSystem fontAtlas)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.One)) demoMode = DemoMode.BasicText;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) demoMode = DemoMode.Wrapping;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) demoMode = DemoMode.Alignment;
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) demoMode = DemoMode.Typography;
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) demoMode = DemoMode.Customizer;

        if (Raylib.IsKeyPressed(KeyboardKey.Up)) settings.PixelSize = Math.Min(settings.PixelSize + 2, 72);
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) settings.PixelSize = Math.Max(settings.PixelSize - 2, 8);
        if (Raylib.IsKeyPressed(KeyboardKey.Right)) settings.MaxWidth = Math.Min(settings.MaxWidth + 50, 1200);
        if (Raylib.IsKeyPressed(KeyboardKey.Left)) settings.MaxWidth = Math.Max(settings.MaxWidth - 50, 240);

        if (Raylib.IsKeyPressed(KeyboardKey.W))
            settings.WrapMode = settings.WrapMode == TextWrapMode.NoWrap ? TextWrapMode.Wrap : TextWrapMode.NoWrap;

        if (Raylib.IsKeyPressed(KeyboardKey.A))
        {
            settings.Alignment = settings.Alignment switch {
                TextAlignment.Left => TextAlignment.Center,
                TextAlignment.Center => TextAlignment.Right,
                TextAlignment.Right => TextAlignment.Left,
                _ => TextAlignment.Left
            };
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift)) settings.LetterSpacing = Math.Max(settings.LetterSpacing - 0.5f, -2);
            else settings.LetterSpacing = Math.Min(settings.LetterSpacing + 0.5f, 10);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) settings.WordSpacing = Math.Max(settings.WordSpacing - 1, -5);
        if (Raylib.IsKeyPressed(KeyboardKey.RightBracket)) settings.WordSpacing = Math.Min(settings.WordSpacing + 1, 20);
        if (Raylib.IsKeyPressed(KeyboardKey.Minus)) settings.LineHeight = Math.Max(settings.LineHeight - 0.05f, 0.8f);
        if (Raylib.IsKeyPressed(KeyboardKey.Equal)) settings.LineHeight = Math.Min(settings.LineHeight + 0.05f, 2.5f);
        if (Raylib.IsKeyPressed(KeyboardKey.T)) settings.TabSize = settings.TabSize == 4 ? 8 : 4;
        if (Raylib.IsKeyPressed(KeyboardKey.R)) { settings = TextLayoutSettings.Default; settings.PixelSize = 18; settings.MaxWidth = 720; }
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) showAtlas = !showAtlas;
        if (Raylib.IsKeyPressed(KeyboardKey.M)) showMetrics = !showMetrics;
    }

    static void DrawDemo(DemoMode mode, string text, TextLayoutSettings settings, FontSystem fontAtlas, bool showMetrics)
    {
        var contentArea = new Rectangle(50, 100, (int)settings.MaxWidth + 40, Raylib.GetScreenHeight() - 160);
        Raylib.DrawRectangleRec(contentArea, new Raylib_cs.Color(240, 240, 240, 100));
        Raylib.DrawRectangleLinesEx(contentArea, 2, Raylib_cs.Color.Gray);
        var position = new Float2(contentArea.X + 20, contentArea.Y + 20);

        switch (mode)
        {
            case DemoMode.BasicText:
                DrawBasic(text, position, settings, fontAtlas);
                break;
            case DemoMode.Wrapping:
                DrawWrapping(text, position, settings, fontAtlas);
                break;
            case DemoMode.Alignment:
                DrawAlignment(position, settings, fontAtlas);
                break;
            case DemoMode.Typography:
                DrawTypography(position, settings, fontAtlas);
                break;
            case DemoMode.Customizer:
                DrawCustomizer(text, position, settings, fontAtlas);
                break;
        }
    }

    // === Existing demo helpers (trimmed) ===
    static void DrawBasic(string text, Float2 pos, TextLayoutSettings settings, FontSystem fs)
    {
        var layout = fs.CreateLayout(text, settings);
        fs.DrawLayout(layout, pos, FontColor.Black);
    }

    static void DrawWrapping(string text, Float2 pos, TextLayoutSettings s, FontSystem fs)
    {
        var w = s; w.WrapMode = TextWrapMode.Wrap; w.MaxWidth = MathF.Max(260, s.MaxWidth * 0.5f);
        var nw = s; nw.WrapMode = TextWrapMode.NoWrap; nw.MaxWidth = w.MaxWidth;
        fs.DrawText("Wrap ON:", pos, FontColor.Red, 16, font);
        fs.DrawLayout(fs.CreateLayout(text, w), pos + new Float2(0, 22), FontColor.Black);
        var x2 = pos.X + w.MaxWidth + 60;
        fs.DrawText("Wrap OFF:", new Float2(x2, pos.Y), FontColor.Red, 16, font);
        fs.DrawLayout(fs.CreateLayout(text, nw), new Float2(x2, pos.Y + 22), FontColor.Black);
    }

    static void DrawAlignment(Float2 pos, TextLayoutSettings s, FontSystem fs)
    {
        var sample = "This paragraph demonstrates left/center/right alignment across a fixed width.";
        float y = pos.Y;
        foreach (var (name, align) in new[] { ("Left", TextAlignment.Left), ("Center", TextAlignment.Center), ("Right", TextAlignment.Right) })
        {
            var t = s; t.Alignment = align; t.WrapMode = TextWrapMode.Wrap; t.MaxWidth = 400;
            fs.DrawText(name + ":", new Float2(pos.X, y), FontColor.Blue, 16, font);
            var layout = fs.CreateLayout(sample, t);
            fs.DrawLayout(layout, new Float2(pos.X, y + 22), FontColor.Black);
            y += layout.Size.Y + 40;
        }
    }

    static void DrawTypography(Float2 pos, TextLayoutSettings s, FontSystem fs)
    {
        float y = pos.Y;
        for (float spacing = 0; spacing <= 2; spacing += 1)
        {
            var t = s; t.LetterSpacing = spacing; t.WrapMode = TextWrapMode.NoWrap;
            fs.DrawText($"Spacing {spacing:F1}:", new Float2(pos.X, y), FontColor.Blue, 14, font);
            fs.DrawText("Letter Spacing", new Float2(pos.X + 120, y), FontColor.Black, t);
            y += 28;
        }
        var lh = s; lh.LineHeight = 1.6f; lh.WrapMode = TextWrapMode.NoWrap;
        fs.DrawText("Line height:", new Float2(pos.X, y), FontColor.Blue, 14, font);
        fs.DrawLayout(fs.CreateLayout("Line\nheight\nsample", lh), new Float2(pos.X + 120, y), FontColor.Black);
    }

    // === Customizer demo ===
    // Layout-time: a GlyphCustomizer picks each character's font, size and decoration, and Scribe
    // shapes, kerns and wraps with the result. Draw-time: a GlyphModifier moves each finished quad.
    static readonly TextLayout customLayout = new();

    static void DrawCustomizer(string text, Float2 pos, TextLayoutSettings baseText, FontSystem fs)
    {
        int bold = text.IndexOf("a bold run");
        int big = text.IndexOf("bigger");
        int under = text.IndexOf("an underlined phrase");
        int struck = text.IndexOf("a struck one");

        var settings = baseText;
        settings.WrapMode = TextWrapMode.Wrap;
        settings.Customizer = (ref GlyphStyle g) =>
        {
            int i = g.CharIndex;
            if (i >= bold && i < bold + 10) g.Font = fontb;
            if (i >= big && i < big + 6) g.PixelSize = baseText.PixelSize * 1.8f;
            g.Underline = i >= under && i < under + 20;
            g.Strikethrough = i >= struck && i < struck + 12;
        };

        fs.UpdateLayout(customLayout, text, settings);

        float time = (float)Raylib.GetTime();
        fs.DrawLayout(customLayout, pos, new FontColor(30, 30, 36, 255), (ref GlyphDraw g) =>
        {
            if (g.IsDecoration) return;
            float lift = MathF.Sin(time * 3f + g.CharIndex * 0.35f) * g.PixelSize * 0.08f;
            g.SetCorners(
                new Float2(g.TopLeft.X, g.TopLeft.Y + lift),
                new Float2(g.TopRight.X, g.TopRight.Y + lift),
                new Float2(g.BottomLeft.X, g.BottomLeft.Y + lift),
                new Float2(g.BottomRight.X, g.BottomRight.Y + lift));
        });
    }

    static void DrawUI(object mode, TextLayoutSettings settings, FontSystem fs, bool showAtlas, bool showMetrics)
    {
        fs.DrawText($"Demo Mode: {mode}", new Float2(50, 20), FontColor.Black, 24, font);
        var sx = Raylib.GetScreenWidth() - 380;
        var sy = 20;
        fs.DrawText("Settings:", new Float2(sx, sy), FontColor.Blue, 16, font); sy += 24;
        fs.DrawText($"Font Size: {settings.PixelSize}", new Float2(sx, sy), FontColor.Black, 14, font); sy += 18;
        fs.DrawText($"Max Width: {settings.MaxWidth}", new Float2(sx, sy), FontColor.Black, 14, font); sy += 18;
        fs.DrawText($"Wrap: {settings.WrapMode}", new Float2(sx, sy), FontColor.Black, 14, font); sy += 18;
        fs.DrawText($"Alignment: {settings.Alignment}", new Float2(sx, sy), FontColor.Black, 14, font); sy += 18;
        fs.DrawText($"Line Height: {settings.LineHeight:F2}", new Float2(sx, sy), FontColor.Black, 14, font);

        var cy = Raylib.GetScreenHeight() - 124;
        fs.DrawText("Controls:", new Float2(50, cy), FontColor.Blue, 16, font); cy += 22;
        fs.DrawText("1-4 Modes, 5 Customizer | ↑↓ size | ←→ width | W wrap | A align", new Float2(50, cy), FontColor.Gray, 12, font); cy += 18;
        fs.DrawText("TAB ± letter spacing | [ ] word spacing | -/= line height | T tabs", new Float2(50, cy), FontColor.Gray, 12, font); cy += 18;
        fs.DrawText("SPACE atlas | M metrics | R reset", new Float2(50, cy), FontColor.Gray, 12, font);
    }

    static void DrawAtlasView(Texture2D atlasTexture, FontSystem fs)
    {
        int disp = 300;
        int ax = Raylib.GetScreenWidth() - disp - 20;
        int ay = 100;
        Raylib.DrawRectangle(ax - 2, ay - 2, disp + 4, disp + 4, Raylib_cs.Color.Black);
        Rectangle src = new Rectangle(0, 0, atlasTexture.Width, atlasTexture.Height);
        Rectangle dst = new Rectangle(ax, ay, disp, disp);
        Raylib.DrawTexturePro(atlasTexture, src, dst, Float2.Zero, 0, Raylib_cs.Color.White);
        fs.DrawText($"Atlas {fs.Width}x{fs.Height}", new Float2(ax, ay + disp + 6), FontColor.Black, 14, font);
    }
}
