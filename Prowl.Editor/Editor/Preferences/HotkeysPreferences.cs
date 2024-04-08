using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Silk.NET.Input;

namespace Prowl.Editor.Editor.Preferences
{
    [FilePath("Hotkeys.pref", FilePathAttribute.Location.EditorPreference)]
    public class Hotkeys : ScriptableSingleton<Hotkeys>
    {
        public class Hotkey
        {
            public Key Key { get; set; }

            public bool Ctrl { get; set; }
            public bool Alt { get; set; }
            public bool Shift { get; set; }
        }

        public Dictionary<string, Hotkey> hotkeys { get; set; } = new();

        public static bool IsHotkeyDown(string name, Hotkey defaultKey)
        {
            if (Instance.hotkeys.TryGetValue(name, out var hotkey))
            {
                if (Input.GetKeyDown(hotkey.Key))
                {
                    bool ctrl = Input.GetKey(Key.ControlLeft) == hotkey.Ctrl;
                    bool alt = Input.GetKey(Key.AltLeft) == hotkey.Alt;
                    bool shift = Input.GetKey(Key.ShiftLeft) == hotkey.Shift;
                    if (ctrl && alt && shift)
                        return true;
                }
                return Input.GetKeyDown(hotkey.Key) && Input.GetKey(Key.ControlLeft) == hotkey.Ctrl && Input.GetKey(Key.AltLeft) == hotkey.Alt && Input.GetKey(Key.ShiftLeft) == hotkey.Shift;
            }
            else
            {
                Instance.hotkeys.Add(name, defaultKey);
                Instance.Save();
            }
            return false;
        }

    }
}
