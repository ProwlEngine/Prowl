using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Xunit;

namespace Prowl.OrigamiUI.Tests;

public class TooltipTests
{
    private static readonly Rect Screen = new(0, 0, 800, 600);

    [Fact]
    public void Hover_WaitsForGlobalDelayBeforeShowing()
    {
        using var scene = new TooltipScene();
        var content = scene.Content("Delayed");

        Assert.Empty(scene.Frame((1, content, 0)));
        Assert.Empty(scene.Frame((1, content, 0)));
        Assert.Equal(new[] { "Delayed" }, scene.Frame((1, content, 0)));
    }

    [Fact]
    public void Hover_PerTooltipDelayOverridesGlobalDelay()
    {
        using var scene = new TooltipScene();
        var content = scene.Content("Immediate", delay: 0);

        Assert.Equal(new[] { "Immediate" }, scene.Frame((1, content, 0)));
        Assert.Equal(0.25f, TooltipSystem.ShowDelay);
    }

    [Fact]
    public void Hover_SwitchingElementsRestartsDelay()
    {
        using var scene = new TooltipScene();
        var first = scene.Content("First");
        var second = scene.Content("Second");

        Assert.Empty(scene.Frame((1, first, 0)));
        Assert.Empty(scene.Frame((1, first, 0)));
        Assert.Equal(new[] { "First" }, scene.Frame((1, first, 0)));

        Assert.Empty(scene.Frame((2, second, 0)));
        Assert.Empty(scene.Frame((2, second, 0)));
        Assert.Equal(new[] { "Second" }, scene.Frame((2, second, 0)));
    }

