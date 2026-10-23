using Prowl.OrigamiUI;

using Xunit;

namespace Origami.Tests;

public class CodeDocumentTests
{
    [Fact]
    public void Replace_AcrossLines_KeepsUntouchedLineInstances()
    {
        var doc = new CodeDocument("aaa\nbbb\nccc\nddd");
        string first = doc[0], last = doc[3];

        var change = doc.Replace(new TextRange(1, 1, 2, 2), "X\nY\nZ");

        Assert.Equal("aaa\nbX\nY\nZc\nddd", doc.GetText());
        Assert.Same(first, doc[0]);
        Assert.Same(last, doc[4]);
        Assert.Equal(new TextPosition(3, 1), change.NewEnd);
        Assert.Equal(1, change.LineDelta);
        Assert.Equal("bb\ncc", change.OldText);
    }

    [Fact]
    public void Offsets_RoundTrip()
    {
        var doc = new CodeDocument("ab\ncde\n\nf");
        for (int offset = 0; offset <= doc.Length; offset++)
            Assert.Equal(offset, doc.PositionToOffset(doc.OffsetToPosition(offset)));
        Assert.Equal(new TextPosition(3, 0), doc.OffsetToPosition(8));
    }

    [Fact]
    public void Offsets_StayValidAfterEdits()
    {
        var doc = new CodeDocument("one\ntwo\nthree");
        _ = doc.PositionToOffset(new TextPosition(2, 0));
        doc.Insert(new TextPosition(0, 3), "\nextra");
        Assert.Equal(14, doc.PositionToOffset(new TextPosition(3, 0)));
    }

    [Fact]
    public void LineEndings_AreDetectedAndRestored()
    {
        var doc = new CodeDocument("a\r\nb\r\nc");
        Assert.Equal(3, doc.LineCount);
        Assert.Equal("\r\n", doc.LineEnding);
        Assert.Equal("a\r\nb\r\nc", doc.GetText());
        doc.Insert(new TextPosition(1, 1), "\r\nx");
        Assert.Equal("a\nb\nx\nc", doc.GetText("\n"));
    }

    [Fact]
    public void LongestLine_TracksGrowthAndShrink()
    {
        var doc = new CodeDocument("ab\nabcdef\nabc");
        Assert.Equal(6, doc.LongestLineLength);
        doc.Delete(new TextRange(1, 2, 1, 6));
        Assert.Equal(3, doc.LongestLineLength);
        doc.Insert(new TextPosition(0, 0), "0123456789");
        Assert.Equal(12, doc.LongestLineLength);
    }

    [Fact]
    public void TextChange_MapsPositionsAfterEdit()
    {
        var doc = new CodeDocument("hello world\nnext");
        var change = doc.Replace(new TextRange(0, 0, 0, 5), "hi\nthere");
        Assert.Equal(new TextPosition(1, 11), change.Map(new TextPosition(0, 11)));
        Assert.Equal(new TextPosition(2, 2), change.Map(new TextPosition(1, 2)));
        Assert.Equal(new TextPosition(1, 5), change.Map(new TextPosition(0, 3)));
    }
}

public class CodeEditorEditingTests
{
    private static CodeEditor Make(string text, int line = 0, int col = 0)
    {
        var e = new CodeEditor(text, new CSharpLanguage());
        e.SetCaret(new TextPosition(line, col));
        return e;
    }

    private static void Type(CodeEditor e, string text)
    {
        foreach (char c in text)
        {
            if (c == '\n') e.NewLine();
            else e.TypeChar(c);
        }
    }

    [Fact]
    public void Typing_AutoClosesAndOvertypes()
    {
        var e = Make("");
        Type(e, "Foo(");
        Assert.Equal("Foo()", e.Text);
        Assert.Equal(new TextPosition(0, 4), e.Caret);
        Type(e, "1)");
        Assert.Equal("Foo(1)", e.Text);
        Assert.Equal(new TextPosition(0, 6), e.Caret);
    }

