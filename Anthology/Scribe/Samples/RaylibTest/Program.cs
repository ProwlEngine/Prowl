using Prowl.Scribe;
using Raylib_cs;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

public sealed class RaylibMarkdownImageProvider : IMarkdownImageProvider, IDisposable
{
    public readonly struct MarkdownImage(object texture, float width, float height)
    {
        public readonly object Texture = texture;
        public readonly float Width = width;
        public readonly float Height = height;
    }

    private static readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, MarkdownImage> _cache = new();

    public bool TryGetImage(string src, out object texture, out Vector2 size)
    {
        texture = default;
        size = default;

        if (_cache.TryGetValue(src, out var image))
        {
            texture = image.Texture;
            size = new Vector2(image.Width, image.Height);
            return true;
        }

        if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var bytes = _httpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                string ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(ext))
                    return false;

                var img = Raylib.LoadImageFromMemory(ext, bytes);
                var tex = Raylib.LoadTextureFromImage(img);
                Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
                Raylib.UnloadImage(img);
                image = new MarkdownImage(tex, tex.Width, tex.Height);
                _cache[src] = image;

                texture = image.Texture;
                size = new Vector2(image.Width, image.Height);
                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDir, src);
            if (!File.Exists(path))
                return false;

            try
            {
                var tex = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
                image = new MarkdownImage(tex, tex.Width, tex.Height);
                _cache[src] = image;

                texture = image.Texture;
                size = new Vector2(image.Width, image.Height);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        foreach (var kv in _cache)
        {
            if (kv.Value.Texture is Texture2D tex)
                Raylib.UnloadTexture(tex);
        }
        _cache.Clear();
    }
}

