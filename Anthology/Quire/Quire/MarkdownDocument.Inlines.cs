using System;

namespace Prowl.Quire;

public sealed partial class MarkdownDocument
{
    private int AddInline(InlineKind kind, InlineStyle style = InlineStyle.None, TextSpan text = default,
        TextSpan href = default, TextSpan title = default, int firstChild = -1)
    {
        _inlines.Add(new Inline(kind, style, text, href, title, firstChild, -1));
        return _inlines.Count - 1;
    }

    private void LinkInline(int previous, int next)
    {
        var p = _inlines[previous];
        _inlines[previous] = new Inline(p.Kind, p.Style, p.Text, p.Href, p.Title, p.FirstChild, next);
    }

    private void AppendInline(int node, ref int first, ref int last)
    {
        if (first < 0)
        {
            first = node;
        }
        else
        {
            LinkInline(last, node);
        }

        last = node;
    }

    /// <summary>Parse one run of inline content, returning the first inline or -1.</summary>
    private int ParseInlines(TextSpan span) => ParseInlineRange(span.Start, span.Start + span.Length);

    // How many link labels the parse is inside. A link cannot hold another link, so inside one every
    // link form is read as plain text.
    private int _linkDepth;

    private int ParseInlineRange(int start, int end)
    {
        int first = -1, last = -1;
        int textStart = start;
        int i = start;

        void FlushText(int stop)
        {
            if (stop > textStart)
            {
                AppendInline(AddInline(InlineKind.Text, text: new TextSpan(textStart, stop - textStart)), ref first, ref last);
            }
        }

        while (i < end)
        {
            char c = _source[i];

            if (c == '\\' && i + 1 < end && IsEscapable(_source[i + 1]))
            {
                FlushText(i);
                AppendInline(AddInline(InlineKind.Text, text: new TextSpan(i + 1, 1)), ref first, ref last);
                i += 2;
                textStart = i;
                continue;
            }

            if (c == '\n')
            {
                FlushText(i);
                AppendInline(AddInline(InlineKind.LineBreak), ref first, ref last);
                i++;
                // Fold the indentation of a wrapped line into the break.
                i = SkipSpaces(i, end);
                textStart = i;
                continue;
            }

            if (c == '`')
            {
                int ticks = 0;
                while (i + ticks < end && _source[i + ticks] == '`')
                {
                    ticks++;
                }

                int close = FindRun(i + ticks, end, '`', ticks);
                if (close >= 0)
                {
                    FlushText(i);
                    AppendInline(AddInline(InlineKind.Code, text: new TextSpan(i + ticks, close - (i + ticks))), ref first, ref last);
                    i = close + ticks;
                    textStart = i;
                    continue;
                }
            }

            if (c == '!' && i + 1 < end && _source[i + 1] == '[')
            {
                if (TryParseLink(i + 1, end, out int afterImage, out TextSpan label, out TextSpan href, out TextSpan title))
                {
                    FlushText(i);
                    AppendInline(AddInline(InlineKind.Image, text: label, href: href, title: title), ref first, ref last);
                    i = afterImage;
                    textStart = i;
                    continue;
                }
            }

            if (c == '[' && _linkDepth == 0)
            {
                if (TryParseLink(i, end, out int afterLink, out TextSpan label, out TextSpan href, out TextSpan title))
                {
                    FlushText(i);
                    _linkDepth++;
                    int children = ParseInlineRange(label.Start, label.Start + label.Length);
                    _linkDepth--;
                    AppendInline(AddInline(InlineKind.Link, href: href, title: title, firstChild: children), ref first, ref last);
                    i = afterLink;
                    textStart = i;
                    continue;
                }
            }

            if (c == '<' && _linkDepth == 0 && TryParseAutolink(i, end, out int afterAuto, out TextSpan target))
            {
                FlushText(i);
                int label = AddInline(InlineKind.Text, text: target);
                AppendInline(AddInline(InlineKind.Link, href: target, firstChild: label), ref first, ref last);
                i = afterAuto;
                textStart = i;
                continue;
            }

            if (c == 'h' && _linkDepth == 0 && TryParseBareUrl(i, start, end, out int urlEnd))
            {
                FlushText(i);
                var url = new TextSpan(i, urlEnd - i);
                int label = AddInline(InlineKind.Text, text: url);
                AppendInline(AddInline(InlineKind.Link, href: url, firstChild: label), ref first, ref last);
                i = urlEnd;
                textStart = i;
                continue;
            }

            if (TryParseEmphasis(i, end, out InlineStyle style, out int contentStart, out int contentEnd, out int afterSpan))
            {
                FlushText(i);
                int children = ParseInlineRange(contentStart, contentEnd);
                AppendInline(AddInline(InlineKind.Span, style: style, firstChild: children), ref first, ref last);
                i = afterSpan;
                textStart = i;
                continue;
            }

            i++;
        }

        FlushText(end);
        return first;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>
    /// A bare http or https URL in running text, the way people actually write them. It ends at
    /// whitespace, and trailing punctuation that belongs to the sentence is left out, as is a closing
    /// parenthesis with no opener inside the URL, so "(see http://x.com/q)" links just the URL.
    /// </summary>
    private bool TryParseBareUrl(int index, int rangeStart, int end, out int urlEnd)
    {
        urlEnd = index;
        if (index > rangeStart && IsWordChar(_source[index - 1]))
        {
            return false;
        }

        int host;
        if (StartsWith(index, end, "https://")) host = index + 8;
        else if (StartsWith(index, end, "http://")) host = index + 7;
        else return false;

        int stop = host;
        while (stop < end && !char.IsWhiteSpace(_source[stop]) && _source[stop] != '<')
        {
            stop++;
        }

        int opens = Count(host, stop, '(');
        int closes = Count(host, stop, ')');
        while (stop > host)
        {
            char last = _source[stop - 1];
            if (last is '.' or ',' or ':' or ';' or '!' or '?' or '*' or '_' or '~' or '\'' or '"')
            {
                stop--;
                continue;
            }

            if (last == ')' && closes > opens)
            {
                stop--;
                closes--;
                continue;
            }

            break;
        }

        if (stop <= host)
        {
            return false;
        }

        urlEnd = stop;
        return true;
    }

    private bool StartsWith(int index, int end, string prefix)
    {
        if (index + prefix.Length > end)
        {
            return false;
        }

        for (int k = 0; k < prefix.Length; k++)
        {
            if (char.ToLowerInvariant(_source[index + k]) != prefix[k])
            {
                return false;
            }
        }

        return true;
    }

    private int Count(int from, int to, char c)
    {
        int n = 0;
        for (int k = from; k < to; k++)
        {
            if (_source[k] == c) n++;
        }

        return n;
    }

    /// <summary>Matches &lt;https://host/path&gt; and &lt;mailto:someone&gt;.</summary>
    private bool TryParseAutolink(int start, int end, out int after, out TextSpan target)
    {
        after = start;
        target = default;
        int close = -1;
        for (int i = start + 1; i < end; i++)
        {
            char c = _source[i];
            if (c == ' ' || c == '<')
            {
                return false;
            }

            if (c == '>')
            {
                close = i;
                break;
            }
        }

        if (close < 0 || close == start + 1)
        {
            return false;
        }

        var span = _source.AsSpan(start + 1, close - (start + 1));
        if (span.IndexOf("://".AsSpan()) < 0 && !span.StartsWith("mailto:".AsSpan()))
        {
            return false;
        }

        target = new TextSpan(start + 1, close - (start + 1));
        after = close + 1;
        return true;
    }

    private static bool IsEscapable(char c) =>
        c is '\\' or '`' or '*' or '_' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|' or '~' or '<' or '>';

    private int FindRun(int start, int end, char c, int count)
    {
        for (int i = start; i + count <= end; i++)
        {
            if (_source[i] != c)
            {
                continue;
            }

            int run = 0;
            while (i + run < end && _source[i + run] == c)
            {
                run++;
            }

            if (run == count)
            {
                return i;
            }

            i += run - 1;
        }

        return -1;
    }

    /// <summary>Matches [label](href "title"). Returns the index just past the closing paren.</summary>
    private bool TryParseLink(int start, int end, out int after, out TextSpan label, out TextSpan href, out TextSpan title)
    {
        after = start;
        label = default;
        href = default;
        title = default;

        int depth = 0;
        int close = -1;
        for (int i = start; i < end; i++)
        {
            char c = _source[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        if (close < 0 || close + 1 >= end || _source[close + 1] != '(')
        {
            return false;
        }

        int target = close + 2;
        int paren = 1;
        int stop = -1;
        for (int i = target; i < end; i++)
        {
            char c = _source[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                paren++;
            }
            else if (c == ')')
            {
                paren--;
                if (paren == 0)
                {
                    stop = i;
                    break;
                }
            }
        }

        if (stop < 0)
        {
            return false;
        }

        label = new TextSpan(start + 1, close - (start + 1));
        int hrefEnd = stop;
        int quote = -1;
        for (int i = target; i < stop; i++)
        {
            if (_source[i] == '"')
            {
                quote = i;
                break;
            }
        }

        if (quote > 0)
        {
            int closeQuote = _source.IndexOf('"', quote + 1);
            if (closeQuote > 0 && closeQuote < stop)
            {
                title = new TextSpan(quote + 1, closeQuote - (quote + 1));
            }

            hrefEnd = quote;
        }

        while (hrefEnd > target && (_source[hrefEnd - 1] == ' ' || _source[hrefEnd - 1] == '\t'))
        {
            hrefEnd--;
        }

        href = new TextSpan(target, Math.Max(0, hrefEnd - target));
        after = stop + 1;
        return true;
    }

    /// <summary>Matches **strong**, *em*, __underline__, _em_, ~~strike~~ and ~sub~.</summary>
    private bool TryParseEmphasis(int start, int end, out InlineStyle style, out int contentStart, out int contentEnd, out int after)
    {
        style = InlineStyle.None;
        contentStart = contentEnd = after = start;
        char c = _source[start];
        if (c != '*' && c != '_' && c != '~')
        {
            return false;
        }

        int run = 0;
        while (start + run < end && _source[start + run] == c)
        {
            run++;
        }

        if (run > 3)
        {
            run = 3;
        }

        // A delimiter must be followed by content, not whitespace.
        if (start + run >= end || _source[start + run] == ' ')
        {
            return false;
        }

        // Underscores inside a word are part of the word: snake_case_name, MAX_INT.
        if (c == '_' && start > 0 && IsWordChar(_source[start - 1]))
        {
            return false;
        }

        int close = FindRun(start + run, end, c, run);
        if (close < 0)
        {
            return false;
        }

        if (c == '_' && close + run < end && IsWordChar(_source[close + run]))
        {
            return false;
        }

        style = c switch
        {
            '~' => run >= 2 ? InlineStyle.Strike : InlineStyle.Overline,
            '_' => run >= 2 ? InlineStyle.Underline : InlineStyle.Emphasis,
            _ => run >= 3 ? InlineStyle.Strong | InlineStyle.Emphasis : run == 2 ? InlineStyle.Strong : InlineStyle.Emphasis
        };

        contentStart = start + run;
        contentEnd = close;
        after = close + run;
        return true;
    }
}