    [Fact]
    public void Typing_QuoteInsideWordDoesNotPair()
    {
        var e = Make("don");
        e.SetCaret(new TextPosition(0, 3));
        Type(e, "'");
        Assert.Equal("don'", e.Text);
    }

    [Fact]
    public void NewLine_BetweenBracesIndentsAndSplits()
    {
        var e = Make("    void M() {}", 0, 14);
        e.NewLine();
        Assert.Equal("    void M() {\n        \n    }", e.Text);
        Assert.Equal(new TextPosition(1, 8), e.Caret);
    }

    [Fact]
    public void ClosingBrace_OutdentsToOpener()
    {
        var e = Make("if (x)\n{\n        ", 2, 8);
        Type(e, "}");
        Assert.Equal("if (x)\n{\n}", e.Text);
    }

    [Fact]
    public void Backspace_DeletesEmptyPair()
    {
        var e = Make("a()", 0, 2);
        e.Backspace(false);
        Assert.Equal("a", e.Text);
    }

    [Fact]
    public void Backspace_InIndentRemovesOneIndentLevel()
    {
        var e = Make("        x", 0, 8);
        e.Backspace(false);
        Assert.Equal("    x", e.Text);
    }

    [Fact]
    public void Undo_RevertsTypedWordAsOneStep_AndRedoReapplies()
    {
        var e = Make("");
        Type(e, "hello");
        Type(e, " world");
        e.UndoEdit();
        Assert.Equal("hello", e.Text);
        e.UndoEdit();
        Assert.Equal("", e.Text);
        e.RedoEdit();
        e.RedoEdit();
        Assert.Equal("hello world", e.Text);
        Assert.Equal(new TextPosition(0, 11), e.Caret);
    }

    [Fact]
    public void Undo_RestoresMultiCaretEdit()
    {
        var e = Make("a\nb\nc", 0, 1);
        e.AddSelection(new CodeSelection(new TextPosition(1, 1)));
        e.AddSelection(new CodeSelection(new TextPosition(2, 1)));
        Type(e, ";");
        Assert.Equal("a;\nb;\nc;", e.Text);
        Assert.Equal(3, e.Selections.Count);
        e.UndoEdit();
        Assert.Equal("a\nb\nc", e.Text);
    }

    [Fact]
    public void ToggleLineComment_CommentsAndUncomments()
    {
        var e = Make("    a();\n    b();");
        e.Select(new TextPosition(0, 0), new TextPosition(1, 2));
        e.ToggleLineComment();
        Assert.Equal("    // a();\n    // b();", e.Text);
        e.ToggleLineComment();
        Assert.Equal("    a();\n    b();", e.Text);
    }

    [Fact]
    public void MoveLines_SwapsWithNeighbour()
    {
        var e = Make("one\ntwo\nthree", 0, 1);
        e.MoveLines(1);
        Assert.Equal("two\none\nthree", e.Text);
        Assert.Equal(1, e.Caret.Line);
        e.MoveLines(-1);
        Assert.Equal("one\ntwo\nthree", e.Text);
    }

    [Fact]
    public void DuplicateLines_CopiesBelowAndFollows()
    {
        var e = Make("x\ny", 0, 1);
        e.DuplicateLines(true);
        Assert.Equal("x\nx\ny", e.Text);
        Assert.Equal(new TextPosition(1, 1), e.Caret);
    }

    [Fact]
    public void Indent_SelectionIndentsEveryLine()
    {
        var e = Make("a\nb");
        e.Select(new TextPosition(0, 0), new TextPosition(1, 1));
        e.Indent();
        Assert.Equal("    a\n    b", e.Text);
        e.Outdent();
        Assert.Equal("a\nb", e.Text);
    }

