using System;
using System.Collections.Generic;

using Prowl.Editor.Docking;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Console")]
public class ConsolePanel : DockPanel
{
    public override string Title => Loc.Get("panel.console");
    public override string Icon => EditorIcons.Terminal;

    private const float ToolbarHeight = 26f;
    private const int MaxMessages = 500;

    private float FullRowHeight => _multiLine ? MultiLineRowHeight : RowHeight;

    private float RowHeight = 26f;
    private float MultiLineRowHeight => RowHeight * 1.8f;
    private const float IconWidth = 16f;
    private float TimeWidth => 68f;
    private const float CountWidth = 30f;
    private const float FontSize = 13f;

    private static readonly List<LogEntry> _messages = new();
    private static bool _subscribed;

    // Settings
    private bool _showTime = false;
    private bool _collapse = true;
    private bool _multiLine = false;

    // Filters
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private string _searchText = "";

    // Cached filtered list (rebuilt when messages or filters change)
    private static int _lastMessageCount;
    private static int _lastFilterHash;
    private static bool _lastCollapseState;
    private static readonly List<int> _filteredIndices = new();


    // Selected entry for inspector display
    private static int _selectedFilteredIndex = -1;

    internal struct LogEntry
    {
        public string Message;
        public string FullMessage;
        public LogSeverity Severity;
        public string TimeString; // cached formatted time
        public int Count;
        public DebugStackTrace? StackTrace;

        // Cached text layouts (created on first draw)
        public Prowl.Scribe.TextLayout? IconLayout;
        public Prowl.Scribe.TextLayout? TimeLayout;
        public Prowl.Scribe.TextLayout? MessageLayout;
        public Prowl.Scribe.TextLayout? CountLayout;
        public Prowl.Scribe.TextLayout? StackTraceLayout;
    }

