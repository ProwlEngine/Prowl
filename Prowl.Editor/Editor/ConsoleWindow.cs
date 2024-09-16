// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

using Debug = Prowl.Runtime.Debug;
using Prowl.Editor.Assets;

namespace Prowl.Editor;

public record class LogMessage(string message, DebugStackTrace? trace, LogSeverity severity)
{
    public int DuplicateCount;
}

public class ConsoleWindow : EditorWindow
{
    // Errors with this string prefix will require a project recompile/reimport to clear.
    // Only really meaningful for c# script compilation errors.
    internal static readonly string UnclearableErrorPrefix = "$__PERR__";

    private const int MaxLogs = 1000;

    protected override double Width { get; } = 512 + (512 / 2);
    protected override double Height { get; } = 256;

    private readonly List<LogMessage> _logMessages;
    private readonly List<LogMessage> _messagesToRender;

    private LogMessage? _selectedMessage = null;


    public ConsoleWindow() : base()
    {
        Title = FontAwesome6.Terminal + " Console";

        _logMessages = [];
        _messagesToRender = [];

        Debug.OnLog += OnLog;
    }


    private static bool FastCompareMessage(LogMessage log, string message, DebugStackTrace? stackTrace, LogSeverity severity)
    {
        // Fastest checks first
        if (log.severity != severity)
            return false;

        if ((log.trace == null) != (stackTrace == null))
            return false;

        if (log.trace != null && log.trace.stackFrames.Length != stackTrace.stackFrames.Length)
            return false;

        // Slower checks later
        // Check stack frame 0 earlier since it potentially avoids comparing big messages.
        if (stackTrace != null && stackTrace.stackFrames.Length > 0)
        {
            DebugStackFrame frame = log.trace.stackFrames[0];
            DebugStackFrame frame2 = stackTrace.stackFrames[0];

            if (frame.line != frame2.line && frame.column != frame2.column)
                return false;

            if (frame.fileName != frame2.fileName)
                return false;
        }

        if (log.message != message)
            return false;

        // Potentially slowest check last
        if (log.trace != null)
        {
            for (int i = 1; i < log.trace.stackFrames.Length; i++)
            {
                DebugStackFrame frame = log.trace.stackFrames[i];
                DebugStackFrame frame2 = stackTrace.stackFrames[i];

                if (frame.line != frame2.line && frame.column != frame2.column)
                    return false;

                if (frame.fileName != frame2.fileName)
                    return false;
            }
        }

        return true;
    }


    private bool SearchDuplicate(string message, DebugStackTrace? stackTrace, LogSeverity severity)
    {
        // Slow, but not slow enough for it to not be nice to have.
        // Takes around a millisecond if every check is a perfect match and has a stack trace of depth 15.
        foreach (LogMessage msg in _logMessages)
        {
            if (FastCompareMessage(msg, message, stackTrace, severity))
            {
                msg.DuplicateCount++;
                return true;
            }
        }

        return false;
    }


    private void OnLog(string message, DebugStackTrace? stackTrace, LogSeverity logSeverity)
    {
        if (SearchDuplicate(message, stackTrace, logSeverity))
            return;

        _logMessages.Add(new LogMessage(message, stackTrace, logSeverity));

        if (_logMessages.Count > MaxLogs)
            _logMessages.RemoveAt(0);
    }


