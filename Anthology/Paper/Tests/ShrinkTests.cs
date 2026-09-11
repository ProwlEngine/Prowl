// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.


using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Tests;
/// <summary>
/// Children giving up space when they do not fit. Shrinking shares out the shortfall, weighted by
/// shrink factor times base size. A child that was not asked to shrink overflows instead.
/// </summary>
public class ShrinkTests
{
    /// <summary>Nothing shrinks unless it was asked to.</summary>
    [Fact]
    public void Children_that_were_not_asked_to_shrink_still_overflow()
    {
        var w = Widths(400, p =>
        {
            Fixed(p, "a", 300);
            Fixed(p, "b", 300);
        });

        Assert.Equal(300f, w["a"], 2);
        Assert.Equal(300f, w["b"], 2);
    }

    [Fact]
    public void A_single_shrinkable_child_absorbs_the_whole_shortfall()
    {
        // 300 + 300 into 400 is 200 over. Only "b" gives, so it lands at 100.
        var w = Widths(400, p =>
        {
            Fixed(p, "a", 300);
            Shrinkable(p, "b", 300);
        });

        Assert.Equal(300f, w["a"], 2);
        Assert.Equal(100f, w["b"], 2);
    }

    /// <summary>
    /// Two equal children, equal factors: they give up the same amount. Weighting by base size
    /// makes no difference here because the bases are equal.
    /// </summary>
    [Fact]
    public void Two_equal_children_give_up_equal_amounts()
    {
        var w = Widths(400, p =>
        {
            Shrinkable(p, "a", 300);
            Shrinkable(p, "b", 300);
        });

        Assert.Equal(200f, w["a"], 2);
        Assert.Equal(200f, w["b"], 2);
    }

    /// <summary>
    /// The weighting that matters: with equal factors, a wide child gives up proportionally more
    /// than a narrow one, so both end up scaled by the same ratio rather than cut by the same
    /// number of pixels.
    /// </summary>
    [Fact]
    public void A_wider_child_gives_up_more_than_a_narrower_one()
    {
        // 400 + 200 = 600 into 300 is 300 over. Weights are 400 and 200, so they give up 200 and
        // 100 and land at 200 and 100 - each exactly half its base.
        var w = Widths(300, p =>
        {
            Shrinkable(p, "wide", 400);
            Shrinkable(p, "narrow", 200);
        });

        Assert.Equal(200f, w["wide"], 2);
        Assert.Equal(100f, w["narrow"], 2);
    }

    [Fact]
    public void A_larger_shrink_factor_gives_up_more()
    {
        // Equal bases, factors 3 and 1: the shortfall of 200 splits 150 / 50.
        var w = Widths(400, p =>
        {
            Shrinkable(p, "eager", 300, factor: 3f);
            Shrinkable(p, "reluctant", 300, factor: 1f);
        });

        Assert.Equal(150f, w["eager"], 2);
        Assert.Equal(250f, w["reluctant"], 2);
    }

    /// <summary>
    /// A minimum stops a child shrinking further, and what it refuses to give up is taken from the
    /// others instead, which takes more than one distribution pass.
    /// </summary>
    [Fact]
    public void A_minimum_holds_and_the_rest_is_taken_from_the_others()
    {
        // 300 + 300 into 300 is 300 over. Split evenly that is 150 each, but "floored" stops at its
        // 250 minimum, so "free" absorbs the rest and lands at 50.
        var w = Widths(300, p =>
        {
            Shrinkable(p, "floored", 300).MinWidth(250);
            Shrinkable(p, "free", 300);
        });

        Assert.Equal(250f, w["floored"], 2);
        Assert.Equal(50f, w["free"], 2);
    }

    /// <summary>Shrinking never runs past zero, however large the shortfall.</summary>
    [Fact]
    public void Shrinking_stops_at_zero()
    {
        var w = Widths(100, p =>
        {
            Fixed(p, "a", 500);
            Shrinkable(p, "b", 200);
        });

        Assert.True(w["b"] >= 0f, $"expected a non-negative width, got {w["b"]}");
        Assert.Equal(0f, w["b"], 2);
    }

    /// <summary>With room to spare there is nothing to share out, so a shrinkable child keeps its size.</summary>
    [Fact]
    public void A_shrinkable_child_that_fits_is_left_alone()
    {
        var w = Widths(800, p =>
        {
            Fixed(p, "a", 100);
            Shrinkable(p, "b", 300);
        });

        Assert.Equal(300f, w["b"], 2);
    }

