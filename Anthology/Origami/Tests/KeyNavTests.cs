using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Xunit;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI.Tests;

/// <summary>Arrow key navigation in the widgets that list items.</summary>
public class KeyNavTests
{
    [Fact]
    public void Table_ArrowKeysMoveTheSelectionAndEnterActivates()
    {
        using var scene = new Scene();
        int selected = -1, activated = -1;
        scene.Draw = p =>
        {
            var table = Origami.Table(p, "tbl", selected, i => selected = i)
                .Bordered(false).Column("Name").OnRowActivate(i => activated = i);
            for (int i = 0; i < 5; i++) table.Row().Cell($"row {i}", Color.White);
            table.Show();
        };
        scene.Frame();

        scene.Key(PaperKey.Down);
        Assert.Equal(-1, selected);

        scene.Click(new Float2(100, 27 + 1 * (scene.RowHeight + 1) + scene.RowHeight / 2));
        Assert.Equal(1, selected);

        scene.Key(PaperKey.Down);
        Assert.Equal(2, selected);
        scene.Key(PaperKey.Up);
        scene.Key(PaperKey.Up);
        scene.Key(PaperKey.Up);
        Assert.Equal(0, selected);
        scene.Key(PaperKey.End);
        Assert.Equal(4, selected);

        scene.Key(PaperKey.Enter);
        Assert.Equal(4, activated);
    }

    [Fact]
    public void Tabs_LeftAndRightSwitchTabsOnceTheStripIsClicked()
    {
        using var scene = new Scene();
        int tab = 1;
        scene.Draw = p => Origami.Tabs(p, "tabs", tab, i => tab = i).Tab("One").Tab("Two").Tab("Three").Show();
        scene.Frame();

        scene.Key(PaperKey.Right);
        Assert.Equal(1, tab);

        scene.Click(new Float2(15, 10));
        Assert.Equal(0, tab);

        scene.Key(PaperKey.Right);
        Assert.Equal(1, tab);
        scene.Key(PaperKey.End);
        Assert.Equal(2, tab);
        scene.Key(PaperKey.Right);
        Assert.Equal(2, tab);
        scene.Key(PaperKey.Left);
        Assert.Equal(1, tab);
    }

    [Fact]
    public void RadioGroup_ArrowKeysSkipDisabledItems()
    {
        using var scene = new Scene();
        string[] items = ["a", "b", "c"];
        string value = "c";
        scene.Draw = p => Origami.RadioGroup(p, "radio", value, v => value = v, items).IsItemEnabled(i => i != "b").Show();
        scene.Frame();

        scene.Click(new Float2(9, 9));
        Assert.Equal("a", value);

        scene.Key(PaperKey.Down);
        Assert.Equal("c", value);
        scene.Key(PaperKey.Down);
        Assert.Equal("c", value);
        scene.Key(PaperKey.Up);
        Assert.Equal("a", value);
    }

    [Fact]
    public void Menu_KeysWalkItemsOpenSubmenusAndPick()
    {
        using var scene = new Scene();
        string picked = "";
        int closed = 0;
        scene.Draw = p => ContextMenu.Panel(p, "keymenu", 10, 10, 5, () => closed++, b => b
            .Item("A", () => picked = "A")
            .Item("Off", () => picked = "Off", enabled: false)
            .Submenu("Sub", s => s.Item("X", () => picked = "X").Item("Y", () => picked = "Y"))
            .Item("C", () => picked = "C"));
        scene.Frame();

        scene.Key(PaperKey.Down);
        scene.Key(PaperKey.Down);
        scene.Key(PaperKey.Right);
        scene.Key(PaperKey.Down);
        scene.Key(PaperKey.Enter);
        Assert.Equal("Y", picked);
        Assert.Equal(1, closed);

        scene.Key(PaperKey.Left);
        scene.Key(PaperKey.Down);
        scene.Key(PaperKey.Enter);
        Assert.Equal("C", picked);

        scene.Key(PaperKey.Up);
        scene.Key(PaperKey.Up);
        scene.Key(PaperKey.Enter);
        Assert.Equal("A", picked);
    }

    private sealed class Scene : IDisposable
    {
        private readonly IDisposable _themeScope;
        private readonly Paper _paper;

        public Action<Paper> Draw = _ => { };
        public float RowHeight { get; }

        public Scene()
        {
            using var stream = typeof(KeyNavTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
            var theme = OrigamiTheme.CreateDefaults();
            theme.Font = new FontFile(stream);
            _themeScope = Origami.PushTheme(theme);
            RowHeight = theme.Metrics.RowHeight;
            _paper = new Paper(new NullRenderer(), 800, 600, new FontAtlasSettings());
            _paper.PointerPos = new Float2(-100, -100);
        }

        public void Frame()
        {
            _paper.BeginFrame(0.125f);
            Draw(_paper);
            _paper.EndFrame();
        }

        public void Click(Float2 pos)
        {
            _paper.PointerPos = pos;
            Frame();
            _paper.SetPointerState(PaperMouseBtn.Left, (int)pos.X, (int)pos.Y, true, false);
            Frame();
            _paper.SetPointerState(PaperMouseBtn.Left, (int)pos.X, (int)pos.Y, false, false);
            Frame();
            Frame();
        }

        public void Key(PaperKey key)
        {
            _paper.SetKeyState(key, true);
            Frame();
            _paper.SetKeyState(key, false);
            Frame();
        }

        public void Dispose() => _themeScope.Dispose();
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
