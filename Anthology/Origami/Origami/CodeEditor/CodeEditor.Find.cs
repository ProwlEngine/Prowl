// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    private Regex? _findRegex;
    private string _findRegexSource = "";
    private RegexOptions _findRegexOptions;
    private int _findCountVersion = -1;
    private string _findCountQuery = "";
    private int _findCount;
    private const int FindCountLimit = 99999;

    public string FindQuery { get; set; } = "";
    public string ReplaceText { get; set; } = "";
    public bool FindCaseSensitive { get; set; }
    public bool FindWholeWord { get; set; }
    public bool FindRegex { get; set; }

    /// <summary>Total matches of <see cref="FindQuery"/>, capped at 99999. Recounted when the text or query changes.</summary>
    public int FindMatchCount
    {
        get
        {
            string key = $"{FindCaseSensitive}{FindWholeWord}{FindRegex}|{FindQuery}";
            if (_findCountVersion != Document.Version || _findCountQuery != key)
            {
                _findCountVersion = Document.Version;
                _findCountQuery = key;
                CountMatchesInBackground(key);
            }
            return _findCount;
        }
    }

    private Regex? GetRegex(string query, bool caseSensitive, bool wholeWord, bool regex)
    {
        if (query.Length == 0) return null;
        string pattern = regex ? query : Regex.Escape(query);
        if (wholeWord) pattern = $@"(?<![\w]){pattern}(?![\w])";
        var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        if (_findRegex != null && _findRegexSource == pattern && _findRegexOptions == options) return _findRegex;
        try
        {
            _findRegex = new Regex(pattern, options, TimeSpan.FromMilliseconds(250));
            _findRegexSource = pattern;
            _findRegexOptions = options;
            return _findRegex;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private Regex? CurrentRegex() => GetRegex(FindQuery, FindCaseSensitive, FindWholeWord, FindRegex);

    /// <summary>True when <see cref="FindQuery"/> is a regex that does not parse.</summary>
    public bool FindQueryInvalid => FindQuery.Length > 0 && CurrentRegex() == null;

    // Huge files would stall a frame per keystroke, so counting runs on a snapshot off the UI thread.
    private void CountMatchesInBackground(string key)
    {
        var regex = CurrentRegex();
        if (regex == null) { _findCount = 0; return; }
        if (Document.LineCount < 20000) { _findCount = CountMatches(regex, null); return; }
        var lines = Document.Snapshot();
        int version = Document.Version;
        System.Threading.Tasks.Task.Run(() => CountMatches(regex, lines)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) Post(() => { if (_findCountQuery == key && version == Document.Version) _findCount = t.Result; });
        });
    }

    private int CountMatches(Regex regex, string[]? lines)
    {
        int count = 0;
        int lineCount = lines?.Length ?? Document.LineCount;
        try
        {
            for (int line = 0; line < lineCount && count < FindCountLimit; line++)
            {
                string s = lines != null ? lines[line] : Document[line];
                if (s.Length == 0) continue;
                for (var m = regex.Match(s); m.Success && count < FindCountLimit; m = m.NextMatch())
                    if (m.Length > 0) count++;
            }
        }
        catch (RegexMatchTimeoutException) { }
        return count;
    }

    /// <summary>Matches of the current query on one line, as column ranges. Used to paint the visible rows.</summary>
    internal void LineMatches(int line, List<(int Start, int Length)> into)
    {
        into.Clear();
        var regex = CurrentRegex();
        if (regex == null) return;
        string s = Document[line];
        if (s.Length == 0) return;
        try
        {
            for (var m = regex.Match(s); m.Success; m = m.NextMatch())
                if (m.Length > 0) into.Add((m.Index, m.Length));
        }
        catch (RegexMatchTimeoutException) { }
    }

    /// <summary>Next plain text match of <paramref name="needle"/> after <paramref name="from"/>.</summary>
    public TextRange? FindNext(string needle, TextPosition from, bool caseSensitive, bool wholeWord, bool wrap = true)
    {
        var regex = new Regex((wholeWord ? @"(?<![\w])" : "") + Regex.Escape(needle) + (wholeWord ? @"(?![\w])" : ""),
            RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase));
        return Search(regex, from, true, wrap);
    }

    private TextRange? Search(Regex regex, TextPosition from, bool forward, bool wrap)
    {
        int count = Document.LineCount;
        try
        {
            if (forward)
            {
                for (int n = 0; n <= count; n++)
                {
                    int line = from.Line + n;
                    if (line >= count) { if (!wrap) return null; line -= count; }
                    string s = Document[line];
                    int startCol = n == 0 ? Math.Min(from.Column, s.Length) : 0;
                    for (var m = regex.Match(s, startCol); m.Success; m = m.NextMatch())
                    {
                        if (m.Length == 0) continue;
                        if (n == count && m.Index >= from.Column) return null;
                        return new TextRange(line, m.Index, line, m.Index + m.Length);
                    }
                }
            }
            else
            {
                for (int n = 0; n <= count; n++)
                {
                    int line = from.Line - n;
                    if (line < 0) { if (!wrap) return null; line += count; }
                    string s = Document[line];
                    int limit = n == 0 ? from.Column : int.MaxValue;
                    Match? best = null;
                    for (var m = regex.Match(s); m.Success; m = m.NextMatch())
                    {
                        if (m.Length == 0) continue;
                        if (m.Index + m.Length > limit) break;
                        best = m;
                    }
                    if (best != null) return new TextRange(line, best.Index, line, best.Index + best.Length);
                }
            }
        }
        catch (RegexMatchTimeoutException) { }
        return null;
    }

    /// <summary>Selects the next match of <see cref="FindQuery"/> after the caret.</summary>
    public bool FindNextMatch(bool forward = true)
    {
        var regex = CurrentRegex();
        if (regex == null) return false;
        var s = Selection;
        var from = forward ? s.End : s.Start;
        var found = Search(regex, from, forward, true);
        if (found == null) return false;
        Select(found.Value.Start, found.Value.End);
        CenterOnCaret(onlyIfOffscreen: true);
        return true;
    }

    private int _matchIndexKey, _matchIndex;

    /// <summary>One based index of the match the selection sits on, or 0.</summary>
    public int CurrentMatchIndex()
    {
        int key = HashCode.Combine(Document.Version, Selection, FindQuery, FindCaseSensitive, FindWholeWord, FindRegex);
        if (key == _matchIndexKey) return _matchIndex;
        _matchIndexKey = key;
        _matchIndex = ComputeMatchIndex();
        return _matchIndex;
    }

    private int ComputeMatchIndex()
    {
        var regex = CurrentRegex();
        if (regex == null || Selection.IsEmpty) return 0;
        var sel = Selection.Range;
        int index = 0;
        try
        {
            for (int line = 0; line <= sel.Start.Line && line < Document.LineCount; line++)
            {
                for (var m = regex.Match(Document[line]); m.Success; m = m.NextMatch())
                {
                    if (m.Length == 0) continue;
                    index++;
                    if (line == sel.Start.Line && m.Index == sel.Start.Column) return m.Length == sel.End.Column - sel.Start.Column ? index : 0;
                    if (line == sel.Start.Line && m.Index > sel.Start.Column) return 0;
                }
            }
        }
        catch (RegexMatchTimeoutException) { }
        return 0;
    }

    private string ExpandReplacement(Match m) => FindRegex ? m.Result(ReplaceText) : ReplaceText;

    /// <summary>Replaces the selected match, if it is one, then moves to the next.</summary>
    public void ReplaceNext()
    {
        var regex = CurrentRegex();
        if (regex == null || IsReadOnly) return;
        var sel = Selection.Range;
        if (!sel.IsEmpty && sel.IsSingleLine)
        {
            string line = Document[sel.Start.Line];
            Match m;
            try { m = regex.Match(line, sel.Start.Column); }
            catch (RegexMatchTimeoutException) { return; }
            if (m.Success && m.Index == sel.Start.Column && m.Length == sel.End.Column - sel.Start.Column)
            {
                string replacement = ExpandReplacement(m);
                Edit(CodeEditKind.Other, () =>
                {
                    var c = Document.Replace(sel, replacement);
                    _selections.Clear();
                    _selections.Add(new CodeSelection(c.NewEnd));
                    _primary = 0;
                });
            }
        }
        FindNextMatch();
    }

    public int ReplaceAll()
    {
        var regex = CurrentRegex();
        if (regex == null || IsReadOnly) return 0;
        int replaced = 0;
        Edit(CodeEditKind.Other, () =>
        {
            for (int line = Document.LineCount - 1; line >= 0; line--)
            {
                string s = Document[line];
                if (s.Length == 0) continue;
                MatchCollection matches;
                try { matches = regex.Matches(s); _ = matches.Count; }
                catch (RegexMatchTimeoutException) { continue; }
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    var m = matches[i];
                    if (m.Length == 0) continue;
                    Document.Replace(new TextRange(line, m.Index, line, m.Index + m.Length), ExpandReplacement(m));
                    replaced++;
                }
            }
        });
        return replaced;
    }

    /// <summary>Puts a caret on every match of the current query.</summary>
    public void SelectAllMatches()
    {
        var regex = CurrentRegex();
        if (regex == null) return;
        var found = new List<CodeSelection>();
        try
        {
            for (int line = 0; line < Document.LineCount && found.Count < 10000; line++)
                for (var m = regex.Match(Document[line]); m.Success; m = m.NextMatch())
                    if (m.Length > 0) found.Add(new CodeSelection(new TextPosition(line, m.Index), new TextPosition(line, m.Index + m.Length)));
        }
        catch (RegexMatchTimeoutException) { }
        if (found.Count == 0) return;
        _selections.Clear();
        _selections.AddRange(found);
        _primary = 0;
        AfterCaretMove(true);
    }
}
