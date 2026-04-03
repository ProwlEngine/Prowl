using System.Drawing;

using Prowl.Scribe;

namespace Prowl.Editor;

public static class EditorTheme
{
    public static FontFile? DefaultFont;

    // 4-shade system: Darkest → Dark → Normal → Bright
    public static Color Darkest = Color.FromArgb(255, 22, 22, 24);   // Menu bar
    public static Color Dark    = Color.FromArgb(255, 10, 10, 12);   // Background (near black)
    public static Color Normal  = Color.FromArgb(255, 37, 37, 40);   // Windows/panels
    public static Color Bright  = Color.FromArgb(255, 65, 65, 70);   // Scrollbar, outline

    // Aliases for readability
    public static Color MenuBarBackground => Darkest;
    public static Color WindowBackground => Normal;
    public static Color PanelBackground => Normal;
    public static Color HeaderBackground = Color.FromArgb(255, 30, 30, 33);
    public static Color InputBackground = Color.FromArgb(255, 28, 28, 30);
    public static Color Border => Bright;

    // Text
    public static Color Text = Color.FromArgb(255, 220, 220, 220);
    public static Color TextDim = Color.FromArgb(255, 150, 150, 150);
    public static Color TextDisabled = Color.FromArgb(255, 90, 90, 90);

    // Interactive
    public static Color ButtonNormal = Color.FromArgb(255, 55, 55, 58);
    public static Color ButtonHovered = Color.FromArgb(255, 70, 70, 74);
    public static Color ButtonActive = Color.FromArgb(255, 40, 40, 43);

    // Accent
    public static Color Accent = Color.FromArgb(255, 51, 122, 183);
    public static Color AccentDim = Color.FromArgb(255, 40, 90, 140);

    // Borders
    public static Color BorderFocused = Color.FromArgb(255, 51, 122, 183);

    // Splitter
    public static Color Splitter = Color.FromArgb(255, 25, 25, 25);
    public static Color SplitterHovered = Color.FromArgb(255, 51, 122, 183);

    // Tab
    public static Color TabActive => Normal;
    public static Color TabInactive => Darkest;
    public static Color TabHovered = Color.FromArgb(255, 55, 55, 58);

    // Sizing
    public const float MenuBarHeight = 26f;
    public const float StatusBarHeight = 22f;
    public const float TabBarHeight = 26f;
    public const float RowHeight = 22f;
    public const float SplitterSize = 14f;
    public const float DockPadding = 14f;
    public const float Spacing = 2f;
    public const float Padding = 4f;
    public const float FontSize = 17f;
    public const float LabelWidth = 120f;
}
