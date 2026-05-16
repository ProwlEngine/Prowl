using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;

using Prowl.Runtime;

namespace Prowl.Editor;

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

    // Theme
    public EditorThemeData Theme { get; set; } = EditorThemeData.CreateDefault();

    /// <summary>Apply the current theme to EditorTheme's static fields.</summary>
    public void ApplyTheme()
    {
        var t = Theme;
        t.InitRamps();

        // Ramp stops → EditorTheme ramp fields
        EditorTheme.Neutral100 = t.Neutral.GetStop(0);
        EditorTheme.Neutral200 = t.Neutral.GetStop(1);
        EditorTheme.Neutral300 = t.Neutral.GetStop(2);
        EditorTheme.Neutral400 = t.Neutral.GetStop(3);
        EditorTheme.Neutral500 = t.Neutral.GetStop(4);

        EditorTheme.Purple100 = t.Purple.GetStop(0);
        EditorTheme.Purple200 = t.Purple.GetStop(1);
        EditorTheme.Purple300 = t.Purple.GetStop(2);
        EditorTheme.Purple400 = t.Purple.GetStop(3);
        EditorTheme.Purple500 = t.Purple.GetStop(4);
        EditorTheme.Purple600 = t.Purple.GetStop(5);
        EditorTheme.Purple700 = t.Purple.GetStop(6);

        EditorTheme.Blue100 = t.Blue.GetStop(0);
        EditorTheme.Blue200 = t.Blue.GetStop(1);
        EditorTheme.Blue300 = t.Blue.GetStop(2);
        EditorTheme.Blue400 = t.Blue.GetStop(3);
        EditorTheme.Blue500 = t.Blue.GetStop(4);
        EditorTheme.Blue600 = t.Blue.GetStop(5);
        EditorTheme.Blue700 = t.Blue.GetStop(6);

        EditorTheme.Red100 = t.Red.GetStop(0);
        EditorTheme.Red200 = t.Red.GetStop(1);
        EditorTheme.Red300 = t.Red.GetStop(2);
        EditorTheme.Red400 = t.Red.GetStop(3);
        EditorTheme.Red500 = t.Red.GetStop(4);
        EditorTheme.Red600 = t.Red.GetStop(5);
        EditorTheme.Red700 = t.Red.GetStop(6);

        EditorTheme.Ink100 = t.Ink.GetStop(0);
        EditorTheme.Ink200 = t.Ink.GetStop(1);
        EditorTheme.Ink300 = t.Ink.GetStop(2);
        EditorTheme.Ink400 = t.Ink.GetStop(3);
        EditorTheme.Ink500 = t.Ink.GetStop(4);

        EditorTheme.DefaultFontName = t.DefaultFontName;
        EditorTheme.DefaultBoldFontName = t.DefaultBoldFontName;
                
        EditorApplication.Instance?.InitializeFont();

        EditorTheme.UserScale = t.UserScale;

        // Sizing
        EditorTheme.MenuBarHeight = t.MenuBarHeight;
        EditorTheme.RowHeight = t.RowHeight;
        EditorTheme.FontSize = t.FontSize;
        EditorTheme.LabelWidth = t.LabelWidth;
        EditorTheme.Spacing = t.Spacing;
        EditorTheme.Padding = t.Padding;
        EditorTheme.SplitterSize = t.SplitterSize;
        EditorTheme.DockPadding = t.DockPadding;
        EditorTheme.TabBarHeight = t.TabBarHeight;
        EditorTheme.TabPadding = t.TabPadding;
        EditorTheme.Roundness = t.Roundness;

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

    private static Color ParseColor(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.Magenta; }
    }
}