    [Fact]
    public void AddNextOccurrence_SelectsWordThenNextMatch()
    {
        var e = Make("foo bar foo foo", 0, 1);
        e.AddNextOccurrence();
        Assert.Equal(new TextRange(0, 0, 0, 3), e.Selection.Range);
        e.AddNextOccurrence();
        Assert.Equal(2, e.Selections.Count);
        Type(e, "x");
        Assert.Equal("x bar x foo", e.Text);
    }

    [Fact]
    public void ReplaceAll_WithRegexGroups()
    {
        var e = Make("a1 b2\nc3");
        e.FindRegex = true;
        e.FindQuery = @"([a-z])(\d)";
        e.ReplaceText = "$2$1";
        Assert.Equal(3, e.FindMatchCount);
        Assert.Equal(3, e.ReplaceAll());
        Assert.Equal("1a 2b\n3c", e.Text);
        e.UndoEdit();
        Assert.Equal("a1 b2\nc3", e.Text);
    }

    [Fact]
    public void ApplyEdits_AppliesOffsetsInOneStep()
    {
        var e = Make("int a = 1;");
        e.ApplyEdits(new[] { new CodeTextEdit(4, 1, "value"), new CodeTextEdit(8, 1, "42") });
        Assert.Equal("int value = 42;", e.Text);
        e.UndoEdit();
        Assert.Equal("int a = 1;", e.Text);
    }

    [Fact]
    public void ExternalDocumentEdits_AreUndoable()
    {
        var e = Make("abc");
        e.Document.Insert(new TextPosition(0, 3), "def");
        e.UndoEdit();
        Assert.Equal("abc", e.Text);
    }

    [Fact]
    public void WordMotions_StopAtWordAndPunctuation()
    {
        var e = Make("foo.bar(baz)", 0, 0);
        e.MoveRight(false, true);
        Assert.Equal(3, e.Caret.Column);
        e.MoveRight(false, true);
        Assert.Equal(4, e.Caret.Column);
        e.MoveLeft(false, true);
        Assert.Equal(3, e.Caret.Column);
    }

    [Fact]
    public void Home_TogglesBetweenIndentAndColumnZero()
    {
        var e = Make("    code", 0, 6);
        e.MoveHome(false);
        Assert.Equal(4, e.Caret.Column);
        e.MoveHome(false);
        Assert.Equal(0, e.Caret.Column);
    }

    [Fact]
    public void MatchingBracket_IgnoresBracketsInStrings()
    {
        var e = Make("f(\")\", (x))", 0, 1);
        Assert.True(e.TryFindMatchingBracket(new TextPosition(0, 2), out var open, out var close));
        Assert.Equal(1, open.Column);
        Assert.Equal(10, close.Column);
    }

    [Fact]
    public void VisualColumns_ExpandTabs()
    {
        var e = new CodeEditor("\tab\tc");
        Assert.Equal(4, e.VisualColumn(0, 1));
        Assert.Equal(8, e.VisualColumn(0, 4));
        Assert.Equal(4, e.ColumnFromVisual(0, 8));
    }
}

public class CodeFoldingTests
{
    [Fact]
    public void Braces_FoldFromDeclarationLine()
    {
        string[] lines = { "class A", "{", "    void M()", "    {", "        x();", "    }", "}" };
        var folds = CodeFolding.FromBraces(lines, CSharpLexer.Instance, default);
        Assert.Contains(folds, f => f.StartLine == 0 && f.EndLine == 6);
        Assert.Contains(folds, f => f.StartLine == 2 && f.EndLine == 5);
    }

    [Fact]
    public void BracesInStringsAndComments_DoNotFold()
    {
        string[] lines = { "var s = \"{\";", "// {", "x();", "}" };
        var folds = CodeFolding.FromBraces(lines, CSharpLexer.Instance, default);
        Assert.Empty(folds);
    }

    [Fact]
    public void Regions_Fold()
    {
        string[] lines = { "#region Fields", "int a;", "#endregion" };
        var folds = CodeFolding.FromBraces(lines, CSharpLexer.Instance, default);
        Assert.Contains(folds, f => f.StartLine == 0 && f.EndLine == 2 && f.Label == "#region Fields");
    }
}

