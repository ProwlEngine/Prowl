using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Texture2D))]
public class TextureAssetEditor : AssetImporterEditor
{
    // Cache settings across frames so changes stick until Save
    private EchoObject? _cachedSettings;
    private Guid _cachedForGuid;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var texture = asset as Texture2D;

        EditorGUI.Header(paper, $"{id}_hdr", $"{EditorIcons.Image}  Texture");

        if (texture != null)
        {
            EditorGUI.Label(paper, $"{id}_size", $"Size: {texture.Width} x {texture.Height}");
            EditorGUI.Label(paper, $"{id}_format", $"Format: {texture.ImageFormat}");
            EditorGUI.Label(paper, $"{id}_mip", $"Mipmaps: {(texture.IsMipmapped ? "Yes" : "No")}");

            paper.Box($"{id}_sp1").Height(8);

            // Texture preview — DrawTextureInto applies a Y-flip brush transform so the
            // image shows right-side up. Plain canvas.DrawImage would render upside-down
            // because Texture2D.FromImage flipped the image before GL upload.
            paper.Box($"{id}_preview")
                .Width(UnitValue.Stretch()).Height(200)
                .Rounded(4)
                .BackgroundColor(System.Drawing.Color.FromArgb(255, 40, 40, 40))
                .DrawTextureInto(paper, texture);
        }

        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);
        if (!File.Exists(metaPath)) return;

        // Load and cache settings (only reload when asset changes)
        if (_cachedSettings == null || _cachedForGuid != entry.Guid)
        {
            var meta = MetaFile.Read(metaPath);
            _cachedSettings = meta.Settings ?? EchoObject.NewCompound();

            // Merge in defaults for any missing keys
            var defaults = new Importers.TextureImporter().DefaultSettings();
            if (defaults != null)
            {
                foreach (var kvp in defaults.Tags)
                {
                    if (!_cachedSettings.TryGet(kvp.Key, out _))
                        _cachedSettings[kvp.Key] = kvp.Value.Clone();
                }
            }

            _cachedForGuid = entry.Guid;
        }

        var settings = _cachedSettings;

        paper.Box($"{id}_sp2").Height(8);
        EditorGUI.Separator(paper, $"{id}_sep");
        EditorGUI.Header(paper, $"{id}_settings_hdr", $"{EditorIcons.Gear}  Import Settings");

        bool genMips = settings.TryGet("generateMipmaps", out var mipTag) && mipTag.BoolValue;
        Origami.Checkbox(paper, $"{id}_mips", genMips,
                v => settings["generateMipmaps"] = new EchoObject(v))
            .LabelRight("Generate Mipmaps").Show();

        bool srgb = settings.TryGet("sRGB", out var srgbTag) && srgbTag.BoolValue;
        Origami.Checkbox(paper, $"{id}_srgb", srgb,
                v => settings["sRGB"] = new EchoObject(v))
            .LabelRight("sRGB").Show();

        var currentMin = settings.TryGet("minFilter", out var minTag)
            ? (TextureMin)minTag.IntValue : TextureMin.LinearMipmapLinear;
        InspectorRow.Draw(paper, $"{id}_min", "Min Filter", () =>
            Origami.EnumDropdown(paper, $"{id}_min_v", currentMin,
                v => settings["minFilter"] = new EchoObject((int)v)).Show());

        var currentMag = settings.TryGet("magFilter", out var magTag)
            ? (TextureMag)magTag.IntValue : TextureMag.Linear;
        InspectorRow.Draw(paper, $"{id}_mag", "Mag Filter", () =>
            Origami.EnumDropdown(paper, $"{id}_mag_v", currentMag,
                v => settings["magFilter"] = new EchoObject((int)v)).Show());

        var currentWrap = settings.TryGet("wrapMode", out var wrapTag)
            ? (TextureWrap)wrapTag.IntValue : TextureWrap.Repeat;
        InspectorRow.Draw(paper, $"{id}_wrap", "Wrap Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_wrap_v", currentWrap,
                v => settings["wrapMode"] = new EchoObject((int)v)).Show());

        paper.Box($"{id}_sp3").Height(8);

        EditorGUI.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save & Reimport", width: 150)
            .OnValueChanged(_ =>
            {
                // Write settings to meta and reimport
                var meta = MetaFile.Read(metaPath);
                meta.Settings = settings;
                MetaFile.Write(metaPath, meta);
                _cachedSettings = null; // Force reload after reimport
                EditorAssetDatabase.Instance?.Reimport(entry.Guid);
            });

        // Open the PBR Forge tool with this texture as the initial diffuse.
        if (texture != null)
        {
            paper.Box($"{id}_sp4").Height(8);
            EditorGUI.Button(paper, $"{id}_pbr_open", $"{EditorIcons.WandMagicSparkles}  Create PBR Maps", width: 180)
                .OnValueChanged(_ => PBRForgeWindow.OpenFor(texture));
        }
    }
}
