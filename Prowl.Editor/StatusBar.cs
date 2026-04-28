// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using static Prowl.Editor.Panels.ConsolePanel;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

public static class StatusBar
{
    public static string CurrentStatus => _currentStatus;

    private static string _currentStatus = "Ready";

    internal struct StatusInfo
    {
        public string Message;
        public string Icon;
        public Color Color;
    }

    private static StatusInfo? _lastStatusInfo = null;

    public static void Initialize()
    {
        Debug.OnLog += SetLogInfo;
    }

    public static void ClearStatus()
    {
        _lastStatusInfo = null;
    }

    private static void SetLogInfo(string message, DebugStackTrace? stackTrace, LogSeverity severity)
    {
        var info = new StatusInfo
        {
            Message = message.Contains('\n') ? message.Split('\n')[0] : message
        };

        switch (severity)
        {
            case LogSeverity.Warning:
                info.Icon = EditorIcons.TriangleExclamation;
                info.Color = Color.FromArgb(255, 230, 200, 80);
                break;
            case LogSeverity.Error:
            case LogSeverity.Exception:
                info.Icon = EditorIcons.CircleExclamation;
                info.Color = Color.FromArgb(255, 230, 80, 80);
                break;
            case LogSeverity.Success:
                info.Icon = EditorIcons.CircleCheck;
                info.Color = Color.FromArgb(255, 80, 200, 80);
                break;
            default:
                info.Icon = EditorIcons.CircleInfo;
                info.Color = EditorTheme.Ink400;
                break;
        }

        _lastStatusInfo = info;
    }

    public static void Draw(Paper paper)
    {
        var font = EditorTheme.DefaultFont;

        using (paper.Row("statusbar")
            .PositionType(PositionType.SelfDirected)
            .Position(0, paper.Height - EditorTheme.StatusBarHeight)
            .Size(paper.Percent(100), EditorTheme.StatusBarHeight)
            .BackgroundColor(EditorTheme.Neutral200)
            .ChildLeft(10)
            .RowBetween(10)
            .Enter())
        {

            paper.Box("status_icon")
                .Position(12, 0)
                .Size(UnitValue.Auto, EditorTheme.MenuBarHeight)
                .IsNotInteractable()
                .Text($"{_lastStatusInfo?.Icon ?? EditorIcons.CircleInfo}", font)
                .TextColor(_lastStatusInfo?.Color ?? EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("status_label")
                .Position(12, 0)
                .Size(UnitValue.StretchOne, EditorTheme.MenuBarHeight)
                .IsNotInteractable()
                .Text($"{(_lastStatusInfo?.Message ?? CurrentStatus)}", font)
                .TextColor(_lastStatusInfo?.Color ?? EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);


        }
    }

}
