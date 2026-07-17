using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Utils;
using static Prowl.Editor.GUI.EditorGUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

public class ConsolePanel : DockPanel
{
    [MenuItem("Window/General/Console", priority: 0)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(ConsolePanel));

    public override string Title => Loc.Get("panel.console");
    public override string Icon => EditorIcons.Terminal;

    private const int MaxMessages = 500;
    private static float RowHeight => EditorTheme.RowHeight + 2f;

    private static readonly List<LogEntry> _messages = new();
    private static bool _subscribed;

    // Settings
    private bool _showTime = true;
    private bool _collapse = true;
    private bool _multiLine = false;

    // Filters
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private string _searchText = "";

    // Cached filtered list (rebuilt when messages or filters change). Per-instance: each Console
    // tab has its own filter settings/selection, so the cache derived from them must be per-instance
    // too - a shared static cache lets two open tabs stomp each other's filtered view and selection.
    private int _lastMessageCount;
    private int _lastFilterHash;
    private bool _lastCollapseState;
    private readonly List<int> _filteredIndices = new();
    private int _selectedFilteredIndex = -1;

    internal struct LogEntry
    {
        public string Message;
        public string FullMessage;
        public LogSeverity Severity;
        public string TimeString;
        public int Count;
        public DebugStackTrace? StackTrace;

        // Cached text layouts, built lazily on first draw and reused every frame.
        public TextLayout? NameLayout;
        public TextLayout? MessageLayout;
        public TextLayout? TimeLayout;
        public TextLayout? CountLayout;
        public TextLayout? SourceLayout;
        public TextLayout? StackLayout;
    }

    public ConsolePanel() => EnsureSubscribed();

