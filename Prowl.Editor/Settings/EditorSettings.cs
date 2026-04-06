using System;
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
    public float FontSize { get; set; } = 17f;
    public bool ShowFPS { get; set; } = true;
    public string DefaultProjectsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProwlProjects");
    public bool AutoSaveLayout { get; set; } = true;

    // Theme
    public EditorThemeData Theme { get; set; } = new();

    /// <summary>Apply the current theme to EditorTheme's static fields.</summary>
    public void ApplyTheme()
    {
        var t = Theme;

        // Functional aliases
        EditorTheme.Background = ParseColor(t.Background);
        EditorTheme.Darkest = ParseColor(t.Darkest);
        EditorTheme.Dark = ParseColor(t.Dark);
        EditorTheme.Normal = ParseColor(t.Normal);
        EditorTheme.Bright = ParseColor(t.Bright);

        EditorTheme.Text = ParseColor(t.Text);
        EditorTheme.TextDim = ParseColor(t.TextDim);
        EditorTheme.TextDisabled = ParseColor(t.TextDisabled);

        EditorTheme.ButtonNormal = ParseColor(t.ButtonNormal);
        EditorTheme.ButtonHovered = ParseColor(t.ButtonHovered);
        EditorTheme.ButtonActive = ParseColor(t.ButtonActive);

        EditorTheme.Accent = ParseColor(t.Accent);
        EditorTheme.AccentDim = ParseColor(t.AccentDim);

        EditorTheme.Splitter = ParseColor(t.Splitter);
        EditorTheme.SplitterHovered = ParseColor(t.SplitterHovered);

        EditorTheme.TabHovered = ParseColor(t.TabHovered);

        EditorTheme.HeaderBackground = ParseColor(t.Dark);
        EditorTheme.InputBackground = ParseColor(t.Dark);
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

    /// <summary>Reset theme to default and apply.</summary>
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