public class RaylibFontRenderer : IFontRenderer
{
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
                Raylib_cs.Raylib.SetTextureFilter(texture, TextureFilter.Point);
                return texture;
            }
        }
    }

    public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) return;
        byte[] rgbaData = new byte[bounds.Width * bounds.Height * 4];
        for (int i = 0; i < data.Length; i++)
        {
            rgbaData[i * 4] = 255;
            rgbaData[i * 4 + 1] = 255;
            rgbaData[i * 4 + 2] = 255;
            rgbaData[i * 4 + 3] = data[i];
        }
        Rectangle updateRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Raylib_cs.Raylib.UpdateTextureRec(tex, updateRect, rgbaData);
    }

    public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
    {
        if (texture is not Texture2D tex) return;
        if (vertices.Length == 0 || indices.Length == 0) return;
        Rlgl.DrawRenderBatchActive();
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
        Rlgl.DrawRenderBatchActive();
        Rlgl.SetTexture(0);
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
        Markdown // NEW
    }

    static void Main(string[] args)
    {
        const int screenWidth = 1400;
        const int screenHeight = 900;
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(screenWidth, screenHeight, "Prowl.Scribe Raylib Sample");

        // Create font atlas with Raylib renderer
        var renderer = new RaylibFontRenderer();
        var fontAtlas = new FontSystem(renderer, 1024, 1024);

        fontAtlas.LoadSystemFonts("Segoe UI", "Arial", "Liberation Sans", "Consola", "Menlo", "Liberation Mono");

        var imageProvider = new RaylibMarkdownImageProvider();

        // Demo state
        var demoMode = DemoMode.Markdown; // start in Markdown mode
        var settings = TextLayoutSettings.Default;
        settings.PixelSize = 18;
        //settings.PreferredFont = primaryFont;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            settings.PreferredFont = fontAtlas.GetFont("Segoe UI", FontStyle.Regular);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            settings.PreferredFont = fontAtlas.GetFont("Arial", FontStyle.Regular);
        else
            settings.PreferredFont = fontAtlas.GetFont("Liberation Sans", FontStyle.Regular);
        
        settings.LineHeight = 1.25f;
        settings.MaxWidth = 720;

        bool showAtlas = false;
        bool showMetrics = false;

        // Sample texts (including Markdown)
        var sampleTexts = new Dictionary<DemoMode, string> {
            [DemoMode.BasicText] = "Hello World! This is a basic text rendering demonstration.\n\nUnicode: €£¥₹ ←↑→↓ ♠♣♥♦\nGreek: αβγδε Hebrew: אבגדה Arabic: ابجده Japanese: こんにちは Chinese: 你好",
            [DemoMode.Wrapping] = "This paragraph demonstrates wrapping capabilities across variable widths. Adjust with ← →.",
            [DemoMode.Alignment] = "This is a sample text showing alignment.",
            [DemoMode.Typography] = "Letter/word spacing and line height",
            [DemoMode.Markdown] = @"#[top]
# Markdown Layout Engine
Welcome to the *Markdown* **layout** ***showcase***. We support ~underline~, ~~strike~~, and ~~~overline~~~ decorations; also inline `code()`.
Links: a labeled [blue link](https://example.com ""title"") and an autolink http://example.org
Images are also supported, You can use file paths or URL's! 
![Prowl logo](ProwlLogo.png).

## Header2

> A blockquote with enough text to wrap across multiple lines at regular font sizes, demonstrating wrapping.
> But you can also expand blockquotes manually with > characters

We've got *Italic* **Bold** and ***Bold Italic*** as well as escaped characters, lets go again \*Italic\* *\*\Bold\*\* and \*\*\*Bold Italic\*\*\*

### Header3

- **Features**
  continuation lines wrap correctly to the available width inside the list content area.
  - Nested list item with more text to prove wrapping works at deeper levels.
  - Another nested item that includes `inline code`.
- A veryveryverylongwordthatcannotpossiblyfitonasingleline will be split as needed.
- Final item before switching lists.

1. Ordered item one with a [link](https://example.com).
 1. Nested Ordered item.
2. Ordered item two wraps properly and keeps its numeric prefix aligned with the text.
3. Ordered item three.

#### Header4

| Head Left | Head Center | Head Right |
|:----------|:-----------:|-----------:|
| left cell |  centered   |      right |
| wrap wrap wrap wrap wrap wrap wrap | bla bla more stuff | imgonnawriteanotherverylongwordonasinglelinesoithastosplitsowecanseeifthatworksinatable |

---
### Code Fence (C#)
```csharp
// no syntax highlighting... for now
for (int i = 0; i < 3; i++) {
    Console.WriteLine($""hello {i}"");
}
```
"
        };

        while (!Raylib.WindowShouldClose())
        {
            HandleInput(ref demoMode, ref settings, ref showAtlas, ref showMetrics, fontAtlas);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(245, 246, 250, 255));

            DrawDemo(demoMode, sampleTexts[demoMode], settings, fontAtlas, renderer, imageProvider, showMetrics);
            DrawUI(demoMode, settings, fontAtlas, showAtlas, showMetrics);

            if (showAtlas && fontAtlas.Texture is Texture2D atlasTexture)
                DrawAtlasView(atlasTexture, fontAtlas);

            Raylib.DrawFPS(10, 10);
            Raylib.EndDrawing();
        }

        imageProvider.Dispose();
        Raylib.CloseWindow();
    }

    static void HandleInput(ref DemoMode demoMode, ref TextLayoutSettings settings, ref bool showAtlas, ref bool showMetrics, FontSystem fontAtlas)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.One)) demoMode = DemoMode.BasicText;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) demoMode = DemoMode.Wrapping;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) demoMode = DemoMode.Alignment;
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) demoMode = DemoMode.Typography;
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) demoMode = DemoMode.Markdown; // NEW

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

    static void DrawDemo(DemoMode mode, string text, TextLayoutSettings settings, FontSystem fontAtlas,
        IFontRenderer renderer, IMarkdownImageProvider imageProvider, bool showMetrics)
    {
        Raylib.SetMouseCursor(MouseCursor.Default);
        var contentArea = new Rectangle(50, 100, (int)settings.MaxWidth + 40, Raylib.GetScreenHeight() - 160);
        Raylib.DrawRectangleRec(contentArea, new Color(240, 240, 240, 100));
        Raylib.DrawRectangleLinesEx(contentArea, 2, Color.Gray);
        var position = new Vector2(contentArea.X + 20, contentArea.Y + 20);

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
            case DemoMode.Markdown:
                DrawMarkdown(text, position, settings, fontAtlas, renderer, imageProvider);
                break;
        }
    }

    // === Existing demo helpers (trimmed) ===
    static void DrawBasic(string text, Vector2 pos, TextLayoutSettings settings, FontSystem fs)
    {
        var layout = fs.CreateLayout(text, settings);
        fs.DrawLayout(layout, pos, FontColor.Black);
    }

    static void DrawWrapping(string text, Vector2 pos, TextLayoutSettings s, FontSystem fs)
    {
        var w = s; w.WrapMode = TextWrapMode.Wrap; w.MaxWidth = MathF.Max(260, s.MaxWidth * 0.5f);
        var nw = s; nw.WrapMode = TextWrapMode.NoWrap; nw.MaxWidth = w.MaxWidth;
        fs.DrawText("Wrap ON:", pos, FontColor.Red, 16);
        fs.DrawLayout(fs.CreateLayout(text, w), pos + new Vector2(0, 22), FontColor.Black);
        var x2 = pos.X + w.MaxWidth + 60;
        fs.DrawText("Wrap OFF:", new Vector2(x2, pos.Y), FontColor.Red, 16);
        fs.DrawLayout(fs.CreateLayout(text, nw), new Vector2(x2, pos.Y + 22), FontColor.Black);
    }

    static void DrawAlignment(Vector2 pos, TextLayoutSettings s, FontSystem fs)
    {
        var sample = "This paragraph demonstrates left/center/right alignment across a fixed width.";
        float y = pos.Y;
        foreach (var (name, align) in new[] { ("Left", TextAlignment.Left), ("Center", TextAlignment.Center), ("Right", TextAlignment.Right) })
        {
            var t = s; t.Alignment = align; t.WrapMode = TextWrapMode.Wrap; t.MaxWidth = 400;
            fs.DrawText(name + ":", new Vector2(pos.X, y), FontColor.Blue, 16);
            var layout = fs.CreateLayout(sample, t);
            fs.DrawLayout(layout, new Vector2(pos.X, y + 22), FontColor.Black);
            y += layout.Size.Y + 40;
        }
    }

    static void DrawTypography(Vector2 pos, TextLayoutSettings s, FontSystem fs)
    {
        float y = pos.Y;
        for (float spacing = 0; spacing <= 2; spacing += 1)
        {
            var t = s; t.LetterSpacing = spacing; t.WrapMode = TextWrapMode.NoWrap;
            fs.DrawText($"Spacing {spacing:F1}:", new Vector2(pos.X, y), FontColor.Blue, 14);
            fs.DrawText("Letter Spacing", new Vector2(pos.X + 120, y), FontColor.Black, t);
            y += 28;
        }
        var lh = s; lh.LineHeight = 1.6f; lh.WrapMode = TextWrapMode.NoWrap;
        fs.DrawText("Line height:", new Vector2(pos.X, y), FontColor.Blue, 14);
        fs.DrawLayout(fs.CreateLayout("Line\nheight\nsample", lh), new Vector2(pos.X + 120, y), FontColor.Black);
    }

    // === Markdown demo ===
    static bool isMouseOverLink = false;
    static void DrawMarkdown(string md, Vector2 pos, TextLayoutSettings baseText, FontSystem fs,
        IFontRenderer renderer, IMarkdownImageProvider imageProvider)
    {

        string fontFamily = "";
        string monoFontFamily = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fontFamily = "Segoe UI";
            monoFontFamily = "Consola";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            fontFamily = "Arial";
            monoFontFamily = "Menlo";
        }
        else
        {
            fontFamily = "Liberation Sans";
            monoFontFamily = "Liberation Mono";
        }

        var r = fs.GetFont(fontFamily, FontStyle.Regular);
        var m = fs.GetFont(monoFontFamily, FontStyle.Regular);
        var b = fs.GetFont(fontFamily, FontStyle.Bold);
        var i = fs.GetFont(fontFamily, FontStyle.Italic);
        var bi = fs.GetFont(fontFamily, FontStyle.BoldItalic);

        var ms = MarkdownLayoutSettings.Default(r, m, b, i, bi, width: baseText.MaxWidth);
        ms.BaseSize = baseText.PixelSize;
        ms.LineHeight = baseText.LineHeight;
        ms.ParagraphSpacing = 10f;
        ms.ColorText = new FontColor(30, 30, 36, 255);
        ms.ColorMutedText = new FontColor(90, 90, 98, 255);
        ms.ColorRule = new FontColor(160, 160, 168, 255);
        ms.ColorQuoteBar = new FontColor(180, 180, 190, 255);
        ms.ColorCodeBg = new FontColor(235, 235, 240, 255);

        var doc = Markdown.Parse(md);
        var dl = MarkdownLayoutEngine.Layout(doc, fs, ms, imageProvider);
        MarkdownLayoutEngine.Render(dl, fs, renderer, pos, ms);

        var mouse = Raylib.GetMousePosition();
        bool isMouseOverLink = MarkdownLayoutEngine.TryGetLinkAt(dl, mouse, pos, out var href);

        //if (isMouseOverLink)
        //    Raylib.SetMouseCursor(MouseCursor.PointingHand);
        //else
        //    Raylib.SetMouseCursor(MouseCursor.Default);

        if (isMouseOverLink && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            try { Process.Start(new ProcessStartInfo(href) { UseShellExecute = true }); }
            catch { }
        }
    }

    static void DrawUI(object mode, TextLayoutSettings settings, FontSystem fs, bool showAtlas, bool showMetrics)
    {
        fs.DrawText($"Demo Mode: {mode}", new Vector2(50, 20), FontColor.Black, 24);
        var sx = Raylib.GetScreenWidth() - 380;
        var sy = 20;
        fs.DrawText("Settings:", new Vector2(sx, sy), FontColor.Blue, 16); sy += 24;
        fs.DrawText($"Font Size: {settings.PixelSize}", new Vector2(sx, sy), FontColor.Black, 14); sy += 18;
        fs.DrawText($"Max Width: {settings.MaxWidth}", new Vector2(sx, sy), FontColor.Black, 14); sy += 18;
        fs.DrawText($"Wrap: {settings.WrapMode}", new Vector2(sx, sy), FontColor.Black, 14); sy += 18;
        fs.DrawText($"Alignment: {settings.Alignment}", new Vector2(sx, sy), FontColor.Black, 14); sy += 18;
        fs.DrawText($"Line Height: {settings.LineHeight:F2}", new Vector2(sx, sy), FontColor.Black, 14);

        var cy = Raylib.GetScreenHeight() - 124;
        fs.DrawText("Controls:", new Vector2(50, cy), FontColor.Blue, 16); cy += 22;
        fs.DrawText("1-4 Modes, 5 Markdown | ↑↓ size | ←→ width | W wrap | A align", new Vector2(50, cy), FontColor.Gray, 12); cy += 18;
        fs.DrawText("TAB ± letter spacing | [ ] word spacing | -/= line height | T tabs", new Vector2(50, cy), FontColor.Gray, 12); cy += 18;
        fs.DrawText("SPACE atlas | M metrics | R reset", new Vector2(50, cy), FontColor.Gray, 12);
    }

    static void DrawAtlasView(Texture2D atlasTexture, FontSystem fs)
    {
        int disp = 300;
        int ax = Raylib.GetScreenWidth() - disp - 20;
        int ay = 100;
        Raylib.DrawRectangle(ax - 2, ay - 2, disp + 4, disp + 4, Color.Black);
        Rectangle src = new Rectangle(0, 0, atlasTexture.Width, atlasTexture.Height);
        Rectangle dst = new Rectangle(ax, ay, disp, disp);
        Raylib.DrawTexturePro(atlasTexture, src, dst, Vector2.Zero, 0, Color.White);
        fs.DrawText($"Atlas {fs.Width}x{fs.Height}", new Vector2(ax, ay + disp + 6), FontColor.Black, 14);
    }
}
