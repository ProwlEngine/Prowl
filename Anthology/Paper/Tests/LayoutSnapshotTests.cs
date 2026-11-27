// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Tests;

/// <summary>
/// Records the rectangle the layout engine computes for every element in a set of representative
/// trees. The expected strings are the engine's own output, captured deliberately: their value is
/// not that any particular number is right, but that reworking the engine's internals cannot move
/// one without the change showing up here.
/// </summary>
public class LayoutSnapshotTests
{
    [Fact]
    public void Row_of_fixed_boxes() => Check(Scenes.RowOfFixedBoxes,
        "root 0,0 800x600 | root_row 0,0 800x600 | a 0,0 100x40 | b 100,0 100x40 | c 200,0 100x40");

    [Fact]
    public void Column_with_stretch() => Check(Scenes.ColumnWithStretch,
        "root 0,0 800x600 | fixed 0,0 800x50 | grow 0,50 800x500 | fixed2 0,550 800x50");

    [Fact]
    public void Nested_rows_and_columns() => Check(Scenes.NestedRowsAndColumns,
        "root 0,0 800x600 | outer 0,0 800x600 | left 0,0 400x600 | l0 0,0 400x30 | l1 0,30 400x30 | right 400,0 400x600 | r0 400,0 200x600 | r1 600,0 200x600");

    [Fact]
    public void Percent_and_padding() => Check(Scenes.PercentAndPadding,
        "root 0,0 800x600 | pad 10,10 780x580 | half 10,10 390x580");

    [Fact]
    public void Alignment_and_spacing() => Check(Scenes.AlignmentAndSpacing,
        "root 0,0 800x600 | bar 0,0 800x100 | x 0,0 100x100 | y 350,0 100x100 | z 700,0 100x100");

    [Fact]
    public void Wrapped_content() => Check(Scenes.WrappedContent,
        "root 0,0 800x600 | wrap 0,0 250x600 | w0 0,0 100x40 | w1 100,0 100x40 | w2 0,40 100x40 | w3 100,40 100x40 | w4 0,80 100x40");

    /// <summary>
    /// Deep nesting is where shared layout buffers would go wrong: an inner container must not be
    /// able to disturb the one it sits inside. Each level here is narrower than its parent, so any
    /// crossed wires show up as a wrong width rather than as nothing at all.
    /// </summary>
    [Fact]
    public void Deeply_nested_containers_keep_their_own_measurements()
    {
        var paper = NewPaper();
        RunOnce(paper, Scenes.DeepNesting);
        string snapshot = RunOnce(paper, Scenes.DeepNesting);

        // Ten levels, each inset 20px on both sides by its parent's padding.
        var expected = new StringBuilder("root 0,0 800x600");
        for (int depth = 0; depth < 10; depth++)
            expected.Append(" | d").Append(depth).Append(' ')
                    .Append(10 * (depth + 1)).Append(',').Append(10 * (depth + 1)).Append(' ')
                    .Append(800 - 20 * (depth + 1)).Append('x').Append(600 - 20 * (depth + 1));

        Assert.Equal(expected.ToString(), snapshot);
    }

    /// <summary>Runs the scene twice: a tree laid out again must land in exactly the same place.</summary>
    [Fact]
    public void Layout_is_stable_across_frames()
    {
        foreach (var scene in new Action<Paper>[]
                 {
                     Scenes.RowOfFixedBoxes, Scenes.ColumnWithStretch, Scenes.NestedRowsAndColumns,
                     Scenes.PercentAndPadding, Scenes.AlignmentAndSpacing, Scenes.WrappedContent,
                     Scenes.DeepNesting, Scenes.ManySiblings,
                 })
        {
            var paper = NewPaper();
            string first = RunOnce(paper, scene);
            string second = RunOnce(paper, scene);
            string third = RunOnce(paper, scene);

            Assert.Equal(first, second);
            Assert.Equal(second, third);
        }
    }

    private static void Check(Action<Paper> scene, string expected)
    {
        var paper = NewPaper();
        RunOnce(paper, scene);                 // first frame warms persistent per-element state
        Assert.Equal(expected, RunOnce(paper, scene));
    }

