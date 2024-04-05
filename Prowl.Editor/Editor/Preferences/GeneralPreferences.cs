using Prowl.Editor.Utilities;
using Prowl.Runtime;

namespace Prowl.Editor.Editor.Preferences
{
    [FilePath("General.pref", FilePathAttribute.Location.PreferencesFolder)]
    public class GeneralPreferences : ScriptableSingleton<GeneralPreferences>
    {
        [Text("General:")]
        public bool VSync = true;
        public int TargetFPS = 120;

        [Indent]
        [Text("Debugging:")]
        public bool ShowDebugLogs = true;
        public bool ShowDebugWarnings = true;
        public bool ShowDebugErrors = true;
        [Unindent]
        public bool ShowDebugSuccess = true;
    }
}
