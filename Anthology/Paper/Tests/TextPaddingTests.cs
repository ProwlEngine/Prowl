using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

public class TextPaddingTests
{
    private sealed class CaptureRenderer : ICanvasRenderer
    {
        public float MinX, MinY, MaxX, MaxY;
        public void Reset() { MinX = MinY = float.MaxValue; MaxX = MaxY = float.MinValue; }
        public void Dispose() { }
        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls)
        {
            foreach (var v in canvas.Vertices)
            {
                MinX = Math.Min(MinX, v.x); MinY = Math.Min(MinY, v.y);
                MaxX = Math.Max(MaxX, v.x); MaxY = Math.Max(MaxY, v.y);
            }
        }
    }

    private static readonly FontFile Font = LoadFont();

    private static FontFile LoadFont()
    {
        using var stream = typeof(TextPaddingTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    // Only the text draws anything, so the captured vertices are exactly the glyphs.
    private static (ElementBuilder element, CaptureRenderer renderer) Frame(Func<Paper, ElementBuilder> build)
    {
        var renderer = new CaptureRenderer();
        renderer.Reset();
        var p = new Paper(renderer, 400, 300, new FontAtlasSettings());
        p.BeginFrame(.01f);
        var element = build(p);
        p.EndFrame();
        return (element, renderer);
    }

    [Fact]
    public void TextIsDrawnInsideThePadding()
    {
        var (element, r) = Frame(p => p.Box("t", lineID: 1).PositionType(PositionType.SelfDirected).Position(50, 50)
            .Width(200).Height(UnitValue.Auto).Padding(20).Text("Hello", Font).FontSize(20));

        // A glyph's side bearing can put its ink a hair either side of the pen.
        Assert.InRange(r.MinX, 68f, 73f);
        Assert.InRange(r.MinY, 68f, 76f);
        Assert.True(r.MaxY <= element._handle.Data.Y + element._handle.Data.LayoutHeight - 18f,
            $"text reaches {r.MaxY}, into the bottom padding of an element ending at {element._handle.Data.Y + element._handle.Data.LayoutHeight}");
    }

    [Fact]
    public void WrappedTextStaysInsideTheContentWidth()
    {
        var (element, r) = Frame(p => p.Box("t", lineID: 1).PositionType(PositionType.SelfDirected).Position(0, 0)
            .Width(160).Height(UnitValue.Auto).Padding(30).Wrap(TextWrapMode.Wrap)
            .Text("the quick brown fox jumps over the lazy dog again and again", Font).FontSize(16));

        Assert.True(r.MaxX <= 160f - 30f + 2f, $"text reaches {r.MaxX}, past the content edge at 130");
        Assert.True(r.MaxY <= element._handle.Data.LayoutHeight - 30f + 2f,
            $"text reaches {r.MaxY}, past the content bottom at {element._handle.Data.LayoutHeight - 30f}");
    }

    [Fact]
    public void CentredTextCentresInsideAsymmetricPadding()
    {
        var (_, r) = Frame(p => p.Box("t", lineID: 1).PositionType(PositionType.SelfDirected).Position(0, 0)
            .Width(300).Height(40).Padding(100, 0, 0, 0).Alignment(Prowl.PaperUI.TextAlignment.Center).Text("mid", Font).FontSize(20));

        // The content area runs from 100 to 300, so its centre is 200 rather than the element's 150.
        float centre = (r.MinX + r.MaxX) * 0.5f;
        Assert.InRange(centre, 195f, 205f);
    }
}
