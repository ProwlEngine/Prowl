// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for .rendertexture assets. Edits the description held in the file itself, then rewrites and
/// reimports it - which rebuilds the GPU resources for every camera already pointing at this asset.
/// </summary>
[CustomAssetEditor(typeof(RenderTexture))]
public class RenderTextureAssetEditor : AssetImporterEditor
{
    private int _width;
    private int _height;
    private bool _hasDepth;
    private List<TextureImageFormat> _formats = [];
    private Guid _forGuid;
    private bool _dirty;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null || Project.Current == null) return;
        if (asset is not RenderTexture rt) return;

        if (_forGuid != entry.Guid)
        {
            _width = rt.Width;
            _height = rt.Height;
            _hasDepth = rt.HasDepthAttachment;
            _formats = [.. rt.TextureFormats];
            _forGuid = entry.Guid;
            _dirty = false;
        }

        EditorGUI.SectionHeader(paper, $"{id}_hdr", "Render Texture", first: true);

        EditorGUI.Row(paper, $"{id}_w", "Width", () =>
            Origami.NumericField<int>(paper, $"{id}_w_v", _width,
                v => { if (v != _width) { _width = Math.Max(1, v); _dirty = true; } }).Show());

        EditorGUI.Row(paper, $"{id}_h", "Height", () =>
            Origami.NumericField<int>(paper, $"{id}_h_v", _height,
                v => { if (v != _height) { _height = Math.Max(1, v); _dirty = true; } }).Show());

        Origami.Checkbox(paper, $"{id}_depth", _hasDepth,
            v => { if (v != _hasDepth) { _hasDepth = v; _dirty = true; } })
            .LabelRight("Depth Attachment").Show();

        Origami.Header(paper, $"{id}_fmt_hdr", "Color Attachments").Show();

        for (int i = 0; i < _formats.Count; i++)
        {
            int index = i;
            EditorGUI.Row(paper, $"{id}_fmt{index}", $"Format {index}", () =>
                Origami.EnumDropdown(paper, $"{id}_fmt{index}_v", _formats[index],
                    v => { if (v != _formats[index]) { _formats[index] = v; _dirty = true; } }).Show());
        }

        using (paper.Row($"{id}_fmt_btns").Height(26).RowBetween(6).Enter())
        {
            Origami.Button(paper, $"{id}_fmt_add", $"{EditorIcons.Plus}  Add",
                () => { _formats.Add(TextureImageFormat.Color4b); _dirty = true; }).Width(90).Show();

            if (_formats.Count > 0)
                Origami.Button(paper, $"{id}_fmt_rem", $"{EditorIcons.Minus}  Remove",
                    () => { _formats.RemoveAt(_formats.Count - 1); _dirty = true; }).Width(110).Show();
        }

        bool dirty = !Origami.IsReadOnly && _dirty;
        paper.Box($"{id}_save").Width(UnitValue.Auto).Height(30)
            .Margin(8, 8, 10, 10).Rounded(8).Padding(16, 16, 0, 0)
            .BackgroundColor(dirty ? EditorTheme.Accent : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(dirty ? EditorTheme.AccentBright : EditorTheme.Neutral300).End()
            .Text($"{EditorIcons.FloppyDisk}  Save & Reimport", EditorTheme.FontSemiBold ?? font)
            .TextColor(dirty ? System.Drawing.Color.White : EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) =>
            {
                if (!dirty) return;
                Save(entry);
            });
    }

    private void Save(AssetEntry entry)
    {
        try
        {
            // Written as a fresh description rather than mutating the imported instance: the file is the
            // source of truth, and the reimport is what rebuilds every camera's view of this asset.
            var described = new RenderTexture();
            described.Configure(_width, _height, _hasDepth, [.. _formats]);
            described.Name = Path.GetFileNameWithoutExtension(entry.Path);

            EchoObject echo = Serializer.Serialize(typeof(object), described);
            File.WriteAllText(Path.Combine(Project.Current!.AssetsPath, entry.Path), echo.WriteToString());
            described.Dispose();

            _dirty = false;
            EditorAssetBackend.Instance?.Reimport(entry.Guid);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save render texture '{entry.Path}': {ex.Message}");
        }
    }
}