    /// <summary>Subscribe the shared log store to Debug.OnLog if not already. Safe to call before any
    /// ConsolePanel is opened - the status-bar footer relies on this to show logs regardless.</summary>
    public static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        Debug.OnLog += OnLogMessage;
    }

    // ---- Status-bar footer accessors (read-only view of the shared log store) ----

    /// <summary>Icon + color for a log severity, matching the console rows.</summary>
    public static (IOrigamiIcon icon, Color color) SeverityStyle(LogSeverity severity)
    {
        var (icon, color, _) = LevelOf(severity);
        return (icon, color);
    }

    /// <summary>Total Info / Warning / Error counts (including collapsed repeats).</summary>
    public static (int info, int warn, int err) LogCounts()
    {
        int info = 0, warn = 0, err = 0;
        foreach (var m in _messages)
        {
            if (m.Severity == LogSeverity.Warning) warn += m.Count;
            else if (m.Severity is LogSeverity.Error or LogSeverity.Exception) err += m.Count;
            else info += m.Count;
        }
        return (info, warn, err);
    }

    /// <summary>The most recent log entry (message, source class, collapse count), or null if none.</summary>
    public static (LogSeverity severity, string message, string? source, int count)? LastLog()
    {
        if (_messages.Count == 0) return null;
        var m = _messages[^1];
        return (m.Severity, m.Message, SourceOf(m), m.Count);
    }

    private static void OnLogMessage(string message, DebugStackTrace? stackTrace, LogSeverity severity)
    {
        string firstLine = message.Contains('\n') ? message.Split('\n')[0] : message;

        if (_messages.Count > 0)
        {
            var last = _messages[^1];
            if (last.FullMessage == message && last.Severity == severity)
            {
                last.Count += 1;
                last.TimeString = DateTime.Now.ToString("HH:mm:ss");
                last.StackTrace = stackTrace ?? last.StackTrace;
                last.TimeLayout = null;
                last.CountLayout = null;
                _messages[^1] = last;
                return;
            }
        }

        _messages.Add(new LogEntry
        {
            Message = firstLine,
            FullMessage = message,
            Severity = severity,
            TimeString = DateTime.Now.ToString("HH:mm:ss"),
            Count = 1,
            StackTrace = stackTrace,
        });

        while (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);
    }

    // ================================================================
    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column("con_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawMessages(paper, font, width, height - 35);
        }
    }

    // ================================================================
    //  Toolbar
    // ================================================================
    private void DrawToolbar(Paper paper, FontFile font, float width)
    {
        int infoCount = 0, warnCount = 0, errCount = 0;
        foreach (var m in _messages)
        {
            if (m.Severity == LogSeverity.Warning) warnCount += m.Count;
            else if (m.Severity is LogSeverity.Error or LogSeverity.Exception) errCount += m.Count;
            else infoCount += m.Count;
        }

        using (paper.Column("con_tb_col").Height(34).Enter())
        {
            using (paper.Row("con_tb").Height(33).Padding(10, 8, 0, 0).RowBetween(4).Enter())
            {
                LevelChip(paper, font, "con_collapse", EditorIcons.LayerGroup_I,
                    _collapse ? EditorTheme.AccentText : EditorTheme.InkDim, Loc.Get("console.collapse"), null, _collapse, false, () => _collapse = !_collapse);

                paper.Box("con_div1").Width(1).Margin(4, 4, 0, 0).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                LevelChip(paper, font, "con_info", EditorIcons.CircleInfo_I, EditorTheme.Blue400, null, infoCount.ToString(), _showInfo, !_showInfo, () => _showInfo = !_showInfo);
                LevelChip(paper, font, "con_warn", EditorIcons.TriangleExclamation_I, EditorTheme.Amber400, null, warnCount.ToString(), _showWarnings, !_showWarnings, () => _showWarnings = !_showWarnings);
                LevelChip(paper, font, "con_err", EditorIcons.CircleExclamation_I, EditorTheme.Red400, null, errCount.ToString(), _showErrors, !_showErrors, () => _showErrors = !_showErrors);

                paper.Box("con_sp");

                using (paper.Row("con_search_wrap").Width(130).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.SearchField(paper, "con_search", _searchText, v => _searchText = v, Loc.Get("console.filter")).Width(130).Height(24).Show();

                ToolbarIconBtn(paper, "con_clear", EditorIcons.Trash, false, () => { _messages.Clear(); _filteredIndices.Clear(); _selectedFilteredIndex = -1; });
                ToolbarIconBtn(paper, "con_opts", EditorIcons.EllipsisVertical, false,
                    () => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, BuildOptionsMenu));
            }
            paper.Box("con_tb_div").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
        }
    }

    // cs-lvl chip: icon + optional label + optional count; "on" gets a glass inset, "off" dims.
    private void LevelChip(Paper p, FontFile font, string id, IOrigamiIcon icon, Color iconColor, string? label, string? count, bool on, bool dim, Action onClick)
    {
        var mono = EditorTheme.FontMono ?? font;
        Color ic = dim ? Color.FromArgb(115, iconColor.R, iconColor.G, iconColor.B) : iconColor;

        using (p.Row(id).Width(UnitValue.Auto).Height(24).Rounded(6).Padding(8, 8, 0, 0).RowBetween(5).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .BackgroundColor(on ? EditorTheme.Glass : Color.Transparent)
            .BorderColor(on ? EditorTheme.BorderSoft : Color.Transparent).BorderWidth(1)
            .Transition(GuiProp.BackgroundColor, 0.15f).Transition(GuiProp.BorderColor, 0.15f)
            .Hovered.BackgroundColor(on ? EditorTheme.Glass : EditorTheme.Hover).End()
            .OnClick(_ => onClick())
            .Enter())
        {
            p.Box(id + "_i").Width(14).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                .Icon(p, icon, ic, size: 12f);
            if (!string.IsNullOrEmpty(label))
                p.Box(id + "_l").Width(UnitValue.Auto).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text(label, font).TextColor(on ? EditorTheme.Ink500 : (dim ? EditorTheme.InkFaint : EditorTheme.InkDim)).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            if (!string.IsNullOrEmpty(count))
                p.Box(id + "_c").Width(UnitValue.Auto).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text(count, mono).TextColor(dim ? EditorTheme.InkFaint : EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
    }

    private void BuildOptionsMenu(ContextBuilder menu)
    {
        menu.Submenu(Loc.Get("console.log_tests"), sub =>
        {
            sub.Item(Loc.Get("console.log"), () => Debug.Log("This is a Normal Log."))
                .Item(Loc.Get("console.log_warning"), () => Debug.LogWarning("This is a Warning Log."))
                .Item(Loc.Get("console.log_error"), () => Debug.LogError("This is an Error Log."))
                .Item(Loc.Get("console.log_success"), () => Debug.LogSuccess("This is a Success Log."));
        }, EditorIcons.Flask);
        menu.Separator();
        menu.Header(Loc.Get("console.display"));
        menu.Toggle(Loc.Get("console.show_time"), () => _showTime = !_showTime, () => _showTime);
        menu.Toggle(Loc.Get("console.multi_line"), () => _multiLine = !_multiLine, () => _multiLine);
    }

    // ================================================================
    //  Message list
    // ================================================================
    // Rows aren't individual elements: the whole list is one fixed-height box that only paints the
    // visible slice, drawing cached TextLayouts straight to the canvas so 500 messages stay cheap.
    private void DrawMessages(Paper paper, FontFile font, float width, float height)
    {
        int filterHash = HashCode.Combine(_showInfo, _showWarnings, _showErrors, _searchText);
        if (_lastMessageCount != _messages.Count || _lastFilterHash != filterHash || _lastCollapseState != _collapse)
        {
            _lastMessageCount = _messages.Count;
            _lastFilterHash = filterHash;
            _lastCollapseState = _collapse;
            RebuildFilteredList();
        }

        int count = _filteredIndices.Count;
        float rowH = _multiLine ? RowHeight * 1.85f : RowHeight;

        Origami.ScrollView(paper, "con_msgs", width, height).ForceScrollbar().Body(() =>
        {
            if (count == 0)
            {
                EditorGUI.EmptyState(paper, "con_empty", Loc.Get("console.no_logs"), font);
                return;
            }

            paper.Box("con_content").Width(width - 12).Height(count * rowH).Clip()
                .OnClick(0, (_, e) =>
                {
                    int row = (int)((float)e.RelativePosition.Y / rowH);
                    if (row < 0 || row >= _filteredIndices.Count) return;
                    _selectedFilteredIndex = row;
                    Selection.Select(new ConsoleLogSelection(_messages[_filteredIndices[row]]));
                })
                .OnPostLayout((handle, contentRect) =>
                {
                    var container = paper.GetElementData(handle.Data.ParentIndex);
                    var clip = paper.GetElementData(container.ParentIndex);
                    float contentTop = (float)contentRect.Min.Y;
                    int first = Math.Max(0, (int)(((float)clip.LayoutRect.Min.Y - contentTop) / rowH) - 1);
                    int last = Math.Min(count - 1, (int)(((float)clip.LayoutRect.Max.Y - contentTop) / rowH) + 1);
                    paper.Draw(ref handle, (canvas, r) => DrawRows(paper, canvas, r, font, rowH, first, last));
                });
        });
    }

    private void DrawRows(Paper paper, Canvas canvas, Rect r, FontFile font, float rowH, int first, int last)
    {
        var mono = EditorTheme.FontMono ?? font;
        var semi = EditorTheme.FontSemiBold ?? font;
        var bold = EditorTheme.FontBold ?? font;

        float left = (float)r.Min.X, right = (float)r.Max.X, top = (float)r.Min.Y, w = (float)r.Size.X;
        const float padL = 12f, padR = 12f, gap = 8f, iconSize = 14f;

        var ptr = paper.PointerPos;
        int hoverRow = (ptr.X >= left && ptr.X <= right && ptr.Y >= top && ptr.Y <= (float)r.Max.Y)
            ? (int)(((float)ptr.Y - top) / rowH) : -1;

        float LW(TextLayout l) => canvas.PixelToLogical((float)l.Size.X);
        float LH(TextLayout l) => canvas.PixelToLogical((float)l.Size.Y);
        void DrawMid(TextLayout l, float x, float cy, Color c) => canvas.DrawLayout(l, x, cy - LH(l) * 0.5f, c);
        TextLayout Make(string text, FontFile f, float size) =>
            canvas.CreateLayout(text, new TextLayoutSettings { Font = f, PixelSize = size, LineHeight = 1f, Quality = FontQuality.Normal });

        for (int vi = first; vi <= last; vi++)
        {
            // first/last were captured at layout time; a mid-frame Clear can shrink the list before
            // this deferred draw runs, so re-check bounds against the live collections.
            if (vi < 0 || vi >= _filteredIndices.Count) break;
            int msgIdx = _filteredIndices[vi];
            if (msgIdx < 0 || msgIdx >= _messages.Count) continue;
            var msg = _messages[msgIdx];
            (IOrigamiIcon icon, Color color, string name) = LevelOf(msg.Severity);
            bool selected = vi == _selectedFilteredIndex;

            float rowY = top + vi * rowH;
            float line1 = rowY + (_multiLine ? rowH * 0.33f : rowH * 0.5f);
            float line2 = rowY + rowH * 0.70f;

            if (selected)
            {
                canvas.RectFilled(left, rowY, w, rowH, EditorTheme.Selected);
                canvas.RectFilled(left, rowY, 2f, rowH, EditorTheme.Accent);
            }
            else if (vi == hoverRow)
                canvas.RectFilled(left, rowY, w, rowH, Color.FromArgb(13, 168, 85, 247));

            float ix = left + padL;
            icon.Draw(canvas, new Rect(ix, line1 - iconSize * 0.5f, ix + iconSize, line1 + iconSize * 0.5f), color, 1.6f);
            float cursorX = ix + iconSize + gap;

            msg.NameLayout ??= Make($"{name}:", semi, EditorTheme.FontSizeSmall);
            DrawMid(msg.NameLayout, cursorX, line1, color);
            cursorX += LW(msg.NameLayout) + 5f;

            // Right cluster, laid out from the right edge inward; message clips before it.
            float rightCursor = right - padR;

            if (_showTime)
            {
                msg.TimeLayout ??= Make(msg.TimeString, mono, EditorTheme.FontSizeSmall);
                float tw = LW(msg.TimeLayout);
                DrawMid(msg.TimeLayout, rightCursor - tw, line1, EditorTheme.InkFaint);
                rightCursor -= tw + gap;
            }

            string? src = SourceOf(msg);
            if (!string.IsNullOrEmpty(src))
            {
                msg.SourceLayout ??= Make($"[{src}]", mono, EditorTheme.FontSizeSmall);
                float sw = LW(msg.SourceLayout);
                DrawMid(msg.SourceLayout, rightCursor - sw, line1, EditorTheme.InkDim);
                rightCursor -= sw + gap;
            }

            if (_collapse && msg.Count > 1)
            {
                msg.CountLayout ??= Make(msg.Count.ToString(), bold, EditorTheme.FontSizeSmall);
                float cw = LW(msg.CountLayout), badgeW = cw + 12f, badgeH = 16f;
                float badgeX = rightCursor - badgeW, badgeY = line1 - badgeH * 0.5f;
                canvas.RoundedRectFilled(badgeX, badgeY, badgeW, badgeH, badgeH * 0.5f, EditorTheme.Neutral400);
                canvas.DrawLayout(msg.CountLayout, badgeX + (badgeW - cw) * 0.5f, line1 - LH(msg.CountLayout) * 0.5f, EditorTheme.Ink300);
                rightCursor -= badgeW + gap;
            }

            float msgLimit = Math.Max(cursorX, rightCursor);
            canvas.SaveState();
            canvas.IntersectScissor(cursorX, rowY, msgLimit - cursorX, rowH);
            msg.MessageLayout ??= Make(msg.Message, mono, EditorTheme.FontSizeSmall);
            DrawMid(msg.MessageLayout, cursorX, line1, EditorTheme.Ink400);
            if (_multiLine && msg.StackTrace is { StackFrames.Length: > 0 })
            {
                msg.StackLayout ??= Make(msg.StackTrace.StackFrames[0].ToString(), mono, EditorTheme.FontSizeSmall);
                DrawMid(msg.StackLayout, cursorX, line2, EditorTheme.InkDim);
            }
            canvas.RestoreState();

            canvas.RectFilled(left, rowY + rowH - 1f, w, 1f, EditorTheme.BorderSoft);

            _messages[msgIdx] = msg;
        }
    }

    // Class the log originated from (first captured frame's declaring type), for the source label.
    private static string? SourceOf(LogEntry msg)
    {
        var frames = msg.StackTrace?.StackFrames;
        if (frames == null || frames.Length == 0) return null;
        string? method = frames[0].Method;
        if (string.IsNullOrEmpty(method)) return null;
        int dot = method.LastIndexOf('.');
        return dot > 0 ? method[..dot] : method;
    }

    private static (IOrigamiIcon, Color, string) LevelOf(LogSeverity s) => s switch
    {
        LogSeverity.Warning => (EditorIcons.TriangleExclamation_I, EditorTheme.Amber400, "warning"),
        LogSeverity.Error or LogSeverity.Exception => (EditorIcons.CircleExclamation_I, EditorTheme.Red400, "error"),
        _ => (EditorIcons.CircleInfo_I, EditorTheme.Blue400, "info"),
    };

    private void RebuildFilteredList()
    {
        _filteredIndices.Clear();
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            var msg = _messages[i];
            if (!ShouldShow(msg.Severity)) continue;
            if (!EditorUtils.MatchesSearch(msg.Message, _searchText))
                continue;

            if (_collapse || msg.Count == 1)
                _filteredIndices.Add(i);
            else
                for (int t = 0; t < msg.Count; t++)
                    _filteredIndices.Add(i);
        }
        if (_selectedFilteredIndex >= _filteredIndices.Count)
            _selectedFilteredIndex = -1;
    }

    private bool ShouldShow(LogSeverity severity) => severity switch
    {
        LogSeverity.Normal or LogSeverity.Success => _showInfo,
        LogSeverity.Warning => _showWarnings,
        LogSeverity.Error or LogSeverity.Exception => _showErrors,
        _ => true,
    };
}

/// <summary>Wrapper for a console log entry so it can be selected and shown in the inspector.</summary>
public class ConsoleLogSelection
{
    public string Message { get; }
    public LogSeverity Severity { get; }
    public string Time { get; }
    public int Count { get; }
    public DebugStackTrace? StackTrace { get; }

    internal ConsoleLogSelection(ConsolePanel.LogEntry entry)
    {
        Message = entry.FullMessage;
        Severity = entry.Severity;
        Time = entry.TimeString;
        Count = entry.Count;
        StackTrace = entry.StackTrace;
    }
}
