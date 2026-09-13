using System;

namespace Prowl.Quire;

public sealed partial class MarkdownDocument
{
    private int AddBlock(BlockKind kind, int level = 0, bool flag = false, TableAlign align = TableAlign.None,
        TextSpan text = default, TextSpan info = default)
    {
        _blocks.Add(new Block(kind, level, flag, align, text, info, -1, -1, -1));
        return _blocks.Count - 1;
    }

    private void SetBlockLinks(int index, int firstInline, int firstChild, int nextSibling)
    {
        var b = _blocks[index];
        _blocks[index] = new Block(b.Kind, b.Level, b.Flag, b.Align, b.Text, b.Info, firstInline, firstChild, nextSibling);
    }

    private void SetFirstInline(int index, int firstInline)
    {
        var b = _blocks[index];
        _blocks[index] = new Block(b.Kind, b.Level, b.Flag, b.Align, b.Text, b.Info, firstInline, b.FirstChild, b.NextSibling);
    }

    private void AppendChild(int parent, int child, ref int first, ref int last)
    {
        if (first < 0)
        {
            first = child;
            var p = _blocks[parent];
            _blocks[parent] = new Block(p.Kind, p.Level, p.Flag, p.Align, p.Text, p.Info, p.FirstInline, child, p.NextSibling);
        }
        else
        {
            var l = _blocks[last];
            _blocks[last] = new Block(l.Kind, l.Level, l.Flag, l.Align, l.Text, l.Info, l.FirstInline, l.FirstChild, child);
        }

        last = child;
    }

    private void ParseDocument()
    {
        IndexLines();
        int root = AddBlock(BlockKind.Document);
        int line = 0;
        ParseBlocks(root, ref line, 0, int.MaxValue);
    }

    /// <summary>Line-oriented block scan. <paramref name="indent"/> is the column every line of this
    /// container must be indented past; <paramref name="limit"/> caps how far the scan may run.</summary>
    private void ParseBlocks(int parent, ref int line, int indent, int limit)
    {
        // Resume from whatever the parent already has, so re-entrant calls keep one child list.
        int first = _blocks[parent].FirstChild, last = LastChild(parent);
        while (line < limit && line < LineCount)
        {
            if (IsBlank(line))
            {
                line++;
                continue;
            }

            int content = ContentStart(line);
            if (content - LineStart(line) < indent)
            {
                break;
            }

            int start = LineStart(line) + indent;
            int lead = SkipSpaces(start, LineEnd(line));

            if (IsIndentedCode(line, indent))
            {
                int codeStart = LineStart(line) + indent + 4;
                int codeEnd = LineEnd(line);
                line++;
                while (line < limit && line < LineCount && (IsBlank(line) || IsIndentedCode(line, indent)))
                {
                    if (!IsBlank(line))
                    {
                        codeEnd = LineEnd(line);
                    }

                    line++;
                }

                AppendChild(parent, AddBlock(BlockKind.CodeBlock, text: new TextSpan(codeStart, Math.Max(0, codeEnd - codeStart))), ref first, ref last);
                continue;
            }

            if (IsHorizontalRule(lead, LineEnd(line)))
            {
                AppendChild(parent, AddBlock(BlockKind.HorizontalRule), ref first, ref last);
                line++;
                continue;
            }

            if (TryParseHeading(lead, LineEnd(line), out int level, out TextSpan headingText))
            {
                int heading = AddBlock(BlockKind.Heading, level);
                SetFirstInline(heading, ParseInlines(headingText));
                AppendChild(parent, heading, ref first, ref last);
                line++;
                continue;
            }

            if (TryParseFence(lead, LineEnd(line), out char fence, out int fenceLength, out TextSpan info))
            {
                int bodyStart = line + 1 < LineCount ? LineStart(line + 1) : LineEnd(line);
                int end = line + 1;
                while (end < LineCount && !IsClosingFence(end, indent, fence, fenceLength))
                {
                    end++;
                }

                int bodyEnd = end > line + 1 ? LineEnd(end - 1) : bodyStart;
                var code = AddBlock(BlockKind.CodeBlock, text: new TextSpan(bodyStart, Math.Max(0, bodyEnd - bodyStart)), info: info);
                AppendChild(parent, code, ref first, ref last);
                line = end < LineCount ? end + 1 : end;
                continue;
            }

            if (_source[lead] == '>')
            {
                int quote = AddBlock(BlockKind.BlockQuote);
                AppendChild(parent, quote, ref first, ref last);
                ParseBlockQuote(quote, ref line, indent);
                continue;
            }

            if (TryParseBullet(lead, LineEnd(line), out bool ordered, out int markerEnd, out int ordinal))
            {
                int list = AddBlock(BlockKind.List, flag: ordered);
                AppendChild(parent, list, ref first, ref last);
                ParseList(list, ref line, indent, ordered);
                continue;
            }

            if (TryParseTable(line, indent, out int tableEnd))
            {
                int table = AddBlock(BlockKind.Table);
                AppendChild(parent, table, ref first, ref last);
                ParseTable(table, line, tableEnd, indent);
                line = tableEnd;
                continue;
            }

            int paraStart = lead;
            int paraEnd = LineEnd(line);
            line++;
            while (line < limit && line < LineCount && !IsBlank(line)
                && !StartsNewBlock(line, indent) && !IsSetextUnderline(line, indent, out _))
            {
                paraEnd = LineEnd(line);
                line++;
            }

            // A run of = or - directly under the text promotes it to a heading instead.
            if (line < limit && line < LineCount && IsSetextUnderline(line, indent, out int setextLevel))
            {
                int setext = AddBlock(BlockKind.Heading, setextLevel);
                SetFirstInline(setext, ParseInlines(new TextSpan(paraStart, paraEnd - paraStart)));
                AppendChild(parent, setext, ref first, ref last);
                line++;
                continue;
            }

            int paragraph = AddBlock(BlockKind.Paragraph);
            SetFirstInline(paragraph, ParseInlines(new TextSpan(paraStart, paraEnd - paraStart)));
            AppendChild(parent, paragraph, ref first, ref last);
        }
    }

