// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Tests;

/// <summary>
/// An element with CacheRender replays what it drew inside itself instead of drawing it again, for
/// as long as its key and size hold, and follows the element when it moves.
/// </summary>
public class RenderCacheTests
{
    private static readonly FontFile Font = LoadFont();

    [Fact]
    public void Contents_are_only_drawn_again_when_the_key_or_size_changes()
    {
        var paper = NewPaper();
        int runs = 0;
        void Frame(long key, float width, float offset)
        {
            paper.BeginFrame(0.016f);
            using (paper.Box("cached").Left(UnitValue.Pixels(offset)).Width(UnitValue.Pixels(width)).Height(UnitValue.Pixels(40)).CacheRender(key).Enter())
                paper.Draw((canvas, rect) => { runs++; canvas.RectFilled(rect.Min.X, rect.Min.Y, 10, 10, Color32.FromArgb(255, 255, 0, 0)); });
            paper.EndFrame();
        }

        Frame(1, 100, 0);
        Frame(1, 100, 0);
        Assert.Equal(1, runs);

        Frame(1, 100, 25);
        Assert.Equal(1, runs);

        Frame(2, 100, 25);
        Assert.Equal(2, runs);

        Frame(2, 140, 25);
        Assert.Equal(3, runs);
    }

    [Fact]
    public void A_moved_replay_matches_recording_at_the_new_position()
    {
        var moving = NewPaper();
        int runs = 0;

        // Both papers draw a frame first, since a new canvas binds its font atlas partway through the
        // first frame, which splits one extra draw call that a settled one would not.
        Scene(moving, 0f, () => runs++);

        // The cached list scrolls inside a clipped viewport, so rows start out of view and come into it.
        // Recording skips culling, so a replay after the move still holds them.
        foreach (float scroll in new[] { 0f, -35.5f, -80f, -12.25f })
        {
            Scene(moving, scroll, () => runs++);

            var fresh = NewPaper();
            Scene(fresh, scroll, () => { });
            Scene(fresh, scroll, () => { });

            AssertSameGeometry(fresh.Canvas, moving.Canvas);
        }

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Contents_holding_a_higher_layer_are_not_cached()
    {
        var paper = NewPaper();
        int runs = 0;
        for (int i = 0; i < 3; i++)
        {
            paper.BeginFrame(0.016f);
            using (paper.Box("cached").Width(UnitValue.Pixels(100)).Height(UnitValue.Pixels(40)).CacheRender(1).Enter())
            {
                paper.Draw((canvas, rect) => runs++);
                paper.Box("popup").Width(UnitValue.Pixels(20)).Height(UnitValue.Pixels(20)).Layer(Layer.Overlay).BackgroundColor(Color.Red);
            }
            paper.EndFrame();
        }

        Assert.Equal(3, runs);
    }

    private static void Scene(Paper paper, float scroll, Action onRecord)
    {
        paper.BeginFrame(0.016f);
        using (paper.Box("viewport").Width(UnitValue.Pixels(300)).Height(UnitValue.Pixels(60)).Clip().BackgroundColor(Color.Gray).Enter())
        {
            using (paper.Column("list").Top(UnitValue.Pixels(scroll)).Width(UnitValue.Pixels(300)).Height(UnitValue.Pixels(200))
                .BackgroundColor(Color.Blue).Rounded(6).Clip().CacheRender(7).Enter())
            {
                paper.Draw((canvas, rect) => onRecord());
                for (int i = 0; i < 8; i++)
                    paper.Box("row", i).Height(UnitValue.Pixels(25)).BorderWidth(1).BorderColor(Color.White)
                        .Rounded(3).Text($"Row {i}", Font).BackgroundColor(i % 2 == 0 ? Color.Black : Color.DarkGray);
            }
        }
        paper.EndFrame();
    }

    private static void AssertSameGeometry(Canvas expected, Canvas actual)
    {
        Assert.Equal(expected.VertexCount, actual.VertexCount);
        Assert.Equal(expected.IndexCount, actual.IndexCount);
        Assert.Equal(expected.DrawCalls.Count, actual.DrawCalls.Count);

        var ev = expected.Vertices;
        var av = actual.Vertices;
        for (int i = 0; i < ev.Length; i++)
        {
            Assert.Equal(ev[i].x, av[i].x, 0.01f);
            Assert.Equal(ev[i].y, av[i].y, 0.01f);
            Assert.Equal(ev[i].u, av[i].u);
            Assert.Equal(ev[i].v, av[i].v);
            Assert.Equal(ev[i].Color, av[i].Color);
        }
        Assert.True(expected.Indices.SequenceEqual(actual.Indices));

        for (int i = 0; i < expected.DrawCalls.Count; i++)
        {
            var e = expected.DrawCalls[i];
            var a = actual.DrawCalls[i];
            Assert.Equal(e.ElementCount, a.ElementCount);
            e.GetScissor(1, out _, out var ep, out var ee);
            a.GetScissor(1, out _, out var ap, out var ae);
            Assert.Equal(ee, ae);
            Assert.Equal(ep.X, ap.X, 0.01f);
            Assert.Equal(ep.Y, ap.Y, 0.01f);
        }
    }

    private static Paper NewPaper() => new(new NullRenderer(), 800, 600, new FontAtlasSettings());

    private static FontFile LoadFont()
    {
        using var stream = typeof(RenderCacheTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        return new FontFile(stream);
    }

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls) { }
    }
}
