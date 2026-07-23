// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for a <see cref="Sprite"/> sub-asset: a preview of its sub-rect in the source texture, its
/// read-only stats, and a button to edit the texture's sprites in the Sprite Editor. Sprites aren't edited
/// here - they're configured on their source texture (Sprite Mode + Sprite Editor).
/// </summary>
[CustomAssetEditor(typeof(Sprite))]
public class SpriteAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (asset is Sprite sprite)
            DrawInspector(paper, id, sprite);
        else
            Origami.Label(paper, $"{id}_none", "Sprite failed to load.").Show();
    }

    /// <summary>Draws the sprite preview + read-only stats + edit button. Reused for sprite sub-assets in the InspectorPanel.</summary>
    public static void DrawInspector(Paper paper, string id, Sprite sprite)
    {
        var m = Origami.Current.Metrics;

        Texture2D? tex = sprite.Texture.Res;

        paper.Box($"{id}_preview")
            .Height(200).Margin(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.Spacing)
            .Rounded(8).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                DrawPreview(canvas, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y, sprite, tex)));

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.Image}  Sprite").Show();

        InfoRow(paper, $"{id}_src", "Source Texture", tex?.Name ?? "None");
        InfoRow(paper, $"{id}_rect", "Rect", $"{sprite.Rect.X}, {sprite.Rect.Y}   {sprite.Rect.Width} x {sprite.Rect.Height}");
        InfoRow(paper, $"{id}_pivot", "Pivot", $"{sprite.Pivot.X:0.###}, {sprite.Pivot.Y:0.###}");
        InfoRow(paper, $"{id}_ppu", "Pixels Per Unit", $"{sprite.PixelsPerUnit:0.##}");
        InfoRow(paper, $"{id}_border", "Border (L/T/R/B)", $"{sprite.Border.X:0} / {sprite.Border.Y:0} / {sprite.Border.Z:0} / {sprite.Border.W:0}");

        paper.Box($"{id}_sp").Height(8);

        Guid texGuid = sprite.Texture.AssetID;
        if (texGuid != Guid.Empty)
        {
            // "Edit Sprites" is navigation, not data editing - keep it enabled even in the read-only sub-asset view.
            bool ro = Origami.IsReadOnly;
            if (ro) Origami.EndReadOnly();
            Origami.Button(paper, $"{id}_edit", $"{EditorIcons.PenToSquare}  Edit Sprites",
                () => SpriteEditorWindow.OpenFor(texGuid)).Width(200).Show();
            if (ro) Origami.BeginReadOnly();
        }
    }

    private static void InfoRow(Paper paper, string id, string label, string value)
    {
        EditorGUI.Row(paper, id, label, () =>
            paper.Box($"{id}_v").Height(EditorTheme.RowHeight)
                .Text(value, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));
    }

    private static void DrawPreview(Prowl.Quill.Canvas canvas, float minX, float minY, float w, float h, Sprite sprite, Texture2D? tex)
    {
        // Checkerboard so alpha reads clearly.
        const float cell = 10f;
        var ca = Color32.FromArgb(255, 44, 40, 54);
        var cb = Color32.FromArgb(255, 34, 30, 44);
        int cols = (int)MathF.Ceiling(w / cell);
        int crows = (int)MathF.Ceiling(h / cell);
        for (int cy = 0; cy < crows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                float px = minX + cx * cell, py = minY + cy * cell;
                float cw = MathF.Min(cell, minX + w - px), ch = MathF.Min(cell, minY + h - py);
                canvas.RectFilled(px, py, cw, ch, ((cx + cy) & 1) == 0 ? ca : cb);
            }

        if (tex == null || tex.Width == 0 || tex.Height == 0 || sprite.Rect.Width <= 0 || sprite.Rect.Height <= 0)
            return;

        // Fit the sprite rect's aspect inside the preview.
        float maxW = w - 16, maxH = h - 16;
        float aspect = sprite.Rect.Width / MathF.Max(1f, sprite.Rect.Height);
        float drawW = maxW, drawH = drawW / aspect;
        if (drawH > maxH) { drawH = maxH; drawW = drawH * aspect; }
        float drawX = minX + (w - drawW) / 2f;
        float drawY = minY + (h - drawH) / 2f;

        // Map the sprite's UV sub-rect onto the draw rect (V flipped, textures are stored Y-up).
        float u0 = sprite.Rect.X / (float)tex.Width, u1 = sprite.Rect.MaxX / (float)tex.Width;
        float v0 = sprite.Rect.Y / (float)tex.Height, v1 = sprite.Rect.MaxY / (float)tex.Height;
        float sx = drawW / MathF.Max(1e-6f, u1 - u0);
        float sy = -drawH / MathF.Max(1e-6f, v1 - v0);

        canvas.SetBrushTexture(tex);
        canvas.SetBrushTextureTransform(
            Transform2D.CreateTranslation(drawX - u0 * sx, drawY + drawH - v0 * sy) *
            Transform2D.CreateScale(sx, sy));
        canvas.RectFilled(drawX, drawY, drawW, drawH, Color32.FromArgb(255, 255, 255, 255));
        canvas.ClearBrushTexture();

        // Pivot marker (normalized, bottom-left origin).
        float pvx = drawX + sprite.Pivot.X * drawW;
        float pvy = drawY + drawH - sprite.Pivot.Y * drawH;
        var accent = EditorTheme.Purple400;
        canvas.CircleFilled(pvx, pvy, 4f, Color32.FromArgb(accent.A, accent.R, accent.G, accent.B));
    }
}
