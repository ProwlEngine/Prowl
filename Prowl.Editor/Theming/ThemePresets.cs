namespace Prowl.Editor.Theming;

/// <summary>
/// A built-in color theme. Stops override a ramp's computed offsets, for themes the dark defaults can't be
/// shifted into. Solid themes use a flat background, so they turn the nebula and the glass blur off.
/// </summary>
public readonly record struct ThemePreset(string Name, string Accent, string Accent2, string Bg, string Panel, string Text,
    bool Solid = false, string[]? AccentStops = null, string[]? NeutralStops = null, string[]? InkStops = null)
{
    /// <summary>Apply this theme's ramps and effects to <paramref name="t"/>. Status ramps and interface colour overrides return to their defaults.</summary>
    public void ApplyTo(EditorThemeData t)
    {
        SetRamp(t.Purple, Accent, AccentStops);
        SetRamp(t.Blue, Accent2);
        SetRamp(t.Neutral, Panel, NeutralStops);
        SetRamp(t.Ink, Text, InkStops);
        SetRamp(t.Red, "#FB7185");
        SetRamp(t.Green, "#4ADE80");
        SetRamp(t.Amber, "#FBBF24");

        t.GlassBlur = !Solid;
        t.WindowOpacity = Solid ? 1f : 0.8f;
        t.AnimatedBackground = !Solid;
        t.BackgroundStyle = Solid ? EditorBackgroundStyle.Color : EditorBackgroundStyle.Nebula;
        if (Solid) t.BackgroundColorA = Bg;

        t.HoverColor = t.SelectionColor = t.FocusColor = t.ScrollbarColor = t.SplitterColor = t.TooltipColor = "";
        t.Name = Name;
    }

    private static void SetRamp(ColorRamp ramp, string primary, string[]? stops = null)
    {
        ramp.Primary = primary;
        ramp.OverrideAll = stops != null;
        if (stops != null) ramp.Overrides = stops;
    }
}

/// <summary>A layout density: shape and sizing only, never colors or effects.</summary>
public readonly record struct LayoutPreset(string Name, float Roundness, float Spacing, float Padding, float RowHeight,
    float FontSize, float LabelWidth, float TabBarHeight, float TabPadding, float MenuBarHeight, float StatusBarHeight,
    float DockSpacing)
{
    public bool Matches(EditorThemeData t) =>
        t.Roundness == Roundness && t.Spacing == Spacing && t.Padding == Padding && t.RowHeight == RowHeight &&
        t.FontSize == FontSize && t.LabelWidth == LabelWidth && t.TabBarHeight == TabBarHeight &&
        t.TabPadding == TabPadding && t.MenuBarHeight == MenuBarHeight && t.StatusBarHeight == StatusBarHeight &&
        t.DockSpacing == DockSpacing;

    public void ApplyTo(EditorThemeData t)
    {
        t.Roundness = Roundness; t.Spacing = Spacing; t.Padding = Padding; t.RowHeight = RowHeight;
        t.FontSize = FontSize; t.LabelWidth = LabelWidth; t.TabBarHeight = TabBarHeight;
        t.TabPadding = TabPadding; t.MenuBarHeight = MenuBarHeight; t.StatusBarHeight = StatusBarHeight;
        t.DockSpacing = DockSpacing;
    }

    /// <summary>The layout values of a theme, under a new name.</summary>
    public static LayoutPreset From(string name, EditorThemeData t) => new(name, t.Roundness, t.Spacing, t.Padding, t.RowHeight,
        t.FontSize, t.LabelWidth, t.TabBarHeight, t.TabPadding, t.MenuBarHeight, t.StatusBarHeight, t.DockSpacing);
}

