using Prowl.Runtime;
using HexaEngine.ImGuiNET;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

// TODO: Overhaul

public class ConsoleWindow : EditorWindow {

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.MenuBar;
    private uint _logCount;
    private readonly List<LogMessage> _logMessages;
    private int _maxLogs = 50;
    private bool _showNormalLogMessages = true;
    private bool _showSuccessLogMessages = true;
    private bool _showWarningLogMessages = true;
    private bool _showErrorLogMessages = true;

    public ConsoleWindow() : base() {
        Title = "Console";
        _logMessages = new List<LogMessage>();
        Debug.OnLog += OnLog;
    }
    
    private void OnLog(string message, LogSeverity logSeverity) {
        _logMessages.Add(new LogMessage(message, logSeverity));
        if(_logMessages.Count > _maxLogs)
            _logMessages.RemoveAt(0);
        
        _logCount++;
    }
    
    protected override void Draw() {
        DrawToolBar();
        DrawLogs();
    }
    
    private void DrawLogs() {
        for(int i = 0; i < _logMessages.Count; i++) {
//            if(_logMessages[i].LogSeverity == LogSeverity.Error && !_showErrorLogMessages)
//                continue;
            _logMessages[i].Draw();
        }
    }
    
    private void DrawToolBar() {
        if(ImGui.BeginMenuBar()) {
            
            if(ImGui.Button("Clear")) {
                _logMessages.Clear();
                _logCount = 0;
            }
            ImGui.Text($"Log Counter: {_logCount}");
            
            ImGui.EndMenuBar();
        }
        
//        ImGui.Checkbox("Errors", ref _showErrorLogMessages);
    }
    
    private record LogMessage(string Message, LogSeverity LogSeverity) {
        
        public readonly string Message = Message;
        public readonly LogSeverity LogSeverity = LogSeverity;
        
        public void Draw() {
            ImGui.TextColored(ToColor(LogSeverity), Message);
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
