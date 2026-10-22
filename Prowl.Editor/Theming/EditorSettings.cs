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
    public static EditorSettings Instance
    {
        get => _instance ??= Load();
        internal set => _instance = value;
    }

    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Prowl", "EditorSettings.json");

    /// <summary>The file as this editor last wrote or read it, to tell its own saves from edits made elsewhere.</summary>
    private static string? s_knownJson;

    // Preferences
    /// <summary> Gets or sets the default directory for new projects. Defaults to Documents/ProwlProjects. </summary>
    public string DefaultProjectsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProwlProjects");
    public string Locale { get; set; } = "en";
    public bool AutoSave { get; set; } = true;
    public bool ReimportOnFocusOnly { get; set; } = true;

    /// <summary> Gets or sets the thumbnail size in pixels for asset previews. Defaults to 32. </summary>
    public int ThumbnailSize { get; set; } = 32;

    /// <summary> Gets or sets the editor window X position in screen coordinates. -1 means auto-place. </summary>
    public int WindowX { get; set; } = -1;

    /// <summary> Gets or sets the editor window Y position in screen coordinates. -1 means auto-place. </summary>
    public int WindowY { get; set; } = -1;

    /// <summary> Gets or sets the editor window width in pixels. Defaults to 1280. </summary>
    public int WindowWidth { get; set; } = 1280;

    /// <summary> Gets or sets the editor window height in pixels. Defaults to 800. </summary>
    public int WindowHeight { get; set; } = 800;

    public bool WindowMaximized { get; set; } = false;

    /// <summary> Gets or sets whether the editor waits for the display before presenting a frame. Play mode ignores this and lets the game decide. </summary>
    public bool VSync { get; set; } = true;

    /// <summary> Gets or sets the frame rate the editor is paced to. 0 is unlimited. Play mode ignores this and lets the game decide. </summary>
    public int TargetFrameRate { get; set; } = 0;

    /// <summary> Gets or sets the frame rate the editor is paced to while its window is not focused. 0 means no separate limit. Play mode ignores this and lets the game decide. </summary>
    public int UnfocusedFrameRate { get; set; } = 5;

    /// <summary> Gets or sets whether the prowl CLI may run arbitrary C# in the editor through eval. </summary>
    public bool AllowCliEval { get; set; } = true;

    // Shortcuts only user-overridden bindings are stored
    /// <summary> Gets or sets the dictionary of user-overridden shortcut bindings, keyed by action name. Only overridden bindings are stored. </summary>
    public Dictionary<string, ShortcutBinding> ShortcutOverrides { get; set; } = [];

    /// <summary> Gets or sets whether the first launch editor setup has been shown. </summary>
    public bool SetupDone { get; set; }

    /// <summary>Serialized per-scene-tool settings, keyed by settings type name. Per-user rather
    /// than per-project: a brush size follows the user, it is not committed with the scene.</summary>
    public Dictionary<string, string> SceneToolSettings { get; set; } = [];

    // Theme
    /// <summary> Gets or sets the current editor theme data. Defaults to the built-in theme. </summary>
    public EditorThemeData Theme { get; set; } = EditorThemeData.CreateDefault();

    /// <summary> Themes the user saved under their own names. </summary>
    public List<EditorThemeData> SavedThemes { get; set; } = [];

    /// <summary> Layout densities the user saved under their own names. </summary>
    public List<LayoutPreset> SavedLayouts { get; set; } = [];

    /// <summary> Prepares the color ramps of the current and saved themes after loading. </summary>
    public void InitThemes()
    {
        Theme.InitRamps();
        foreach (EditorThemeData saved in SavedThemes) saved.InitRamps();
    }

    /// <summary>Apply the current theme to EditorTheme's static fields.</summary>
    public void ApplyTheme()
    {
        EditorThemeData t = Theme;
        t.InitRamps();

        // Origami's default theme is the base; this data is the customization overlaid on top of it
        // (see EditorTheme.BuildOrigamiTheme). SyncOrigami below rebuilds the live palette from it.
        EditorTheme.Customization = t;

        EditorTheme.DefaultFontName = t.DefaultFontName;
        EditorTheme.DefaultBoldFontName = t.DefaultBoldFontName;
        EditorTheme.MonoFontName = t.MonoFontName;
        EditorTheme.SmallFontDelta = t.SmallFontDelta;

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
        EditorTheme.WindowOpacity = t.WindowOpacity;
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
        EditorTheme.BackgroundVoidColor = ColorRamp.ParseHex(t.BackgroundVoidColor);
        EditorTheme.BackgroundImagePath = t.BackgroundImagePath;
        EditorTheme.BackgroundImageFit = t.BackgroundImageFit;
        EditorTheme.BackgroundImageDim = t.BackgroundImageDim;
        EditorTheme.BackgroundImageBlur = t.BackgroundImageBlur;

        // Push the freshly-applied editor theme into Origami. Brief lerp so user-visible
        // theme tweaks animate instead of snapping.
        EditorTheme.SyncOrigami();
    }

    /// <summary> Serializes this instance to the editor settings JSON file on disk. Silently logs a warning on failure. </summary>
    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
            s_knownJson = json;
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
                string json = File.ReadAllText(_filePath);
                EditorSettings? settings = JsonSerializer.Deserialize<EditorSettings>(json);
                s_knownJson = json;
                if (settings != null)
                {
                    settings.InitThemes();
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

    /// <summary>Applies the settings file again when something other than this editor changed it, such as a hand edit.</summary>
    internal static void ReloadIfChangedOnDisk()
    {
        string json;
        try
        {
            if (!File.Exists(_filePath)) return;
            json = File.ReadAllText(_filePath);
        }
        catch (IOException) { return; } // still being written, the next change event retries

        if (json == s_knownJson) return;
        s_knownJson = json;

        try
        {
            if (JsonSerializer.Deserialize<EditorSettings>(json) is not { } settings) return;
            settings.InitThemes();
            Replace(settings);
            Debug.Log("Editor settings reloaded from disk.");
        }
        catch (JsonException ex)
        {
            Debug.LogWarning($"EditorSettings.json was changed but could not be read, keeping the current settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Swaps in other settings, such as an imported profile or a full reset, and applies them everywhere. This machine's
    /// window placement and projects folder are kept, and a finished setup stays finished.
    /// </summary>
    public static void Replace(EditorSettings next)
    {
        EditorSettings current = Instance;
        next.WindowX = current.WindowX;
        next.WindowY = current.WindowY;
        next.WindowWidth = current.WindowWidth;
        next.WindowHeight = current.WindowHeight;
        next.WindowMaximized = current.WindowMaximized;
        next.DefaultProjectsPath = current.DefaultProjectsPath;
        next.SetupDone |= current.SetupDone;

        Instance = next;
        next.ApplyTheme();
        next.Save();

        Rosetta.Loc.SetLocale(next.Locale);
        ShortcutManager.ReloadOverrides();
        GUI.SceneView.SceneToolManager.ForgetSettings();
        EditorApplication.ApplyFramePacing();
        if (next.ThumbnailSize != current.ThumbnailSize)
        {
            Thumbnails.ThumbnailGenerator.DeleteAll();
            EditorAssetBackend.Instance?.ClearThumbnailTextureCache();
        }
    }

    /// <summary> Resets the theme to the default, applies it, and saves the settings. </summary>
    public void ResetTheme()
    {
        Theme = EditorThemeData.CreateDefault();
        ApplyTheme();
        Save();
    }
}
