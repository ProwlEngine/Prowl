using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;
public class LayoutIntegrationTests
{

    [Fact]
    public void PublicContractsDoNotExposeTheLayoutBackend()
    {
        void CheckType(Type type)
        {
            Assert.NotEqual("Prowl.Scaffold", type.Namespace);
            if (type.HasElementType)
            {
                CheckType(type.GetElementType()!);
            }

            foreach (Type argument in type.GetGenericArguments())
            {
                CheckType(argument);
            }
        }

        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        foreach (Type type in typeof(Paper).Assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(flags))
            {
                CheckType(method.ReturnType);
                foreach (var parameter in method.GetParameters())
                {
                    CheckType(parameter.ParameterType);
                }
            }

            foreach (var constructor in type.GetConstructors(flags))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    CheckType(parameter.ParameterType);
                }
            }

            foreach (var field in type.GetFields(flags))
            {
                CheckType(field.FieldType);
            }
        }
    }

    [Fact]
    public void AnimatedDimensionsInvalidateUntilSettled()
    {
        var p = NewPaper();
        ElementBuilder Frame(float target)
        {
            p.BeginFrame(.1f);
            var a = Box(p, "animated").Transition(GuiProp.Width, 1).Width(target).Height(10);
            p.EndFrame();
            return a;
        }

        Frame(40);
        float previous = Frame(100)._handle.Data.LayoutWidth;
        float next = Frame(100)._handle.Data.LayoutWidth;
        Assert.True(next > previous);
        for (int i = 0; i < 15; i++)
        {
            Frame(100);
        }

        Assert.Equal(100, Frame(100)._handle.Data.LayoutWidth);
        Assert.Equal(0, p.LayoutStatistics.ArrangedNodes);
    }

    [Fact]
    public void OmittedDeclarationsAndTemplatesInvalidateCachedStyles()
    {
        var p = NewPaper();
        var template = new StyleTemplate().Width(70);
        p.BeginFrame(.01f);
        Box(p, "a").Width(40).Height(10);
        p.EndFrame();
        p.BeginFrame(.01f);
        var a = Box(p, "a").Height(10);
        p.EndFrame();
        Assert.Equal(400, a._handle.Data.LayoutWidth);
        p.BeginFrame(.01f);
        a = Box(p, "a").Style(template).Height(10);
        p.EndFrame();
        Assert.Equal(70, a._handle.Data.LayoutWidth);
        template.Width(90);
        p.BeginFrame(.01f);
        a = Box(p, "a").Style(template).Height(10);
        p.EndFrame();
        Assert.Equal(90, a._handle.Data.LayoutWidth);
        p.BeginFrame(.01f);
        a = Box(p, "a").Width(20).Width(90).Height(10);
        p.EndFrame();
        Assert.Equal(0, p.LayoutStatistics.ArrangedNodes);
    }

    [Fact]
    public void InheritedDimensionsInvalidateWhenParentChanges()
    {
        var p = NewPaper();
        ElementBuilder Frame(float width)
        {
            p.BeginFrame(.01f);
            var source = Box(p, "source").Width(width).Height(10);
            var child = Box(p, "inherited").InheritStyle(source._handle).Height(10);
            p.EndFrame();
            return child;
        }

        Assert.Equal(40, Frame(40)._handle.Data.LayoutWidth);
        Assert.Equal(80, Frame(80)._handle.Data.LayoutWidth);
        Frame(80);
        Assert.Equal(0, p.LayoutStatistics.ArrangedNodes);
    }

    [Fact]
    public void HiddenStateCanBeRestoredAcrossFrames()
    {
        var p = NewPaper();
        ElementBuilder Frame(bool hidden)
        {
            p.BeginFrame(.01f);
            var a = Box(p, "a").Width(40).Height(20).Visible(!hidden);
            p.EndFrame();
            return a;
        }

        Rect(Frame(false), 0, 0, 40, 20);
        Assert.False(Frame(true)._handle.Data.Visible);
        Assert.False(Frame(true)._handle.Data.Visible);
        Rect(Frame(false), 0, 0, 40, 20);
    }

    private static Paper NewPaper() => new(new NullRenderer(), 400, 300, new FontAtlasSettings());
    private static ElementBuilder Box(Paper p, string name) => p.Box(name, lineID: 1);
    private static void Rect(ElementBuilder e, float x, float y, float w, float h)
    {
        ref var d = ref e._handle.Data;
        Assert.Equal(x, d.X, 2);
        Assert.Equal(y, d.Y, 2);
        Assert.Equal(w, d.LayoutWidth, 2);
        Assert.Equal(h, d.LayoutHeight, 2);
    }

    [Fact]
    public void StableFramesAndPaintChangesReuseGeometry()
    {
        var p = NewPaper();
        void Frame(int frame)
        {
            p.BeginFrame(1f / 60);
            Box(p, "a").Size(50).BackgroundColor(frame % 2 == 0 ? Color.Red : Color.Blue);
            p.EndFrame();
        }

        Frame(0);
        Frame(1);
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
        Assert.Equal(0, p.LayoutStatistics.ArrangedNodes);
    }

    [Fact]
    public void ReorderRemovalAndIndexReuseDoNotReuseWrongGeometry()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        Box(p, "a").Size(20);
        Box(p, "b").Size(30);
        p.EndFrame();
        p.BeginFrame(.01f);
        var b = Box(p, "b").Size(30);
        var a = Box(p, "a").Size(20);
        p.EndFrame();
        Rect(b, 0, 0, 30, 30);
        Rect(a, 0, 30, 20, 20);
        p.BeginFrame(.01f);
        var c = Box(p, "c").Size(40);
        p.EndFrame();
        Rect(c, 0, 0, 40, 40);
        p.BeginFrame(.01f);
        a = Box(p, "a").Size(20);
        p.EndFrame();
        Rect(a, 0, 0, 20, 20);
    }

    [Fact]
    public void NativeGridAndOverlayUseScaffoldFeatures()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        ElementBuilder a, b, c, overlayChild;
        using (p.Grid("grid").Columns(2).Width(210).Height(100).Gap(10).LineGap(5).Enter())
        {
            a = Box(p, "a").Height(20);
            b = Box(p, "b").Height(40);
            c = Box(p, "c").Height(30);
        }

        using (p.Overlay("overlay").Size(100).AlignItems(LayoutAlignment.Center).Enter())
            overlayChild = Box(p, "inside").Size(20);
        p.EndFrame();
        Rect(a, 0, 0, 100, 20);
        Rect(b, 110, 0, 100, 40);
        Rect(c, 0, 45, 100, 30);
        Rect(overlayChild, 40, 140, 20, 20);
    }

    /// <summary>Anchoring both edges of an axis stretches the element across it.</summary>
    [Fact]
    public void AnchorsPositionAndStretchSelfDirectedElements()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        var anchored = Box(p, "anchored").PositionType(PositionType.SelfDirected)
            .AnchorLeft(10).AnchorRight(20).AnchorBottom(5).Height(30);
        var percent = Box(p, "percent").Width(UnitValue.Percentage(50) - UnitValue.Pixels(8)).Height(10);
        p.EndFrame();
        Rect(anchored, 10, 265, 370, 30);
        Rect(percent, 0, 0, 192, 10);
    }

    [Fact]
    public void CachedMeasurementsUseRevisionsAndExplicitDirtying()
    {
        var p = NewPaper();
        int calls = 0, id = 0;
        float height = 10;
        ElementBuilder Frame(long revision)
        {
            p.BeginFrame(.01f);
            // A fresh closure every frame: the cached path keys on the revision, not the delegate.
            var b = Box(p, "measure").Width(UnitValue.Auto).Height(UnitValue.Auto)
                .ContentSizer((_, _) => { calls++; return (20, height); }, revision);
            id = b._handle.Data.ID;
            p.EndFrame();
            return b;
        }

        Frame(0);
        int before = calls;
        Frame(0);
        Assert.Equal(before, calls);
        height = 20;
        Rect(Frame(1), 0, 0, 20, 20);
        Assert.True(calls > before);
        height = 30;
        p.MarkLayoutDirty(id);
        Rect(Frame(1), 0, 0, 20, 30);
    }

    [Fact]
    public void LegacySizerRemainsLiveAndFixedSizerIsCached()
    {
        var p = NewPaper();
        float height = 10;
        Func<float?, float?, (float, float)?> sizer = (_, _) => (20, height);
        ElementBuilder Frame()
        {
            p.BeginFrame(.01f);
            var b = Box(p, "legacy").Size(UnitValue.Auto).ContentSizer(sizer);
            p.EndFrame();
            return b;
        }

        Frame();
        height = 20;
        Rect(Frame(), 0, 0, 20, 20);
        void FixedFrame()
        {
            p.BeginFrame(.01f);
            Box(p, "fixed").Size(UnitValue.Auto).ContentSizer(20, 30);
            p.EndFrame();
        }

        FixedFrame();
        FixedFrame();
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
    }

    [Fact]
    public void FlexibleMarginsPreservePaperAlignmentAndParentSpacing()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        ElementBuilder a, b;
        using (p.Row("row").Size(400, 100).PaddingLeft(10).PaddingRight(10).Gap(20).Enter())
        {
            a = Box(p, "a").Size(100, 20).Top(UnitValue.Stretch()).Bottom(UnitValue.Stretch());
            b = Box(p, "b").Width(UnitValue.Stretch()).Height(20);
        }

        p.EndFrame();
        Rect(a, 10, 40, 100, 20);
        Rect(b, 130, 0, 260, 20);
    }

    [Fact]
    public void WrapFillAndLineGapArePreserved()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        ElementBuilder a, b, c;
        using (p.Row("row").Size(250, 100).WrapContent().JustifyContent(LayoutJustification.Fill).Gap(10).LineGap(5).Enter())
        {
            a = Box(p, "a").Size(100, 20);
            b = Box(p, "b").Size(100, 20);
            c = Box(p, "c").Size(100, 20);
        }

        p.EndFrame();
        Rect(a, 0, 0, 120, 20);
        Rect(b, 130, 0, 120, 20);
        Rect(c, 0, 25, 250, 20);
    }


    [Fact]
    public void TextAndFontSizeChangesInvalidateGeometryAcrossFrames()
    {
        using var stream = typeof(LayoutIntegrationTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        var font = new FontFile(stream);
        var p = NewPaper();
        ElementBuilder Frame(string text, float size)
        {
            p.BeginFrame(.01f);
            var b = Box(p, "text").Size(UnitValue.Auto).Text(text, font).FontSize(size);
            p.EndFrame();
            return b;
        }

        float width = Frame("hello", 12)._handle.Data.LayoutWidth;
        Frame("hello", 12);
        Assert.Equal(0, p.LayoutStatistics.MeasuredNodes);
        Assert.True(Frame("hello hello", 12)._handle.Data.LayoutWidth > width);
        Assert.True(Frame("hello", 24)._handle.Data.LayoutWidth > width);
    }

    [Fact]
    public void MoveToRootUpdatesHierarchyAndClampMovesDescendants()
    {
        var p = NewPaper();
        p.BeginFrame(.01f);
        ElementBuilder popup, child;
        using (p.Column("holder").Size(50).Enter())
        using ((popup = Box(p, "popup").Size(80).PositionType(PositionType.SelfDirected).Position(390, 290).ClampToScreen()).Enter())
        {
            p.MoveToRoot();
            child = Box(p, "child").Size(10);
        }

        p.EndFrame();
        Assert.Equal(p.RootElement.Index, popup._handle.Data.ParentIndex);
        Rect(popup, 316, 216, 80, 80);
        Rect(child, 316, 216, 10, 10);
    }

    [Fact]
    public void ResolvedLayoutBridgeAllocatesNothingAfterWarmup()
    {
        var p = NewPaper();
        for (int i = 0; i < 100; i++)
        {
            var h = p.CreateElement(i + 1);
            h.Data.ParentIndex = p.RootElement.Index;
            p.RootElement.Data.ChildIndices.Add(h.Index);
            h.Data._elementStyle.SetDirectValue(GuiProp.Width, UnitValue.Pixels(10));
            h.Data._elementStyle.SetDirectValue(GuiProp.Height, UnitValue.Pixels(10));
        }

        for (int i = 0; i < 100; i++)
        {
            p.ComputeLayout();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            p.ComputeLayout();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, p.LayoutStatistics.ArrangedNodes);
    }

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose()
        {
        }

        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
        }

        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls)
        {
        }
    }
}
