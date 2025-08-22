// RaylibFontRenderer.cs - Implementation for Raylib
using Prowl.Scribe;
using Raylib_cs;
using StbTrueTypeSharp;
using System.Numerics;
using System.Runtime.InteropServices;

public class RaylibFontRenderer : IFontRenderer
{
    public object CreateTexture(int width, int height)
    {
        unsafe
        {
            // Create empty RGBA data
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

        // Convert single channel to RGBA
        byte[] rgbaData = new byte[bounds.Width * bounds.Height * 4];
        for (int i = 0; i < data.Length; i++)
        {
            rgbaData[i * 4] = 255;     // R
            rgbaData[i * 4 + 1] = 255; // G
            rgbaData[i * 4 + 2] = 255; // B
            rgbaData[i * 4 + 3] = data[i]; // A
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
        Typography
    }

    static void Main(string[] args)
    {
        const int screenWidth = 1400;
        const int screenHeight = 900;

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(screenWidth, screenHeight, "Advanced Text Layout Engine Demo");

        // Create font atlas with Raylib renderer
        var renderer = new RaylibFontRenderer();
        var fontAtlas = new FontSystem(renderer, 512, 512);

        // Load fonts
        FontInfo primaryFont = null;
        FontInfo headerFont = null;
        try
        {
            string fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts", "arial.ttf");
            string headerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts", "arial-bold.ttf");

            if (File.Exists(fontPath))
            {
                primaryFont = fontAtlas.AddFont(fontPath);
                Raylib.TraceLog(TraceLogLevel.Info, $"Loaded primary font: {fontPath}");
            }

            if (File.Exists(headerPath))
            {
                headerFont = fontAtlas.AddFont(headerPath);
            }
            else
            {
                headerFont = primaryFont; // Fallback to primary
            }
        }
        catch (Exception e)
        {
            Raylib.TraceLog(TraceLogLevel.Error, $"Error loading fonts: {e.Message}");
        }

        // Load system fonts as fallbacks
        try
        {
            fontAtlas.LoadSystemFonts();
            Raylib.TraceLog(TraceLogLevel.Info, $"Loaded {fontAtlas.FontCount} total fonts");
        }
        catch (Exception e)
        {
            Raylib.TraceLog(TraceLogLevel.Error, $"Error loading system fonts: {e.Message}");
        }

        // Demo state
        var demoMode = DemoMode.BasicText;
        var settings = TextLayoutSettings.Default;
        settings.PixelSize = 18;
        settings.PreferredFont = primaryFont;
        settings.LineHeight = 1.2f;
        settings.MaxWidth = 600;

        bool showAtlas = false;
        bool showMetrics = false;
        float animationTime = 0;

        // Sample texts for different demos
        var sampleTexts = new Dictionary<DemoMode, string> {
            [DemoMode.BasicText] = "Hello World! This is a basic text rendering demonstration.\n\nThis text includes multiple lines, unicode characters like: €£¥₹ ←↑→↓ ♠♣♥♦, and various symbols to test font fallback systems.\n\nGreek: αβγδε Hebrew: אבגדה Arabic: ابجده Japanese: こんにちは Chinese: 你好",

            [DemoMode.Wrapping] = "This is a long paragraph that demonstrates text wrapping capabilities. The layout engine can intelligently wrap text at word boundaries, and when words are too long to fit on a single line, it will split them appropriately. This ensures that text always fits within the specified boundaries while maintaining readability. You can see how the engine handles hyphenation and maintains proper spacing between words and lines.",

            [DemoMode.Alignment] = "",

            [DemoMode.Typography] = "",
        };

        while (!Raylib.WindowShouldClose())
        {
            animationTime += Raylib.GetFrameTime();

            // Handle input
            HandleInput(ref demoMode, ref settings, ref showAtlas, ref showMetrics, fontAtlas);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.RayWhite);

            // Draw current demo
            DrawDemo(demoMode, sampleTexts[demoMode], settings, fontAtlas, primaryFont, headerFont, animationTime, showMetrics);

            // Draw UI
            DrawUI(demoMode, settings, fontAtlas, showAtlas, showMetrics);

            // Show atlas debug view
            if (showAtlas && fontAtlas.Texture is Texture2D atlasTexture)
            {
                DrawAtlasView(atlasTexture, fontAtlas);
            }

            Raylib.DrawFPS(10, 10);
            Raylib.EndDrawing();
        }

        // Cleanup
        Raylib.CloseWindow();
    }

