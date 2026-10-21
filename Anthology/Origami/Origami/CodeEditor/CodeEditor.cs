// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>A diagnostic placed in the document. Its range follows the text as it is edited.</summary>
public sealed class CodeEditorDiagnostic
{
    public TextRange Range { get; internal set; }
    public CodeDiagnosticSeverity Severity { get; }
    public string Id { get; }
    public string Message { get; }

    public CodeEditorDiagnostic(TextRange range, CodeDiagnosticSeverity severity, string id, string message)
    {
        Range = range;
        Severity = severity;
        Id = id;
        Message = message;
    }
}

public enum CodeLineMarkerKind : byte
{
    /// <summary>A filled dot in the glyph margin.</summary>
    Breakpoint,
    /// <summary>An outlined dot in the glyph margin.</summary>
    DisabledBreakpoint,
    /// <summary>An arrow in the glyph margin and a tinted line, such as the line a debugger stopped on.</summary>
    Execution,
    Bookmark,
    /// <summary>Only tints the line background.</summary>
    LineHighlight,
}

/// <summary>Host owned decoration on a line, such as a breakpoint. The editor keeps <see cref="Line"/> on the same text as lines are added and removed.</summary>
public sealed class CodeLineMarker
{
    public int Line { get; set; }
    public CodeLineMarkerKind Kind { get; set; }
    public Color32? Color { get; set; }
    public object? Tag { get; set; }
}

/// <summary>
/// A virtualized code editor: the state, commands and language plumbing behind <see cref="Origami.CodeEditor"/>.
/// Hold one per open file and draw it each frame with <c>Origami.CodeEditor(paper, id, editor).Show()</c>.
/// </summary>
/// <remarks>
/// Only the visible rows are ever lexed, laid out or drawn, so files of millions of lines scroll as fast as
/// small ones. Language features run on background tasks and their results are applied on the UI thread
/// the next time the editor is drawn.
/// </remarks>
public sealed partial class CodeEditor : IDisposable
{
    private readonly List<CodeSelection> _selections = new() { new CodeSelection(TextPosition.Zero) };
    private int _primary;

    private CodeLanguage _language = new();
    private readonly ConcurrentQueue<Action> _uiQueue = new();
    private readonly List<CodeEditorDiagnostic> _diagnostics = new();
    private readonly List<CodeLineMarker> _markers = new();

    private bool _replaying;
    private double _time;
    private double _lastEditTime = -1;
    private bool _analysisPending;
    private bool _foldsPending = true;
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _foldCts;

    public CodeDocument Document { get; }
    public CodeEditorOptions Options { get; set; } = new();

    /// <summary>Colours to draw with. Null follows the current Origami theme.</summary>
    public CodeEditorColors? Colors { get; set; }

    public CodeUndoStack Undo { get; } = new();

    /// <summary>Path or name of the file being edited. Only used for display and by language sessions that care.</summary>
    public string? FilePath { get; set; }

    public CodeLanguage Language
    {
        get => _language;
        set => SetLanguage(value);
    }

    /// <summary>The compiler backed half of <see cref="Language"/>, or null when it is lexical only.</summary>
    public CodeLanguageSession? Session { get; private set; }

    public IReadOnlyList<CodeSelection> Selections => _selections;
    public CodeSelection Selection => _selections[_primary];
    public TextPosition Caret => _selections[_primary].Caret;
    public IReadOnlyList<CodeEditorDiagnostic> Diagnostics => _diagnostics;
    public IList<CodeLineMarker> Markers => _markers;
    public bool IsDirty => Undo.IsDirty;
    public bool IsFocused { get; private set; }

    public string Text
    {
        get => Document.GetText();
        set => SetText(value);
    }

    /// <summary>Raised after every edit.</summary>
    public event Action<TextChange>? TextChanged;

    /// <summary>Ctrl+S was pressed.</summary>
    public event Action? SaveRequested;

    /// <summary>Go to definition or a reference landed outside this document. The host opens it.</summary>
    public event Action<CodeLocation>? NavigateRequested;

