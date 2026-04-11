using System;
using System.Collections.Generic;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Console")]
public class ConsolePanel : DockPanel
{
    public override string Title => "Console";
    public override string Icon => EditorIcons.Terminal;

    private const float ToolbarHeight = 26f;
    private const int MaxMessages = 500;
    private const float RowHeight = 20f;
    private const float IconWidth = 14f;
    private const float TimeWidth = 52f;
    private const float CountWidth = 30f;
    private const float FontSize = 10f;

    private static readonly List<LogEntry> _messages = new();
    private static bool _subscribed;

    // Filters
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private string _searchText = "";

    // Cached filtered list (rebuilt when messages or filters change)
    private static int _lastMessageCount;
    private static int _lastFilterHash;
    private static readonly List<int> _filteredIndices = new();


    // Selected entry for inspector display
    private static int _selectedFilteredIndex = -1;

    internal struct LogEntry
    {
        public string Message;
        public LogSeverity Severity;
        public string TimeString; // cached formatted time
        public int Count;
        public DebugStackTrace? StackTrace;

        // Cached text layouts (created on first draw)
        public Prowl.Scribe.TextLayout? IconLayout;
        public Prowl.Scribe.TextLayout? TimeLayout;
        public Prowl.Scribe.TextLayout? MessageLayout;
        public Prowl.Scribe.TextLayout? CountLayout;
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
                    Message = last.Message,
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
            Message = message,
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

        using (paper.Column("con_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawMessages(paper, font, width, height - 33);
        }
    }

    private void DrawToolbar(Paper paper, FontFile font, float width)
    {
        using (paper.Row("con_toolbar")
            .Height(ToolbarHeight)
            .Margin(4, 4, 4, 0)
            .RowBetween(4)
            .Enter())
        {
            EditorGUI.Button(paper, "con_clear", "Clear", width: 50)
                .OnValueChanged(_ => { _messages.Clear(); _filteredIndices.Clear(); });

            paper.Box("con_sep1").Width(1).Height(20).BackgroundColor(EditorTheme.Ink200);

            int infoCount = 0, warnCount = 0, errCount = 0;
            foreach (var m in _messages)
            {
                if (m.Severity == LogSeverity.Normal || m.Severity == LogSeverity.Success) infoCount += m.Count;
                else if (m.Severity == LogSeverity.Warning) warnCount += m.Count;
                else errCount += m.Count;
            }

            using (paper.Row("buttons").Enter())
            {
                EditorGUI.ToggleButton(paper, "con_info", $"{EditorIcons.CircleInfo} {infoCount}", _showInfo)
                    .OnValueChanged(v => _showInfo = v);
                EditorGUI.ToggleButton(paper, "con_warn", $"{EditorIcons.TriangleExclamation} {warnCount}", _showWarnings)
                    .OnValueChanged(v => _showWarnings = v);
                EditorGUI.ToggleButton(paper, "con_err", $"{EditorIcons.CircleExclamation} {errCount}", _showErrors)
                    .OnValueChanged(v => _showErrors = v);
            }

            EditorGUI.SearchBar(paper, "con_search", _searchText, "Filter...")
                .OnValueChanged(v => _searchText = v);
        }
    }

