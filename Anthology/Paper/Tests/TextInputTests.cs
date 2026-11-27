// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Tests;

/// <summary>Keyboard editing in a focused text field, driven through Paper's own input.</summary>
public class TextInputTests
{
    private static readonly FontFile Font = LoadFont();

    private static FontFile LoadFont()
    {
        using var stream = typeof(TextInputTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
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

    private sealed class Clipboard : IClipboardHandler
    {
        public string Text = "";
        public string GetClipboardText() => Text;
        public void SetClipboardText(string text) => Text = text;
    }

    /// <summary>A focused field with its cursor at the end of the text.</summary>
    private sealed class Field
    {
        public readonly Paper Paper = new(new NullRenderer(), 800, 600, new FontAtlasSettings());
        public readonly Clipboard Clipboard = new();
        public string Value;
        private readonly bool _multiLine, _readOnly;
        private bool _focused;

        public Field(string value, bool multiLine = false, bool readOnly = false)
        {
            Value = value;
            _multiLine = multiLine;
            _readOnly = readOnly;
            Paper.SetClipboardHandler(Clipboard);
            Frame();
            Frame();
        }

        public void Frame()
        {
            Paper.BeginFrame(1f / 60f);
            var settings = ElementBuilder.TextInputSettings.Default;
            settings.Font = Font;
            settings.ReadOnly = _readOnly;
            var box = Paper.Box("field").Width(300).Height(_multiLine ? 200 : 30).FontSize(16);
            var field = _multiLine ? box.TextArea(Value, settings, v => Value = v) : box.TextField(Value, settings, v => Value = v);
            if (!_focused)
            {
                Paper.SetFocus(field._handle);
                _focused = true;
            }
            Paper.EndFrame();
        }

        public void Key(PaperKey key, params PaperKey[] held)
        {
            foreach (var h in held) Paper.SetKeyState(h, true);
            Paper.SetKeyState(key, true);
            Frame();
            Paper.SetKeyState(key, false);
            foreach (var h in held) Paper.SetKeyState(h, false);
            Frame();
        }

        public void Type(string text)
        {
            Paper.AddInputCharacter(text);
            Frame();
        }
    }

    [Fact]
    public void Typing_adds_text_at_the_cursor()
    {
        var field = new Field("abc");
        field.Type("d");
        Assert.Equal("abcd", field.Value);
    }

    [Fact]
    public void Ctrl_backspace_deletes_the_word_before_the_cursor()
    {
        var field = new Field("hello world");
        field.Key(PaperKey.Backspace, PaperKey.LeftControl);
        Assert.Equal("hello ", field.Value);
    }

    [Fact]
    public void A_read_only_field_keeps_its_text()
    {
        var field = new Field("abc", readOnly: true);
        field.Clipboard.Text = "x";

        field.Key(PaperKey.Backspace);
        field.Key(PaperKey.Home);
        field.Key(PaperKey.Delete);
        field.Key(PaperKey.V, PaperKey.LeftControl);
        field.Key(PaperKey.A, PaperKey.LeftControl);
        field.Key(PaperKey.X, PaperKey.LeftControl);

        Assert.Equal("abc", field.Value);
    }

    [Fact]
    public void Home_and_end_stay_on_the_cursors_line()
    {
        var field = new Field("ab\ncd", multiLine: true);
        field.Key(PaperKey.Home);
        field.Type("X");
        Assert.Equal("ab\nXcd", field.Value);

        field.Key(PaperKey.Up);
        field.Key(PaperKey.End);
        field.Type("Y");
        Assert.Equal("abY\nXcd", field.Value);
    }

    [Fact]
    public void Ctrl_home_and_end_reach_the_ends_of_the_text()
    {
        var field = new Field("ab\ncd", multiLine: true);
        field.Key(PaperKey.Home, PaperKey.LeftControl);
        field.Type("X");
        field.Key(PaperKey.End, PaperKey.LeftControl);
        field.Type("Y");
        Assert.Equal("Xab\ncdY", field.Value);
    }

    [Fact]
    public void Moving_through_a_short_line_keeps_the_column()
    {
        var field = new Field("abcdef\nx\nabcdef", multiLine: true);
        field.Key(PaperKey.Up);
        field.Key(PaperKey.Up);
        field.Type("X");
        Assert.Equal("abcdefX\nx\nabcdef", field.Value);
    }

    [Fact]
    public void Up_and_down_follow_wrapped_lines()
    {
        string text = string.Join(" ", Enumerable.Repeat("word", 30));
        var field = new Field(text, multiLine: true);
        field.Key(PaperKey.Up);
        field.Type("X");

        int at = field.Value.IndexOf('X');
        Assert.InRange(at, 1, text.Length - 1);
    }

    [Fact]
    public void Pasting_windows_line_ends_leaves_no_carriage_returns()
    {
        var area = new Field("", multiLine: true);
        area.Clipboard.Text = "a\r\nb";
        area.Key(PaperKey.V, PaperKey.LeftControl);
        Assert.Equal("a\nb", area.Value);

        var line = new Field("");
        line.Clipboard.Text = "a\r\nb";
        line.Key(PaperKey.V, PaperKey.LeftControl);
        Assert.Equal("a b", line.Value);
    }

    [Fact]
    public void A_surrogate_pair_is_deleted_and_stepped_over_whole()
    {
        var field = new Field("a😀");
        field.Key(PaperKey.Backspace);
        Assert.Equal("a", field.Value);

        field = new Field("😀b");
        field.Key(PaperKey.Home);
        field.Key(PaperKey.Right);
        field.Type("X");
        Assert.Equal("😀Xb", field.Value);

        field.Key(PaperKey.Left);
        field.Key(PaperKey.Left);
        field.Key(PaperKey.Delete);
        Assert.Equal("Xb", field.Value);
    }

    [Fact]
    public void Down_reaches_the_last_wrapped_line_from_the_left_edge()
    {
        string text = string.Join(" ", Enumerable.Repeat("word", 30));
        var field = new Field(text, multiLine: true);
        field.Key(PaperKey.Home, PaperKey.LeftControl);
        for (int i = 0; i < 10; i++) field.Key(PaperKey.Down);
        field.Type("X");

        Assert.InRange(field.Value.IndexOf('X'), text.Length - 50, text.Length);
    }
}