    protected override void Draw()
    {
        gui.CurrentNode.Layout(LayoutType.Column);
        gui.CurrentNode.ScaleChildren();

        using (gui.Node("Header").ExpandWidth().MaxHeight(EditorStylePrefs.Instance.ItemSize).Layout(LayoutType.Row).Enter())
        {
            if (EditorGUI.StyledButton(FontAwesome6.TrashCan + "  Clear", 75, EditorStylePrefs.Instance.ItemSize, false, tooltip: "Clear all logs"))
            {
                _logMessages.RemoveAll(x => !x.message.StartsWith(UnclearableErrorPrefix));
                _selectedMessage = null;
            }

            Color disabled = Color.white * 0.45f;
            Color enabled = Color.white;

            gui.Node("Padding").Width(gui.CurrentNode.LayoutData.Rect.width - (75 + (30 * 4)));

            void DrawMessageButton(string text, ref bool value, string tooltip)
            {
                Color color = value ? enabled : disabled;

                if (EditorGUI.StyledButton(text, 30, EditorStylePrefs.Instance.ItemSize, false, textcolor: color, tooltip: tooltip))
                    value = !value;
            }

            DrawMessageButton(FontAwesome6.Terminal, ref GeneralPreferences.Instance.ShowDebugLogs, "Logs");
            DrawMessageButton(FontAwesome6.TriangleExclamation, ref GeneralPreferences.Instance.ShowDebugWarnings, "Warnings");
            DrawMessageButton(FontAwesome6.CircleExclamation, ref GeneralPreferences.Instance.ShowDebugErrors, "Errors");
            DrawMessageButton(FontAwesome6.CircleCheck, ref GeneralPreferences.Instance.ShowDebugSuccess, "Success");
        }

        _messagesToRender.Clear();

        LogSeverity mask = 0;

        if (GeneralPreferences.Instance.ShowDebugLogs)
            mask |= LogSeverity.Normal;

        if (GeneralPreferences.Instance.ShowDebugWarnings)
            mask |= LogSeverity.Warning;

        if (GeneralPreferences.Instance.ShowDebugSuccess)
            mask |= LogSeverity.Success;

        if (GeneralPreferences.Instance.ShowDebugErrors)
            mask |= LogSeverity.Error | LogSeverity.Exception;

        foreach (LogMessage message in _logMessages)
        {
            if (!((mask & message.severity) == message.severity))
                continue;

            _messagesToRender.Add(message);
        }

        using (gui.Node("LogContent").ExpandHeight().ExpandWidth().Layout(LayoutType.Row).ScaleChildren().Spacing(10).Padding(0, 5, 5, 5).Enter())
        {
            using (gui.Node("List").ExpandHeight().Layout(LayoutType.Column).Scroll(true, false).Clip().Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);

                double viewHeight = gui.CurrentNode.LayoutData.Rect.height;

                int messageBottom = Math.Max(0, (int)Math.Floor(gui.CurrentNode.LayoutData.VScroll / MessageHeight));
                int messageTop = Math.Min((int)Math.Ceiling((gui.CurrentNode.LayoutData.VScroll + viewHeight) / MessageHeight), _messagesToRender.Count);

                gui.Node("TopPadding").Height(messageBottom * MessageHeight);

                for (int i = messageBottom; i < Math.Min(messageTop, MaxLogs); i++)
                {
                    using (gui.Node("ConsoleMessage", i).ExpandWidth().Height(MessageHeight).Enter())
                    {
                        DrawMessage(_messagesToRender[i], i);
                    }
                }

                gui.Node("BottomPadding").Height((_messagesToRender.Count - messageTop) * MessageHeight);
            }

            if (_selectedMessage == null)
                return;

            using (gui.Node("Expanded").ExpandHeight().Layout(LayoutType.Column).Spacing(5).Scroll().Clip().Enter())
            {
                if (DrawExpandedMessage(_selectedMessage))
                    _selectedMessage = null;
            }
        }
    }


    private const float MessageHeight = 40;

    private void DrawMessage(LogMessage message, int index)
    {
        Interactable interact = gui.GetInteractable();

        if (interact.TakeFocus())
            _selectedMessage = message;

        Color bgColor = Color.clear;

        if (interact.IsFocused())
            bgColor = EditorStylePrefs.Instance.Highlighted * 0.7f;
        else if (interact.IsHovered())
            bgColor = EditorStylePrefs.Instance.Hovering;
        else if (index % 2 == 1)
            bgColor = EditorStylePrefs.Instance.WindowBGOne * 0.9f;

        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, bgColor, (float)EditorStylePrefs.Instance.ButtonRoundness);

        GetSeverityStyles(message.severity, out string icon, out Color color);

        Rect rect = gui.CurrentNode.LayoutData.Rect;

        Rect textRect = rect;

        textRect.x += 7.5;

        bool hasTrace = message.trace != null && message.trace.stackFrames.Length > 0;

        Vector2 textPos = textRect.Position;
        textPos.y += (rect.height / 2) - (7.5 + (hasTrace ? 5 : 0));

        Rect mainRect = textRect;
        mainRect.y = textPos.y;
        mainRect.height = 22;

        gui.Draw2D.DrawText(Font.DefaultFont, icon + " " + message.message, 20, textPos, color, 0, mainRect);

        if (message.DuplicateCount > 0)
        {
            using (gui.Node("DuplicateCounter", index).Scale(35, 15).Top((MessageHeight / 2) - 7.5).Enter())
            {
                gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 10));

                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, Color.white * 0.6f, 8);

                string counter = message.DuplicateCount > 1000 ? "999+" : (message.DuplicateCount + 1).ToString();

                gui.Draw2D.DrawText(counter, 20, gui.CurrentNode.LayoutData.Rect);
            }
        }

        if (hasTrace)
        {
            textPos.y += 15;

            DebugStackFrame frame = message.trace!.stackFrames[0];

            string frameText = frame.ToString();

            gui.Draw2D.DrawText(Font.DefaultFont, frameText, 17.5, textPos, Color.gray, 0, textRect);

            if (interact.IsHovered() && gui.IsPointerDoubleClick())
                OpenStackFrame(frame);
        }

        Vector2 left = rect.Position;
        left.y += rect.height;

        Vector2 right = left;
        right.x += rect.width;

        left.x += 5;
        right.x -= 5;

        gui.Draw2D.DrawLine(left, right, EditorStylePrefs.Instance.Borders, 1);
    }


    private bool DrawExpandedMessage(LogMessage message)
    {
        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);

        GetSeverityStyles(message.severity, out string icon, out Color color);
        string selMsg = icon + " " + message.message;

        Vector2 msgSize = Font.DefaultFont.CalcTextSize(selMsg, font_size: 22, 0);

        using (gui.Node("Header").Width(msgSize.x).Height(msgSize.y).Margin(10, 0, 0, 10).Enter())
        {
            Rect headerRect = gui.CurrentNode.LayoutData.Rect;
            Vector2 textPos = headerRect.Position;

            gui.Draw2D.DrawText(Font.DefaultFont, selMsg, 20, textPos, color);
        }

        if (message.trace != null && message.trace.stackFrames.Length != 0)
        {
            for (int i = 0; i < message.trace.stackFrames.Length; i++)
            {
                DebugStackFrame frame = message.trace.stackFrames[i];
                string frameText = frame.ToString();
                Vector2 frameSize = Font.DefaultFont.CalcTextSize(frameText, font_size: 21, 0);

                using (gui.Node("StackFrame", i).Margin(0, 0, 0, 10).Width(frameSize.x).Height(15).Enter())
                {
                    Interactable interact = gui.GetInteractable();
                    Color col = Color.white * 0.65f;

                    if (interact.IsHovered())
                    {
                        col = EditorStylePrefs.Instance.Highlighted * 0.7f;

                        if (gui.IsPointerClick())
                            OpenStackFrame(frame);
                    }

                    Rect frameRect = gui.CurrentNode.LayoutData.Rect;
                    gui.Draw2D.DrawText(Font.DefaultFont, frameText, 19, frameRect.Position, col, 0, frameRect);
                }
            }
        }

        using (gui.Node("CloseBtn").IgnoreLayout().Scale(18).Top(10).Enter())
        {
            gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 10));

            Interactable interact = gui.GetInteractable();

            Rect closeRect = gui.CurrentNode.LayoutData.Rect;

            if (interact.TakeFocus())
            {
                gui.Draw2D.DrawRectFilled(closeRect, EditorStylePrefs.Instance.Highlighted, 5, CornerRounding.All);
                return true;
            }
            else if (interact.IsHovered())
                gui.Draw2D.DrawRectFilled(closeRect, EditorStylePrefs.Instance.Hovering, 5, CornerRounding.All);

            closeRect.y += 1;
            gui.Draw2D.DrawText(FontAwesome6.Xmark, 23, closeRect);
        }

        return false;
    }


    private static void OpenStackFrame(DebugStackFrame frame)
    {
        if (frame.fileName == null)
            return;

        AssetDatabase.OpenPath(new FileInfo(frame.fileName), frame.line, frame.column);
    }


    private static void GetSeverityStyles(LogSeverity severity, out string icon, out Color color)
    {
        color = Color.white;
        icon = FontAwesome6.Terminal;

        switch (severity)
        {
            case LogSeverity.Success:
                color = Color.green;
                icon = FontAwesome6.CircleCheck;
                break;

            case LogSeverity.Warning:
                color = Color.yellow;
                icon = FontAwesome6.TriangleExclamation;
                break;

            case LogSeverity.Error:
                color = Color.red;
                icon = FontAwesome6.CircleExclamation;
                break;

            case LogSeverity.Exception:
                color = Color.red;
                icon = FontAwesome6.TriangleExclamation; // Triangles are a bit more 'danger'y than circles, so use them for exceptions too.
                break;
        };
    }
}
