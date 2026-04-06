using System;
using System.Collections.Generic;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

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
    private static readonly List<LogEntry> _messages = new();
    private static bool _subscribed;

    // Filters
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private bool _autoScroll = true;
    private string _searchText = "";


    private struct LogEntry
    {
        public string Message;
        public LogSeverity Severity;
        public DateTime Time;
        public int Count; // collapse count
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
                    Time = DateTime.Now,
                    Count = last.Count + 1
                };
                return;
            }
        }

        _messages.Add(new LogEntry
        {
            Message = message,
            Severity = severity,
            Time = DateTime.Now,
            Count = 1
        });

        // Trim old messages
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

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        using (paper.Row("con_toolbar")
            .Height(ToolbarHeight)
            .Margin(4, 4, 4, 0)
            .RowBetween(4)
            .Enter())
        {
            // Clear
            EditorGUI.Button(paper, "con_clear", "Clear", width: 50)
                .OnValueChanged(_ => _messages.Clear());

            // Separator
            paper.Box("con_sep1").Width(1).Height(20).BackgroundColor(EditorTheme.Ink200);

            // Filter toggles
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

            // Search
            EditorGUI.SearchBar(paper, "con_search", _searchText, "Filter...")
                .OnValueChanged(v => _searchText = v);
        }
    }

    private void DrawMessages(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        using (ScrollView.Begin(paper, "con_scroll", width, height))
        {
            for (int i = 0; i < _messages.Count; i++)
            {
                var msg = _messages[i];

                // Filter by severity
                if (!ShouldShow(msg.Severity)) continue;

                // Filter by search
                if (!string.IsNullOrEmpty(_searchText) &&
                    !msg.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    continue;

                string icon;
                Color textColor;
                Color bgColor;

                switch (msg.Severity)
                {
                    case LogSeverity.Warning:
                        icon = EditorIcons.TriangleExclamation;
                        textColor = Color.FromArgb(255, 230, 200, 80);
                        bgColor = i % 2 == 0 ? Color.FromArgb(10, 230, 200, 80) : Color.Transparent;
                        break;
                    case LogSeverity.Error:
                    case LogSeverity.Exception:
                        icon = EditorIcons.CircleExclamation;
                        textColor = Color.FromArgb(255, 230, 80, 80);
                        bgColor = i % 2 == 0 ? Color.FromArgb(10, 230, 80, 80) : Color.Transparent;
                        break;
                    case LogSeverity.Success:
                        icon = EditorIcons.CircleCheck;
                        textColor = Color.FromArgb(255, 80, 200, 80);
                        bgColor = Color.Transparent;
                        break;
                    default:
                        icon = EditorIcons.CircleInfo;
                        textColor = EditorTheme.Ink500;
                        bgColor = i % 2 == 0 ? Color.FromArgb(8, 255, 255, 255) : Color.Transparent;
                        break;
                }

                using (paper.Row($"con_msg_{i}")
                    .Height(RowHeight)
                    .BackgroundColor(bgColor)
                    .ChildLeft(6).RowBetween(4)
                    .Enter())
                {
                    // Icon
                    paper.Box($"con_ico_{i}")
                        .Width(14).Height(RowHeight)
                        .Text(icon, font).TextColor(textColor)
                        .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                    // Time
                    paper.Box($"con_time_{i}")
                        .Width(50).Height(RowHeight)
                        .Text(msg.Time.ToString("HH:mm:ss"), font)
                        .TextColor(EditorTheme.Ink200)
                        .FontSize(EditorTheme.FontSize - 4).Alignment(TextAlignment.MiddleLeft);

                    // Message
                    paper.Box($"con_txt_{i}")
                        .Height(RowHeight).Clip()
                        .Text(msg.Message, font).TextColor(textColor)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    // Count badge
                    if (msg.Count > 1)
                    {
                        paper.Box($"con_cnt_{i}")
                            .Width(UnitValue.Auto).Height(16)
                            .ChildLeft(4).ChildRight(4)
                            .BackgroundColor(Color.FromArgb(120, 80, 80, 85))
                            .Rounded(8)
                            .Text($"{msg.Count}", font).TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize - 4).Alignment(TextAlignment.MiddleCenter);
                    }
                }
            }
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