    static void HandleInput(ref DemoMode demoMode, ref TextLayoutSettings settings, ref bool showAtlas, ref bool showMetrics, FontSystem fontAtlas)
    {
        // Mode switching
        if (Raylib.IsKeyPressed(KeyboardKey.One)) demoMode = DemoMode.BasicText;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) demoMode = DemoMode.Wrapping;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) demoMode = DemoMode.Alignment;
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) demoMode = DemoMode.Typography;

        // Font size
        if (Raylib.IsKeyPressed(KeyboardKey.Up))
            settings.PixelSize = Math.Min(settings.PixelSize + 2, 72);
        if (Raylib.IsKeyPressed(KeyboardKey.Down))
            settings.PixelSize = Math.Max(settings.PixelSize - 2, 8);

        // Width adjustment
        if (Raylib.IsKeyPressed(KeyboardKey.Right))
            settings.MaxWidth = Math.Min(settings.MaxWidth + 50, 1200);
        if (Raylib.IsKeyPressed(KeyboardKey.Left))
            settings.MaxWidth = Math.Max(settings.MaxWidth - 50, 200);

        // Wrap mode
        if (Raylib.IsKeyPressed(KeyboardKey.W))
            settings.WrapMode = settings.WrapMode == TextWrapMode.NoWrap ? TextWrapMode.Wrap : TextWrapMode.NoWrap;

        // Alignment cycling
        if (Raylib.IsKeyPressed(KeyboardKey.A))
        {
            settings.Alignment = settings.Alignment switch {
                TextAlignment.Left => TextAlignment.Center,
                TextAlignment.Center => TextAlignment.Right,
                TextAlignment.Right => TextAlignment.Left,
                _ => TextAlignment.Left
            };
        }

        // Typography settings
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift))
            {
                settings.LetterSpacing = Math.Max(settings.LetterSpacing - 0.5f, -2);
            }
            else
            {
                settings.LetterSpacing = Math.Min(settings.LetterSpacing + 0.5f, 10);
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket))
            settings.WordSpacing = Math.Max(settings.WordSpacing - 1, -5);
        if (Raylib.IsKeyPressed(KeyboardKey.RightBracket))
            settings.WordSpacing = Math.Min(settings.WordSpacing + 1, 20);

        if (Raylib.IsKeyPressed(KeyboardKey.Minus))
            settings.LineHeight = Math.Max(settings.LineHeight - 0.1f, 0.5f);
        if (Raylib.IsKeyPressed(KeyboardKey.Equal))
            settings.LineHeight = Math.Min(settings.LineHeight + 0.1f, 3.0f);

        // Tab size
        if (Raylib.IsKeyPressed(KeyboardKey.T))
            settings.TabSize = settings.TabSize == 4 ? 8 : 4;

        // Reset
        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            settings = TextLayoutSettings.Default;
            settings.PixelSize = 18;
            settings.MaxWidth = 600;
        }

        // Toggle views
        if (Raylib.IsKeyPressed(KeyboardKey.Space))
            showAtlas = !showAtlas;
        if (Raylib.IsKeyPressed(KeyboardKey.M))
            showMetrics = !showMetrics;
    }

    static void DrawDemo(DemoMode mode, string text, TextLayoutSettings settings, FontSystem fontAtlas,
        FontInfo primaryFont, FontInfo headerFont, float animationTime, bool showMetrics)
    {
        var contentArea = new Rectangle(50, 100, settings.MaxWidth + 40, (Raylib.GetScreenHeight() - 200) + 40);

        // Draw content area background
        Raylib.DrawRectangleRec(contentArea, new Color(240, 240, 240, 100));
        Raylib.DrawRectangleLinesEx(contentArea, 2, Color.Gray);

        var position = new Vector2(50 + 20, 100 + 20);

        switch (mode)
        {
            case DemoMode.BasicText:
                DrawBasicTextDemo(text, position, settings, fontAtlas);
                break;

            case DemoMode.Wrapping:
                DrawWrappingDemo(text, position, settings, fontAtlas);
                break;

            case DemoMode.Alignment:
                DrawAlignmentDemo(position, settings, fontAtlas);
                break;

            case DemoMode.Typography:
                DrawTypographyDemo(position, settings, fontAtlas);
                break;
        }
    }

    static void DrawBasicTextDemo(string text, Vector2 position, TextLayoutSettings settings, FontSystem fontAtlas)
    {
        var layout = fontAtlas.CreateLayout(text, settings);
        fontAtlas.DrawLayout(layout, position, FontColor.Black);
    }

    static void DrawWrappingDemo(string text, Vector2 position, TextLayoutSettings settings, FontSystem fontAtlas)
    {
        // Show both wrapped and non-wrapped versions
        var wrappedSettings = settings;
        wrappedSettings.WrapMode = TextWrapMode.Wrap;
        wrappedSettings.MaxWidth = 300;

        var noWrapSettings = settings;
        noWrapSettings.WrapMode = TextWrapMode.NoWrap;
        noWrapSettings.MaxWidth = 300;

        var wrappedLayout = fontAtlas.CreateLayout(text, wrappedSettings);
        var noWrapLayout = fontAtlas.CreateLayout(text, noWrapSettings);

        // Draw comparison
        fontAtlas.DrawText("Text Wrapping Enabled:", position, FontColor.Red, 16);
        fontAtlas.DrawLayout(wrappedLayout, position + new Vector2(0, 25), FontColor.Black);

        var secondColumnX = position.X + 350;
        fontAtlas.DrawText("Text Wrapping Disabled:", new Vector2(secondColumnX, position.Y), FontColor.Red, 16);
        fontAtlas.DrawLayout(noWrapLayout, new Vector2(secondColumnX, position.Y + 25), FontColor.Black);

        // Draw bounding boxes
        Raylib.DrawRectangleLines((int)position.X - 5, (int)position.Y + 20, 310, (int)wrappedLayout.Size.Y + 10, Color.Green);
        Raylib.DrawRectangleLines((int)secondColumnX - 5, (int)position.Y + 20, 310, (int)noWrapLayout.Size.Y + 10, Color.Red);
    }

    static void DrawAlignmentDemo(Vector2 position, TextLayoutSettings settings, FontSystem fontAtlas)
    {
        var alignments = new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right };
        var alignmentNames = new[] { "Left Aligned", "Center Aligned", "Right Aligned" };
        var sampleText = "This is a sample text that demonstrates different text alignment options. Each alignment has its own use cases and visual impact.";

        float y = position.Y;
        for (int i = 0; i < alignments.Length; i++)
        {
            var alignSettings = settings;
            alignSettings.Alignment = alignments[i];
            alignSettings.WrapMode = TextWrapMode.Wrap;
            alignSettings.MaxWidth = 400;

            // Draw title
            fontAtlas.DrawText(alignmentNames[i] + ":", new Vector2(position.X, y), FontColor.Blue, 16);
            y += 25;

            // Draw sample text
            var layout = fontAtlas.CreateLayout(sampleText, alignSettings);
            fontAtlas.DrawLayout(layout, new Vector2(position.X, y), FontColor.Black);

            // Draw bounding box
            Raylib.DrawRectangleLines((int)position.X - 2, (int)y - 2, (int)alignSettings.MaxWidth + 4, (int)layout.Size.Y + 4, Color.Gray);

            y += layout.Size.Y + 30;
        }
    }

    static void DrawTypographyDemo(Vector2 position, TextLayoutSettings settings, FontSystem fontAtlas)
    {
        float y = position.Y;

        // Letter spacing demo
        var letterSpacingText = "Letter Spacing";
        for (float spacing = 0; spacing <= 3; spacing += 1)
        {
            var spacingSettings = settings;
            spacingSettings.LetterSpacing = spacing;
            spacingSettings.WrapMode = TextWrapMode.NoWrap;

            fontAtlas.DrawText($"Spacing {spacing:F1}:", new Vector2(position.X, y), FontColor.Blue, 14);
            fontAtlas.DrawText(letterSpacingText, new Vector2(position.X + 100, y), FontColor.Black, spacingSettings);
            y += 30;
        }

        y += 20;

        // Line height demo
        var lineHeightText = "Line height affects\nthe vertical spacing\nbetween text lines";
        for (float height = 1.0f; height <= 2.0f; height += 0.5f)
        {
            var heightSettings = settings;
            heightSettings.LineHeight = height;
            heightSettings.WrapMode = TextWrapMode.NoWrap;

            fontAtlas.DrawText($"Height {height:F1}:", new Vector2(position.X + 300, position.Y + (height - 1.0f) * 200), FontColor.Blue, 14);
            var layout = fontAtlas.CreateLayout(lineHeightText, heightSettings);
            fontAtlas.DrawLayout(layout, new Vector2(position.X + 400, position.Y + (height - 1.0f) * 200), FontColor.Black);
        }

        // Tab demo
        var tabText = "Name:\tJohn Doe\nAge:\t25\nCity:\tNew York";
        var tabSettings = settings;
        tabSettings.TabSize = settings.TabSize;
        fontAtlas.DrawText("Tab Stops Demo:", new Vector2(position.X, y), FontColor.Blue, 14);
        fontAtlas.DrawText(tabText, new Vector2(position.X, y + 20), FontColor.Black, tabSettings);
    }

    static void DrawUI(DemoMode mode, TextLayoutSettings settings, FontSystem fontAtlas, bool showAtlas, bool showMetrics)
    {
        var headerY = 20;

        // Title
        fontAtlas.DrawText($"Layout Engine Demo - {mode}", new Vector2(50, headerY), FontColor.Black, 24);

        // Settings display
        var settingsX = Raylib.GetScreenWidth() - 400;
        var settingsY = 20;

        fontAtlas.DrawText("Current Settings:", new Vector2(settingsX, settingsY), FontColor.Blue, 16);
        settingsY += 25;

        fontAtlas.DrawText($"Font Size: {settings.PixelSize}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Max Width: {settings.MaxWidth}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Wrap: {settings.WrapMode}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Alignment: {settings.Alignment}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Letter Spacing: {settings.LetterSpacing:F1}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Word Spacing: {settings.WordSpacing:F1}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Line Height: {settings.LineHeight:F1}", new Vector2(settingsX, settingsY), FontColor.Black, 14);
        settingsY += 20;
        fontAtlas.DrawText($"Tab Size: {settings.TabSize}", new Vector2(settingsX, settingsY), FontColor.Black, 14);

        // Controls
        var controlsY = Raylib.GetScreenHeight() - 160;
        fontAtlas.DrawText("Controls:", new Vector2(50, controlsY), FontColor.Blue, 16);
        controlsY += 25;

        var controls = new[]
        {
            "1-4: Demo Modes | ↑↓: Font Size | ←→: Width | W: Wrap | A: Alignment",
            "TAB: Letter Spacing | []: Word Spacing | -/=: Line Height | T: Tab Size",
            "SPACE: Atlas View | M: Metrics | R: Reset | ESC: Exit"
        };

        foreach (var control in controls)
        {
            fontAtlas.DrawText(control, new Vector2(50, controlsY), FontColor.Gray, 12);
            controlsY += 18;
        }
    }

    static void DrawAtlasView(Texture2D atlasTexture, FontSystem fontAtlas)
    {
        var atlasDisplaySize = 300;
        var atlasX = Raylib.GetScreenWidth() - atlasDisplaySize - 20;
        var atlasY = 100;

        Raylib.DrawRectangle(atlasX - 2, atlasY - 2, atlasDisplaySize + 4, atlasDisplaySize + 4, Color.Black);

        Rectangle sourceRec = new Rectangle(0, 0, atlasTexture.Width, atlasTexture.Height);
        Rectangle destRec = new Rectangle(atlasX, atlasY, atlasDisplaySize, atlasDisplaySize);

        Raylib.DrawTexturePro(atlasTexture, sourceRec, destRec, Vector2.Zero, 0, Color.White);

        Raylib.DrawText($"Font Atlas {fontAtlas.Width}x{fontAtlas.Height}",
            atlasX, atlasY + atlasDisplaySize + 5, 14, Color.Black);
    }
}