    private void ParseBlockQuote(int quote, ref int line, int indent)
    {
        int first = -1, last = -1;
        while (line < LineCount && !IsBlank(line))
        {
            int lead = SkipSpaces(LineStart(line) + indent, LineEnd(line));
            if (lead >= LineEnd(line) || _source[lead] != '>')
            {
                break;
            }

            int textStart = lead + 1;
            if (textStart < LineEnd(line) && _source[textStart] == ' ')
            {
                textStart++;
            }

            int paragraph = AddBlock(BlockKind.Paragraph);
            SetFirstInline(paragraph, ParseInlines(new TextSpan(textStart, LineEnd(line) - textStart)));
            AppendChild(quote, paragraph, ref first, ref last);
            line++;
        }
    }

    private void ParseList(int list, ref int line, int indent, bool ordered)
    {
        int first = -1, last = -1;
        while (line < LineCount)
        {
            if (IsBlank(line))
            {
                if (line + 1 >= LineCount || !ContinuesList(line + 1, indent, ordered))
                {
                    break;
                }

                line++;
                continue;
            }

            int lead = SkipSpaces(LineStart(line) + indent, LineEnd(line));
            if (!TryParseBullet(lead, LineEnd(line), out bool itemOrdered, out int markerEnd, out int ordinal) || itemOrdered != ordered)
            {
                break;
            }

            bool done = false;
            int textStart = markerEnd;
            if (TryParseTaskMarker(textStart, LineEnd(line), out bool isChecked, out int afterTask))
            {
                done = isChecked;
                textStart = afterTask;
            }

            int item = AddBlock(BlockKind.ListItem, level: ordinal, flag: done);
            AppendChild(list, item, ref first, ref last);

            int lead0 = AddBlock(BlockKind.Paragraph);
            SetFirstInline(lead0, ParseInlines(new TextSpan(textStart, LineEnd(line) - textStart)));
            int itemFirst = -1, itemLast = -1;
            AppendChild(item, lead0, ref itemFirst, ref itemLast);
            int markerColumn = markerEnd - LineStart(line);
            line++;

            // Anything indented past the marker belongs to this item.
            int contentEnd = line;
            while (contentEnd < LineCount && !IsBlank(contentEnd)
                && ContentStart(contentEnd) - LineStart(contentEnd) >= markerColumn)
            {
                contentEnd++;
            }

            if (contentEnd > line)
            {
                ParseBlocks(item, ref line, markerColumn, contentEnd);
                line = contentEnd;
            }
        }
    }

    private int LastChild(int parent)
    {
        int child = _blocks[parent].FirstChild, last = -1;
        while (child >= 0)
        {
            last = child;
            child = _blocks[child].NextSibling;
        }

        return last;
    }

    private bool ContinuesList(int line, int indent, bool ordered)
    {
        if (IsBlank(line))
        {
            return false;
        }

        int lead = SkipSpaces(LineStart(line) + indent, LineEnd(line));
        return TryParseBullet(lead, LineEnd(line), out bool o, out _, out _) && o == ordered;
    }

    private void ParseTable(int table, int line, int end, int indent)
    {
        Span<TableAlign> alignment = stackalloc TableAlign[32];
        int columns = ReadAlignments(line + 1, indent, alignment);
        int first = -1, last = -1;
        for (int i = line; i < end; i++)
        {
            if (i == line + 1)
            {
                continue;
            }

            int row = AddBlock(BlockKind.TableRow, flag: i == line);
            AppendChild(table, row, ref first, ref last);
            int cellFirst = -1, cellLast = -1;
            int column = 0;
            foreach (var cell in SplitRow(i, indent))
            {
                var align = column < columns ? alignment[column] : TableAlign.None;
                int node = AddBlock(BlockKind.TableCell, align: align);
                SetFirstInline(node, ParseInlines(cell));
                AppendChild(row, node, ref cellFirst, ref cellLast);
                column++;
            }
        }
    }
}