    public ConsolePanel()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            Debug.OnLog += OnLogMessage;
        }
    }

    private static void OnLogMessage(string message, DebugStackTrace? stackTrace, LogSeverity severity)
    {
        // Collapse duplicates
        if (_messages.Count > 0)
        {
            var last = _messages[^1];
            if (last.Message == message && last.Severity == severity)
            {
                _messages[^1] = new LogEntry
                {
                    Message = last.Message.Contains('\n') ? last.Message.Split('\n')[0] : last.Message,
                    FullMessage = last.Message,
                    Severity = last.Severity,
                    TimeString = DateTime.Now.ToString("HH:mm:ss"),
                    Count = last.Count + 1,
                    StackTrace = stackTrace ?? last.StackTrace
                };
                // Invalidate count layout since count changed
                var updated = _messages[^1];
                updated.CountLayout = null;
                _messages[^1] = updated;
                return;
            }
        }

        _messages.Add(new LogEntry
        {
            Message = message.Contains('\n') ? message.Split('\n')[0] : message,
            FullMessage = message,
            Severity = severity,
            TimeString = DateTime.Now.ToString("HH:mm:ss"),
            Count = 1,
            StackTrace = stackTrace
        });

        while (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column("con_root")
            .Size(width, height)
            .Enter())
        {
            DrawToolbar(paper, font, width);
            DrawMessages(paper, font, width-2, height - 44);
        }
    }

    private void DrawToolbar(Paper paper, FontFile font, float width)
    {
        using (paper.Row("con_toolbar")
            .Height(ToolbarHeight)
            .Margin(4, 4, 4, 0)
            .RowBetween(12)
            .Margin(8)
            .Enter())
        {
            Origami.Button(paper, "con_clear", Loc.Get("console.clear"), () => { _messages.Clear(); _filteredIndices.Clear(); }).Width(50).Show();

            paper.Box("con_sep1").Width(1).Height(24).BackgroundColor(EditorTheme.Ink200);

            int infoCount = 0, warnCount = 0, errCount = 0;
            foreach (var m in _messages)
            {
                if (m.Severity == LogSeverity.Normal || m.Severity == LogSeverity.Success) infoCount += m.Count;
                else if (m.Severity == LogSeverity.Warning) warnCount += m.Count;
                else errCount += m.Count;
            }

            Origami.Switch(paper, "con_collapse", _collapse, v => _collapse = v)
                .LabelRight(Loc.Get("console.collapse")).Show();

            using (paper.Row("buttons").RowBetween(12).Enter())
            {
                Origami.Switch(paper, "con_info", _showInfo, v => _showInfo = v)
                    .Info().LabelRight($"{EditorIcons.CircleInfo} {infoCount}").Show();

                Origami.Switch(paper, "con_warn", _showWarnings, v => _showWarnings = v)
                    .Warning().LabelRight($"{EditorIcons.TriangleExclamation} {warnCount}").Show();

                Origami.Switch(paper, "con_err", _showErrors, v => _showErrors = v)
                    .Danger().LabelRight($"{EditorIcons.CircleExclamation} {errCount}").Show();
            }

            Origami.SearchField(paper, "con_search", _searchText, v => _searchText = v, Loc.Get("console.filter")).Show();

            Origami.IconButton(paper, "con_settingsButton", $"{EditorIcons.Gear}", () =>
            {
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
                {
                    menu.Submenu(Loc.Get("console.log_tests"), subMenu =>
                    {
                        subMenu.Item(Loc.Get("console.log"), () => Debug.Log("This is a Normal Log."))
                            .Item(Loc.Get("console.log_warning"), () => Debug.LogWarning("This is a Warning Log."))
                            .Item(Loc.Get("console.log_error"), () => Debug.LogError("This is an Error Log."))
                            .Item(Loc.Get("console.log_success"), () => Debug.LogSuccess("This is a Success Log."));
                    }, EditorIcons.Flask)
                    .Separator()
                    .Toggle(Loc.Get("console.show_time"), () => _showTime = !_showTime, () => _showTime)
                    .Toggle(Loc.Get("console.multi_line"), () => _multiLine = !_multiLine, () => _multiLine);
                });
            }).Show();
        }
    }

    private void DrawMessages(Paper paper, FontFile font, float width, float height)
    {
        // Rebuild filtered list when needed
        int filterHash = HashCode.Combine(_showInfo, _showWarnings, _showErrors, _searchText);
        if (_lastMessageCount != _messages.Count || _lastFilterHash != filterHash || _lastCollapseState != _collapse)
        {
            _lastMessageCount = _messages.Count;
            _lastFilterHash = filterHash;
            _lastCollapseState = _collapse;
            RebuildFilteredList();
        }

        int visibleCount = _filteredIndices.Count;
        float totalContentHeight = visibleCount * FullRowHeight;

        Origami.ScrollView(paper, "con_scroll", width, height).ForceScrollbar().Body(() =>
        {
            // Single element for ALL messages fixed height based on count
            paper.Box("con_content")
                .Width(width - 10)
                .Height(totalContentHeight)
                .BackgroundColor(EditorTheme.Neutral100)
                .Clip()
                .OnClick(0, (_, e) =>
                {
                    // Determine clicked row from mouse Y relative to content
                    float relY = (float)e.RelativePosition.Y;
                    float totalRowHeight = FullRowHeight;
                    int clickedRow = (int)(relY / totalRowHeight);
                    if (clickedRow >= 0 && clickedRow < _filteredIndices.Count)
                    {
                        _selectedFilteredIndex = (_selectedFilteredIndex == clickedRow) ? -1 : clickedRow;

                        // Select the log entry for inspector display
                        if (_selectedFilteredIndex >= 0)
                        {
                            int msgIdx = _filteredIndices[_selectedFilteredIndex];
                            if (msgIdx < _messages.Count)
                                Selection.Select(new ConsoleLogSelection(_messages[msgIdx]));
                        }
                        else
                        {
                            Selection.Clear();
                        }
                    }
                })
                .OnPostLayout((handle, contentRect) =>
                {
                    // Get the parent (scroll view clip) rect
                    var containerData = paper.GetElementData(handle.Data.ParentIndex);
                    var clipSpaceData = paper.GetElementData(containerData.ParentIndex);

                    float clipTop = clipSpaceData.LayoutRect.Min.Y;
                    float clipBottom = clipSpaceData.LayoutRect.Max.Y;
                    float contentTop = (float)contentRect.Min.Y;

                    // Calculate visible row range
                    int firstVisible = Math.Max(0, (int)((clipTop - contentTop) / FullRowHeight));
                    int lastVisible = Math.Min(visibleCount - 1, (int)((clipBottom - contentTop) / FullRowHeight));

                    // Draw all visible rows in one draw call using cached TextLayouts
                    paper.Draw(ref handle, (canvas, r) =>
                    {
                        float startX = (float)r.Min.X;
                        float paddedX = startX + 8;
                        float startY = (float)r.Min.Y;
                        float size = FontSize * 1.5f;

                        for (int vi = firstVisible; vi <= lastVisible; vi++)
                        {
                            if (vi < 0 || vi >= _filteredIndices.Count) continue;
                            int msgIdx = _filteredIndices[vi];
                            if (msgIdx >= _messages.Count) continue;

                            var totalRowSize = FullRowHeight;

                            var msg = _messages[msgIdx];
                            float rowY = startY + vi * totalRowSize;
                            float textY = rowY + totalRowSize * (_multiLine ? 0.25f : 0.5f) - size * 0.5f + (_multiLine ? 2 : 0);
                            float iconY = rowY + totalRowSize * 0.5f - size * 0.5f;

                            GetEntryStyle(msg.Severity, vi, out string icon, out Color textColor, out Color bgColor);

                            // Selection highlight
                            if (vi == _selectedFilteredIndex)
                                canvas.RectFilled(startX, rowY, (float)r.Size.X, totalRowSize, Color.FromArgb(60, EditorTheme.Purple400));
                            else if (bgColor != Color.Transparent)
                                canvas.RectFilled(startX, rowY, (float)r.Size.X, totalRowSize, bgColor);

                            canvas.RectFilled(startX+2, rowY+1, (float)4, totalRowSize-2, LerpRGB(textColor, Color.Black, 0.5f));

                            // Create layouts lazily
                            msg.IconLayout ??= canvas.CreateLayout(icon, new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });
                            msg.TimeLayout ??= canvas.CreateLayout(msg.TimeString, new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });
                            msg.MessageLayout ??= canvas.CreateLayout(msg.Message, new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });
                            


                            float padStack = 4;
                            // Draw using cached layouts
                            canvas.DrawLayout(msg.IconLayout, paddedX + padStack, iconY, textColor);
                            padStack += IconWidth + 10;

                            if (_showTime)
                            {
                                canvas.DrawLayout(msg.TimeLayout, paddedX + padStack, iconY, EditorTheme.Ink200);
                                padStack += TimeWidth + 4;
                            }

                            if (_multiLine)
                            {
                                float stackSize = size * 0.8f;
                                float stackY = rowY + totalRowSize * (_multiLine ? 0.75f : 0.5f) - stackSize * 0.5f - 2;
                                msg.StackTraceLayout ??= canvas.CreateLayout(msg.StackTrace.StackFrames[0].ToString(), new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = stackSize });
                                canvas.DrawLayout(msg.StackTraceLayout, paddedX + padStack+1, stackY, LerpRGB(textColor,Color.Black,0.25f));
                            }

                            canvas.DrawLayout(msg.MessageLayout, paddedX + padStack, textY, textColor);

                            // Count badge
                            if (_collapse && msg.Count > 1)
                            {
                                var textSize = size / 1.2f;
                                msg.CountLayout ??= canvas.CreateLayout(msg.Count.ToString(), new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = textSize });
                                float badgeW = msg.CountLayout.Size.X + 8; // Size is in scaled pixels
                                float badgeH = RowHeight - 6;
                                float badgeX = startX + (float)r.Size.X - badgeW - 4;
                                float badgeY = rowY + (_multiLine ? MultiLineRowHeight*0.25f : 0) + 3;

                                canvas.RoundedRectFilled(badgeX, badgeY, badgeW, badgeH, badgeH, Color.FromArgb(120, 80, 80, 85));
                                canvas.DrawLayout(msg.CountLayout, badgeX + 4, badgeY + (badgeH - textSize) / 2f, EditorTheme.Ink400);
                            }

                            // Write back the cached layouts (struct copy)
                            _messages[msgIdx] = msg;
                        }
                    });
                });
        });
    }

    private static Color LerpRGB(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t)
        );
    }

    private void RebuildFilteredList()
    {
        _filteredIndices.Clear();
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            var msg = _messages[i];
            if (!ShouldShow(msg.Severity)) continue;
            if (!string.IsNullOrEmpty(_searchText) &&
                !msg.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_collapse || msg.Count == 1)
            {
                _filteredIndices.Add(i);
            }
            else if (msg.Count > 1)
            {
                for (int t = 0; t < msg.Count; t++)
                {
                    _filteredIndices.Add(i);
                }
            }
        }
    }

    private static void GetToggleStyle(LogSeverity severity, out Color textColor, out Color bgColor)
    {
        switch (severity)
        {
            case LogSeverity.Warning:
                textColor = Color.FromArgb(255, 230, 200, 80);
                bgColor = Color.FromArgb(20, 230, 200, 80);//visualIndex % 2 == 0 ? Color.FromArgb(10, 230, 200, 80) : Color.Transparent;
                break;
            case LogSeverity.Error:
            case LogSeverity.Exception:
                textColor = Color.FromArgb(255, 230, 80, 80);
                bgColor = Color.FromArgb(20, 230, 80, 80);//visualIndex % 2 == 0 ? Color.FromArgb(10, 230, 80, 80) : Color.Transparent;
                break;
            case LogSeverity.Success:
                textColor = Color.FromArgb(255, 80, 200, 80);
                bgColor = Color.Transparent;
                break;
            default:
                textColor = EditorTheme.Ink400;
                bgColor = Color.FromArgb(10, 255, 255, 255);
                break;
        }
    }

    private static void GetEntryStyle(LogSeverity severity, int visualIndex, out string icon, out Color textColor, out Color bgColor)
    {
        switch (severity)
        {
            case LogSeverity.Warning:
                icon = EditorIcons.TriangleExclamation;
                textColor = Color.FromArgb(255, 230, 200, 80);
                bgColor = Color.FromArgb(20, 230, 200, 80);//visualIndex % 2 == 0 ? Color.FromArgb(10, 230, 200, 80) : Color.Transparent;
                break;
            case LogSeverity.Error:
            case LogSeverity.Exception:
                icon = EditorIcons.CircleExclamation;
                textColor = Color.FromArgb(255, 230, 80, 80);
                bgColor = Color.FromArgb(20, 230, 80, 80);//visualIndex % 2 == 0 ? Color.FromArgb(10, 230, 80, 80) : Color.Transparent;
                break;
            case LogSeverity.Success:
                icon = EditorIcons.CircleCheck;
                textColor = Color.FromArgb(255, 80, 200, 80);
                bgColor = Color.FromArgb(25, 80, 200, 80);
                break;
            default:
                icon = EditorIcons.CircleInfo;
                textColor = EditorTheme.Ink400;
                bgColor = visualIndex % 2 == 0 ? Color.FromArgb(10, 255, 255, 255) : Color.Transparent;
                break;
        }
    }

    private bool ShouldShow(LogSeverity severity) => severity switch
    {
        LogSeverity.Normal or LogSeverity.Success => _showInfo,
        LogSeverity.Warning => _showWarnings,
        LogSeverity.Error or LogSeverity.Exception => _showErrors,
        _ => true
    };
}

/// <summary>
/// Wrapper for a console log entry so it can be selected and shown in the inspector.
/// </summary>
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
