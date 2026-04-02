using System.Drawing;

using Prowl.Scribe;

namespace Prowl.Editor;

public static class EditorTheme
{
    public static FontFile? DefaultFont;
    // Backgrounds
    public static Color WindowBackground = Color.FromArgb(255, 30, 30, 30);
    public static Color PanelBackground = Color.FromArgb(255, 37, 37, 38);
    public static Color HeaderBackground = Color.FromArgb(255, 45, 45, 48);
    public static Color InputBackground = Color.FromArgb(255, 60, 60, 60);
    public static Color MenuBarBackground = Color.FromArgb(255, 45, 45, 48);

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
    public static Color Border = Color.FromArgb(255, 60, 60, 63);
    public static Color BorderFocused = Color.FromArgb(255, 51, 122, 183);

    // Splitter
    public static Color Splitter = Color.FromArgb(255, 25, 25, 25);
    public static Color SplitterHovered = Color.FromArgb(255, 51, 122, 183);

    // Tab
    public static Color TabActive = Color.FromArgb(255, 37, 37, 38);
    public static Color TabInactive = Color.FromArgb(255, 45, 45, 48);
    public static Color TabHovered = Color.FromArgb(255, 55, 55, 58);

    // Sizing
    public const float MenuBarHeight = 26f;
    public const float StatusBarHeight = 22f;
    public const float TabBarHeight = 26f;
    public const float RowHeight = 22f;
    public const float SplitterSize = 4f;
    public const float Spacing = 2f;
    public const float Padding = 4f;
    public const float FontSize = 14f;
    public const float LabelWidth = 120f;
}
