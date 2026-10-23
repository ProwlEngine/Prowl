// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.ScriptEditor;

/// <summary>
/// A code editor colour scheme: a base palette with some colours and token styles replaced. Colours are keyed by
/// the <see cref="CodeEditorColors"/> field name or the <see cref="CodeTokenKind"/> name, as #RRGGBBAA.
/// </summary>
public sealed class CodeColorScheme
{
    public string Name { get; set; } = "";

    /// <summary>"Theme" follows the editor theme, "Dark" starts from the code editor's own dark palette.</summary>
    public string Base { get; set; } = "Theme";

    public Dictionary<string, string> Colors { get; set; } = [];
    public Dictionary<string, CodeTokenStyle> Styles { get; set; } = [];

    /// <summary>Builds the palette, taking the surfaces of a "Theme" based scheme from <paramref name="theme"/>.</summary>
    public CodeEditorColors Build(OrigamiTheme theme)
    {
        CodeEditorColors colors = Base == "Dark" ? CodeEditorColors.CreateDark() : CodeEditorColors.FromTheme(theme);
        foreach ((string key, string hex) in Colors)
        {
            if (!CodeColorSchemes.TryParse(hex, out Color32 color)) continue;
            if (Enum.TryParse(key, out CodeTokenKind kind)) colors[kind] = color;
            else if (CodeColorSchemes.UiField(key) is { } field) field.SetValue(colors, color);
        }
        foreach ((string key, CodeTokenStyle style) in Styles)
            if (Enum.TryParse(key, out CodeTokenKind kind)) colors.Styles[(int)kind] = style;
        return colors;
    }

    public CodeColorScheme Clone(string name) => new()
    {
        Name = name,
        Base = Base,
        Colors = new(Colors),
        Styles = new(Styles),
    };
}

/// <summary>The built-in colour schemes, the user's own saved in the Prowl folder, and the palette in use.</summary>
public static class CodeColorSchemes
{
    /// <summary>The folder user schemes are saved in, one JSON file each.</summary>
    public static string FolderPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prowl", "ColorSchemes");

    public const string FileExtension = ".prowlcolors";

    public static readonly CodeColorScheme[] BuiltIn = [Theme(), Dark(), Light(), HighContrast()];

    private static List<CodeColorScheme>? s_user;
    private static int s_version;
    private static (string Name, int Version, OrigamiTheme? Theme) s_paletteKey;
    private static CodeEditorColors? s_palette;

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    /// <summary>The user's own schemes.</summary>
    public static IReadOnlyList<CodeColorScheme> User => s_user ??= LoadUser();

    public static IEnumerable<CodeColorScheme> All => BuiltIn.Concat(User);

    public static bool IsBuiltIn(string name) => BuiltIn.Any(s => s.Name == name);

    public static CodeColorScheme Find(string name) => All.FirstOrDefault(s => s.Name == name) ?? BuiltIn[0];

    /// <summary>The palette for the named scheme. The same object is returned until the scheme or theme changes, so the editor keeps its caches.</summary>
    public static CodeEditorColors Palette(string name, OrigamiTheme theme)
    {
        var key = (name, s_version, theme);
        if (s_palette != null && s_paletteKey == key) return s_palette;
        s_paletteKey = key;
        return s_palette = Find(name).Build(theme);
    }

