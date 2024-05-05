using Prowl.Runtime;
using Hexa.NET.ImGui;
using Prowl.Icons;
using Prowl.Editor.Editor.Preferences;

namespace Prowl.Editor.EditorWindows;

public class OldConsoleWindow : OldEditorWindow {

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.MenuBar;
    private uint _logCount;
    private readonly List<LogMessage> _logMessages;
    private int _maxLogs = 100;

    public OldConsoleWindow() : base()
    {
        Title = FontAwesome6.Terminal + " Console";
        _logMessages = new List<LogMessage>();
        Debug.OnLog += OnLog;
    }

    private void OnLog(string message, LogSeverity logSeverity)
    {
        if (logSeverity == LogSeverity.Normal && !GeneralPreferences.Instance.ShowDebugLogs) return;
        else if (logSeverity == LogSeverity.Warning && !GeneralPreferences.Instance.ShowDebugWarnings) return;
        else if (logSeverity == LogSeverity.Error && !GeneralPreferences.Instance.ShowDebugErrors) return;
        else if (logSeverity == LogSeverity.Success && !GeneralPreferences.Instance.ShowDebugSuccess) return;

        _logMessages.Add(new LogMessage(message, logSeverity));
        if (_logMessages.Count > _maxLogs)
            _logMessages.RemoveAt(0);
        _logCount++;
    }

    protected override void Draw()
    {
        DrawToolBar();
        DrawLogs();
    }

    private void DrawLogs()
    {
        for (int i = _logMessages.Count; i-- > 0;)
        {
            var logSeverity = _logMessages[i].LogSeverity;
            if (logSeverity == LogSeverity.Normal && !GeneralPreferences.Instance.ShowDebugLogs) continue;
            else if (logSeverity == LogSeverity.Warning && !GeneralPreferences.Instance.ShowDebugWarnings) continue;
            else if (logSeverity == LogSeverity.Error && !GeneralPreferences.Instance.ShowDebugErrors) continue;
            else if (logSeverity == LogSeverity.Success && !GeneralPreferences.Instance.ShowDebugSuccess) continue;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.2f, 0.2f, 0.2f, 1.0f));
            var size = ImGui.CalcTextSize(_logMessages[i].Message, ImGui.GetWindowWidth());
            ImGui.BeginChild($"LogEntry_{i}", new System.Numerics.Vector2(-1, size.Y), ImGuiChildFlags.Border);
            _logMessages[i].Draw();
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.Spacing();
        }
    }

    private void DrawToolBar()
    {
        if (ImGui.BeginMenuBar())
        {
            ImGui.Text($"{_logCount}");

            if (ImGui.Button("Clear"))
            {
                _logMessages.Clear();
                _logCount = 0;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            GeneralPreferences.Instance.ShowDebugLogs = ShowToggleFor(GeneralPreferences.Instance.ShowDebugLogs, FontAwesome6.Info);
            GeneralPreferences.Instance.ShowDebugWarnings = ShowToggleFor(GeneralPreferences.Instance.ShowDebugWarnings, FontAwesome6.Exclamation);
            GeneralPreferences.Instance.ShowDebugErrors = ShowToggleFor(GeneralPreferences.Instance.ShowDebugErrors, FontAwesome6.Bug);
            GeneralPreferences.Instance.ShowDebugSuccess = ShowToggleFor(GeneralPreferences.Instance.ShowDebugSuccess, FontAwesome6.Check);

            ImGui.EndMenuBar();
        }
    }

    private static bool ShowToggleFor(bool selected, string name)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(1, 1, 1, 0.5f));
            if (ImGui.Button(name, new System.Numerics.Vector2(50, 0)))
            {
                ImGui.PopStyleColor();
                return false;
            }
            ImGui.PopStyleColor();
        }
        else if (ImGui.Button(name, new System.Numerics.Vector2(50, 0)))
            return true;
        return selected;
    }

    private record LogMessage(string Message, LogSeverity LogSeverity)
    {
        public readonly string Message = Message;
        public readonly LogSeverity LogSeverity = LogSeverity;

        public void Draw()
        {
            var color = ToColor(LogSeverity);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(Message);
            ImGui.PopStyleColor();
        }

        private static System.Numerics.Vector4 ToColor(LogSeverity logSeverity) => logSeverity switch {
            LogSeverity.Normal => new System.Numerics.Vector4(1, 1, 1, 1),
            LogSeverity.Success => new System.Numerics.Vector4(0, 1, 0, 1),
            LogSeverity.Warning => new System.Numerics.Vector4(1, 1, 0, 1),
            LogSeverity.Error => new System.Numerics.Vector4(1, 0, 0, 1),
            _ => throw new NotImplementedException("log level not implemented")
        };
    }

}