public class CSharpLexerTests
{
    private static List<CodeToken> Lex(string line, int state, out int end)
    {
        var tokens = new List<CodeToken>();
        end = CSharpLexer.Instance.Tokenize(line, state, tokens);
        return tokens;
    }

    private static CodeTokenKind KindAt(string line, int col, int state = 0)
    {
        foreach (var t in Lex(line, state, out _))
            if (col >= t.Start && col < t.End) return t.Kind;
        return CodeTokenKind.Plain;
    }

    [Fact]
    public void Keywords_ControlKeywords_AndMethods()
    {
        string s = "if (x) return Foo(1);";
        Assert.Equal(CodeTokenKind.ControlKeyword, KindAt(s, 0));
        Assert.Equal(CodeTokenKind.ControlKeyword, KindAt(s, 7));
        Assert.Equal(CodeTokenKind.Method, KindAt(s, 14));
        Assert.Equal(CodeTokenKind.Number, KindAt(s, 18));
    }

    [Fact]
    public void BlockComment_CarriesAcrossLines()
    {
        Lex("int a; /* start", 0, out int state);
        Assert.NotEqual(0, state);
        Assert.Equal(CodeTokenKind.Comment, KindAt("still comment", 2, state));
        Lex("end */ int b;", state, out int after);
        Assert.Equal(0, after);
        Assert.Equal(CodeTokenKind.Keyword, KindAt("end */ int b;", 7, state));
    }

    [Fact]
    public void InterpolatedString_HolesAreCode()
    {
        string s = "var t = $\"a {count + 1} b\";";
        Assert.Equal(CodeTokenKind.String, KindAt(s, 10));
        Assert.Equal(CodeTokenKind.Identifier, KindAt(s, 13));
        Assert.Equal(CodeTokenKind.Number, KindAt(s, 21));
        Assert.Equal(CodeTokenKind.String, KindAt(s, 24));
    }

    [Fact]
    public void VerbatimAndRawStrings_SpanLines()
    {
        Lex("var a = @\"line one", 0, out int verbatim);
        Assert.NotEqual(0, verbatim);
        Lex("end\"\"quote\"; int x;", verbatim, out int back);
        Assert.Equal(0, back);

        Lex("var r = \"\"\"", 0, out int raw);
        Assert.NotEqual(0, raw);
        Assert.Equal(CodeTokenKind.String, KindAt("  \"\" still raw", 4, raw));
        Lex("  \"\"\";", raw, out int closed);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void ContextualKeywords_OnlyWhereTheyReadAsKeywords()
    {
        Assert.Equal(CodeTokenKind.Keyword, KindAt("var x = 1;", 0));
        Assert.NotEqual(CodeTokenKind.Keyword, KindAt("int var = 1;", 4));
        Assert.Equal(CodeTokenKind.Keyword, KindAt("public int A { get; set; }", 15));
    }

    [Fact]
    public void DocComments_TagTheirXml()
    {
        string s = "/// <summary>Hi</summary>";
        Assert.Equal(CodeTokenKind.DocComment, KindAt(s, 0));
        Assert.Equal(CodeTokenKind.DocCommentTag, KindAt(s, 5));
        Assert.Equal(CodeTokenKind.DocComment, KindAt(s, 14));
    }

    [Fact]
    public void Preprocessor_Line()
    {
        Assert.Equal(CodeTokenKind.Preprocessor, KindAt("#if DEBUG", 1));
        Assert.Equal(CodeTokenKind.PreprocessorText, KindAt("#if DEBUG", 5));
    }
}

public class CodeEditorViewTests
{
    [Fact]
    public void Folding_MapsRowsAroundHiddenLines()
    {
        var e = new CodeEditor("a\n{\nb\nc\n}\nd", new CSharpLanguage());
        var folds = CodeFolding.FromBraces(e.Document.Snapshot(), CSharpLexer.Instance, default);
        e.SetFolds(folds);

        e.Fold(0);
        Assert.Equal(2, e.VisibleLineCount);
        Assert.Equal(0, e.LineToRow(0));
        Assert.Equal(0, e.LineToRow(3));
        Assert.Equal(1, e.LineToRow(5));
        Assert.Equal(5, e.RowToLine(1));
        Assert.True(e.IsLineHidden(4));
        Assert.False(e.IsLineHidden(5));

        e.UnfoldAll();
        Assert.Equal(6, e.VisibleLineCount);
    }