    private void DrawMessages(Paper paper, FontFile font, float width, float height)
    {
        // Rebuild filtered list when needed
        int filterHash = HashCode.Combine(_showInfo, _showWarnings, _showErrors, _searchText);
        if (_lastMessageCount != _messages.Count || _lastFilterHash != filterHash)
        {
            _lastMessageCount = _messages.Count;
            _lastFilterHash = filterHash;
            RebuildFilteredList();
        }

        int visibleCount = _filteredIndices.Count;
        float totalContentHeight = visibleCount * RowHeight;

        using (ScrollView.Begin(paper, "con_scroll", width, height))
        {
            // Single element for ALL messages — fixed height based on count
            paper.Box("con_content")
                .Width(width)
                .Height(totalContentHeight)
                .OnClick(0, (_, e) =>
                {
                    // Determine clicked row from mouse Y relative to content
                    float relY = (float)e.RelativePosition.Y;
                    int clickedRow = (int)(relY / RowHeight);
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
                    int firstVisible = Math.Max(0, (int)((clipTop - contentTop) / RowHeight));
                    int lastVisible = Math.Min(visibleCount - 1, (int)((clipBottom - contentTop) / RowHeight));

                    // Draw all visible rows in one draw call using cached TextLayouts
                    paper.Draw(ref handle, (canvas, r) =>
                    {
                        float startX = (float)r.Min.X;
                        float startY = (float)r.Min.Y;
                        float size = FontSize * 1.5f;

                        for (int vi = firstVisible; vi <= lastVisible; vi++)
                        {
                            if (vi < 0 || vi >= _filteredIndices.Count) continue;
                            int msgIdx = _filteredIndices[vi];
                            if (msgIdx >= _messages.Count) continue;

                            var msg = _messages[msgIdx];
                            float rowY = startY + vi * RowHeight;
                            float textY = rowY + RowHeight * 0.5f - size * 0.5f;

                            GetEntryStyle(msg.Severity, vi, out string icon, out Color textColor, out Color bgColor);

                            // Selection highlight
                            if (vi == _selectedFilteredIndex)
                                canvas.RectFilled(startX, rowY, (float)r.Size.X, RowHeight, Color.FromArgb(60, EditorTheme.Purple400));
                            else if (bgColor != Color.Transparent)
                                canvas.RectFilled(startX, rowY, (float)r.Size.X, RowHeight, bgColor);

                            // Create layouts lazily
                            msg.IconLayout ??= canvas.CreateLayout(icon, new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });
                            msg.TimeLayout ??= canvas.CreateLayout(msg.TimeString, new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });
                            msg.MessageLayout ??= canvas.CreateLayout(msg.Message, new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });

                            // Draw using cached layouts
                            canvas.DrawLayout(msg.IconLayout, startX + 4, textY, textColor);
                            canvas.DrawLayout(msg.TimeLayout, startX + IconWidth + 8, textY, EditorTheme.Ink200);
                            canvas.DrawLayout(msg.MessageLayout, startX + IconWidth + TimeWidth + 12, textY, textColor);

                            // Count badge
                            if (msg.Count > 1)
                            {
                                msg.CountLayout ??= canvas.CreateLayout(msg.Count.ToString(), new Prowl.Scribe.TextLayoutSettings { Font = font, PixelSize = size });
                                float badgeW = msg.CountLayout.Size.X / 2f + 8; // Size is in scaled pixels
                                float badgeX = startX + (float)r.Size.X - badgeW - 6;
                                float badgeY = rowY + (RowHeight - 14) * 0.5f;

                                canvas.RoundedRectFilled(badgeX, badgeY, badgeW, 14, 7, Color.FromArgb(120, 80, 80, 85));
                                canvas.DrawLayout(msg.CountLayout, badgeX + 4, badgeY + 1, EditorTheme.Ink300);
                            }

                            // Write back the cached layouts (struct copy)
                            _messages[msgIdx] = msg;
                        }
                    });
                });
        }
    }

    private void RebuildFilteredList()
    {
        _filteredIndices.Clear();
        for (int i = 0; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            if (!ShouldShow(msg.Severity)) continue;
            if (!string.IsNullOrEmpty(_searchText) &&
                !msg.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                continue;
            _filteredIndices.Add(i);
        }
    }

    private static void GetEntryStyle(LogSeverity severity, int visualIndex, out string icon, out Color textColor, out Color bgColor)
    {
        switch (severity)
        {
            case LogSeverity.Warning:
                icon = EditorIcons.TriangleExclamation;
                textColor = Color.FromArgb(255, 230, 200, 80);
                bgColor = visualIndex % 2 == 0 ? Color.FromArgb(10, 230, 200, 80) : Color.Transparent;
                break;
            case LogSeverity.Error:
            case LogSeverity.Exception:
                icon = EditorIcons.CircleExclamation;
                textColor = Color.FromArgb(255, 230, 80, 80);
                bgColor = visualIndex % 2 == 0 ? Color.FromArgb(10, 230, 80, 80) : Color.Transparent;
                break;
            case LogSeverity.Success:
                icon = EditorIcons.CircleCheck;
                textColor = Color.FromArgb(255, 80, 200, 80);
                bgColor = Color.Transparent;
                break;
            default:
                icon = EditorIcons.CircleInfo;
                textColor = EditorTheme.Ink500;
                bgColor = visualIndex % 2 == 0 ? Color.FromArgb(8, 255, 255, 255) : Color.Transparent;
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
        Message = entry.Message;
        Severity = entry.Severity;
        Time = entry.TimeString;
        Count = entry.Count;
        StackTrace = entry.StackTrace;
    }
}
