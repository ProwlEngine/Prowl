// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

namespace Tests;

/// <summary>
/// Hit testing against hidden elements. A hidden element keeps an empty rectangle at its parent's
/// corner, and the inclusive bounds test still contains that corner point, so hit testing has to
/// skip hidden elements rather than rely on their geometry.
/// </summary>
public class VisibilityTests
{
    [Fact]
    public void A_hidden_element_does_not_take_the_pointer_where_it_used_to_be()
    {
        var paper = NewPaper();

        Assert.Equal("target", HoveredAt(paper, 150, 50, p => Bar(p, targetVisible: true)));
        Assert.NotEqual("target", HoveredAt(paper, 150, 50, p => Bar(p, targetVisible: false)));
    }

    /// <summary>
    /// The corner it collapses to. A hidden element keeps an empty rectangle at its parent's
    /// origin, and an inclusive bounds test still counts that single point as inside, so this is
    /// where a hidden element takes the pointer from the element actually drawn there.
    /// </summary>
    [Fact]
    public void A_hidden_element_does_not_take_the_pointer_at_the_corner_it_collapses_to()
    {
        var paper = NewPaper();

        // "other" really covers (0,0); the hidden one only appears to, and is tested later.
        Assert.Equal("other", HoveredAt(paper, 0, 0, p => Bar(p, targetVisible: false)));
    }

    [Fact]
    public void A_hidden_child_does_not_take_the_pointer_at_its_parents_corner()
    {
        var paper = NewPaper();

        Assert.Equal("holder", HoveredAt(paper, 300, 200, p => Offset(p, childVisible: false)));
    }

    /// <summary>Hiding a container hides everything inside it, for the pointer as well as the eye.</summary>
    [Fact]
    public void Hiding_a_container_takes_its_children_out_of_hit_testing_too()
    {
        var paper = NewPaper();

        Assert.Equal("child", HoveredAt(paper, 150, 50, p => Nested(p, parentVisible: true)));
        Assert.NotEqual("child", HoveredAt(paper, 150, 50, p => Nested(p, parentVisible: false)));
    }

    /// <summary>A hidden element on a raised layer is collected for hit testing separately, and has
    /// to be left out of that too.</summary>
    [Fact]
    public void A_hidden_element_on_a_higher_layer_does_not_take_the_pointer()
    {
        var paper = NewPaper();

        Assert.Equal("overlay", HoveredAt(paper, 150, 50, p => Layered(p, overlayVisible: true)));
        Assert.NotEqual("overlay", HoveredAt(paper, 150, 50, p => Layered(p, overlayVisible: false)));
    }

    /// <summary>
    /// The raised-layer equivalent of the corner case: a hidden overlay must not even be gathered
    /// as a hit-test candidate, or it answers for its collapsed corner ahead of the whole base tree.
    /// </summary>
    [Fact]
    public void A_hidden_overlay_is_not_gathered_as_a_candidate_at_all()
    {
        var paper = NewPaper();

        Assert.Equal("other", HoveredAt(paper, 0, 0, p => Layered(p, overlayVisible: false)));
    }

    /// <summary>
    /// A hidden element inside a visible overlay. The overlay is gathered legitimately, so it is
    /// the walk through its children that has to leave the hidden one out.
    /// </summary>
    [Fact]
    public void A_hidden_child_of_a_visible_overlay_does_not_take_the_pointer()
    {
        var paper = NewPaper();

        Assert.Equal("panel", HoveredAt(paper, 300, 200, p => LayeredWithChild(p, childVisible: false)));
    }

    /// <summary>Showing it again puts it back, so nothing above is a one-way latch.</summary>
    [Fact]
    public void Showing_it_again_restores_hit_testing()
    {
        var paper = NewPaper();

        HoveredAt(paper, 150, 50, p => Bar(p, targetVisible: true));
        HoveredAt(paper, 150, 50, p => Bar(p, targetVisible: false));

        Assert.Equal("target", HoveredAt(paper, 150, 50, p => Bar(p, targetVisible: true)));
    }

    private static Paper NewPaper() => new(new NullRenderer(), 800, 600, new FontAtlasSettings());

    private static string HoveredAt(Paper paper, float x, float y, Action<Paper> tree)
    {
        paper.SetPointerPosition(x, y);
        paper.BeginFrame(1f / 60f);
        tree(paper);
        paper.EndFrame();

        foreach ((string name, int id) in Ids)
            if (id == paper.HoveredElementId)
                return name;

        return "none";
    }

    private static readonly Dictionary<string, int> Ids = new();

    private static ElementBuilder Named(Paper p, string name, bool row = false)
    {
        var b = row ? p.Row(name) : p.Box(name);
        Ids[name] = b._handle.Data.ID;
        return b;
    }

    private static void Bar(Paper p, bool targetVisible)
    {
        using (Named(p, "bar", row: true).Height(100).Enter())
        {
            Named(p, "other").Width(100).Height(100);
            Named(p, "target").Width(100).Height(100).Visible(targetVisible);
        }
    }

    private static void Nested(Paper p, bool parentVisible)
    {
        using (Named(p, "bar", row: true).Height(100).Enter())
        {
            Named(p, "other").Width(100).Height(100);
            using (Named(p, "holder", row: true).Width(100).Height(100).Visible(parentVisible).Enter())
                Named(p, "child").Width(100).Height(100);
        }
    }

    private static void Offset(Paper p, bool childVisible)
    {
        using (Named(p, "holder", row: true).Left(300).Top(200).Width(100).Height(100).Enter())
            Named(p, "child").Width(100).Height(100).Visible(childVisible);
    }

    private static void Layered(Paper p, bool overlayVisible)
    {
        using (Named(p, "bar", row: true).Height(100).Enter())
        {
            Named(p, "other").Width(100).Height(100);
            Named(p, "overlay").Width(200).Height(100)
                .Layer(Layer.Overlay).Visible(overlayVisible);
        }
    }

    private static void LayeredWithChild(Paper p, bool childVisible)
    {
        using (Named(p, "panel", row: true).Left(300).Top(200).Width(200).Height(100)
                   .Layer(Layer.Overlay).Enter())
            Named(p, "inner").Width(200).Height(100).Visible(childVisible);
    }

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint width, uint height) => new Int2((int)width, (int)height);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) { }
    }
}