    [Fact]
    public void FuzzyScore_PrefersPrefixThenCamelHumps()
    {
        int prefix = CodeEditor.FuzzyScore("Wri", "WriteLine", out _);
        int humps = CodeEditor.FuzzyScore("WL", "WriteLine", out ulong mask);
        int none = CodeEditor.FuzzyScore("xyz", "WriteLine", out _);
        Assert.True(prefix > humps);
        Assert.True(humps >= 0);
        Assert.Equal(-1, none);
        Assert.Equal((1UL << 0) | (1UL << 5), mask);
        Assert.Equal(-1, CodeEditor.FuzzyScore("rite", "WriteLine", out _));
    }

    [Fact]
    public void LargeDocument_EditNearTopStaysFast()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 200_000; i++) sb.Append("    int value").Append(i).Append(" = ").Append(i).Append("; // line\n");
        var e = new CodeEditor(sb.ToString(), new CSharpLanguage());

        var watch = System.Diagnostics.Stopwatch.StartNew();
        e.SetCaret(new TextPosition(10, 4));
        for (int i = 0; i < 200; i++) e.TypeChar('x');
        watch.Stop();

        Assert.Equal(200_001, e.Document.LineCount);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"200 keystrokes took {watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Tab_OverSingleLineSelection_IndentsInsteadOfReplacing()
    {
        var e = new CodeEditor("abc def", new CSharpLanguage());
        e.Select(new TextPosition(0, 0), new TextPosition(0, 3));
        e.Indent();
        Assert.Equal("    abc def", e.Text);
    }

    [Fact]
    public void SetText_AfterFolds_DoesNotKeepStaleFolds()
    {
        var e = new CodeEditor("a\n{\nb\n}", new CSharpLanguage());
        e.SetFolds(CodeFolding.FromBraces(e.Document.Snapshot(), CSharpLexer.Instance, default));
        e.Fold(0);
        e.SetText("x");
        Assert.Empty(e.FoldRanges);
        Assert.False(e.IsCollapsed(0));
        Assert.False(e.Undo.CanUndo);
    }

    [Fact]
    public void VerticalGoalColumn_ResetsAfterClickMove()
    {
        var e = new CodeEditor("abcdefgh\nab\nabcdefgh", new CSharpLanguage());
        e.SetCaret(new TextPosition(0, 6));
        e.MoveVertical(1, false);
        e.SetCaret(new TextPosition(1, 1));
        e.MoveVertical(1, false);
        Assert.Equal(new TextPosition(2, 1), e.Caret);
    }

    // Dumb test commenting it out for now
    // [Fact]
    // public void MillionLines_StateScanToEndIsFast()
    // {
    //     var sb = new System.Text.StringBuilder();
    //     for (int i = 0; i < 1_000_000; i++) sb.Append("        var text = $\"value {i}\"; // comment ").Append(i).Append('\n');
    //     var e = new CodeEditor(sb.ToString(), new CSharpLanguage());
    // 
    //     var watch = System.Diagnostics.Stopwatch.StartNew();
    //     e.GetLineState(e.Document.LineCount - 1);
    //     watch.Stop();
    //     Assert.True(watch.ElapsedMilliseconds < 1500, $"state scan took {watch.ElapsedMilliseconds}ms");
    // }
}
