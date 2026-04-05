using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Texture2D))]
public class TextureAssetEditor : AssetImporterEditor
{
    // Cached settings from meta file for Apply/Revert
    private bool _generateMipmaps = true;
    private bool _sRGB = true;
    private int _filterModeIndex; // 0=Nearest, 1=Linear, 2=NearestMipLinear, 3=LinearMipLinear
    private int _wrapModeIndex;   // 0=Repeat, 1=Clamp, 2=Mirror
    private bool _settingsLoaded;
    private bool _settingsDirty;

    private static readonly string[] FilterModeNames = { "Nearest", "Linear", "Nearest Mipmap Linear", "Linear Mipmap Linear" };
    private static readonly string[] WrapModeNames = { "Repeat", "Clamp to Edge", "Mirrored Repeat" };

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var tex = asset as Texture2D;

        // Load settings from meta on first draw
        if (!_settingsLoaded)
        {
            LoadSettingsFromMeta(entry);
            _settingsLoaded = true;
        }

        // Texture preview
        if (tex != null)
        {
            float previewSize = 200f;
            float aspect = tex.Width / (float)Math.Max(1, tex.Height);
            float pw = aspect >= 1 ? previewSize : previewSize * aspect;
            float ph = aspect >= 1 ? previewSize / aspect : previewSize;

            using (paper.Row($"{id}_preview_row").Height(ph + 8).Enter())
            {
                paper.Box($"{id}_preview_spacer");
                paper.Box($"{id}_preview")
                    .Size(pw, ph)
                    .BackgroundColor(Color.FromArgb(255, 20, 20, 22))
                    .Rounded(4)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        canvas.DrawImage(tex,
                            (float)r.Min.X, (float)r.Min.Y,
                            (float)r.Size.X, (float)r.Size.Y);
                    }));
                paper.Box($"{id}_preview_spacer2");
            }
        }

        EditorGUI.Separator(paper, $"{id}_sep_info");

        // Info
        EditorGUI.Header(paper, $"{id}_h_info", "Texture Info");
        if (tex != null)
        {
            EditorGUI.Label(paper, $"{id}_size", $"Size: {tex.Width} x {tex.Height}");
            EditorGUI.Label(paper, $"{id}_format", $"Format: {tex.ImageFormat}");
            EditorGUI.Label(paper, $"{id}_mips", $"Mipmapped: {tex.IsMipmapped}");
        }
        EditorGUI.Label(paper, $"{id}_path", $"Path: {entry.Path}");
        EditorGUI.Label(paper, $"{id}_guid", $"GUID: {entry.Guid}");

        EditorGUI.Separator(paper, $"{id}_sep_settings");

        // Import Settings
        EditorGUI.Header(paper, $"{id}_h_settings", "Import Settings");

        EditorGUI.Toggle(paper, $"{id}_mipmaps", "Generate Mipmaps", _generateMipmaps)
            .OnValueChanged(v => { _generateMipmaps = v; _settingsDirty = true; });

        EditorGUI.Toggle(paper, $"{id}_srgb", "sRGB", _sRGB)
            .OnValueChanged(v => { _sRGB = v; _settingsDirty = true; });

        EditorGUI.Dropdown(paper, $"{id}_filter", "Filter Mode", _filterModeIndex, FilterModeNames)
            .OnValueChanged(v => { _filterModeIndex = v; _settingsDirty = true; });

        EditorGUI.Dropdown(paper, $"{id}_wrap", "Wrap Mode", _wrapModeIndex, WrapModeNames)
            .OnValueChanged(v => { _wrapModeIndex = v; _settingsDirty = true; });

        EditorGUI.Separator(paper, $"{id}_sep_actions");

        // Apply / Revert buttons
        if (_settingsDirty)
        {
            using (paper.Row($"{id}_btn_row").Height(28).RowBetween(8).ChildLeft(8).ChildRight(8).Enter())
            {
                paper.Box($"{id}_btn_spacer");

                EditorGUI.Button(paper, $"{id}_revert", "Revert", width: 80)
                    .OnValueChanged(_ =>
                    {
                        LoadSettingsFromMeta(entry);
                        _settingsDirty = false;
                    });

                EditorGUI.Button(paper, $"{id}_apply", "Apply", width: 80)
                    .OnValueChanged(_ =>
                    {
                        SaveSettingsToMeta(entry);
                        _settingsDirty = false;

                        // Reimport
                        EditorAssetDatabase.Instance?.Reimport(entry.Guid);
                    });
            }
        }
    }

    private void LoadSettingsFromMeta(AssetEntry entry)
    {
        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);
        if (!File.Exists(metaPath)) return;

        try
        {
            var meta = MetaFile.Read(metaPath);
            var s = meta.Settings;
            if (s == null) return;

            _generateMipmaps = s.TryGet("generateMipmaps", out var mip) && mip.BoolValue;
            _sRGB = s.TryGet("sRGB", out var srgb) && srgb.BoolValue;
            _filterModeIndex = s.TryGet("filterMode", out var fm) ? fm.IntValue : 0;
            _wrapModeIndex = s.TryGet("wrapMode", out var wm) ? wm.IntValue : 0;
        }
        catch { }
    }

    private void SaveSettingsToMeta(AssetEntry entry)
    {
        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);

        MetaFileData meta;
        try { meta = File.Exists(metaPath) ? MetaFile.Read(metaPath) : MetaFile.CreateNew(entry.ImporterType); }
        catch { meta = MetaFile.CreateNew(entry.ImporterType); }

        var s = EchoObject.NewCompound();
        s["generateMipmaps"] = new EchoObject(_generateMipmaps);
        s["sRGB"] = new EchoObject(_sRGB);
        s["filterMode"] = new EchoObject(_filterModeIndex);
        s["wrapMode"] = new EchoObject(_wrapModeIndex);
        meta.Settings = s;

        MetaFile.Write(metaPath, meta);
    }
}