    private static Paper NewPaper() => new(new NullRenderer(), 800, 600, new FontAtlasSettings());

    private static string RunOnce(Paper paper, Action<Paper> scene)
    {
        paper.BeginFrame(1f / 60f);
        scene(paper);
        paper.EndFrame();

        var sb = new StringBuilder();
        Walk(paper.GetRootElementHandle(), sb);
        return sb.ToString();
    }

    private static void Walk(ElementHandle handle, StringBuilder sb)
    {
        ref var data = ref handle.Data;
        if (sb.Length > 0) sb.Append(" | ");
        sb.Append(Names.TryGetValue(data.ID, out string? name) ? name : data.ID == 0 ? "root" : data.ID.ToString());
        sb.Append(' ').Append(R(data.X)).Append(',').Append(R(data.Y))
          .Append(' ').Append(R(data.LayoutWidth)).Append('x').Append(R(data.LayoutHeight));

        foreach (int child in data.ChildIndices)
            Walk(new ElementHandle(handle.Owner, child), sb);
    }

    private static string R(double v) => Math.Round(v, 2).ToString("0.##");

    // Element ids are hashes, so the scenes register readable names as they build.
    internal static readonly Dictionary<int, string> Names = new();

    internal static ElementBuilder Named(Paper p, string name)
    {
        var b = p.Box(name);
        Names[b._handle.Data.ID] = name;
        return b;
    }

    internal static ElementBuilder NamedRow(Paper p, string name)
    {
        var b = p.Row(name);
        Names[b._handle.Data.ID] = name;
        return b;
    }

    internal static ElementBuilder NamedColumn(Paper p, string name)
    {
        var b = p.Column(name);
        Names[b._handle.Data.ID] = name;
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

    private static class Scenes
    {
        public static void RowOfFixedBoxes(Paper p)
        {
            using (NamedRow(p, "root_row").Enter())
            {
                Named(p, "a").Width(100).Height(40);
                Named(p, "b").Width(100).Height(40);
                Named(p, "c").Width(100).Height(40);
            }
        }

        public static void ColumnWithStretch(Paper p)
        {
            Named(p, "fixed").Height(50);
            Named(p, "grow").Height(UnitValue.Stretch(1));
            Named(p, "fixed2").Height(50);
        }

        public static void NestedRowsAndColumns(Paper p)
        {
            using (NamedRow(p, "outer").Enter())
            {
                using (NamedColumn(p, "left").Enter())
                {
                    Named(p, "l0").Height(30);
                    Named(p, "l1").Height(30);
                }
                using (NamedRow(p, "right").Enter())
                {
                    Named(p, "r0");
                    Named(p, "r1");
                }
            }
        }

        public static void PercentAndPadding(Paper p)
        {
            using (NamedColumn(p, "pad").Margin(10).Enter())
                Named(p, "half").Width(UnitValue.Percentage(50));
        }

        public static void AlignmentAndSpacing(Paper p)
        {
            using (NamedRow(p, "bar").Height(100).Enter())
            {
                Named(p, "x").Width(100);
                Named(p, "y").Width(100).Left(UnitValue.Stretch(1));
                Named(p, "z").Width(100).Left(UnitValue.Stretch(1));
            }
        }

        public static void DeepNesting(Paper p)
        {
            Nest(p, 0);

            static void Nest(Paper p, int depth)
            {
                if (depth == 10) return;
                using (NamedColumn(p, "d" + depth).Margin(10).Enter())
                    Nest(p, depth + 1);
            }
        }

        public static void ManySiblings(Paper p)
        {
            using (NamedRow(p, "many").Enter())
                for (int i = 0; i < 200; i++)
                {
                    var b = p.Box("s", i).Width(3).Height(10);
                    Names[b._handle.Data.ID] = "s" + i;
                }
        }

        public static void WrappedContent(Paper p)
        {
            using (NamedRow(p, "wrap").Width(250).WrapContent().Enter())
                for (int i = 0; i < 5; i++)
                {
                    var b = p.Box("w", i).Width(100).Height(40);
                    Names[b._handle.Data.ID] = "w" + i;
                }
        }
    }
}
