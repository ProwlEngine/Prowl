using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;
/// <summary>
/// Layout caching across changes Scaffold cannot see for itself: the viewport, the DPI scale and
/// the fonts. Anything here that does not invalidate would leave stale geometry on screen.
/// </summary>
public class EnvironmentInvalidationTests
{
    [Fact]
    public void ResolutionChangeRelayoutsPercentageChildren()
    {
        var p = NewPaper();
        ElementBuilder Frame()
        {
            p.BeginFrame(.01f);
            var half = p.Box("half", lineID: 1).Width(UnitValue.Percentage(50)).Height(10);
            p.EndFrame();
            return half;
        }

        Assert.Equal(200, Frame()._handle.Data.LayoutWidth);
        p.SetResolution(800, 300);
        Assert.Equal(400, Frame()._handle.Data.LayoutWidth);
    }

    [Fact]
    public void DpiChangeRemeasuresText()
    {
        var font = LoadFont();
        var p = NewPaper();
        ElementBuilder Frame(float dpi)
        {
            p.BeginFrame(.01f, dpi);
            var label = p.Box("label", lineID: 1).Size(UnitValue.Auto).Text("hello world", font).FontSize(16);
            p.EndFrame();
            return label;
        }

        Frame(1f);
        Frame(1f);
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
        Frame(2f);
        // A DPI change alters the rasterised metrics behind the same logical text, so the element
        // has to be measured again rather than reusing the cached size.
        Assert.True(p.LayoutStatistics.MeasuredNodes > 0, "a DPI change did not re-measure text");
    }

    [Fact]
    public void AddingAFallbackFontDoesNotStrandTextGeometry()
    {
        var font = LoadFont();
        var p = NewPaper();
        ElementBuilder Frame()
        {
            p.BeginFrame(.01f);
            var label = p.Box("label", lineID: 1).Size(UnitValue.Auto).Text("hello world", font).FontSize(16);
            p.EndFrame();
            return label;
        }

        Frame();
        Frame();
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
        p.AddFallbackFont(LoadFont());
        Frame();
        Assert.True(p.LayoutStatistics.MeasuredNodes > 0, "a new fallback font did not re-measure text");
    }

    /// <summary>Fallback fonts are normally registered during setup, before anything is laid out.</summary>
    [Fact]
    public void AddingAFallbackFontBeforeTheFirstFrameIsSafe()
    {
        var p = NewPaper();
        p.AddFallbackFont(LoadFont());
        p.BeginFrame(.01f);
        var label = p.Box("label", lineID: 1).Size(UnitValue.Auto).Text("hello", LoadFont()).FontSize(16);
        p.EndFrame();
        Assert.True(label._handle.Data.LayoutWidth > 0);
    }

    [Fact]
    public void MarkAllLayoutDirtyForcesAFullRemeasure()
    {
        var font = LoadFont();
        var p = NewPaper();
        void Frame()
        {
            p.BeginFrame(.01f);
            p.Box("label", lineID: 1).Size(UnitValue.Auto).Text("hello world", font).FontSize(16);
            p.EndFrame();
        }

        Frame();
        Frame();
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
        p.MarkAllLayoutDirty();
        Frame();
        Assert.True(p.LayoutStatistics.MeasuredNodes > 0);
    }

    /// <summary>Entering an element is how per-element commands address it, exactly as Paper.Draw
    /// does. That works for a leaf: entering gives it a context, it does not give it children.</summary>
    [Fact]
    public void MarkLayoutDirtyInsideAnEnteredLeafInvalidatesThatLeaf()
    {
        var p = NewPaper();
        int calls = 0;
        bool dirty = false;
        void Frame()
        {
            p.BeginFrame(.01f);
            using (p.Box("leaf", lineID: 1).Size(UnitValue.Auto)
                .ContentSizer((_, _) => { calls++; return (20f, 20f); }, revision: 0).Enter())
            {
                if (dirty)
                {
                    p.MarkLayoutDirty();
                }
            }

            p.EndFrame();
        }

        Frame();
        int settled = calls;
        Frame();
        Assert.Equal(settled, calls);
        dirty = true;
        Frame();
        Assert.True(calls > settled, "MarkLayoutDirty inside the element's scope did not invalidate it");
    }

    private static FontFile LoadFont()
    {
        using var stream = typeof(EnvironmentInvalidationTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    private static Paper NewPaper() => new(new NullRenderer(), 400, 300, new FontAtlasSettings());

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls) { }
    }
}