    /// <summary>Saves a user scheme, replacing one with the same name.</summary>
    public static void Save(CodeColorScheme scheme)
    {
        List<CodeColorScheme> user = (List<CodeColorScheme>)User;
        user.RemoveAll(s => s.Name == scheme.Name);
        user.Add(scheme);
        s_version++;
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(PathOf(scheme.Name), JsonSerializer.Serialize(scheme, s_json));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Script Editor] Could not save colour scheme '{scheme.Name}': {ex.Message}");
        }
    }

    public static void Delete(string name)
    {
        ((List<CodeColorScheme>)User).RemoveAll(s => s.Name == name);
        s_version++;
        try { File.Delete(PathOf(name)); }
        catch (Exception ex) { Debug.LogWarning($"[Script Editor] Could not delete colour scheme '{name}': {ex.Message}"); }
    }

    public static void Export(CodeColorScheme scheme, string path) => File.WriteAllText(path, JsonSerializer.Serialize(scheme, s_json));

    /// <summary>Reads a scheme file and saves it as a user scheme, renamed when its name is taken by a built-in one.</summary>
    public static CodeColorScheme Import(string path)
    {
        CodeColorScheme scheme = JsonSerializer.Deserialize<CodeColorScheme>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a colour scheme.");
        if (string.IsNullOrWhiteSpace(scheme.Name)) scheme.Name = Path.GetFileNameWithoutExtension(path);
        if (IsBuiltIn(scheme.Name)) scheme.Name += " (Imported)";
        Save(scheme);
        return scheme;
    }

    /// <summary>The editable UI colours of the code editor, by field name.</summary>
    public static IEnumerable<FieldInfo> UiFields => typeof(CodeEditorColors).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.FieldType == typeof(Color32));

    public static FieldInfo? UiField(string name)
    {
        FieldInfo? field = typeof(CodeEditorColors).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field?.FieldType == typeof(Color32) ? field : null;
    }

    public static string ToHex(Color32 c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    public static bool TryParse(string hex, out Color32 color)
    {
        color = default;
        string h = hex.TrimStart('#');
        if ((h.Length != 6 && h.Length != 8) || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        if (h.Length == 6) v = (v << 8) | 0xFF;
        color = new Color32((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    private static string PathOf(string name)
    {
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(FolderPath, safe + FileExtension);
    }

    private static List<CodeColorScheme> LoadUser()
    {
        var list = new List<CodeColorScheme>();
        if (!Directory.Exists(FolderPath)) return list;
        foreach (string file in Directory.EnumerateFiles(FolderPath, "*" + FileExtension))
        {
            try
            {
                if (JsonSerializer.Deserialize<CodeColorScheme>(File.ReadAllText(file)) is { } scheme && !IsBuiltIn(scheme.Name))
                    list.Add(scheme);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Script Editor] Could not read colour scheme '{file}': {ex.Message}");
            }
        }
        return list;
    }

    private static CodeColorScheme Theme() => new() { Name = "Theme", Base = "Theme" };

    private static CodeColorScheme Dark() => new() { Name = "Dark", Base = "Dark" };

    private static CodeColorScheme Light() => new()
    {
        Name = "Light",
        Base = "Dark",
        Colors = new()
        {
            ["Background"] = "#FFFFFF", ["GutterBackground"] = "#F5F5F7", ["LineNumber"] = "#9A9AA5", ["LineNumberActive"] = "#2B2B33",
            ["CurrentLine"] = "#0000000D", ["Selection"] = "#3B82F650", ["SelectionInactive"] = "#3B82F628", ["Caret"] = "#1F1F24",
            ["IndentGuide"] = "#00000018", ["IndentGuideActive"] = "#00000040", ["Ruler"] = "#00000014", ["Whitespace"] = "#00000030",
            ["BracketMatch"] = "#3B82F640", ["OccurrenceHighlight"] = "#0000001A", ["FindMatch"] = "#F59E0B50", ["FindMatchCurrent"] = "#F59E0B90",
            ["Error"] = "#DC2626", ["Warning"] = "#B45309", ["Info"] = "#2563EB", ["FoldPlaceholder"] = "#0000001A", ["FoldMarker"] = "#6B6B76",
            ["Popup"] = "#FFFFFFFC", ["PopupBorder"] = "#D4D4DC", ["PopupSelection"] = "#3B82F640", ["PopupText"] = "#1F1F24",
            ["PopupTextDim"] = "#6B6B76", ["PopupMatch"] = "#2563EB", ["MinimapSlider"] = "#00000018", ["Scrollbar"] = "#00000030",
            ["ScrollbarHover"] = "#00000060", ["StatusBar"] = "#F0F0F3", ["StatusText"] = "#5B5B66", ["TrailingWhitespace"] = "#DC262630",
            ["Plain"] = "#1F1F24", ["Identifier"] = "#1F1F24", ["Keyword"] = "#7C3AED", ["ControlKeyword"] = "#C026D3",
            ["Preprocessor"] = "#6B7280", ["PreprocessorText"] = "#6B7280", ["Comment"] = "#6B7280", ["DocComment"] = "#15803D",
            ["DocCommentTag"] = "#166534", ["String"] = "#B45309", ["StringEscape"] = "#9A3412", ["Char"] = "#B45309", ["Number"] = "#0F766E",
            ["Operator"] = "#374151", ["Punctuation"] = "#4B5563", ["Namespace"] = "#1F1F24", ["Class"] = "#0E7490", ["Record"] = "#0E7490",
            ["Delegate"] = "#0E7490", ["Struct"] = "#047857", ["Interface"] = "#4D7C0F", ["Enum"] = "#047857", ["TypeParameter"] = "#0F766E",
            ["Method"] = "#A16207", ["ExtensionMethod"] = "#A16207", ["Property"] = "#1D4ED8", ["Field"] = "#1D4ED8", ["Event"] = "#C2410C",
            ["EnumMember"] = "#6D28D9", ["Constant"] = "#6D28D9", ["Local"] = "#1E40AF", ["Parameter"] = "#1E40AF", ["Label"] = "#1F1F24",
            ["ExcludedCode"] = "#9CA3AF",
        },
        Styles = new() { ["Comment"] = CodeTokenStyle.Italic },
    };

    private static CodeColorScheme HighContrast() => new()
    {
        Name = "High Contrast",
        Base = "Dark",
        Colors = new()
        {
            ["Background"] = "#000000", ["GutterBackground"] = "#000000", ["LineNumber"] = "#BBBBBB", ["LineNumberActive"] = "#FFFFFF",
            ["CurrentLine"] = "#FFFFFF1A", ["Selection"] = "#FFD40066", ["SelectionInactive"] = "#FFD40033", ["Caret"] = "#FFFFFF",
            ["IndentGuide"] = "#FFFFFF40", ["Error"] = "#FF4D4D", ["Warning"] = "#FFD400", ["StatusBar"] = "#000000", ["StatusText"] = "#FFFFFF",
            ["Popup"] = "#000000FF", ["PopupBorder"] = "#FFFFFF", ["PopupText"] = "#FFFFFF",
            ["Plain"] = "#FFFFFF", ["Identifier"] = "#FFFFFF", ["Keyword"] = "#FFD400", ["ControlKeyword"] = "#FF8AE2", ["Comment"] = "#7CFC00",
            ["DocComment"] = "#7CFC00", ["String"] = "#FFA94D", ["Char"] = "#FFA94D", ["Number"] = "#66FFCC", ["Class"] = "#00E5FF",
            ["Record"] = "#00E5FF", ["Struct"] = "#00E5FF", ["Interface"] = "#00E5FF", ["Enum"] = "#00E5FF", ["Method"] = "#FFFF66",
            ["ExtensionMethod"] = "#FFFF66", ["Property"] = "#9CDCFE", ["Field"] = "#9CDCFE", ["Local"] = "#E0E0FF", ["Parameter"] = "#E0E0FF",
        },
        Styles = new() { ["Keyword"] = CodeTokenStyle.Bold, ["ControlKeyword"] = CodeTokenStyle.Bold },
    };
}
