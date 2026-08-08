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
    /// <summary>What the inspector is authoring. Serialized to Echo for the base class to diff against
    /// the last written version, which is what decides whether anything needs applying.</summary>
    private sealed class Description
    {
        public int Width = 1;
        public int Height = 1;
        public bool HasDepth;
        public List<TextureImageFormat> Formats = [];
    }

    /// <summary>Per asset, so edits survive looking at something else and coming back. Per-instance
    /// state would be one slot that the next selection overwrites.</summary>
    private static readonly Dictionary<Guid, Description> s_edits = new();

    private Description Edits(AssetEntry entry, RenderTexture rt)
    {
        if (s_edits.TryGetValue(entry.Guid, out Description? existing)) return existing;

        var seeded = new Description
        {
            Width = rt.Width,
            Height = rt.Height,
            HasDepth = rt.HasDepthAttachment,
            Formats = [.. rt.TextureFormats],
        };
        s_edits[entry.Guid] = seeded;
        Rebaseline(entry, rt); // what was imported is the clean state
        return seeded;
    }

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => s_edits.TryGetValue(entry.Guid, out Description? d) ? Serializer.Serialize(typeof(Description), d) : null;

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset) => Save(entry);

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (Serializer.Deserialize<Description>(baseline) is Description restored)
            s_edits[entry.Guid] = restored;
    }

    /// <summary>Forgets every buffered edit. GUIDs only mean anything within one project.</summary>
    internal static void ClearEdits() => s_edits.Clear();

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null || Project.Current == null) return;
        if (asset is not RenderTexture rt) return;

        Description edits = Edits(entry, rt);

        EditorGUI.SectionHeader(paper, $"{id}_hdr", "Render Texture", first: true);

        EditorGUI.Row(paper, $"{id}_w", "Width", () =>
            Origami.NumericField<int>(paper, $"{id}_w_v", edits.Width,
                v => edits.Width = Math.Max(1, v)).Show());

        EditorGUI.Row(paper, $"{id}_h", "Height", () =>
            Origami.NumericField<int>(paper, $"{id}_h_v", edits.Height,
                v => edits.Height = Math.Max(1, v)).Show());

        Origami.Checkbox(paper, $"{id}_depth", edits.HasDepth,
            v => edits.HasDepth = v)
            .LabelRight("Depth Attachment").Show();

        Origami.Header(paper, $"{id}_fmt_hdr", "Color Attachments").Show();

        for (int i = 0; i < edits.Formats.Count; i++)
        {
            int index = i;
            EditorGUI.Row(paper, $"{id}_fmt{index}", $"Format {index}", () =>
                Origami.EnumDropdown(paper, $"{id}_fmt{index}_v", edits.Formats[index],
                    v => edits.Formats[index] = v).Show());
        }

        using (paper.Row($"{id}_fmt_btns").Height(26).RowBetween(6).Enter())
        {
            Origami.Button(paper, $"{id}_fmt_add", $"{EditorIcons.Plus}  Add",
                () => edits.Formats.Add(TextureImageFormat.Color4b)).Width(90).Show();

            if (edits.Formats.Count > 0)
                Origami.Button(paper, $"{id}_fmt_rem", $"{EditorIcons.Minus}  Remove",
                    () => edits.Formats.RemoveAt(edits.Formats.Count - 1)).Width(110).Show();
        }

        bool dirty = !Origami.IsReadOnly && HasPendingChanges(entry, asset);
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
                ApplyPendingChanges(entry, asset);
            });
    }

    private bool Save(AssetEntry entry)
    {
        try
        {
            // Written as a fresh description rather than mutating the imported instance: the file is the
            // source of truth, and the reimport is what rebuilds every camera's view of this asset.
            var described = new RenderTexture();
            Description edits = s_edits[entry.Guid];
            described.Configure(edits.Width, edits.Height, edits.HasDepth, [.. edits.Formats]);
            described.Name = Path.GetFileNameWithoutExtension(entry.Path);

            EchoObject echo = Serializer.Serialize(typeof(object), described);
            File.WriteAllText(Path.Combine(Project.Current!.AssetsPath, entry.Path), echo.WriteToString());
            described.Dispose();

            EditorAssetBackend.Instance?.Reimport(entry.Guid);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save render texture '{entry.Path}': {ex.Message}");
            return false;
        }
    }
}