    /// <summary>Surplus is still shared out by grow weight.</summary>
    [Fact]
    public void Growing_is_unchanged()
    {
        var w = Widths(400, p =>
        {
            Fixed(p, "a", 100);
            Named(p, "b").Width(UnitValue.Stretch(1)).Height(50);
        });

        Assert.Equal(100f, w["a"], 2);
        Assert.Equal(300f, w["b"], 2);
    }

    /// <summary>The same thing down a column, so it is the main axis rather than the width.</summary>
    [Fact]
    public void Shrinking_works_down_a_column_too()
    {
        var paper = NewPaper();
        paper.BeginFrame(1f / 60f);
        using (Named(paper, "col", row: false, container: true).Width(200).Height(300).Enter())
        {
            Named(paper, "a").Width(50).Height(200);
            Named(paper, "b").Width(50).Height(UnitValue.Pixels(200).Shrinkable());
        }
        paper.EndFrame();

        Assert.Equal(200f, Height(paper, "a"), 2);
        Assert.Equal(100f, Height(paper, "b"), 2);
    }

    /// <summary>A shrunk child lays its own contents out again at the size it ended up.</summary>
    [Fact]
    public void A_shrunk_child_reflows_its_own_contents()
    {
        var paper = NewPaper();
        paper.BeginFrame(1f / 60f);
        using (Named(paper, "row", row: true, container: true).Width(400).Height(100).Enter())
        {
            Named(paper, "a").Width(300).Height(50);
            using (Named(paper, "b").Width(UnitValue.Pixels(300).Shrinkable()).Height(50).Enter())
                Named(paper, "inner").Width(UnitValue.Stretch(1)).Height(50);
        }
        paper.EndFrame();

        Assert.Equal(100f, Width(paper, "b"), 2);
        Assert.Equal(100f, Width(paper, "inner"), 2);   // followed its parent down
    }

    /// <summary>
    /// A wrapping container already has an answer for children that do not fit, so shrinking must
    /// not pre-empt it: the children keep their size and go onto the next line.
    /// </summary>
    [Fact]
    public void A_wrapping_container_wraps_rather_than_shrinking()
    {
        var paper = NewPaper();
        paper.BeginFrame(1f / 60f);
        using (Named(paper, "wrap", row: true, container: true).Width(250).Height(200).WrapContent().Enter())
        {
            Named(paper, "a").Width(UnitValue.Pixels(150).Shrinkable()).Height(40);
            Named(paper, "b").Width(UnitValue.Pixels(150).Shrinkable()).Height(40);
        }
        paper.EndFrame();

        Assert.Equal(150f, Width(paper, "a"), 2);
        Assert.Equal(150f, Width(paper, "b"), 2);

        // Kept their width, so the second one had to go onto a line of its own.
        var a = paper.FindElementHandleByID(Ids["a"]).Data;
        var b = paper.FindElementHandleByID(Ids["b"]).Data;
        Assert.True(b.Y > a.Y, $"expected b below a, got a.Y={a.Y} b.Y={b.Y}");
    }

    private static Paper NewPaper() => new(new NullRenderer(), 800, 600, new FontAtlasSettings());

    /// <summary>Lays the children out in a row of the given width and reports what each ended up.</summary>
    private static Dictionary<string, float> Widths(float rowWidth, Action<Paper> children)
    {
        var paper = NewPaper();
        paper.BeginFrame(1f / 60f);
        using (Named(paper, "row", row: true, container: true).Width(rowWidth).Height(100).Enter())
            children(paper);
        paper.EndFrame();

        var widths = new Dictionary<string, float>();
        foreach ((string name, int id) in Ids)
        {
            var handle = paper.FindElementHandleByID(id);
            if (handle.IsValid) widths[name] = handle.Data.LayoutWidth;
        }
        return widths;
    }

    private static float Width(Paper paper, string name) => paper.FindElementHandleByID(Ids[name]).Data.LayoutWidth;

    private static float Height(Paper paper, string name) => paper.FindElementHandleByID(Ids[name]).Data.LayoutHeight;

    private static readonly Dictionary<string, int> Ids = new();

    private static ElementBuilder Named(Paper p, string name, bool row = false, bool container = false)
    {
        var b = container ? (row ? p.Row(name) : p.Column(name)) : p.Box(name);
        Ids[name] = b._handle.Data.ID;
        return b;
    }

    private static ElementBuilder Fixed(Paper p, string name, float width)
        => Named(p, name).Width(width).Height(50);

    private static ElementBuilder Shrinkable(Paper p, string name, float width, float factor = 1f)
        => Named(p, name).Width(UnitValue.Pixels(width).Shrinkable(factor)).Height(50);

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint width, uint height) => new Int2((int)width, (int)height);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) { }
    }
}