/// <summary>The editor's built-in color themes.</summary>
public static class ThemePresets
{
    /// <summary>Every built-in theme, in the order the Preferences panel shows them.</summary>
    public static readonly ThemePreset[] All =
    [
        new("Dark",     "#3B82F6", "#0EA5E9", "#111113", "#1F1F23", "#E4E4E7", Solid: true,
            NeutralStops: ["#0C0C0E", "#A0A0AA", "#18181B", "#141417", "#1F1F23", "#27272B", "#34343A"],
            InkStops: ["#4A4A50", "#6B6B72", "#8E8E96", "#B8B8BF", "#E4E4E7", "#FFFFFF", "#FFFFFF"]),
        new("Indigo",   "#6366F1", "#8B5CF6", "#0C0C1A", "#181830", "#EAEAF7"),
        new("Light",    "#4F46E5", "#7C3AED", "#B9BCC6", "#E3E5EA", "#22242E", Solid: true,
            AccentStops: ["#D6D8F2", "#C8CBF0", "#B0B5EC", "#8F95E4", "#4F46E5", "#4338CA", "#3730A3"],
            NeutralStops: ["#C6C8D0", "#4A4C5E", "#D0D2D9", "#DADCE2", "#E3E5EA", "#D3D5DC", "#C6C8D0"],
            InkStops: ["#9A9CAA", "#7E8090", "#636576", "#45475A", "#22242E", "#15161E", "#08090E"]),
        new("Bloom",    "#EC4899", "#A855F7", "#170C14", "#2A1826", "#F7E8F2"),
        new("Nebula",   "#A855F7", "#60A5FA", "#0F0C18", "#262036", "#F0EEF7"),
        new("Ember",    "#F97316", "#38BDF8", "#160F0C", "#2A1E16", "#F7EFE8"),
        new("Verdant",  "#4ADE80", "#22C55E", "#0B1410", "#182A20", "#E8F7EF"),
        new("Abyss",    "#60A5FA", "#06B6D4", "#0A0F1A", "#182233", "#E8F0F7"),
        new("Graphite", "#94A3B8", "#64748B", "#0D0F12", "#20242C", "#ECEEF2"),
        new("Solar",    "#FBBF24", "#60A5FA", "#161009", "#221A0C", "#F7F1E4"),
        new("Cyan",     "#06B6D4", "#14B8A6", "#0A1416", "#122528", "#E4F5F7"),
        new("Crimson",  "#F43F5E", "#FB923C", "#160A0D", "#2A161B", "#F7E8EB"),
        new("High Contrast", "#FFD400", "#00E5FF", "#000000", "#000000", "#FFFFFF", Solid: true,
            AccentStops: ["#332B00", "#4D4000", "#806A00", "#B39500", "#FFD400", "#FFE14D", "#FFEB80"],
            NeutralStops: ["#000000", "#FFFFFF", "#000000", "#000000", "#000000", "#1F1F1F", "#3A3A3A"],
            InkStops: ["#C8C8C8", "#DDDDDD", "#EEEEEE", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF"]),
    ];

    /// <summary>The built-in layout densities. "Default" matches the EditorThemeData defaults.</summary>
    public static readonly LayoutPreset[] Layouts =
    [
        new("Default",  Roundness: 6f,  Spacing: 2f, Padding: 6f, RowHeight: 24f, FontSize: 17f, LabelWidth: 150f, TabBarHeight: 32f, TabPadding: 12f, MenuBarHeight: 40f, StatusBarHeight: 26f, DockSpacing: 6f),
        new("Compact",  Roundness: 0f,  Spacing: 1f, Padding: 4f, RowHeight: 20f, FontSize: 15f, LabelWidth: 130f, TabBarHeight: 26f, TabPadding: 8f,  MenuBarHeight: 32f, StatusBarHeight: 22f, DockSpacing: 3f),
        new("Spacious", Roundness: 10f, Spacing: 4f, Padding: 9f, RowHeight: 28f, FontSize: 18f, LabelWidth: 170f, TabBarHeight: 36f, TabPadding: 14f, MenuBarHeight: 44f, StatusBarHeight: 28f, DockSpacing: 8f),
    ];

    /// <summary>The theme a fresh install and Reset use.</summary>
    public static ThemePreset Default => All[0];
}