    /// <summary>Find all references finished. The editor also lists them in a popup.</summary>
    public event Action<IReadOnlyList<CodeLocation>>? ReferencesFound;

    /// <summary>Rename produced edits for other documents, keyed by path. The host applies them.</summary>
    public event Action<IReadOnlyDictionary<string, IReadOnlyList<CodeTextEdit>>>? ExternalEditsRequested;

    /// <summary>The glyph margin left of a line number was clicked. Toggle a breakpoint here.</summary>
    public event Action<int>? GlyphMarginClicked;

    public event Action? CaretMoved;

    public CodeEditor() : this(new CodeDocument()) { }

    public CodeEditor(string text, CodeLanguage? language = null) : this(new CodeDocument(text), language) { }

    public CodeEditor(CodeDocument document, CodeLanguage? language = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Document.Changed += OnDocumentChanged;
        Document.Reset += OnDocumentReset;
        ResetLineStates();
        if (language != null) SetLanguage(language);
    }

    public void Dispose()
    {
        Document.Changed -= OnDocumentChanged;
        Document.Reset -= OnDocumentReset;
        _analysisCts?.Cancel();
        _foldCts?.Cancel();
        CancelPopupRequests();
        Session?.Dispose();
        Session = null;
    }

    public void SetText(string text)
    {
        Document.SetText(text);
        Undo.Clear();
    }

    public void SetLanguage(CodeLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        Session?.Dispose();
        _analysisCts?.Cancel();
        _semanticCts?.Cancel();
        _foldCts?.Cancel();
        CancelPopupRequests();
        ClosePopups();
        _language = language;
        Session = language.CreateSession(Document);
        Session?.OnTextReset(Document.GetText("\n"));
        ResetLineStates();
        _semantic.Clear();
        ClearLenses();
        _diagnostics.Clear();
        ScheduleAnalysis();
        _foldsPending = true;
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread the next time the editor is drawn. Safe from any thread.</summary>
    public void Post(Action action) => _uiQueue.Enqueue(action);

    private void PumpUiQueue()
    {
        int budget = 256;
        while (budget-- > 0 && _uiQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Console.WriteLine($"CodeEditor: {e}"); }
        }
    }

