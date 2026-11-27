using System;
using System.Collections.Generic;

namespace Prowl.Quire;

public sealed partial class MarkdownDocument
{
    private readonly List<int> _lineStarts = new();

    private int LineCount => _lineStarts.Count;
    private int LineStart(int line) => _lineStarts[line];

    private int LineEnd(int line)
    {
        int end = line + 1 < _lineStarts.Count ? _lineStarts[line + 1] : _source.Length;
        while (end > _lineStarts[line] && (_source[end - 1] == '\n' || _source[end - 1] == '\r'))
        {
            end--;
        }

        return end;
    }

    private void IndexLines()
    {
        _lineStarts.Clear();
        if (_source.Length == 0)
        {
            return;
        }

        _lineStarts.Add(0);
        for (int i = 0; i < _source.Length; i++)
        {
            if (_source[i] == '\n' && i + 1 < _source.Length)
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    private bool IsBlank(int line)
    {
        for (int i = LineStart(line); i < LineEnd(line); i++)
        {
            if (_source[i] != ' ' && _source[i] != '\t')
            {
                return false;
            }
        }

        return true;
    }

    private int ContentStart(int line) => SkipSpaces(LineStart(line), LineEnd(line));

    private int SkipSpaces(int index, int end)
    {
        while (index < end && (_source[index] == ' ' || _source[index] == '\t'))
        {
            index++;
        }

        return index;
    }

    private bool IsHorizontalRule(int start, int end)
    {
        if (start >= end)
        {
            return false;
        }

        char c = _source[start];
        if (c != '-' && c != '*' && c != '_')
        {
            return false;
        }

        int count = 0;
        for (int i = start; i < end; i++)
        {
            if (_source[i] == c)
            {
                count++;
            }
            else if (_source[i] != ' ' && _source[i] != '\t')
            {
                return false;
            }
        }

        return count >= 3;
    }

    private bool TryParseHeading(int start, int end, out int level, out TextSpan text)
    {
        level = 0;
        text = default;
        int i = start;
        while (i < end && _source[i] == '#' && level < 6)
        {
            i++;
            level++;
        }

        if (level == 0 || (i < end && _source[i] != ' ' && _source[i] != '\t'))
        {
            level = 0;
            return false;
        }

        i = SkipSpaces(i, end);
        // Trailing hashes are decoration, not content.
        int stop = end;
        while (stop > i && (_source[stop - 1] == '#' || _source[stop - 1] == ' '))
        {
            stop--;
        }

        text = new TextSpan(i, Math.Max(0, stop - i));
        return true;
    }

    private bool TryParseFence(int start, int end, out char fence, out int length, out TextSpan info)
    {
        fence = '\0';
        length = 0;
        info = default;
        if (start >= end)
        {
            return false;
        }

        char c = _source[start];
        if (c != '`' && c != '~')
        {
            return false;
        }

        int i = start;
        while (i < end && _source[i] == c)
        {
            i++;
        }

        if (i - start < 3)
        {
            return false;
        }

        fence = c;
        length = i - start;
        i = SkipSpaces(i, end);
        info = new TextSpan(i, Math.Max(0, end - i));
        return true;
    }

    private bool IsClosingFence(int line, int indent, char fence, int length)
    {
        int start = SkipSpaces(LineStart(line) + Math.Min(indent, LineEnd(line) - LineStart(line)), LineEnd(line));
        int end = LineEnd(line);
        int count = 0;
        int i = start;
        while (i < end && _source[i] == fence)
        {
            i++;
            count++;
        }

        return count >= length && SkipSpaces(i, end) >= end;
    }

    private bool TryParseBullet(int start, int end, out bool ordered, out int markerEnd, out int ordinal)
    {
        ordered = false;
        markerEnd = start;
        ordinal = 0;
        if (start >= end)
        {
            return false;
        }

        char c = _source[start];
        if (c == '-' || c == '*' || c == '+')
        {
            // A rule wins over a bullet.
            if (IsHorizontalRule(start, end))
            {
                return false;
            }

            if (start + 1 >= end || (_source[start + 1] != ' ' && _source[start + 1] != '\t'))
            {
                return false;
            }

            markerEnd = SkipSpaces(start + 1, end);
            return true;
        }

        int i = start;
        while (i < end && _source[i] >= '0' && _source[i] <= '9')
        {
            ordinal = ordinal * 10 + (_source[i] - '0');
            i++;
        }

        if (i == start || i >= end || (_source[i] != '.' && _source[i] != ')'))
        {
            ordinal = 0;
            return false;
        }

        i++;
        if (i < end && _source[i] != ' ' && _source[i] != '\t')
        {
            ordinal = 0;
            return false;
        }

        ordered = true;
        markerEnd = SkipSpaces(i, end);
        return true;
    }

    private bool TryParseTaskMarker(int start, int end, out bool isChecked, out int after)
    {
        isChecked = false;
        after = start;
        if (start + 2 >= end || _source[start] != '[')
        {
            return false;
        }

        char state = _source[start + 1];
        if (_source[start + 2] != ']' || (state != ' ' && state != 'x' && state != 'X'))
        {
            return false;
        }

        isChecked = state != ' ';
        after = SkipSpaces(start + 3, end);
        return true;
    }

    /// <summary>A run of = or - under a paragraph, which promotes it to a heading.</summary>
    private bool IsSetextUnderline(int line, int indent, out int level)
    {
        level = 0;
        int start = SkipSpaces(LineStart(line) + indent, LineEnd(line));
        int end = LineEnd(line);
        if (start >= end)
        {
            return false;
        }

        char c = _source[start];
        if (c != '=' && c != '-')
        {
            return false;
        }

        int i = start;
        while (i < end && _source[i] == c)
        {
            i++;
        }

        if (SkipSpaces(i, end) < end)
        {
            return false;
        }

        level = c == '=' ? 1 : 2;
        return true;
    }

    /// <summary>Four spaces past the container indent starts a code block.</summary>
    private bool IsIndentedCode(int line, int indent) =>
        !IsBlank(line) && ContentStart(line) - LineStart(line) >= indent + 4;

    /// <summary>True when a line would begin a different block, so a paragraph must stop before it.</summary>
    private bool StartsNewBlock(int line, int indent)
    {
        int start = LineStart(line) + indent;
        if (start >= LineEnd(line))
        {
            return true;
        }

        int lead = SkipSpaces(start, LineEnd(line));
        if (lead >= LineEnd(line))
        {
            return true;
        }

        char c = _source[lead];
        return c == '>'
            || IsHorizontalRule(lead, LineEnd(line))
            || TryParseHeading(lead, LineEnd(line), out _, out _)
            || TryParseFence(lead, LineEnd(line), out _, out _, out _)
            || TryParseBullet(lead, LineEnd(line), out _, out _, out _);
    }

    private bool TryParseTable(int line, int indent, out int end)
    {
        end = line;
        if (line + 1 >= LineCount)
        {
            return false;
        }

        if (!HasPipe(line, indent) || !IsDelimiterRow(line + 1, indent))
        {
            return false;
        }

        end = line + 2;
        while (end < LineCount && !IsBlank(end) && HasPipe(end, indent))
        {
            end++;
        }

        return true;
    }

    private bool HasPipe(int line, int indent)
    {
        for (int i = LineStart(line) + indent; i < LineEnd(line); i++)
        {
            if (_source[i] == '|')
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDelimiterRow(int line, int indent)
    {
        bool any = false;
        for (int i = SkipSpaces(LineStart(line) + indent, LineEnd(line)); i < LineEnd(line); i++)
        {
            char c = _source[i];
            if (c == '-')
            {
                any = true;
            }
            else if (c != '|' && c != ':' && c != ' ' && c != '\t')
            {
                return false;
            }
        }

        return any;
    }

    private int ReadAlignments(int line, int indent, Span<TableAlign> alignment)
    {
        int count = 0;
        foreach (var cell in SplitRow(line, indent))
        {
            if (count >= alignment.Length)
            {
                break;
            }

            int start = cell.Start, end = cell.Start + cell.Length;
            bool left = start < end && _source[start] == ':';
            bool right = end > start && _source[end - 1] == ':';
            alignment[count++] = left && right ? TableAlign.Center
                : left ? TableAlign.Left
                : right ? TableAlign.Right
                : TableAlign.None;
        }

        return count;
    }


    /// <summary>Split a table row into <see cref="_cells"/>. The buffer is reused, so a caller must
    /// finish with it before splitting another row.</summary>
    private readonly List<TextSpan> _cells = new();

    private List<TextSpan> SplitRow(int line, int indent)
    {
        var cells = _cells;
        cells.Clear();
        int start = SkipSpaces(LineStart(line) + indent, LineEnd(line));
        int end = LineEnd(line);
        if (start < end && _source[start] == '|')
        {
            start++;
        }

        while (end > start && (_source[end - 1] == ' ' || _source[end - 1] == '\t'))
        {
            end--;
        }

        if (end > start && _source[end - 1] == '|')
        {
            end--;
        }

        int cellStart = start;
        for (int i = start; i <= end; i++)
        {
            bool escaped = i > start && _source[i - 1] == '\\';
            if (i == end || (_source[i] == '|' && !escaped))
            {
                int trimStart = SkipSpaces(cellStart, i);
                int trimEnd = i;
                while (trimEnd > trimStart && (_source[trimEnd - 1] == ' ' || _source[trimEnd - 1] == '\t'))
                {
                    trimEnd--;
                }

                cells.Add(new TextSpan(trimStart, trimEnd - trimStart));
                cellStart = i + 1;
            }
        }

        return cells;
    }
}
