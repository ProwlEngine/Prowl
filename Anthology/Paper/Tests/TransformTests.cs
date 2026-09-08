// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

namespace Tests;

/// <summary>
/// Element transforms, and the hit testing that reads them. Transforms are resolved once per frame
/// and cached, with an identity flag carrying the common case, so what matters is that the cached
/// value composes down the tree the same way accumulating it at each step did, and that the flag is
/// only set when there really is nothing to apply.
/// </summary>
public class TransformTests
{
    [Fact]
    public void An_untransformed_tree_hit_tests_by_position()
    {
        var paper = NewPaper();

        Assert.Equal("target", HoveredAt(paper, 150, 50, Trees.TwoBoxes));
        Assert.Equal("other", HoveredAt(paper, 50, 50, Trees.TwoBoxes));
    }

    /// <summary>A translated element is hit where it was moved to, not where it was laid out.</summary>
    [Fact]
    public void A_translated_element_is_hit_at_its_moved_position()
    {
        var paper = NewPaper();

        // "target" is laid out at x 100..200 and pushed 200px right, so it now covers 300..400.
        Assert.Equal("target", HoveredAt(paper, 350, 50, Trees.TranslatedTarget));
        Assert.NotEqual("target", HoveredAt(paper, 150, 50, Trees.TranslatedTarget));
    }

    /// <summary>
    /// A transform on a parent has to reach its children too. This is the case the cached world
    /// transform exists for, and the one a stale or unpropagated cache would get wrong.
    /// </summary>
    [Fact]
    public void A_transform_on_a_parent_moves_its_children()
    {
        var paper = NewPaper();

        Assert.Equal("child", HoveredAt(paper, 350, 50, Trees.TranslatedParent));
        Assert.NotEqual("child", HoveredAt(paper, 150, 50, Trees.TranslatedParent));
    }

    /// <summary>Transforms compose down the tree rather than replacing one another.</summary>
    [Fact]
    public void Nested_transforms_compose()
    {
        var paper = NewPaper();

        // Laid out at 0..100, moved +100 by its parent and +100 again by the one above that, so it
        // ends up covering 200..300. A transform that replaced rather than composed would leave it
        // at 100..200.
        Assert.Equal("child", HoveredAt(paper, 250, 50, Trees.NestedTranslations));
        Assert.NotEqual("child", HoveredAt(paper, 150, 50, Trees.NestedTranslations));
    }

    /// <summary>Scaling about the origin moves the far edge, so a point outside the box falls in.</summary>
    [Fact]
    public void A_scaled_element_is_hit_over_its_scaled_area()
    {
        var paper = NewPaper();

        Assert.Equal("target", HoveredAt(paper, 250, 50, Trees.ScaledTarget));
        Assert.NotEqual("target", HoveredAt(paper, 450, 50, Trees.ScaledTarget));
    }

    /// <summary>
    /// The flag that lets every walk skip the matrix. It has to be false for an element under a
    /// transformed ancestor even though that element declares nothing itself, or hit testing would
    /// use the pointer unmoved and miss.
    /// </summary>
    [Fact]
    public void The_identity_flag_accounts_for_ancestors()
    {
        var paper = NewPaper();
        Run(paper, Trees.TranslatedParent);

        Assert.True(Data(paper, "plain")._isIdentityTransform);
        Assert.True(Data(paper, "plain")._isIdentityWorldTransform);

        // Declares the transform: not identity locally, and not in the world either.
        Assert.False(Data(paper, "moved")._isIdentityTransform);
        Assert.False(Data(paper, "moved")._isIdentityWorldTransform);

        // Declares nothing itself, but inherits one: identity locally, not in the world.
        Assert.True(Data(paper, "child")._isIdentityTransform);
        Assert.False(Data(paper, "child")._isIdentityWorldTransform);
    }

    /// <summary>The cached inverse has to actually undo the cached transform.</summary>
    [Fact]
    public void The_cached_inverse_undoes_the_cached_transform()
    {
        var paper = NewPaper();
        Run(paper, Trees.NestedTranslations);

        ref var child = ref Data(paper, "child");
        var point = new Float2(123f, 45f);
        Float2 roundTrip = child._worldInverse.TransformPoint(child._worldTransform.TransformPoint(point));

        Assert.Equal(point.X, roundTrip.X, 3);
        Assert.Equal(point.Y, roundTrip.Y, 3);
    }

    private static Paper NewPaper() => new(new NullRenderer(), 800, 600, new FontAtlasSettings());

    private static void Run(Paper paper, Action<Paper> tree)
    {
        paper.BeginFrame(1f / 60f);
        tree(paper);
        paper.EndFrame();
    }

    private static ref Prowl.PaperUI.LayoutEngine.ElementData Data(Paper paper, string name)
    {
        var handle = paper.FindElementHandleByID(Ids[name]);
        Assert.True(handle.IsValid, $"no element named {name}");
        return ref handle.Data;
    }

    /// <summary>Runs a frame with the pointer at a position and reports which element it landed on.</summary>
    private static string HoveredAt(Paper paper, float x, float y, Action<Paper> tree)
    {
        // Interaction reads the pointer during EndFrame, and hover is resolved from the tree built
        // in the same frame, so one frame per query is enough. HoveredElementId is the topmost hit,
        // where IsElementHovered would also be true for every container along the way.
        paper.SetPointerPosition(x, y);
        Run(paper, tree);

        foreach ((string name, int id) in Ids)
            if (id == paper.HoveredElementId)
                return name;

        return "none";
    }

    private static readonly Dictionary<string, int> Ids = new();

    private static ElementBuilder Box(Paper p, string name)
    {
        var b = p.Box(name);
        Ids[name] = b._handle.Data.ID;
        return b;
    }

    private static ElementBuilder Row(Paper p, string name)
    {
        var b = p.Row(name);
        Ids[name] = b._handle.Data.ID;
        return b;
    }

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint width, uint height) => new Int2((int)width, (int)height);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) { }
    }

    private static class Trees
    {
        public static void TwoBoxes(Paper p)
        {
            using (Row(p, "bar").Height(100).Enter())
            {
                Box(p, "other").Width(100).Height(100);
                Box(p, "target").Width(100).Height(100);
            }
        }

        public static void TranslatedTarget(Paper p)
        {
            using (Row(p, "bar").Height(100).Enter())
            {
                Box(p, "other").Width(100).Height(100);
                Box(p, "target").Width(100).Height(100).Translate(200, 0);
            }
        }

        public static void TranslatedParent(Paper p)
        {
            using (Row(p, "bar").Height(100).Enter())
            {
                Box(p, "plain").Width(100).Height(100);
                using (Row(p, "moved").Width(100).Height(100).Translate(200, 0).Enter())
                    Box(p, "child").Width(100).Height(100);
            }
        }

        public static void NestedTranslations(Paper p)
        {
            using (Row(p, "outer").Height(100).Translate(100, 0).Enter())
            using (Row(p, "inner").Width(100).Height(100).Translate(100, 0).Enter())
                Box(p, "child").Width(100).Height(100);
        }

        public static void ScaledTarget(Paper p)
        {
            using (Row(p, "bar").Height(100).Enter())
                Box(p, "target").Width(100).Height(100).TransformOrigin(0, 0).Scale(3, 1);
        }
    }
}
