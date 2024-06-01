using Prowl.Editor.EditorWindows;
using Prowl.Editor.Utilities;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences
{
    [FilePath("General.pref", FilePathAttribute.Location.EditorPreference)]
    public class GeneralPreferences : ScriptableSingleton<GeneralPreferences>
    {
        [Text("General:")]
        public bool LockFPS = false;
        [ShowIf("LockFPS")]
        public int TargetFPS = 0;
        [ShowIf("LockFPS", true)]
        public bool VSync = true;

        [Indent]
        [Text("Debugging:")]
        public bool ShowDebugLogs = true;
        public bool ShowDebugWarnings = true;
        public bool ShowDebugErrors = true;
        [Unindent]
        public bool ShowDebugSuccess = true;

        [Text("Game View:")]
        public bool AutoFocusGameView = true;
        public bool AutoRefreshGameView = true;
        public GameWindow.Resolutions Resolution = GameWindow.Resolutions.fit;
        [HideInInspector]
        public int CurrentWidth = 1280;
        [HideInInspector]
        public int CurrentHeight = 720;

    }
}
