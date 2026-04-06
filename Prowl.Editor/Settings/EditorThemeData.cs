using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prowl.Editor;

/// <summary>
/// Serializable theme data. All colors stored as hex strings for readability in JSON.
/// </summary>
public class EditorThemeData
{
    public string Name { get; set; } = "Default Dark";

    // Neutral ramp
    public string Neutral100 { get; set; } = "#101116";
    public string Neutral200 { get; set; } = "#16151A";
    public string Neutral300 { get; set; } = "#18191D";
    public string Neutral400 { get; set; } = "#1D1E22";
    public string Neutral500 { get; set; } = "#2E2D35";

    // Purple ramp
    public string Purple100 { get; set; } = "#1D1010";
    public string Purple200 { get; set; } = "#271D36";
    public string Purple300 { get; set; } = "#3D2660";
    public string Purple400 { get; set; } = "#563784";
    public string Purple500 { get; set; } = "#7252AA";
    public string Purple600 { get; set; } = "#A886D8";
    public string Purple700 { get; set; } = "#D4B8F4";

    // Blue ramp
    public string Blue100 { get; set; } = "#0D1A24";
    public string Blue200 { get; set; } = "#1D3044";
    public string Blue300 { get; set; } = "#2E5470";
    public string Blue400 { get; set; } = "#82AAC6";
    public string Blue500 { get; set; } = "#AECADD";
    public string Blue600 { get; set; } = "#CDDEED";
    public string Blue700 { get; set; } = "#E8F2F9";

    // Red ramp
    public string Red100 { get; set; } = "#1C0E0E";
    public string Red200 { get; set; } = "#361818";
    public string Red300 { get; set; } = "#7A3030";
    public string Red400 { get; set; } = "#CB594F";
    public string Red500 { get; set; } = "#E68880";
    public string Red600 { get; set; } = "#F2B0AB";
    public string Red700 { get; set; } = "#FDE0DE";

    // Ink ramp
    public string Ink100 { get; set; } = "#2E2D35";
    public string Ink200 { get; set; } = "#3E3D47";
    public string Ink300 { get; set; } = "#6C6A7A";
    public string Ink400 { get; set; } = "#B0ADBE";
    public string Ink500 { get; set; } = "#F0EEF8";

    // Legacy / functional aliases
    public string Background { get; set; } = "#0A0A0C";
    public string Darkest { get; set; } = "#141416";
    public string Dark { get; set; } = "#1C1C1E";
    public string Normal { get; set; } = "#252528";
    public string Bright { get; set; } = "#414146";

    public string Text { get; set; } = "#DCDCDC";
    public string TextDim { get; set; } = "#969696";
    public string TextDisabled { get; set; } = "#5A5A5A";

    public string ButtonNormal { get; set; } = "#37373A";
    public string ButtonHovered { get; set; } = "#46464A";
    public string ButtonActive { get; set; } = "#28282B";

    public string Accent { get; set; } = "#337AB7";
    public string AccentDim { get; set; } = "#285A8C";

    public string Splitter { get; set; } = "#191919";
    public string SplitterHovered { get; set; } = "#337AB7";

    public string TabHovered { get; set; } = "#37373A";

    /// <summary>Create a deep copy.</summary>
    public EditorThemeData Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<EditorThemeData>(json)!;
    }

    /// <summary>Export to a .prowltheme file.</summary>
    public void ExportToFile(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>Import from a .prowltheme file.</summary>
    public static EditorThemeData? ImportFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<EditorThemeData>(json);
        }
        catch { return null; }
    }

    public static EditorThemeData CreateDefault() => new();
}
