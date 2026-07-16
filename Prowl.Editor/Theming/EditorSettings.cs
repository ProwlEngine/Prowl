using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Prowl.Editor.Core;
using Prowl.Runtime;

namespace Prowl.Editor.Theming;

/// <summary>
/// Global editor settings. Saved to AppData/Prowl/EditorSettings.json.
/// Persists across projects. Contains preferences and the active theme.
/// </summary>
public class EditorSettings
{
    private static EditorSettings? _instance;
    public static EditorSettings Instance => _instance ??= Load();

    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Prowl", "EditorSettings.json");

    // Preferences
    public string DefaultProjectsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProwlProjects");
    public string Locale { get; set; } = "en";
    public bool AutoSaveLayout { get; set; } = true;
    public bool ReimportOnFocusOnly { get; set; } = true;
    public int ThumbnailSize { get; set; } = 32;

    public int WindowX { get; set; } = -1;

    public int WindowY { get; set; } = -1;

    public int WindowWidth { get; set; } = 1280;

    public int WindowHeight { get; set; } = 800;

    public bool WindowMaximized { get; set; } = false;

    // Shortcuts only user-overridden bindings are stored
    public Dictionary<string, ShortcutBinding> ShortcutOverrides { get; set; } = new();

    // IDs of interactive guides/tutorials the user has already completed or skipped.
    public List<string> SeenGuides { get; set; } = new();

    // Theme
    public EditorThemeData Theme { get; set; } = EditorThemeData.CreateDefault();

    /// <summary>Apply the current theme to EditorTheme's static fields.</summary>
    public void ApplyTheme()
    {
        var t = Theme;
        t.InitRamps();

        // Origami's default theme is the base; this data is the customization overlaid on top of it
        // (see EditorTheme.BuildOrigamiTheme). SyncOrigami below rebuilds the live palette from it.
        EditorTheme.Customization = t;

        EditorTheme.DefaultFontName = t.DefaultFontName;
        EditorTheme.DefaultBoldFontName = t.DefaultBoldFontName;

        EditorApplication.Instance?.InitializeFont();

        EditorTheme.UserScale = t.UserScale;

        // Sizing
        EditorTheme.MenuBarHeight = t.MenuBarHeight;
        EditorTheme.StatusBarHeight = t.StatusBarHeight;
        EditorTheme.RowHeight = t.RowHeight;
        EditorTheme.FontSize = t.FontSize;
        EditorTheme.LabelWidth = t.LabelWidth;
        EditorTheme.Spacing = t.Spacing;
        EditorTheme.Padding = t.Padding;
        EditorTheme.SplitterSize = t.DockSpacing;
        EditorTheme.DockPadding = t.DockSpacing;
        EditorTheme.TabBarHeight = t.TabBarHeight;
        EditorTheme.TabPadding = t.TabPadding;
        EditorTheme.Roundness = t.Roundness;

        // Effects
        EditorTheme.GlassBlur = t.GlassBlur;
        EditorTheme.BlurAmount = t.BlurAmount;
        EditorTheme.DropShadows = t.DropShadows;
        EditorTheme.AccentGlow = t.AccentGlow;
        EditorTheme.AntiAliasing = t.AntiAliasing;
        EditorTheme.AnimatedBackground = t.AnimatedBackground;
        EditorTheme.BackgroundSpeed = t.BackgroundSpeed;
        EditorTheme.BackgroundStyle = t.BackgroundStyle;
        EditorTheme.BackgroundColorA = ColorRamp.ParseHex(t.BackgroundColorA);
        EditorTheme.BackgroundColorB = ColorRamp.ParseHex(t.BackgroundColorB);
        EditorTheme.BgShowGradients = t.BgShowGradients;
        EditorTheme.BgShowStars = t.BgShowStars;
        EditorTheme.BgShowComets = t.BgShowComets;
        EditorTheme.BackgroundVoidColor = ColorRamp.ParseHex(t.BackgroundVoidColor);

        // Push the freshly-applied editor theme into Origami. Brief lerp so user-visible
        // theme tweaks animate instead of snapping.
        EditorTheme.SyncOrigami();
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to save editor settings: {ex.Message}");
        }
    }

    private static EditorSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<EditorSettings>(json);
                if (settings != null)
                {
                    settings.Theme.InitRamps();
                    settings.ApplyTheme();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to load editor settings: {ex.Message}");
        }

        var def = new EditorSettings();
        def.ApplyTheme();
        return def;
    }

    public void ResetTheme()
    {
        Theme = EditorThemeData.CreateDefault();
        ApplyTheme();
        Save();
    }
}