    // Continues a language request on the UI thread, dropping it when cancelled, faulted or out of date.
    private void Continue<T>(Task<T> task, int version, Action<T> onResult, bool requireVersion = true)
    {
        task.ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            if (t.IsFaulted)
            {
                Console.WriteLine($"CodeEditor language request failed: {t.Exception?.GetBaseException().Message}");
                return;
            }
            var result = t.Result;
            Post(() =>
            {
                if (requireVersion && version != Document.Version) return;
                onResult(result);
            });
        }, TaskScheduler.Default);
    }

    #region Document events

    private void OnDocumentReset()
    {
        _selections.Clear();
        _selections.Add(new CodeSelection(TextPosition.Zero));
        _primary = 0;
        _collapsed.Clear();
        _folds.Clear();
        _foldStartIndex.Clear();
        RebuildHiddenLines();
        Undo.Clear();
        _diagnostics.Clear();
        _errorCount = _warningCount = 0;
        _semantic.Clear();
        ClearLenses();
        ResetLineStates();
        Session?.OnTextReset(Document.GetText("\n"));
        _scrollY = _scrollYTarget = 0;
        _scrollX = _scrollXTarget = 0;
        _foldsPending = true;
        ClosePopups();
        ScheduleAnalysis();
    }

    private void OnDocumentChanged(TextChange change)
    {
        for (int i = 0; i < _selections.Count; i++)
        {
            var s = _selections[i];
            _selections[i] = new CodeSelection(change.Map(s.Anchor), change.Map(s.Caret));
        }

        if (!_replaying)
        {
            if (Undo.IsRecording) Undo.Record(change);
            else
            {
                Undo.Begin(SnapshotSelections(), CodeEditKind.Other, _time);
                Undo.Record(change);
                Undo.End(SnapshotSelections());
            }
        }

        UpdateLineStates(change);
        ShiftFolds(change);
        ShiftDiagnostics(change);
        ShiftMarkers(change);
        ShiftLenses(change);
        Session?.OnTextChanged(change);
        OnEditForPopups(change);

        _lastEditTime = _time;
        ScheduleAnalysis();
        _foldsPending = true;
        _revealCaret = true;
        TextChanged?.Invoke(change);
    }

    private void ShiftDiagnostics(TextChange change)
    {
        for (int i = _diagnostics.Count - 1; i >= 0; i--)
        {
            var d = _diagnostics[i];
            var start = change.Map(d.Range.Start, false);
            var end = change.Map(d.Range.End);
            d.Range = new TextRange(start, end);
        }
    }

    private void ShiftMarkers(TextChange change)
    {
        if (_markers.Count == 0 || change.LineDelta == 0) return;
        int from = change.Range.Start.Line;
        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            var m = _markers[i];
            if (m.Line <= from) continue;
            if (m.Line <= change.Range.End.Line)
            {
                // The marker's line was merged away, so it lands on the line the edit ends on.
                m.Line = change.NewEnd.Line;
                continue;
            }
            m.Line += change.LineDelta;
        }
    }

    #endregion

    #region Lexer state cache

    // Start state of every line, valid for lines below _statesValid.
    private readonly List<int> _lineStates = new();
    private int _statesValid;
    private const int EagerRelexLimit = 4000;

    private void ResetLineStates()
    {
        _lineStates.Clear();
        for (int i = 0; i < Document.LineCount; i++) _lineStates.Add(0);
        _statesValid = 1;
    }

    private int LexEndState(int line)
    {
        var lexer = _language.Lexer;
        return lexer == null ? 0 : lexer.Tokenize(Document[line], _lineStates[line], null);
    }

    /// <summary>Start state of <paramref name="line"/>, lexing forward from the last known line if needed.</summary>
    internal int GetLineState(int line)
    {
        if (_language.Lexer == null) return 0;
        while (_statesValid <= line)
        {
            int k = _statesValid - 1;
            _lineStates[k + 1] = LexEndState(k);
            _statesValid++;
        }
        return _lineStates[line];
    }

    private void UpdateLineStates(TextChange change)
    {
        int s = change.Range.Start.Line;
        int removed = change.Range.End.Line - s;
        int added = change.NewEnd.Line - s;
        int oldValid = _statesValid;

        if (removed > 0) _lineStates.RemoveRange(s + 1, removed);
        if (added > 0) _lineStates.InsertRange(s + 1, new int[added]);

        if (oldValid <= s) return;
        if (_language.Lexer == null) { _statesValid = Document.LineCount; return; }

        // Lines past the edit kept their old states. Relex from the edit until a state matches again.
        int trustedFrom = oldValid > change.Range.End.Line + 1 ? oldValid + change.LineDelta : -1;
        int count = Document.LineCount;
        int k = s;
        for (; k < count - 1; k++)
        {
            int end = LexEndState(k);
            if (k >= change.NewEnd.Line && trustedFrom > k + 1 && _lineStates[k + 1] == end)
            {
                _statesValid = trustedFrom;
                return;
            }
            _lineStates[k + 1] = end;
            if (trustedFrom < 0 && k >= change.NewEnd.Line) break;
            if (k - s > EagerRelexLimit) break;
        }
        _statesValid = Math.Min(count, k + 2);
    }

    #endregion

    #region Selections

    internal CodeSelection[] SnapshotSelections() => _selections.ToArray();

    internal void RestoreSelections(CodeSelection[] selections)
    {
        _selections.Clear();
        foreach (var s in selections)
            _selections.Add(new CodeSelection(Document.Clamp(s.Anchor), Document.Clamp(s.Caret)));
        if (_selections.Count == 0) _selections.Add(new CodeSelection(TextPosition.Zero));
        _primary = Math.Clamp(_primary, 0, _selections.Count - 1);
        NormalizeSelections();
    }

    /// <summary>Replaces every caret with one selection.</summary>
    public void Select(TextPosition anchor, TextPosition caret)
    {
        _selections.Clear();
        _selections.Add(new CodeSelection(Document.Clamp(anchor), Document.Clamp(caret)));
        _primary = 0;
        AfterCaretMove(true);
    }

    public void Select(TextRange range) => Select(range.Start, range.End);

    public void SetCaret(TextPosition caret) => Select(caret, caret);

    public void SelectAll() => Select(TextPosition.Zero, Document.EndPosition);

    /// <summary>Adds a caret without touching the others.</summary>
    public void AddSelection(CodeSelection selection)
    {
        _selections.Add(new CodeSelection(Document.Clamp(selection.Anchor), Document.Clamp(selection.Caret)));
        _primary = _selections.Count - 1;
        NormalizeSelections();
        AfterCaretMove(true);
    }

    public string GetSelectedText()
    {
        var parts = new List<string>();
        foreach (var s in OrderedSelections())
            if (!s.IsEmpty) parts.Add(Document.GetText(s.Range));
        return string.Join("\n", parts);
    }

    internal List<CodeSelection> OrderedSelections()
    {
        var list = new List<CodeSelection>(_selections);
        list.Sort((a, b) => a.Start.CompareTo(b.Start));
        return list;
    }

    /// <summary>Sorts carets and merges any that overlap, keeping the primary one primary.</summary>
    internal void NormalizeSelections()
    {
        if (_selections.Count <= 1) return;
        var primary = _selections[_primary];
        _selections.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<CodeSelection>(_selections.Count);
        foreach (var s in _selections)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                bool overlap = s.Start < last.End || s.Start == last.Start || (s.IsEmpty && s.Start == last.End && last.IsEmpty);
                if (overlap)
                {
                    var start = TextPosition.Min(last.Start, s.Start);
                    var end = TextPosition.Max(last.End, s.End);
                    bool reversed = last.IsReversed && s.IsReversed;
                    merged[^1] = reversed ? new CodeSelection(end, start) : new CodeSelection(start, end);
                    continue;
                }
            }
            merged.Add(s);
        }

        _selections.Clear();
        _selections.AddRange(merged);
        _primary = 0;
        for (int i = 0; i < _selections.Count; i++)
            if (_selections[i].Range.Contains(primary.Caret)) { _primary = i; break; }
    }

    private void CollapseToPrimary()
    {
        var p = _selections[_primary];
        _selections.Clear();
        _selections.Add(p);
        _primary = 0;
    }

    private void AfterCaretMove(bool reveal)
    {
        _keepGoalColumns = false;
        _caretMoveTime = _time;
        if (reveal) _revealCaret = true;
        Undo.Break();
        UnfoldAtCarets();
        OnCaretMovedForPopups();
        CaretMoved?.Invoke();
    }

    #endregion

    #region Analysis

    private void ScheduleAnalysis()
    {
        _analysisPending = true;
        _semanticRequestedVersion = -1;
    }

    private void TickAnalysis()
    {
        if (_foldsPending && _time - _lastEditTime >= Options.AnalysisDelay) RecomputeFolds();

        if (Session == null || !_analysisPending) return;
        if (_time - _lastEditTime < Options.AnalysisDelay) return;
        _analysisPending = false;

        _analysisCts?.Cancel();
        _analysisCts = new CancellationTokenSource();
        var ct = _analysisCts.Token;
        int version = Document.Version;

        Continue(Session.GetDiagnosticsAsync(ct), version, ApplyDiagnostics);
    }

    private int _errorCount, _warningCount;

    private void ApplyDiagnostics(IReadOnlyList<CodeDiagnostic> diagnostics)
    {
        _diagnostics.Clear();
        _errorCount = _warningCount = 0;
        int length = Document.Length;
        foreach (var d in diagnostics)
        {
            if (d.Severity == CodeDiagnosticSeverity.Hidden) continue;
            int start = Math.Clamp(d.Start, 0, length);
            int end = Math.Clamp(d.Start + Math.Max(0, d.Length), 0, length);
            var range = new TextRange(Document.OffsetToPosition(start), Document.OffsetToPosition(end));
            _diagnostics.Add(new CodeEditorDiagnostic(range, d.Severity, d.Id, d.Message));
            if (d.Severity == CodeDiagnosticSeverity.Error) _errorCount++;
            else if (d.Severity == CodeDiagnosticSeverity.Warning) _warningCount++;
        }
        _diagnostics.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start));
    }

    public int CountDiagnostics(CodeDiagnosticSeverity severity) => severity switch
    {
        CodeDiagnosticSeverity.Error => _errorCount,
        CodeDiagnosticSeverity.Warning => _warningCount,
        _ => _diagnostics.Count - _errorCount - _warningCount,
    };

    // First diagnostic that could reach the given line. Diagnostics stay sorted as edits shift them.
    internal int FirstDiagnosticFrom(int line)
    {
        int lo = 0, hi = _diagnostics.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_diagnostics[mid].Range.Start.Line < line - 200) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // Semantic colours keyed by the line's string instance. An edited line gets a new string, so stale
    // colours can never land on changed text, while lines that merely moved keep theirs.
    private readonly Dictionary<string, CodeToken[]> _semantic = new(ReferenceEqualityComparer.Instance);
    private int _semanticRequestedVersion = -1;
    private int _semanticFrom, _semanticTo;
    private CancellationTokenSource? _semanticCts;

    private void TickSemantic(int firstLine, int lastLine)
    {
        if (Session == null) return;
        if (_time - _lastEditTime < Options.AnalysisDelay) return;
        bool covered = _semanticRequestedVersion == Document.Version && firstLine >= _semanticFrom && lastLine <= _semanticTo;
        if (covered) return;

        int margin = Math.Max(50, lastLine - firstLine);
        int from = Math.Max(0, firstLine - margin);
        int to = Math.Min(Document.LineCount - 1, lastLine + margin);
        _semanticRequestedVersion = Document.Version;
        _semanticFrom = from;
        _semanticTo = to;

        int start = Document.GetLineOffset(from);
        int end = Document.GetLineOffset(to) + Document.GetLineLength(to);
        int version = Document.Version;

        _semanticCts?.Cancel();
        _semanticCts = new CancellationTokenSource();
        Continue(Session.GetClassificationsAsync(start, end - start, _semanticCts.Token), version,
            spans => ApplySemantic(spans, from, to));
    }

    private void ApplySemantic(IReadOnlyList<CodeClassifiedSpan> spans, int from, int to)
    {
        var perLine = new Dictionary<int, List<CodeToken>>();
        foreach (var s in spans)
        {
            var a = Document.OffsetToPosition(s.Start);
            var b = Document.OffsetToPosition(s.Start + s.Length);
            for (int line = a.Line; line <= b.Line; line++)
            {
                int c0 = line == a.Line ? a.Column : 0;
                int c1 = line == b.Line ? b.Column : Document.GetLineLength(line);
                if (c1 <= c0) continue;
                if (!perLine.TryGetValue(line, out var list)) perLine[line] = list = new List<CodeToken>();
                list.Add(new CodeToken(c0, c1 - c0, s.Kind));
            }
        }

        if (_semantic.Count > 20000) _semantic.Clear();
        for (int line = from; line <= to && line < Document.LineCount; line++)
        {
            string text = Document[line];
            if (perLine.TryGetValue(line, out var list))
            {
                list.Sort((x, y) => x.Start.CompareTo(y.Start));
                _semantic[text] = list.ToArray();
            }
            else _semantic.Remove(text);
        }
    }

    #endregion

    #region Folding

    // Every foldable range, sorted by start line, and the ones currently collapsed.
    private List<CodeFoldRange> _folds = new();
    private readonly List<CodeFoldRange> _collapsed = new();
    private readonly Dictionary<int, int> _foldStartIndex = new();
    private readonly Dictionary<int, CodeFoldRange> _collapsedByStart = new();

    // Hidden line intervals (inclusive), sorted and disjoint, with the count of hidden lines before each.
    private int[] _hidStart = Array.Empty<int>();
    private int[] _hidEnd = Array.Empty<int>();
    private int[] _hidBefore = Array.Empty<int>();
    private int _hiddenTotal;

    public IReadOnlyList<CodeFoldRange> FoldRanges => _folds;

    /// <summary>Rows shown, which is the line count less every line hidden in a fold.</summary>
    public int VisibleLineCount => Document.LineCount - _hiddenTotal;

    private void RecomputeFolds()
    {
        _foldsPending = false;
        _foldCts?.Cancel();
        _foldCts = new CancellationTokenSource();
        var ct = _foldCts.Token;
        var snapshot = Document.Snapshot();
        var language = _language;
        int version = Document.Version;
        Continue(Task.Run(() => language.ComputeFolds(snapshot, ct), ct), version, SetFolds);
    }

    internal void SetFolds(List<CodeFoldRange> folds)
    {
        _folds = folds;
        _foldStartIndex.Clear();
        for (int i = 0; i < folds.Count; i++)
            _foldStartIndex.TryAdd(folds[i].StartLine, i);

        // Collapsed folds survive a refresh when a fold still starts on the same line.
        for (int i = _collapsed.Count - 1; i >= 0; i--)
        {
            if (_foldStartIndex.TryGetValue(_collapsed[i].StartLine, out int idx)) _collapsed[i] = folds[idx];
            else _collapsed.RemoveAt(i);
        }
        RebuildHiddenLines();
    }

    internal bool TryGetFoldAt(int line, out CodeFoldRange fold)
    {
        if (_foldStartIndex.TryGetValue(line, out int idx) && idx < _folds.Count) { fold = _folds[idx]; return true; }
        fold = default;
        return false;
    }

    public bool IsCollapsed(int startLine) => _collapsedByStart.ContainsKey(startLine);

    internal bool TryGetCollapsed(int startLine, out CodeFoldRange fold) => _collapsedByStart.TryGetValue(startLine, out fold);

    public void ToggleFold(int startLine)
    {
        if (IsCollapsed(startLine)) Unfold(startLine);
        else Fold(startLine);
    }

    public void Fold(int startLine)
    {
        if (!TryGetFoldAt(startLine, out var fold) || IsCollapsed(startLine)) return;
        _collapsed.Add(fold);
        _collapsed.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
        RebuildHiddenLines();
        MoveCaretsOutOfFolds();
    }

    public void Unfold(int startLine)
    {
        _collapsed.RemoveAll(c => c.StartLine == startLine);
        RebuildHiddenLines();
    }

    /// <summary>Folds the innermost range around the caret.</summary>
    public void FoldAtCaret()
    {
        int line = Caret.Line;
        CodeFoldRange? best = null;
        foreach (var f in _folds)
        {
            if (f.StartLine > line) break;
            if (f.EndLine >= line && !IsCollapsed(f.StartLine) && (best == null || f.StartLine >= best.Value.StartLine))
                best = f;
        }
        if (best != null) Fold(best.Value.StartLine);
    }

    public void UnfoldAtCaret()
    {
        int line = Caret.Line;
        for (int i = _collapsed.Count - 1; i >= 0; i--)
            if (_collapsed[i].StartLine <= line && _collapsed[i].EndLine >= line) { Unfold(_collapsed[i].StartLine); return; }
    }

    public void FoldAll()
    {
        _collapsed.Clear();
        _collapsed.AddRange(_folds);
        RebuildHiddenLines();
        MoveCaretsOutOfFolds();
    }

    public void UnfoldAll()
    {
        _collapsed.Clear();
        RebuildHiddenLines();
    }

    private void RebuildHiddenLines()
    {
        _collapsedByStart.Clear();
        foreach (var c in _collapsed) _collapsedByStart.TryAdd(c.StartLine, c);

        var starts = new List<int>();
        var ends = new List<int>();
        int lastLine = Document.LineCount - 1;
        foreach (var c in _collapsed)
        {
            int s = c.StartLine + 1, e = Math.Min(c.EndLine, lastLine);
            if (e < s) continue;
            if (starts.Count > 0 && s <= ends[^1] + 1)
            {
                if (e > ends[^1]) ends[^1] = e;
                continue;
            }
            starts.Add(s);
            ends.Add(e);
        }

        _hidStart = starts.ToArray();
        _hidEnd = ends.ToArray();
        _hidBefore = new int[starts.Count];
        int total = 0;
        for (int i = 0; i < starts.Count; i++)
        {
            _hidBefore[i] = total;
            total += ends[i] - starts[i] + 1;
        }
        _hiddenTotal = total;
        RebuildDisplayRows();
    }

    private int HiddenIntervalAtOrBefore(int line)
    {
        int lo = 0, hi = _hidStart.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_hidStart[mid] <= line) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    public bool IsLineHidden(int line)
    {
        int i = HiddenIntervalAtOrBefore(line);
        return i >= 0 && line <= _hidEnd[i];
    }

    /// <summary>The row a line is drawn on. A hidden line maps to the row of the fold that hides it.</summary>
    public int LineToRow(int line)
    {
        int i = HiddenIntervalAtOrBefore(line);
        if (i < 0) return line;
        if (line <= _hidEnd[i]) return _hidStart[i] - 1 - _hidBefore[i];
        return line - _hidBefore[i] - (_hidEnd[i] - _hidStart[i] + 1);
    }

    public int RowToLine(int row)
    {
        row = Math.Clamp(row, 0, Math.Max(0, VisibleLineCount - 1));
        int lo = 0, hi = _hidStart.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_hidStart[mid] - _hidBefore[mid] <= row) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (found < 0) return row;
        return row + _hidBefore[found] + (_hidEnd[found] - _hidStart[found] + 1);
    }

    private void ShiftFolds(TextChange change)
    {
        int s = change.Range.Start.Line, e = change.Range.End.Line, delta = change.LineDelta;
        if (_collapsed.Count == 0 && delta == 0) return;

        static CodeFoldRange Shift(CodeFoldRange f, int s, int e, int delta, int newEndLine)
        {
            int start = f.StartLine > e ? f.StartLine + delta : f.StartLine > s ? newEndLine : f.StartLine;
            int end = f.EndLine > e ? f.EndLine + delta : f.EndLine >= s ? Math.Max(start, f.EndLine + delta) : f.EndLine;
            return new CodeFoldRange(start, end, f.Label);
        }

        bool changed = false;
        for (int i = _collapsed.Count - 1; i >= 0; i--)
        {
            var c = _collapsed[i];
            // Editing the hidden lines, or splitting the header line, opens the fold.
            bool touchesHidden = e > c.StartLine && s <= c.EndLine;
            bool splitsHeader = delta != 0 && s <= c.StartLine && e >= c.StartLine;
            if (touchesHidden || splitsHeader)
            {
                _collapsed.RemoveAt(i);
                changed = true;
                continue;
            }
            _collapsed[i] = Shift(c, s, e, delta, change.NewEnd.Line);
            changed = true;
        }

        if (delta != 0)
        {
            for (int i = 0; i < _folds.Count; i++) _folds[i] = Shift(_folds[i], s, e, delta, change.NewEnd.Line);
            _foldStartIndex.Clear();
            for (int i = 0; i < _folds.Count; i++) _foldStartIndex.TryAdd(_folds[i].StartLine, i);
        }

        if (changed || delta != 0) RebuildHiddenLines();
    }

    private void MoveCaretsOutOfFolds()
    {
        for (int i = 0; i < _selections.Count; i++)
        {
            var c = _selections[i].Caret;
            if (!IsLineHidden(c.Line)) continue;
            int header = RowToLine(LineToRow(c.Line));
            var p = new TextPosition(header, Document.GetLineLength(header));
            _selections[i] = new CodeSelection(p);
        }
        NormalizeSelections();
    }

    // A caret placed inside a fold by a command, such as go to line, opens the fold around it.
    private void UnfoldAtCarets()
    {
        if (_collapsed.Count == 0) return;
        bool changed = false;
        foreach (var s in _selections)
        {
            int line = s.Caret.Line;
            if (!IsLineHidden(line)) continue;
            changed |= _collapsed.RemoveAll(c => c.StartLine < line && c.EndLine >= line) > 0;
        }
        if (changed) RebuildHiddenLines();
    }

    #endregion
}
