using Prowl.Scribe;

namespace Tests
{
    public class MarkdownParserTests
    {
        private static Document Doc(string s) => Markdown.Parse(s);
        private static Block B(Document d, int i) => d.Blocks[i];
        private static Paragraph P(Document d, int i) => B(d, i).Paragraph;
        private static Heading H(Document d, int i) => B(d, i).Heading;
        private static ListBlock L(Document d, int i) => B(d, i).List;
        private static CodeBlock Cb(Document d, int i) => B(d, i).CodeBlock;
        private static BlockQuote Q(Document d, int i) => B(d, i).BlockQuote;
        private static Table T(Document d, int i) => B(d, i).Table;
        private static Anchor A(Document d, int i) => B(d, i).Anchor;


        private static string Plain(List<Inline> xs)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var x in xs)
            {
                switch (x.Kind)
                {
                    case InlineKind.Text: sb.Append(x.Text); break;
                    case InlineKind.Code: sb.Append(x.Text); break;
                    case InlineKind.Span: sb.Append(Plain(x.Children)); break;
                    case InlineKind.Link: sb.Append(Plain(x.Children)); break;
                    case InlineKind.Image: sb.Append(x.Text); break; // alt text
                    case InlineKind.LineBreak: sb.Append("\n"); break;
                }
            }
            return sb.ToString();
        }


        private static string Plain(Inline x) => Plain(new List<Inline> { x });


        [Fact]
        public void Heading_Basic()
        {
            var d = Doc("# Hello\n## World\n");
            Assert.Equal(2, d.Blocks.Count);
            Assert.Equal(BlockKind.Heading, d.Blocks[0].Kind);
            Assert.Equal(1, H(d, 0).Level);
            Assert.Equal("Hello", Plain(H(d, 0).Inlines));
            Assert.Equal(2, H(d, 1).Level);
            Assert.Equal("World", Plain(H(d, 1).Inlines));
        }

        [Fact]
        public void Underscore_Within_Word_Is_Literal()
        {
            var d = Doc("foo_bar_baz");
            var inl = P(d, 0).Inlines;
            Assert.DoesNotContain(inl, x => x.Kind == InlineKind.Span);
            Assert.Equal("foo_bar_baz", Plain(inl));
        }


        [Fact]
        public void Paragraph_And_LineBreaks()
        {
            var d = Doc("Hello\nWorld\n\nNext");
            Assert.Equal(BlockKind.Paragraph, d.Blocks[0].Kind);
            var inl = P(d, 0).Inlines;
            Assert.Equal("Hello\nWorld", Plain(inl)); // our parser emits hard break on any newline
            Assert.Equal(BlockKind.Paragraph, d.Blocks[1].Kind);
            Assert.Equal("Next", Plain(P(d, 1).Inlines));
        }


        [Fact]
        public void BlockQuote_Basic()
        {
            var d = Doc("> quote line 1\n> line 2\n\npara");
            Assert.Equal(BlockKind.BlockQuote, d.Blocks[0].Kind);
            Assert.Equal("quote line 1\nline 2", Plain(Q(d, 0).Inlines));
            Assert.Equal(BlockKind.Paragraph, d.Blocks[1].Kind);
        }


        [Fact]
        public void CodeBlock_WithLanguage()
        {
            var md = "```csharp\nConsole.WriteLine(\"hi\");\n```\n";
            var d = Doc(md);
            Assert.Single(d.Blocks);
            Assert.Equal(BlockKind.CodeBlock, d.Blocks[0].Kind);
            Assert.Equal("csharp", Cb(d, 0).Language);
            Assert.Equal("Console.WriteLine(\"hi\");\n", Cb(d, 0).Code);
        }

        [Fact]
        public void Autolink_Excludes_Trailing_Punctuation()
        {
            var d = Doc("Visit http://example.com.\n");
            var p = P(d, 0).Inlines;
            var link = p.First(x => x.Kind == InlineKind.Link);
            Assert.Equal("http://example.com", link.Href);
            Assert.Equal("http://example.com", Plain(link.Children));
            Assert.Equal(".", Plain(p.Last()));
        }

        [Fact]
        public void List_Items_Allow_Empty()
        {
            var d = Doc("- \n- b\n");
            Assert.Single(d.Blocks);
            var list = L(d, 0);
            Assert.Equal(2, list.Items.Count);
            Assert.Equal(string.Empty, Plain(list.Items[0].Lead));
            Assert.Equal("b", Plain(list.Items[1].Lead));
        }

        [Fact]
        public void List_Items_With_Tab_After_Marker()
        {
            var d = Doc("-\tfirst\n-\tsecond\n");
            Assert.Single(d.Blocks);
            var list = L(d, 0);
            Assert.Equal(new[] { "first", "second" }, list.Items.Select(i => Plain(i.Lead)).ToArray());
        }

        [Fact]
        public void Escaped_Style_Markers_Are_Literal()
        {
            var md = @"\*not italic\* and \*\*not bold\*\*";
            var d = Doc(md);
            var inl = P(d, 0).Inlines;
            // no span elements should be created from escaped markers
            Assert.DoesNotContain(inl, x => x.Kind == InlineKind.Span);
            Assert.Equal("*not italic* and **not bold**", Plain(inl));
        }

        [Fact]
        public void UnorderedList_Asterisk()
        {
            var d = Doc("* alpha\n* beta\n");
            Assert.Single(d.Blocks);
            var list = L(d, 0);
            Assert.False(list.Ordered);
            Assert.Equal(2, list.Items.Count);
            Assert.Equal("alpha", Plain(list.Items[0].Lead));
            Assert.Equal("beta", Plain(list.Items[1].Lead));
        }

        [Fact]
        public void HorizontalRule_Variants()
        {
            var d = Doc("Hello\n\n---\n\n===\n");
            Assert.Equal(BlockKind.Paragraph, d.Blocks[0].Kind);
            Assert.Equal(BlockKind.HorizontalRule, d.Blocks[1].Kind);
            Assert.Equal(BlockKind.HorizontalRule, d.Blocks[2].Kind);
        }


        [Fact]
        public void Anchor_Block()
        {
            var d = Doc("#[top]\nText");
            Assert.Equal(BlockKind.Anchor, d.Blocks[0].Kind);
            Assert.Equal("top", A(d, 0).Name);
            Assert.Equal(BlockKind.Paragraph, d.Blocks[1].Kind);
        }


        [Fact]
        public void UnorderedList_Simple()
        {
            var d = Doc("- a\n- b\n- c\n");
            Assert.Single(d.Blocks);
            Assert.Equal(BlockKind.List, d.Blocks[0].Kind);
            var list = L(d, 0);
            Assert.False(list.Ordered);
            Assert.Equal(3, list.Items.Count);
            Assert.Equal("a", Plain(list.Items[0].Lead));
            Assert.Equal("b", Plain(list.Items[1].Lead));
            Assert.Equal("c", Plain(list.Items[2].Lead));
        }


        [Fact]
        public void OrderedList_Simple()
        {
            var d = Doc("1. one\n2. two\n3. three\n");
            var list = L(d, 0);
            Assert.True(list.Ordered);
            Assert.Equal(new[] { "one", "two", "three" }, list.Items.Select(x => Plain(x.Lead)).ToArray());
        }

        [Fact]
        public void List_Items_With_Continuation_Lines()
        {
            var md = "- first line\ncontinued line\n- second\n";
            var d = Doc(md);
            Assert.Single(d.Blocks);
            var list = L(d, 0);
            Assert.Equal(2, list.Items.Count);
            Assert.Equal("first line", Plain(list.Items[0].Lead));
            Assert.Single(list.Items[0].Children);
            Assert.Equal(BlockKind.Paragraph, list.Items[0].Children[0].Kind);
            Assert.Equal("continued line", Plain(list.Items[0].Children[0].Paragraph.Inlines));
            Assert.Equal("second", Plain(list.Items[1].Lead));
            Assert.Empty(list.Items[1].Children);
        }

        [Fact]
        public void List_Nested_ByIndent()
        {
            var d = Doc("- a\n - b\n - c\n- d\n");
            var list = L(d, 0);
            Assert.Equal(2, list.Items.Count);
            Assert.Equal("a", Plain(list.Items[0].Lead));
            Assert.Single(list.Items[0].Children);
            Assert.Equal(BlockKind.List, list.Items[0].Children[0].Kind);
            var sub = list.Items[0].Children[0].List;
            Assert.Equal(new[] { "b", "c" }, sub.Items.Select(i => Plain(i.Lead)).ToArray());
            Assert.Equal("d", Plain(list.Items[1].Lead));
        }


        [Fact]
        public void Table_With_Header_And_Alignment()
        {
            var md = "| h1 | h2 | h3 |\n| :--- | :--: | ---: |\n| a | b | c |\n| d | e | f |\n";
            var d = Doc(md);
            Assert.Single(d.Blocks);
            Assert.Equal(BlockKind.Table, d.Blocks[0].Kind);
            var t = T(d, 0);
            // rows: header + two body rows (underline excluded)
            Assert.Equal(3, t.Rows.Count);
            // header row
            var hdr = t.Rows[0].Cells;
            Assert.All(hdr, c => Assert.True(c.Header));
            Assert.Equal(new[] { "h1", "h2", "h3" }, hdr.Select(c => Plain(c.Inlines)).ToArray());
            Assert.Equal(new[] { TableAlign.Left, TableAlign.Center, TableAlign.Right }, hdr.Select(c => c.Align).ToArray());
            // first body row
            var r1 = t.Rows[1].Cells.Select(c => Plain(c.Inlines)).ToArray();
            Assert.Equal(new[] { "a", "b", "c" }, r1);
        }

        [Fact]
        public void Table_Single_Dash_Alignment()
        {
            var md = "| h1 | h2 | h3 |\n| :- | :-: | -: |\n| a | b | c |\n";
            var d = Doc(md);
            var t = T(d, 0);
            var hdr = t.Rows[0].Cells;
            Assert.Equal(new[] { TableAlign.Left, TableAlign.Center, TableAlign.Right }, hdr.Select(c => c.Align).ToArray());
        }

        [Fact]
        public void Links_Images_Autolinks()
        {
            var md = "A [site](http://a.com \"T\") and ![alt](img.png) plus http://x.com/test\n";
            var d = Doc(md);
            var p = P(d, 0).Inlines;
            // Find link
            var link = p.FirstOrDefault(x => x.Kind == InlineKind.Link);
            Assert.Equal("http://a.com", link.Href);
            Assert.Equal("T", link.Title);
            Assert.Equal("site", Plain(link.Children));
            // Find image
            var img = p.FirstOrDefault(x => x.Kind == InlineKind.Image);
            Assert.Equal("img.png", img.Href);
            Assert.Equal("alt", img.Text);
            // Autolink
            var auto = p.Last(x => x.Kind == InlineKind.Link);
            Assert.Equal("http://x.com/test", auto.Href);
            Assert.Equal("http://x.com/test", Plain(auto.Children));
        }


        [Fact]
        public void Autolink_Stops_Before_Closing_Paren()
        {
            var d = Doc("(http://x.com/q) end\n");
            var p = P(d, 0).Inlines;
            var link = p.First(x => x.Kind == InlineKind.Link);
            Assert.Equal("http://x.com/q", link.Href);
        }


        [Fact]
        public void Inline_Code_And_Emphasis_Variants()
        {
            var md = "Use `a*b` and *em* **strong** ***both*** ~under~ ~~strike~~ ~~~del~~~\n";
            var d = Doc(md);
            var inl = P(d, 0).Inlines;
            Assert.Contains(inl, x => x.Kind == InlineKind.Code && x.Text == "a*b");
            Inline spanEm = inl.First(x => x.Kind == InlineKind.Span && x.Style.HasFlag(InlineStyle.Emphasis) && !x.Style.HasFlag(InlineStyle.Strong)); // line 211, error here
            Inline spanStr = inl.First(x => x.Kind == InlineKind.Span && x.Style.HasFlag(InlineStyle.Strong) && !x.Style.HasFlag(InlineStyle.Emphasis));
            Inline spanBoth = inl.First(x => x.Kind == InlineKind.Span && x.Style.HasFlag(InlineStyle.Strong) && x.Style.HasFlag(InlineStyle.Emphasis));
            Assert.Equal("em", Plain(spanEm.Children));
            Assert.Equal("strong", Plain(spanStr.Children));
            Assert.Equal("both", Plain(spanBoth.Children));


            // ~ underline, ~~ strike, ~~~ delete
            var u = inl.First(x => x.Kind == InlineKind.Span && x.Style.HasFlag(InlineStyle.Underline));
            var s = inl.First(x => x.Kind == InlineKind.Span && x.Style.HasFlag(InlineStyle.Strike));
            var dlt = inl.First(x => x.Kind == InlineKind.Span && x.Style.HasFlag(InlineStyle.Overline));
            Assert.Equal("under", Plain(u.Children));
            Assert.Equal("strike", Plain(s.Children));
            Assert.Equal("del", Plain(dlt.Children));
        }


        [Fact]
        public void Paragraph_Stops_Before_Block()
        {
            var md = "Para line 1\nPara line 2\n\n## Heading\nNext";
            var d = Doc(md);
            Assert.Equal(BlockKind.Paragraph, d.Blocks[0].Kind);
            Assert.Equal(BlockKind.Heading, d.Blocks[1].Kind);
            Assert.Equal(BlockKind.Paragraph, d.Blocks[2].Kind);
        }


        [Fact]
        public void Mixed_Content_EndToEnd()
        {
            var md = @"#[top]
# Title
Some *text* with a [link](https://example.com) and ![img](a.png)


- item 1
more on item 1
- sub a
- item 2


| H1 | H2 |
| --- | --- |
| A | B |


---


```js
console.log('ok');
```";
            var d = Doc(md);
            Assert.True(d.Blocks.Count >= 7);
            Assert.Equal(BlockKind.Anchor, d.Blocks[0].Kind);
            Assert.Equal(BlockKind.Heading, d.Blocks[1].Kind);
            Assert.Equal(BlockKind.Paragraph, d.Blocks[2].Kind);
            Assert.Equal(BlockKind.List, d.Blocks[3].Kind);
            Assert.Equal(BlockKind.Table, d.Blocks[4].Kind);
            Assert.Equal(BlockKind.HorizontalRule, d.Blocks[5].Kind);
            Assert.Equal(BlockKind.CodeBlock, d.Blocks[6].Kind);
        }
    }
}