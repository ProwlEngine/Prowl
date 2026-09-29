// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Tests;

/// <summary>
/// A replayed snapshot has to come out exactly as drawing the same calls again would, and has to
/// refuse to replay whenever that would not be true.
/// </summary>
public class SnapshotTests
{
    private static readonly FontFile Font = LoadFont();
    private static readonly object Texture = new Int2(64, 32);
    private static readonly Color32 White = Color32.FromArgb(255, 255, 255, 255);

    [Fact]
    public void A_replay_in_place_matches_drawing_directly()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 10, 20, c => DrawScene(c, 10, 20));

        var direct = Frame(canvas, c => DrawScene(c, 10, 20));
        var replayed = Frame(canvas, c => Assert.True(c.DrawSnapshot(snapshot, 10, 20)));

        AssertSameOutput(direct, replayed);
    }

    [Fact]
    public void A_replay_at_a_new_anchor_matches_drawing_directly_there()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 10, 20, c => DrawScene(c, 10, 20));

        var direct = Frame(canvas, c => DrawScene(c, 37.25f, 91.5f));
        var replayed = Frame(canvas, c => Assert.True(c.DrawSnapshot(snapshot, 37.25f, 91.5f)));

        AssertSameOutput(direct, replayed);
    }

    [Fact]
    public void A_replay_under_a_translated_transform_matches_drawing_directly()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c => DrawScene(c, 0, 0));

        var direct = Frame(canvas, c => { c.TransformBy(Transform2D.CreateTranslation(15, -4)); DrawScene(c, 0, 0); });
        var replayed = Frame(canvas, c => { c.TransformBy(Transform2D.CreateTranslation(15, -4)); Assert.True(c.DrawSnapshot(snapshot, 0, 0)); });

        AssertSameOutput(direct, replayed);
    }

    [Fact]
    public void A_replay_merges_into_the_current_batch()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c => c.RectFilled(0, 0, 10, 10, White));

        var replayed = Frame(canvas, c =>
        {
            c.RectFilled(50, 50, 10, 10, White);
            Assert.True(c.DrawSnapshot(snapshot, 0, 0));
            c.RectFilled(80, 80, 10, 10, White);
        });

        Assert.Single(replayed.Calls);
    }

    [Fact]
    public void A_requested_draw_call_split_survives_replay()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c =>
        {
            c.RectFilled(0, 0, 10, 10, White);
            c.RequestNewDrawCall();
            c.RectFilled(20, 0, 10, 10, White);
        });

        var replayed = Frame(canvas, c => Assert.True(c.DrawSnapshot(snapshot, 0, 0)));

        Assert.Equal(2, replayed.Calls.Length);
    }

    [Fact]
    public void A_different_rotation_scale_or_alpha_refuses_to_replay()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c => DrawScene(c, 0, 0));

        canvas.BeginFrame(800, 600);
        canvas.TransformBy(Transform2D.CreateRotation(0.3f));
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));
        Assert.Equal(0, canvas.VertexCount);

        canvas.BeginFrame(800, 600, 2f);
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));

        canvas.BeginFrame(800, 600);
        canvas.SetGlobalAlpha(0.5f);
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));
    }

    [Fact]
    public void A_snapshot_only_replays_on_the_canvas_that_recorded_it()
    {
        var snapshot = Record(NewCanvas(), 0, 0, c => c.RectFilled(0, 0, 10, 10, White));

        Assert.False(NewCanvas().DrawSnapshot(snapshot, 0, 0));
    }

    [Fact]
    public void Setting_the_scissor_pins_a_snapshot_to_where_it_was_recorded()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c =>
        {
            c.SaveState();
            c.IntersectScissor(0, 0, 50, 50);
            c.RectFilled(0, 0, 100, 100, White);
            c.RestoreState();
        });

        canvas.BeginFrame(800, 600);
        Assert.True(canvas.DrawSnapshot(snapshot, 0, 0));
        Assert.False(canvas.DrawSnapshot(snapshot, 5, 0));

        canvas.Scissor(0, 0, 20, 20);
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));
    }

    [Fact]
    public void An_inherited_scissor_follows_the_live_one()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c => c.RectFilled(0, 0, 100, 100, White));

        var direct = Frame(canvas, c => { c.Scissor(10, 10, 30, 30); c.RectFilled(5, 5, 100, 100, White); });
        var replayed = Frame(canvas, c => { c.Scissor(10, 10, 30, 30); Assert.True(c.DrawSnapshot(snapshot, 5, 5)); });

        AssertSameOutput(direct, replayed);
    }

    [Fact]
    public void Setting_an_absolute_transform_pins_a_snapshot()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c =>
        {
            c.SaveState();
            c.ResetTransform();
            c.RectFilled(0, 0, 10, 10, White);
            c.RestoreState();
        });

        canvas.BeginFrame(800, 600);
        Assert.True(canvas.DrawSnapshot(snapshot, 0, 0));
        Assert.False(canvas.DrawSnapshot(snapshot, 5, 0));
    }

    [Fact]
    public void A_brush_handed_in_from_outside_only_replays_in_place()
    {
        var canvas = NewCanvas();
        var black = Color32.FromArgb(255, 0, 0, 0);
        canvas.SetLinearBrush(0, 0, 100, 0, White, black);
        var snapshot = new CanvasSnapshot();
        canvas.BeginSnapshot(snapshot, 0, 0);
        canvas.RectFilled(0, 0, 100, 10, White);
        Assert.True(canvas.EndSnapshot());

        canvas.BeginFrame(800, 600);
        canvas.SetLinearBrush(0, 0, 100, 0, White, black);
        Assert.True(canvas.DrawSnapshot(snapshot, 0, 0));
        Assert.False(canvas.DrawSnapshot(snapshot, 5, 0));

        canvas.ClearBrush();
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));
    }

    [Fact]
    public void Restoring_a_state_saved_before_the_capture_makes_it_unreplayable()
    {
        var canvas = NewCanvas();
        var snapshot = new CanvasSnapshot();
        canvas.SaveState();
        canvas.BeginSnapshot(snapshot, 0, 0);
        canvas.RestoreState();
        canvas.RectFilled(0, 0, 10, 10, White);

        Assert.False(canvas.EndSnapshot());
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));
    }

    [Fact]
    public void Leaving_state_changed_makes_it_unreplayable()
    {
        Assert.False(TryRecord(c => { c.SetFillColor(Color32.FromArgb(255, 1, 2, 3)); c.RectFilled(0, 0, 10, 10, White); }));
        Assert.False(TryRecord(c => { c.SetGlobalAlpha(0.5f); c.RectFilled(0, 0, 10, 10, White); }));
        Assert.False(TryRecord(c => { c.SaveState(); c.RectFilled(0, 0, 10, 10, White); }));
        Assert.False(TryRecord(c => { c.TransformBy(Transform2D.CreateTranslation(1, 0)); c.RectFilled(0, 0, 10, 10, White); }));
    }

    [Fact]
    public void Triangles_reaching_outside_the_capture_make_it_unreplayable()
    {
        var canvas = NewCanvas();
        canvas.RectFilled(0, 0, 10, 10, White);
        var snapshot = new CanvasSnapshot();
        canvas.BeginSnapshot(snapshot, 0, 0);
        canvas.AddVertex(new Vertex(new Float2(0, 0), new Float2(1, 1), White));
        canvas.AddVertex(new Vertex(new Float2(5, 0), new Float2(1, 1), White));
        canvas.AddTriangle(0, canvas.VertexCount - 2, canvas.VertexCount - 1);

        Assert.False(canvas.EndSnapshot());
    }

    [Fact]
    public void A_snapshot_replayed_inside_another_capture_replays_with_it()
    {
        var canvas = NewCanvas();
        var inner = Record(canvas, 0, 0, c => DrawScene(c, 0, 0));
        var outer = Record(canvas, 0, 0, c =>
        {
            c.RectFilled(0, 0, 5, 5, White);
            Assert.True(c.DrawSnapshot(inner, 0, 0));
        });

        var direct = Frame(canvas, c => { c.RectFilled(12, 3, 5, 5, White); DrawScene(c, 12, 3); });
        var replayed = Frame(canvas, c => Assert.True(c.DrawSnapshot(outer, 12, 3)));

        AssertSameOutput(direct, replayed);
    }

    [Fact]
    public void An_invalidated_or_empty_snapshot_does_not_draw()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c => c.RectFilled(0, 0, 10, 10, White));
        snapshot.Invalidate();
        var empty = Record(canvas, 0, 0, _ => { });

        canvas.BeginFrame(800, 600);
        Assert.False(canvas.DrawSnapshot(snapshot, 0, 0));
        Assert.False(canvas.DrawSnapshot(new CanvasSnapshot(), 0, 0));
        Assert.True(canvas.DrawSnapshot(empty, 0, 0));
        Assert.Equal(0, canvas.VertexCount);
    }

    [Fact]
    public void Snapshots_do_not_nest()
    {
        var canvas = NewCanvas();
        canvas.BeginSnapshot(new CanvasSnapshot(), 0, 0);
        Assert.Throws<InvalidOperationException>(() => canvas.BeginSnapshot(new CanvasSnapshot(), 0, 0));
    }

    [Fact]
    public void Recapturing_reuses_a_snapshot()
    {
        var canvas = NewCanvas();
        var snapshot = Record(canvas, 0, 0, c => DrawScene(c, 0, 0));
        canvas.BeginFrame(800, 600);
        canvas.BeginSnapshot(snapshot, 0, 0);
        canvas.RectFilled(0, 0, 10, 10, White);
        Assert.True(canvas.EndSnapshot());

        var direct = Frame(canvas, c => c.RectFilled(0, 0, 10, 10, White));
        var replayed = Frame(canvas, c => Assert.True(c.DrawSnapshot(snapshot, 0, 0)));

        AssertSameOutput(direct, replayed);
    }

    // Shapes, a gradient, an image, a stroke and text, so every kind of state a segment carries is covered.
    private static void DrawScene(Canvas canvas, float x, float y)
    {
        canvas.RectFilled(x, y, 40, 20, Color32.FromArgb(255, 255, 0, 0));
        canvas.RoundedRectFilled(x + 50, y, 40, 20, 6, Color32.FromArgb(128, 0, 255, 0));
        canvas.SetLinearBrush(x, y, x + 40, y, Color32.FromArgb(255, 0, 0, 255), White);
        canvas.RectFilled(x, y + 30, 40, 20, White);
        canvas.ClearBrush();
        canvas.DrawImage(Texture, x + 50, y + 30, 32, 16);
        canvas.SetStrokeColor(White);
        canvas.SetStrokeWidth(3);
        canvas.BeginPath();
        canvas.MoveTo(x, y + 60);
        canvas.LineTo(x + 30, y + 75);
        canvas.Stroke();
        canvas.SetStrokeWidth(1);
        canvas.SetStrokeColor(Color32.FromArgb(255, 0, 0, 0));
        canvas.DrawText("snapshot", x, y + 80, White, 14, Font);
    }

    private sealed record Output(Vertex[] Vertices, uint[] Indices, DrawCall[] Calls);

    // Draws one frame and copies out what it produced.
    private static Output Frame(Canvas canvas, Action<Canvas> draw)
    {
        canvas.BeginFrame(800, 600);
        draw(canvas);
        return new Output(canvas.Vertices.ToArray(), canvas.Indices.ToArray(), canvas.DrawCalls.ToArray());
    }

    private static CanvasSnapshot Record(Canvas canvas, float x, float y, Action<Canvas> draw)
    {
        canvas.BeginFrame(800, 600);
        var snapshot = new CanvasSnapshot();
        canvas.BeginSnapshot(snapshot, x, y);
        draw(canvas);
        Assert.True(canvas.EndSnapshot());
        return snapshot;
    }

    private static bool TryRecord(Action<Canvas> draw)
    {
        var canvas = NewCanvas();
        var snapshot = new CanvasSnapshot();
        canvas.BeginSnapshot(snapshot, 0, 0);
        draw(canvas);
        return canvas.EndSnapshot();
    }

    private static void AssertSameOutput(Output expected, Output actual)
    {
        Assert.Equal(expected.Vertices.Length, actual.Vertices.Length);
        Assert.Equal(expected.Calls.Length, actual.Calls.Length);
        Assert.Equal(expected.Indices, actual.Indices);

        for (int i = 0; i < expected.Vertices.Length; i++)
        {
            var e = expected.Vertices[i];
            var a = actual.Vertices[i];
            Assert.Equal(e.x, a.x, 0.01f);
            Assert.Equal(e.y, a.y, 0.01f);
            Assert.Equal(e.u, a.u);
            Assert.Equal(e.v, a.v);
            Assert.Equal(e.Color, a.Color);
        }

        for (int i = 0; i < expected.Calls.Length; i++)
        {
            var e = expected.Calls[i];
            var a = actual.Calls[i];
            Assert.Equal(e.ElementCount, a.ElementCount);
            Assert.Equal(e.Brush.Type, a.Brush.Type);
            Assert.Same(e.Texture, a.Texture);
            Assert.Same(e.FontAtlas, a.FontAtlas);

            e.GetScissor(1, out _, out var ep, out var ee);
            a.GetScissor(1, out _, out var ap, out var ae);
            Assert.Equal(ee, ae);
            Assert.Equal(ep.X, ap.X, 0.01f);
            Assert.Equal(ep.Y, ap.Y, 0.01f);

            // Replay moves a gradient or texture by its transform rather than its points, so compare where it lands.
            if (e.Brush.Type != BrushType.None)
                AssertSamePoint(e.Brush.Transform.TransformPoint(e.Brush.Point1), a.Brush.Transform.TransformPoint(a.Brush.Point1));
            if (e.Texture != null)
                AssertSamePoint(e.Brush.TextureTransform.TransformPoint(Float2.Zero), a.Brush.TextureTransform.TransformPoint(Float2.Zero));
        }
    }

    private static void AssertSamePoint(Float2 expected, Float2 actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01f);
        Assert.Equal(expected.Y, actual.Y, 0.01f);
    }

    private static Canvas NewCanvas()
    {
        var canvas = new Canvas(new NullRenderer(), new FontAtlasSettings());
        canvas.BeginFrame(800, 600);
        return canvas;
    }

    private static FontFile LoadFont()
    {
        using var stream = typeof(SnapshotTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
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
