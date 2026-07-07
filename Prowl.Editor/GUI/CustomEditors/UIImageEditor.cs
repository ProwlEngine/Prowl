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
            typeof(Material), img.Material.Res,
            v => img.Material = v as Material, 0);

        Origami.Checkbox(paper, $"{id}_ray", img.RaycastTarget, v => img.RaycastTarget = v)
            .LabelRight("Raycast Target").Show();

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
                DrawFilledOptions(paper, $"{id}_filled", img, font);
                break;
        }

        EditorGUI.Row(paper, $"{id}_cr", "Corner Radius", () =>
            Origami.NumericField<float>(paper, $"{id}_cr_v", img.CornerRadius,
                v => img.CornerRadius = MathF.Max(0f, v)).Min(0f).Show());
    }

    private static void DrawSourceRow(Paper paper, string id, UIImage img)
    {
        var font = EditorTheme.DefaultFont!;

        DrawPreview(paper, $"{id}_prev", img);

        using (paper.Column($"{id}_left").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
        {
            PropertyGridUtils.DrawField(paper, $"{id}_tex", "Source Image",
                typeof(Texture2D), img.Texture.Res,
                v => img.Texture = v as Texture2D, 0);

            if (img.Texture.Res != null)
            {
                var tex = img.Texture.Res;
                paper.Box($"{id}_info").Height(16)
                    .Text($"{tex.Width} x {tex.Height}", font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    private static void DrawPreview(Paper paper, string id, UIImage img)
    {
        const float size = 128f;
        var tex = img.Texture.Res ?? UIImage.defaultTexture;
        var color = img.Color;

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

                // Preserve aspect inside the swatch so the preview matches what the
                // component would actually draw with PreserveAspect on.
                if (tex.Width > 0 && tex.Height > 0)
                {
                    float aspect = tex.Width / (float)tex.Height;
                    if (aspect >= 1f) { float nh = w / aspect; y += (h - nh) * 0.5f; h = nh; }
                    else { float nw = h * aspect; x += (w - nw) * 0.5f; w = nw; }
                }

                var tint = System.Drawing.Color.FromArgb(
                    Math.Clamp((int)(color.A * 255), 0, 255),
                    Math.Clamp((int)(color.R * 255), 0, 255),
                    Math.Clamp((int)(color.G * 255), 0, 255),
                    Math.Clamp((int)(color.B * 255), 0, 255));
                canvas.DrawImage(tex, x, y, w, h, tint);
            }));
    }

    // ================================================================
    //  Per-type option groups
    // ================================================================

    private static void DrawSimpleOptions(Paper paper, string id, UIImage img)
    {
        Origami.Checkbox(paper, $"{id}_pa", img.PreserveAspect, v => img.PreserveAspect = v)
            .LabelRight("Preserve Aspect").Show();

        DrawSetNativeSizeButton(paper, $"{id}_sns", img);
    }

    private static void DrawSlicedOptions(Paper paper, string id, UIImage img)
    {
        Origami.Checkbox(paper, $"{id}_pa", img.PreserveAspect, v => img.PreserveAspect = v)
            .LabelRight("Preserve Aspect").Show();

        DrawBorderField(paper, $"{id}_border", img);
        DrawPixelsPerUnitField(paper, $"{id}_ppu", img);
        DrawSetNativeSizeButton(paper, $"{id}_sns", img);
    }

    private static void DrawTiledOptions(Paper paper, string id, UIImage img)
    {
        DrawBorderField(paper, $"{id}_border", img);
        DrawPixelsPerUnitField(paper, $"{id}_ppu", img);
        DrawSetNativeSizeButton(paper, $"{id}_sns", img);
    }

    private static void DrawFilledOptions(Paper paper, string id, UIImage img, Prowl.Scribe.FontFile font)
    {
        Origami.Checkbox(paper, $"{id}_pa", img.PreserveAspect, v => img.PreserveAspect = v)
            .LabelRight("Preserve Aspect").Show();

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
        {
            Origami.Checkbox(paper, $"{id}_cw", img.FillClockwise, v => img.FillClockwise = v)
                .LabelRight("Clockwise").Show();
        }
    }

    // ================================================================
    //  Shared widgets
    // ================================================================

    private static void DrawBorderField(Paper paper, string id, UIImage img)
    {
        // Float4 packing: X=Left, Y=Top, Z=Right, W=Bottom (source-texture pixels).
        EditorGUI.Row(paper, id, "Border (L/T/R/B)", () =>
            Origami.Float4Field(paper, $"{id}_v", img.Border, v =>
                img.Border = new Float4(MathF.Max(0f, v.X), MathF.Max(0f, v.Y), MathF.Max(0f, v.Z), MathF.Max(0f, v.W))).Show());
    }

    private static void DrawPixelsPerUnitField(Paper paper, string id, UIImage img)
    {
        EditorGUI.Row(paper, id, "Pixels Per Unit", () =>
        {
            using (paper.Row($"{id}_row").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
            {
                using (paper.Box($"{id}_n").Width(UnitValue.Stretch()).Enter())
                    Origami.NumericField<float>(paper, $"{id}_n_v", img.PixelsPerUnit,
                        v => img.PixelsPerUnit = MathF.Max(0.0001f, v)).Min(0.0001f).Show();

                Origami.IconButton(paper, $"{id}_r", EditorIcons.ArrowsRotate, () => img.PixelsPerUnit = 1f)
                    .Width(EditorTheme.RowHeight).Height(EditorTheme.RowHeight)
                    .Tooltip("Reset to 1").Show();
            }
        });
    }

    private static void DrawSetNativeSizeButton(Paper paper, string id, UIImage img)
    {
        if (img.Texture.Res == null) return;

        EditorGUI.Row(paper, id, string.Empty, () =>
            Origami.Button(paper, $"{id}_b", "Set Native Size", () =>
            {
                var rt = img.GameObject?.RectTransform;
                var tex = img.Texture.Res;
                if (rt == null || tex == null) return;
                float ppu = img.PixelsPerUnit > 0 ? img.PixelsPerUnit : 1f;
                Undo.Snapshot(rt);
                rt.SizeDelta = new Float2(tex.Width / ppu, tex.Height / ppu);
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
