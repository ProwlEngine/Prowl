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

        [Text("Debugging:")]
        public bool ShowDebugLogs = true;
        public bool ShowDebugWarnings = true;
        public bool ShowDebugErrors = true;
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

    [FilePath("Editor.pref", FilePathAttribute.Location.EditorPreference)]
    public class EditorPreferences : ScriptableSingleton<EditorPreferences>
    {
        [Text("UI:")]
        public bool AntiAliasing = true;

    }

    [FilePath("EditorStyle.pref", FilePathAttribute.Location.EditorPreference)]
    public class EditorStylePrefs : ScriptableSingleton<EditorStylePrefs>
    {
        [Text("Colors:")]
        public double Disabled      = 0.7;
        public Color LesserText     = new(110, 110, 120);
        public Color Background     = new(15, 15, 18);
        public Color WindowBGOne    = new(31, 33, 40);
        public Color WindowBGTwo    = new Color(31, 33, 40) * 0.6f;
        public Color Borders        = new(49, 52, 66);
        public Color Hovering       = new Color(255, 255, 255) * 0.8f;
        public Color Highlighted    = Indigo;
        public Color Ping           = Yellow;
        public Color DropHighlight  = Orange;
        public Color Warning        = Red;

        public bool UseRandomColorForComponents = false;
        public Color ComponentColor = new(25, 25, 25);
        public bool UseRandomColorForAssets = false;

        [Text("Sizing:")]
        public double Scale = 1;
        public double ItemSize = 30;

        [Text("Spacing:")]
        public double DockSpacing = 8;

        [Text("Rounding:")]
        public double WindowRoundness = 10;
        public double TabRoundness = 4;
        public double AssetRoundness = 4;
        public double ButtonRoundness = 10;

        // Base Colors 
        public static Color Black => new(0, 0, 0, 255);
        public static Color Base4 => new(100, 100, 110);
        public static Color Base5 => new(139, 139, 147);
        public static Color Base6 => new(112, 112, 124);
        public static Color Base7 => new(138, 138, 152);
        public static Color Base8 => new(169, 169, 183);
        public static Color Base9 => new(208, 208, 218);
        public static Color Base10 => new(234, 234, 244);
        public static Color Base11 => new(255, 255, 255);

        // Accents
        public static Color Blue => new(39, 117, 255);
        public static Color Green => new(80, 209, 178);
        public static Color Violet => new(119, 71, 202);
        public static Color Orange => new(236, 140, 85);
        public static Color Yellow => new(236, 230, 99);
        public static Color Indigo => new(84, 21, 241);
        public static Color Emerald => new(94, 234, 141);
        public static Color Fuchsia => new(221, 80, 214);
        public static Color Red => new(226, 55, 56);
        public static Color Sky => new(11, 214, 244);
        public static Color Pink => new(251, 123, 184);

        public static Color RandomPastelColor(int seed)
        {
            System.Random random = new System.Random(seed);
            float r = (float)(random.NextDouble() * 0.5 + 0.5);
            float g = (float)(random.NextDouble() * 0.5 + 0.5);
            float b = (float)(random.NextDouble() * 0.5 + 0.5);
            return new Color(r, g, b) * 0.8f;
        }

        public override void OnValidate()
        {
            Scale = MathD.Clamp(Scale, 0.5, 2);
            ItemSize = MathD.Clamp(ItemSize, 20, 40);

            DockSpacing = MathD.Clamp(DockSpacing, 0, 8);
        }

        [GUIButton("Reset to Default")]
        public static void ResetDefault()
        {
            // Shortcut to reset values
            _instance = new EditorStylePrefs();
            _instance.Save();
        }
    }
}