    [Fact]
    public void Hover_LeavingHidesTooltipAndRestartsDelayOnReturn()
    {
        using var scene = new TooltipScene();
        var content = scene.Content("Returning");

        scene.Frame((1, content, 0));
        scene.Frame((1, content, 0));
        Assert.Equal(new[] { "Returning" }, scene.Frame((1, content, 0)));
        Assert.Empty(scene.Frame());

        Assert.Empty(scene.Frame((1, content, 0)));
        Assert.Empty(scene.Frame((1, content, 0)));
        Assert.Equal(new[] { "Returning" }, scene.Frame((1, content, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_DeeperClaimWinsWithoutRestartingDelay(bool childFirst)
    {
        using var scene = new TooltipScene();
        var parent = (Id: 1, Content: scene.Content("Parent"), Depth: 1);
        var child = (Id: 2, Content: scene.Content("Child"), Depth: 2);
        var claims = childFirst ? new[] { child, parent } : new[] { parent, child };

        Assert.Empty(scene.Frame(claims));
        Assert.Empty(scene.Frame(claims));
        Assert.Equal(new[] { "Child" }, scene.Frame(claims));
        Assert.Equal(new[] { "Child" }, scene.Frame(claims));
    }

    [Theory]
    [InlineData(TooltipPlacement.Top, 210, 130)]
    [InlineData(TooltipPlacement.Bottom, 210, 290)]
    [InlineData(TooltipPlacement.Left, 70, 210)]
    [InlineData(TooltipPlacement.Right, 350, 210)]
    public void ElementAnchor_CentersOnEachSide(TooltipPlacement side, float x, float y)
    {
        Rect result = TooltipSystem.PositionTooltip(new Rect(200, 200, 340, 280), new Float2(120, 60), Screen, side, true);
        Assert.Equal(new Float2(x, y), result.Min);
    }

    [Theory]
    [InlineData(TooltipPlacement.Top, 300, 0, 260, 48)]
    [InlineData(TooltipPlacement.Bottom, 300, 570, 260, 500)]
    [InlineData(TooltipPlacement.Left, 0, 200, 50, 189)]
    [InlineData(TooltipPlacement.Right, 760, 200, 630, 189)]
    public void WindowEdge_FlipsToOppositeSide(TooltipPlacement side, float x, float y, float expectedX, float expectedY)
    {
        Rect result = TooltipSystem.PositionTooltip(new Rect(x, y, x + 40, y + 38), new Float2(120, 60), Screen, side, true);
        Assert.Equal(new Float2(expectedX, expectedY), result.Min);
    }

    [Fact]
    public void Cursor_DefaultOffsetAndOptInArrowAlignment()
    {
        Rect cursor = new(200, 200, 200, 200);
        Rect plain = TooltipSystem.PositionTooltip(cursor, new Float2(120, 60), Screen, TooltipPlacement.Bottom, false);
        Rect arrow = TooltipSystem.PositionTooltip(cursor, new Float2(120, 60), Screen, TooltipPlacement.Bottom, false, true);
        Assert.Equal(new Float2(214, 218), plain.Min);
        Assert.Equal(new Float2(140, 218), arrow.Min);
    }

    [Fact]
    public void Clamp_RespectsScreenOrigin()
    {
        Rect screen = new(100, 100, 400, 300);
        Rect result = TooltipSystem.PositionTooltip(new Rect(390, 280, 390, 280), new Float2(120, 60), screen, TooltipPlacement.Bottom, false);
        Assert.Equal(new Float2(276, 202), result.Min);
    }

    [Fact]
    public void WrappedTooltip_FirstFrameUsesMeasuredHeightAndMovesChildren()
    {
        using var stream = typeof(TooltipTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        var font = new FontFile(stream);
        var theme = OrigamiTheme.CreateDefaults();
        theme.Font = font;
        using var pushed = Origami.PushTheme(theme);
        var paper = new Paper(new NullRenderer(), 400, 300, new FontAtlasSettings());
        paper.PointerPos = new Float2(200, 280);
        var content = new TooltipContent("A long tooltip that wraps across several lines and must flip above the cursor.")
        {
            MaxWidth = 120,
            Options = new TooltipOptions { Delay = 0 }
        };
        ElementHandle root = default;
        content.CustomDraw = p => root = p.CurrentParent;
        paper.BeginFrame(0.1f);
        TooltipSystem.Hover(123, content);
        TooltipSystem.Draw(paper);
        paper.EndFrame();

        Assert.True(root.IsValid);
        Rect bubble = root.Data.LayoutRect;
        Assert.True(bubble.Size.Y > 40);
        Assert.Equal(262f, bubble.Max.Y);
        Assert.InRange(bubble.Min.Y, 4, 262);
        var label = new ElementHandle(paper, root.Data.ChildIndices[0]);
        Assert.InRange(label.Data.Y, bubble.Min.Y, bubble.Max.Y);
        Assert.InRange(label.Data.X, bubble.Min.X, bubble.Max.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElementAnchor_UsesCurrentLayoutOrFallsBackWhenRemoved(bool removeAnchor)
    {
        using var stream = typeof(TooltipTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
        var theme = OrigamiTheme.CreateDefaults();
        theme.Font = new FontFile(stream);
        using var pushed = Origami.PushTheme(theme);
        var paper = new Paper(new NullRenderer(), 800, 600, new FontAtlasSettings());
        paper.PointerPos = new Float2(200, 150);
        ElementHandle bubble = default;
        var content = new TooltipContent("Moving anchor")
        {
            Options = new TooltipOptions { Anchor = TooltipAnchor.Element, Delay = 0 },
            CustomDraw = p => bubble = p.CurrentParent
        };

        // Drain any hover claim left by another test before starting the real hover sequence.
        paper.BeginFrame(0.1f);
        TooltipSystem.Draw(paper);
        paper.EndFrame();

        try
        {
            paper.BeginFrame(0.1f);
            paper.Box("moving_anchor", lineID: 1).PositionType(PositionType.SelfDirected)
                .Position(100, 100).Width(200).Height(100).Tooltip(content);
            TooltipSystem.Draw(paper);
            paper.EndFrame();

            paper.BeginFrame(0.1f);
            // Reuse the old anchor's storage slot to catch lookups by a stale handle index.
            paper.Box("unrelated").PositionType(PositionType.SelfDirected)
                .Position(500, 400).Width(50).Height(50);
            ElementHandle anchor = default;
            if (!removeAnchor)
                anchor = paper.Box("moving_anchor", lineID: 1).PositionType(PositionType.SelfDirected)
                    .Position(150, 120).Width(240).Height(120).Tooltip(content)._handle;
            TooltipSystem.Draw(paper);
            paper.EndFrame();

            Assert.True(bubble.IsValid);
            Rect actual = bubble.Data.LayoutRect;
            if (removeAnchor)
            {
                Assert.Equal(new Float2(214, 168), actual.Min);
            }
            else
            {
                Assert.Equal(270f - actual.Size.X / 2, actual.Min.X, 3);
                Assert.Equal(anchor.Data.LayoutRect.Max.Y + 10, actual.Min.Y);
            }
        }
        finally
        {
            // The real hover callback can leave another claim at EndFrame.
            paper.BeginFrame(0.1f);
            TooltipSystem.Draw(paper);
            paper.EndFrame();
        }
    }

    private sealed class TooltipScene : IDisposable
    {
        private readonly IDisposable _themeScope;
        private readonly float _previousDelay = TooltipSystem.ShowDelay;
        private readonly List<string> _drawn = new();
        private readonly Paper _paper;

        public TooltipScene()
        {
            using var stream = typeof(TooltipTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
            var theme = OrigamiTheme.CreateDefaults();
            theme.Font = new FontFile(stream);
            _themeScope = Origami.PushTheme(theme);
            _paper = new Paper(new NullRenderer(), 800, 600, new FontAtlasSettings());
            _paper.PointerPos = new Float2(200, 200);
            TooltipSystem.ShowDelay = 0.25f;

            // Consume any prior claim, then reset the continuous-hover timer.
            Frame();
            Frame();
        }

        public TooltipContent Content(string text, float? delay = null) => new(text)
        {
            Options = delay.HasValue ? new TooltipOptions { Delay = delay } : null,
            CustomDraw = _ => _drawn.Add(text)
        };

        public string[] Frame(params (int Id, TooltipContent Content, int Depth)[] claims)
        {
            _drawn.Clear();
            // An exact binary fraction keeps delay-boundary assertions free of rounding error.
            _paper.BeginFrame(0.125f);
            foreach (var claim in claims)
                TooltipSystem.Hover(claim.Id, claim.Content, claim.Depth);
            TooltipSystem.Draw(_paper);
            _paper.EndFrame();
            return _drawn.ToArray();
        }

        public void Dispose()
        {
            try
            {
                Frame();
            }
            finally
            {
                TooltipSystem.ShowDelay = _previousDelay;
                _themeScope.Dispose();
            }
        }
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
