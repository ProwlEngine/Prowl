using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Xunit;

namespace Prowl.OrigamiUI.Tests;

public class TextFieldTests
{
    [Fact]
    public void AutoFocusWithSelectAll_TypingReplacesTheText()
    {
        using var scene = new FieldScene("Old", commit: false);
        scene.Frame();
        scene.Type("New");

        Assert.Equal("New", scene.Value);
    }

    [Fact]
    public void CommitOnEnter_AppliesOnceOnEnter()
    {
        using var scene = new FieldScene("Old", commit: true);
        scene.Frame();
        scene.Type("New");
        Assert.Equal("Old", scene.Value);
        Assert.Equal(0, scene.Sets);

        scene.Press(PaperKey.Enter);

        Assert.Equal("New", scene.Value);
        Assert.Equal(1, scene.Sets);
    }

    [Fact]
    public void CommitOnEnter_EscapeDropsTheEdit()
    {
        using var scene = new FieldScene("Old", commit: true);
        scene.Frame();
        scene.Type("New");

        scene.Press(PaperKey.Escape);
        scene.Frame();
        scene.Frame();

        Assert.Equal("Old", scene.Value);
        Assert.Equal(0, scene.Sets);
    }

    [Fact]
    public void CommitOnEnter_AppliesWhenTheFieldStopsBeingDrawn()
    {
        using var scene = new FieldScene("Old", commit: true);
        scene.Frame();
        scene.Type("New");

        scene.Visible = false;
        scene.Frame();
        scene.Frame();

        Assert.Equal("New", scene.Value);
        Assert.Equal(1, scene.Sets);
    }

    [Fact]
    public void CommitOnEnter_UnchangedTextAppliesNothing()
    {
        using var scene = new FieldScene("Old", commit: true);
        scene.Frame();
        scene.Press(PaperKey.Enter);

        Assert.Equal(0, scene.Sets);
    }

    private sealed class FieldScene : IDisposable
    {
        private readonly IDisposable _themeScope;
        private readonly Paper _paper;
        private readonly bool _commit;

        public string Value;
        public int Sets;
        public bool Visible = true;

        public FieldScene(string value, bool commit)
        {
            using var stream = typeof(TextFieldTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
            var theme = OrigamiTheme.CreateDefaults();
            theme.Font = new FontFile(stream);
            _themeScope = Origami.PushTheme(theme);
            _paper = new Paper(new NullRenderer(), 800, 600, new FontAtlasSettings());
            _paper.PointerPos = new Float2(-100, -100);
            Value = value;
            _commit = commit;
        }

        public void Frame()
        {
            _paper.BeginFrame(0.016f);
            if (Visible)
            {
                var field = Origami.TextField(_paper, "field", Value, v => { Value = v; Sets++; })
                    .AutoFocus().SelectAllOnFocus().Width(200);
                if (_commit) field.CommitOnEnter();
                field.Show();
            }
            TextFieldCommits.EndFrame();
            _paper.EndFrame();
        }

        public void Type(string text)
        {
            _paper.AddInputCharacter(text);
            Frame();
            Frame();
        }

        public void Press(PaperKey key)
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
