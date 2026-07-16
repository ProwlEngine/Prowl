// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using FillMethod = Prowl.Runtime.UI.FillMethod;
using ImageType = Prowl.Runtime.UI.ImageType;
using UIImage = Prowl.Runtime.UI.UIImage;

namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(UIImage))]
public class UIImageEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var img = (UIImage)target;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Undo.Snapshot(img);

        DrawSourceRow(paper, $"{id}_src", img);

        paper.Box($"{id}_sp0").Height(EditorTheme.Spacing);

        EditorGUI.Row(paper, $"{id}_color", "Color", () =>
            Origami.ColorField(paper, $"{id}_color_v", img.Color, v => img.Color = v).Show());

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material",
            typeof(AssetRef<Material>), img.Material,
            v => img.Material = (AssetRef<Material>)v!, 0);

        BoolRow(paper, $"{id}_ray", "Raycast Target", img.RaycastTarget, v => img.RaycastTarget = v);

        paper.Box($"{id}_sp1").Height(EditorTheme.Spacing * 2);

        Origami.Header(paper, $"{id}_h_type", "Image Settings").Show();

        EditorGUI.Row(paper, $"{id}_type", "Image Type", () =>
            Origami.EnumDropdown<ImageType>(paper, $"{id}_type_v", img.Type, v => img.Type = v).Show());

        switch (img.Type)
        {
            case ImageType.Simple:
                DrawSimpleOptions(paper, $"{id}_simple", img);
                break;
            case ImageType.Sliced:
                DrawSlicedOptions(paper, $"{id}_sliced", img);
                break;
            case ImageType.Tiled:
                DrawTiledOptions(paper, $"{id}_tiled", img);
                break;
            case ImageType.Filled:
                DrawFilledOptions(paper, $"{id}_filled", img);
                break;
        }
    }

    private static void BoolRow(Paper paper, string id, string label, bool value, Action<bool> setter)
        => EditorGUI.Row(paper, id, label, () =>
            Origami.Checkbox(paper, $"{id}_v", value, setter).Show());

    /// <summary>
    /// A sliced sprite (one that carries a border) is almost always meant to be drawn nine-sliced, so on
    /// assignment we default the draw type from the sprite: bordered -> Sliced, otherwise -> Simple.
    /// </summary>
    private static void InferTypeFromSprite(UIImage img)
    {
        if (img.Sprite.Res is not Sprite s) return;
        img.Type = s.HasBorder ? ImageType.Sliced : ImageType.Simple;
    }

    private static void DrawSourceRow(Paper paper, string id, UIImage img)
    {
        DrawPreview(paper, $"{id}_prev", img);

        using (paper.Column($"{id}_left").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
        {
            PropertyGridUtils.DrawField(paper, $"{id}_sprite", "Sprite",
                typeof(AssetRef<Sprite>), img.Sprite,
                v =>
                {
                    img.Sprite = (AssetRef<Sprite>)v!;
                    InferTypeFromSprite(img);
                }, 0);
        }
    }

    private static void DrawPreview(Paper paper, string id, UIImage img)
    {
        const float size = 128f;
        var sprite = img.Sprite.Res;
        var tex = sprite?.Texture.Res ?? UIImage.defaultTexture;
        var color = img.Color;

        // Aspect of the drawn region: the sprite's rect when available, else the texture.
        float srcW = sprite != null ? sprite.Rect.Width : tex.Width;
        float srcH = sprite != null ? sprite.Rect.Height : tex.Height;

        paper.Box(id)
            .Size(size, size)
            .Margin(UnitValue.Stretch(), EditorTheme.Spacing + 4)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Neutral500).BorderWidth(1).Rounded(3)
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float x = (float)r.Min.X + 2;
                float y = (float)r.Min.Y + 2;
                float w = (float)r.Size.X - 4;
                float h = (float)r.Size.Y - 4;

                if (srcW > 0 && srcH > 0)
                {
                    float aspect = srcW / srcH;
                    if (aspect >= 1f) { float nh = w / aspect; y += (h - nh) * 0.5f; h = nh; }
                    else { float nw = h * aspect; x += (w - nw) * 0.5f; w = nw; }
                }

                var tint = System.Drawing.Color.FromArgb(
                    Math.Clamp((int)(color.A * 255), 0, 255),
                    Math.Clamp((int)(color.R * 255), 0, 255),
                    Math.Clamp((int)(color.G * 255), 0, 255),
                    Math.Clamp((int)(color.B * 255), 0, 255));

                // Draw only the sprite's sub-rect (flipped V, textures are Y-up); no sprite -> whole texture.
                float texW = tex.Width, texH = tex.Height;
                float u0 = 0f, v0 = 0f, du = 1f, dv = 1f;
                if (sprite != null && texW > 0 && texH > 0)
                {
                    u0 = sprite.Rect.X / texW; v0 = sprite.Rect.Y / texH;
                    du = sprite.Rect.Width / texW; dv = sprite.Rect.Height / texH;
                }
                if (du <= 0f) du = 1f;
                if (dv <= 0f) dv = 1f;

                float sx = w / du, sy = -h / dv;
                float tx = x - u0 * sx, ty = (y + h) - v0 * sy;
                canvas.SetBrushTexture(tex);
                canvas.SetBrushTextureTransform(
                    Prowl.Vector.Spatial.Transform2D.CreateTranslation(tx, ty) *
                    Prowl.Vector.Spatial.Transform2D.CreateScale(sx, sy));
                canvas.RectFilled(x, y, w, h, tint);
                canvas.ClearBrushTexture();
            }));
    }

    // ================================================================
    //  Per-type option groups
    // ================================================================

    private static void DrawSimpleOptions(Paper paper, string id, UIImage img)
    {
        BoolRow(paper, $"{id}_pa", "Preserve Aspect", img.PreserveAspect, v => img.PreserveAspect = v);
        DrawSetNativeSizeButton(paper, $"{id}_sns", img);
    }

    private static void DrawSlicedOptions(Paper paper, string id, UIImage img)
    {
        BoolRow(paper, $"{id}_pa", "Preserve Aspect", img.PreserveAspect, v => img.PreserveAspect = v);
        DrawPixelsPerUnitMultiplierField(paper, $"{id}_ppu", img);
    }

    private static void DrawTiledOptions(Paper paper, string id, UIImage img)
    {
        DrawPixelsPerUnitMultiplierField(paper, $"{id}_ppu", img);
    }

    private static void DrawFilledOptions(Paper paper, string id, UIImage img)
    {
        BoolRow(paper, $"{id}_pa", "Preserve Aspect", img.PreserveAspect, v => img.PreserveAspect = v);

        EditorGUI.Row(paper, $"{id}_fm", "Fill Method", () =>
            Origami.EnumDropdown<FillMethod>(paper, $"{id}_fm_v", img.FillMethod, v =>
            {
                img.FillMethod = v;
                img.FillOrigin = Math.Clamp(img.FillOrigin, 0, MaxOriginIndex(v));
            }).Show());

        DrawFillOriginRow(paper, $"{id}_fo", img);

        EditorGUI.Row(paper, $"{id}_fa", "Fill Amount", () =>
            Origami.Slider(paper, $"{id}_fa_v", img.FillAmount,
                v => img.FillAmount = v, 0f, 1f).Format("F2").Show());

        if (IsRadial(img.FillMethod))
            BoolRow(paper, $"{id}_cw", "Clockwise", img.FillClockwise, v => img.FillClockwise = v);

        DrawSetNativeSizeButton(paper, $"{id}_sns", img);
    }

    // ================================================================
    //  Shared widgets
    // ================================================================

    private static void DrawPixelsPerUnitMultiplierField(Paper paper, string id, UIImage img)
    {
        EditorGUI.Row(paper, id, "Pixels Per Unit Multiplier", () =>
        {
            using (paper.Row($"{id}_row").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
            {
                using (paper.Box($"{id}_n").Width(UnitValue.Stretch()).Enter())
                    Origami.NumericField<float>(paper, $"{id}_n_v", img.PixelsPerUnitMultiplier,
                        v => img.PixelsPerUnitMultiplier = MathF.Max(0.0001f, v)).Min(0.0001f).Show();

                Origami.IconButton(paper, $"{id}_r", EditorIcons.ArrowsRotate, () => img.PixelsPerUnitMultiplier = 1f)
                    .Width(EditorTheme.RowHeight).Height(EditorTheme.RowHeight)
                    .Tooltip("Reset to 1").Show();
            }
        });
    }

    private static void DrawSetNativeSizeButton(Paper paper, string id, UIImage img)
    {
        if (img.Sprite.Res is not Sprite s) return;

        EditorGUI.Row(paper, id, string.Empty, () =>
            Origami.Button(paper, $"{id}_b", "Set Native Size", () =>
            {
                var rt = img.GameObject?.RectTransform;
                if (rt == null) return;
                Undo.Snapshot(rt);

                // A stretched anchor (edge/fullscreen) makes SizeDelta a padding offset, so setting a
                // literal pixel size wouldn't work. Collapse it to a bottom-left point anchor first; a
                // point anchor is already size-driven, so leave it untouched.
                const float eps = 1e-4f;
                bool stretched = MathF.Abs(rt.AnchorMin.X - rt.AnchorMax.X) > eps
                              || MathF.Abs(rt.AnchorMin.Y - rt.AnchorMax.Y) > eps;
                if (stretched)
                {
                    rt.AnchorMin = Float2.Zero;
                    rt.AnchorMax = Float2.Zero;
                }

                rt.SizeDelta = new Float2(s.Rect.Width, s.Rect.Height);
            }).Show());
    }

    // ================================================================
    //  Fill-origin segmented control.
    // ================================================================

    private static void DrawFillOriginRow(Paper paper, string id, UIImage img)
    {
        int origin = Math.Clamp(img.FillOrigin, 0, MaxOriginIndex(img.FillMethod));

        EditorGUI.Row(paper, id, "Fill Origin", () =>
        {
            var group = Origami.ButtonGroup(paper, $"{id}_g", origin, v => img.FillOrigin = v)
                .Height(EditorTheme.RowHeight).FullWidth();

            switch (img.FillMethod)
            {
                case FillMethod.Horizontal:
                    group.Item("", EditorIcons.AlignLeft, "Left");
                    group.Item("", EditorIcons.AlignRight, "Right");
                    break;
                case FillMethod.Vertical:
                    group.Item("Bottom", tooltip: "Bottom");
                    group.Item("Top", tooltip: "Top");
                    break;
                case FillMethod.Radial90:
                    group.Item("BL", tooltip: "Bottom Left");
                    group.Item("TL", tooltip: "Top Left");
                    group.Item("TR", tooltip: "Top Right");
                    group.Item("BR", tooltip: "Bottom Right");
                    break;
                case FillMethod.Radial180:
                    group.Item("Bottom", tooltip: "Bottom");
                    group.Item("Left", tooltip: "Left");
                    group.Item("Top", tooltip: "Top");
                    group.Item("Right", tooltip: "Right");
                    break;
                case FillMethod.Radial360:
                    group.Item("Bottom", tooltip: "Bottom");
                    group.Item("Right", tooltip: "Right");
                    group.Item("Top", tooltip: "Top");
                    group.Item("Left", tooltip: "Left");
                    break;
            }

            group.Show();
        });
    }

    private static bool IsRadial(FillMethod m)
        => m == FillMethod.Radial90 || m == FillMethod.Radial180 || m == FillMethod.Radial360;

    private static int MaxOriginIndex(FillMethod m) => m switch
    {
        FillMethod.Horizontal => 1,
        FillMethod.Vertical   => 1,
        FillMethod.Radial90   => 3,
        FillMethod.Radial180  => 3,
        FillMethod.Radial360  => 3,
        _ => 0
    };
}
