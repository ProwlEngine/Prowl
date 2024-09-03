using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor
{
    public class ConsoleWindow : EditorWindow
    {
        protected override double Width { get; } = 512 + (512 / 2);
        protected override double Height { get; } = 256;

        private uint _logCount;
        private readonly List<LogMessage> _logMessages;
        private int _maxLogs = 100;

        public ConsoleWindow() : base()
        {
            Title = FontAwesome6.Terminal + " Console";
            _logMessages = new List<LogMessage>();
            Debug.OnLog += OnLog;
        }

        private void OnLog(string message, LogSeverity logSeverity)
        {
            _logMessages.Add(new LogMessage(message, logSeverity));
            if (_logMessages.Count > _maxLogs)
                _logMessages.RemoveAt(0);
            _logCount++;
        }

        protected override void Draw()
        {
            gui.CurrentNode.Layout(LayoutType.Column);
            gui.CurrentNode.ScaleChildren();

            using (gui.Node("Header").Width(Size.Percentage(1f)).MaxHeight(EditorStylePrefs.Instance.ItemSize).Layout(LayoutType.Row).Enter())
            {
                if (EditorGUI.StyledButton(FontAwesome6.TrashCan + "  Clear", 75, EditorStylePrefs.Instance.ItemSize, false, null, null, 0, tooltip: "Clear all logs"))
                {
                    _logMessages.Clear();
                    _logCount = 0;
                }

                // Logs
                if (EditorGUI.StyledButton(FontAwesome6.Terminal, 30, EditorStylePrefs.Instance.ItemSize, false, null, null, 0, tooltip: "Logs"))
                    GeneralPreferences.Instance.ShowDebugLogs = !GeneralPreferences.Instance.ShowDebugLogs;
                DrawHand(GeneralPreferences.Instance.ShowDebugLogs);
                // Warnings
                if (EditorGUI.StyledButton(FontAwesome6.TriangleExclamation, 30, EditorStylePrefs.Instance.ItemSize, false, null, null, 0, tooltip: "Warnings"))
                    GeneralPreferences.Instance.ShowDebugWarnings = !GeneralPreferences.Instance.ShowDebugWarnings;
                DrawHand(GeneralPreferences.Instance.ShowDebugWarnings);
                // Errors
                if (EditorGUI.StyledButton(FontAwesome6.CircleExclamation, 30, EditorStylePrefs.Instance.ItemSize, false, null, null, 0, tooltip: "Errors"))
                    GeneralPreferences.Instance.ShowDebugErrors = !GeneralPreferences.Instance.ShowDebugErrors;
                DrawHand(GeneralPreferences.Instance.ShowDebugErrors);
                // Success
                if (EditorGUI.StyledButton(FontAwesome6.CircleCheck, 30, EditorStylePrefs.Instance.ItemSize, false, null, null, 0, tooltip: "Success"))
                    GeneralPreferences.Instance.ShowDebugSuccess = !GeneralPreferences.Instance.ShowDebugSuccess;
                DrawHand(GeneralPreferences.Instance.ShowDebugSuccess);
            }

            using (gui.Node("List").Width(Size.Percentage(1f)).Padding(0, 3, 3, 3).Clip().Scroll().Enter())
            {
                double height = 0;
                for (int i = _logMessages.Count; i-- > 0;)
                {
                    var logSeverity = _logMessages[i].LogSeverity;
                    if (logSeverity == LogSeverity.Normal && !GeneralPreferences.Instance.ShowDebugLogs) continue;
                    else if (logSeverity == LogSeverity.Warning && !GeneralPreferences.Instance.ShowDebugWarnings) continue;
                    else if (logSeverity == LogSeverity.Error && !GeneralPreferences.Instance.ShowDebugErrors) continue;
                    else if (logSeverity == LogSeverity.Success && !GeneralPreferences.Instance.ShowDebugSuccess) continue;

                    int width = (int)gui.CurrentNode.LayoutData.InnerRect.width;
                    var pos = gui.CurrentNode.LayoutData.InnerRect.Position;
                    pos.y -= gui.CurrentNode.LayoutData.VScroll;
                    var size = Font.DefaultFont.CalcTextSize(_logMessages[i].Message, 0, width - 24);

                    gui.Draw2D.DrawLine(new(pos.x + 12, pos.y + height), new(pos.x + width - 12, pos.y + height), EditorStylePrefs.Instance.Borders, 1);

                    _logMessages[i].Draw(pos + new Vector2(12, height + 8), width - 24);
                    height += size.y + 8;
                }

                // Dummy node to set the height of the scroll area
                gui.Node("Dummy").Width(5).Height(height);
            }
        }

        private void DrawHand(bool v)
        {
            if (v) return;
            using (gui.PreviousNode.Enter())
            {
                var rect = gui.CurrentNode.LayoutData.Rect;
                rect.Expand(-10);
                rect.Min += new Vector2(7, -7);
                rect.Max += new Vector2(7, -7);

                gui.Draw2D.DrawText(FontAwesome6.Hand, 20, rect, EditorStylePrefs.Instance.Warning, false, false);
            }
        }

        private record LogMessage(string Message, LogSeverity LogSeverity)
        {
            public readonly string Message = Message;
            public readonly LogSeverity LogSeverity = LogSeverity;

            public void Draw(Vector2 position, double wrapWidth)
            {
                var color = ToColor(LogSeverity);
                Gui.ActiveGUI.Draw2D.DrawText(Font.DefaultFont, Message, 20, position, color, wrapWidth);
            }

            private static System.Numerics.Vector4 ToColor(LogSeverity logSeverity) => logSeverity switch
            {
                LogSeverity.Normal => new System.Numerics.Vector4(1, 1, 1, 1),
                LogSeverity.Success => new System.Numerics.Vector4(0, 1, 0, 1),
                LogSeverity.Warning => new System.Numerics.Vector4(1, 1, 0, 1),
                LogSeverity.Error => new System.Numerics.Vector4(1, 0, 0, 1),
                _ => throw new NotImplementedException("log level not implemented")
            };
        }
    }
}
