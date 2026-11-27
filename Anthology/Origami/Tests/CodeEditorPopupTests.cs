using Prowl.OrigamiUI;

using Xunit;

namespace Prowl.OrigamiUI.Tests;

public class CodeEditorPopupTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(77)]
    [InlineData(80)]
    [InlineData(81)]
    [InlineData(200)]
    public void WrapTagged_APartNearThePopupWidth_WrapsOnce(int length)
    {
        var lines = new List<CodeEditor.PopupLine>();
        var parts = new[] { new CodeTaggedText("void Name"), new CodeTaggedText(new string('a', length)) };

        CodeEditor.WrapTagged(parts, 80, CodeEditorColors.CreateDark(), lines);

        Assert.Equal(length + 9 > 80 ? 2 : 1, lines.Count);
    }
}
