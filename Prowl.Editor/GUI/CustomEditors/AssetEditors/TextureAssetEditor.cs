using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Texture2D))]
public class TextureAssetEditor : ImportSettingsEditor
{
    /// <summary>
    /// Folds the live sprite configuration into the import-settings compound. Sprite edits are authored
    /// on a separate object (shared with the Sprite Editor window), so they have to reach the compound
    /// for the inspector's diff to see them - that compound is the single thing compared against disk.
    /// </summary>
    private static void FoldSpriteSettings(AssetEntry entry, EchoObject settings)
    {
        Importers.SpriteEditTarget target = Importers.SpriteEditRegistry.Get(entry.Guid);
        if (target.LoadFailed) return; // never write defaults over slicing we failed to read

        Importers.TextureSpriteMeta.WriteInto(settings, target.Settings);
        target.Dirty = false;
    }

    protected override void OnBeforeApply(AssetEntry entry, EchoObject settings) => FoldSpriteSettings(entry, settings);

    protected override void OnAfterRevert(AssetEntry entry, EchoObject settings)
    {
        // The restored compound is now the truth; the live sprite object has to be rebuilt from it or
        // the Sprite Editor would still be holding the edits that were just discarded.
        Importers.SpriteEditRegistry.SetSettings(entry.Guid, Importers.TextureSpriteMeta.ReadFrom(settings));
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        OrigamiMetrics m = Origami.Current.Metrics;
        Texture2D? tex = asset as Texture2D;

        if (tex.IsValid())
        {
            // Preview card: checkerboard behind the image so alpha reads clearly.
            paper.Box($"{id}_preview")
                .Height(200).Margin(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.Spacing)
                .Rounded(m.ContainerRounding).Clip()
                .BackgroundColor(EditorTheme.Neutral300)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    // Checkerboard so texture alpha reads clearly.
                    const float cell = 10f;
                    Color ca = Prowl.Vector.Color32.FromArgb(255, 44, 40, 54);
                    Color cb = Prowl.Vector.Color32.FromArgb(255, 34, 30, 44);
                    int cols = (int)MathF.Ceiling((float)r.Size.X / cell);
                    int crows = (int)MathF.Ceiling((float)r.Size.Y / cell);
                    for (int cy = 0; cy < crows; cy++)
                        for (int cx = 0; cx < cols; cx++)
                        {
                            float px = (float)r.Min.X + cx * cell, py = (float)r.Min.Y + cy * cell;
                            float cw = MathF.Min(cell, (float)r.Max.X - px), ch = MathF.Min(cell, (float)r.Max.Y - py);
                            canvas.RectFilled(px, py, cw, ch, ((cx + cy) & 1) == 0 ? ca : cb);
                        }

                    if (tex.IsNotValid()) return;

                    float maxW = (float)r.Size.X - 16, maxH = (float)r.Size.Y - 16;
                    float aspect = tex.Width / MathF.Max(1f, tex.Height);
                    float drawW = maxW, drawH = drawW / aspect;
                    if (drawH > maxH) { drawH = maxH; drawW = drawH * aspect; }
                    float drawX = (float)r.Min.X + ((float)r.Size.X - drawW) / 2f;
                    float drawY = (float)r.Min.Y + ((float)r.Size.Y - drawH) / 2f;

                    // Flip V (textures are stored Y-up), same idiom the scene view uses for its RT.
                    canvas.SetBrushTexture(tex);
                    canvas.SetBrushTextureTransform(
                        Transform2D.CreateTranslation(drawX, drawY + drawH) *
                        Transform2D.CreateScale(drawW, -drawH));
                    canvas.RectFilled(drawX, drawY, drawW, drawH, Prowl.Vector.Color32.FromArgb(255, 255, 255, 255));
                    canvas.ClearBrushTexture();
                }));

            // Quick-facts chip strip.
            using (paper.Row($"{id}_stats").Height(UnitValue.Auto)
                .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(m.SpacingMedium).Enter())
            {
                EditorGUI.StatChip(paper, $"{id}_st_size", $"{tex.Width} x {tex.Height}", font);
                EditorGUI.StatChip(paper, $"{id}_st_fmt", tex.Srgb ? $"{tex.Format} sRGB" : tex.Format.ToString(), font);
                EditorGUI.StatChip(paper, $"{id}_st_mip", tex.MipCount > 1 ? $"{tex.MipCount} Mips" : "No Mipmaps", font);
                paper.Box($"{id}_st_pad").Height(1).IsNotInteractable();
            }
        }

        // No meta means nothing can be applied, so don't offer settings that can't be saved.
        if (MetaPathOf(entry) is not string metaPath || !File.Exists(metaPath)) return;

        // Read once and kept live per asset by the base; edits stay put while you look at something else.
        EchoObject settings = Settings(entry);

        EditorGUI.SectionHeader(paper, $"{id}_settings_hdr", "Import Settings", first: tex.IsNotValid());

        string texturePath = metaPath[..^".meta".Length];
        SpriteEditTarget spriteTarget = Importers.SpriteEditRegistry.Get(entry.Guid);

        TextureImportType type = TextureImporter.TypeOf(settings, texturePath);
        EditorGUI.Row(paper, $"{id}_type", "Texture Type", () =>
            Origami.EnumDropdown(paper, $"{id}_type_v", type, v => ApplyType(settings, spriteTarget, v)).Show());

        EditorGUI.SettingsToggle(paper, $"{id}_srgb", "sRGB (Colour)", TextureImporter.IsSrgb(settings, texturePath),
            v => { settings["srgb"] = new EchoObject(v); });

        EditorGUI.SettingsToggle(paper, $"{id}_mips", "Generate Mipmaps", settings.TryGet("generateMipmaps", out EchoObject? mipTag) && mipTag.BoolValue,
            v => { settings["generateMipmaps"] = new EchoObject(v); });

        EnumRow(paper, $"{id}_filter", "Filter", settings, "filter", FilterMode.Trilinear);
        if (EnumOf(settings, "filter", FilterMode.Trilinear) != FilterMode.Point)
        {
            int anisotropy = settings.TryGet("anisotropy", out EchoObject? anisoTag) ? anisoTag.IntValue : 0;
            EditorGUI.Row(paper, $"{id}_aniso", "Anisotropy", () =>
                Origami.IntSlider(paper, $"{id}_aniso_v", anisotropy, v => { settings["anisotropy"] = new EchoObject(v); }, 0, 16).Show());
        }

        EnumRow(paper, $"{id}_wrapu", "Wrap U", settings, "wrapU", WrapMode.Repeat);
        EnumRow(paper, $"{id}_wrapv", "Wrap V", settings, "wrapV", WrapMode.Repeat);
        if (EnumOf(settings, "wrapU", WrapMode.Repeat) == WrapMode.Border || EnumOf(settings, "wrapV", WrapMode.Repeat) == WrapMode.Border)
            EnumRow(paper, $"{id}_border", "Border Colour", settings, "borderColor", TextureBorderColor.TransparentBlack);

        // Sprite settings: mode + a button to open the full Sprite Editor, which edits the shared sprite
        // object. Fold any edit of it back into the compound so the inspector's diff can see it.
        if (spriteTarget.Dirty) FoldSpriteSettings(entry, settings);
        if (type == TextureImportType.Sprite || spriteTarget.Settings.Mode != Importers.SpriteMode.None)
        {
            Origami.Header(paper, $"{id}_sprite_hdr", "Sprite").Show();
            EditorGUI.Row(paper, $"{id}_spmode", "Sprite Mode", () =>
                Origami.EnumDropdown(paper, $"{id}_spmode_v", spriteTarget.Settings.Mode,
                    v => { spriteTarget.Settings.Mode = v; spriteTarget.Dirty = true; }).Show());

            if (spriteTarget.Settings.Mode != Importers.SpriteMode.None)
                Origami.Button(paper, $"{id}_spopen", $"{EditorIcons.PenToSquare}  Open Sprite Editor",
                    () => SpriteEditorWindow.OpenFor(entry.Guid)).Width(200).Show();
        }

        // The settings on screen are defaults, not what the meta actually holds, so saving would replace
        // the real slicing with them. Say so and keep the save locked out until the meta is fixed.
        if (spriteTarget.LoadFailed)
            Origami.Label(paper, $"{id}_sperr",
                $"{EditorIcons.TriangleExclamation}  This texture's sprite settings could not be read. Saving is disabled so the existing data isn't overwritten - see the console.")
                .Show();

        DrawApplyRevertBar(paper, id, entry, asset);
    }

    // A type sets the settings that suit it; sRGB then follows the type until set by hand.
    private static void ApplyType(EchoObject settings, SpriteEditTarget sprite, TextureImportType type)
    {
        settings["textureType"] = new EchoObject((int)type);
        settings.Remove("srgb");
        if (type is TextureImportType.Sprite or TextureImportType.Lut)
        {
            settings["generateMipmaps"] = new EchoObject(false);
            settings["filter"] = new EchoObject((int)FilterMode.Bilinear);
            settings["wrapU"] = new EchoObject((int)WrapMode.Clamp);
            settings["wrapV"] = new EchoObject((int)WrapMode.Clamp);
        }
        if (type == TextureImportType.Sprite && sprite.Settings.Mode == SpriteMode.None)
        {
            sprite.Settings.Mode = SpriteMode.Single;
            sprite.Dirty = true;
        }
    }

    private static T EnumOf<T>(EchoObject settings, string key, T fallback) where T : struct, Enum
        => settings.TryGet(key, out EchoObject? tag) ? (T)Enum.ToObject(typeof(T), tag.IntValue) : fallback;

    private static void EnumRow<T>(Paper paper, string id, string label, EchoObject settings, string key, T fallback) where T : struct, Enum
        => EditorGUI.Row(paper, id, label, () =>
            Origami.EnumDropdown(paper, $"{id}_v", EnumOf(settings, key, fallback),
                v => { settings[key] = new EchoObject(Convert.ToInt32(v)); }).Show());

}
