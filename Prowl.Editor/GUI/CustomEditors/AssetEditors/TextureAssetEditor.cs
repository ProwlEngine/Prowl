using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Graphite;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
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
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;
        AssetRef<Texture2D> texture = (AssetRef<Texture2D>)(Texture2D)asset;

        if (texture != null)
        {
            // Preview card: checkerboard behind the image so alpha reads clearly.
            paper.Box($"{id}_preview")
                .Height(200).Margin(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.Spacing)
                .Rounded(8).Clip()
                .BackgroundColor(EditorTheme.Neutral300)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    // Checkerboard so texture alpha reads clearly.
                    const float cell = 10f;
                    var ca = Prowl.Vector.Color32.FromArgb(255, 44, 40, 54);
                    var cb = Prowl.Vector.Color32.FromArgb(255, 34, 30, 44);
                    int cols = (int)MathF.Ceiling((float)r.Size.X / cell);
                    int crows = (int)MathF.Ceiling((float)r.Size.Y / cell);
                    for (int cy = 0; cy < crows; cy++)
                        for (int cx = 0; cx < cols; cx++)
                        {
                            float px = (float)r.Min.X + cx * cell, py = (float)r.Min.Y + cy * cell;
                            float cw = MathF.Min(cell, (float)r.Max.X - px), ch = MathF.Min(cell, (float)r.Max.Y - py);
                            canvas.RectFilled(px, py, cw, ch, ((cx + cy) & 1) == 0 ? ca : cb);
                        }

                    float maxW = (float)r.Size.X - 16, maxH = (float)r.Size.Y - 16;
                    float aspect = texture.Res.Width / MathF.Max(1f, texture.Res.Height);
                    float drawW = maxW, drawH = drawW / aspect;
                    if (drawH > maxH) { drawH = maxH; drawW = drawH * aspect; }
                    float drawX = (float)r.Min.X + ((float)r.Size.X - drawW) / 2f;
                    float drawY = (float)r.Min.Y + ((float)r.Size.Y - drawH) / 2f;

                    // Flip V (textures are stored Y-up), same idiom the scene view uses for its RT.
                    canvas.SetBrushTexture(texture.Res);
                    canvas.SetBrushTextureTransform(
                        Transform2D.CreateTranslation(drawX, drawY + drawH) *
                        Transform2D.CreateScale(drawW, -drawH));
                    canvas.RectFilled(drawX, drawY, drawW, drawH, Prowl.Vector.Color32.FromArgb(255, 255, 255, 255));
                    canvas.ClearBrushTexture();
                }));

            // Quick-facts chip strip.
            using (paper.Row($"{id}_stats").Height(UnitValue.Auto)
                .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).RowBetween(m.SpacingMedium).Enter())
            {
                EditorGUI.StatChip(paper, $"{id}_st_size", $"{texture.Res.Width} x {texture.Res.Height}", font);
                EditorGUI.StatChip(paper, $"{id}_st_fmt", texture.Res.ImageFormat.ToString(), font);
                EditorGUI.StatChip(paper, $"{id}_st_mip", texture.Res.IsMipmapped ? "Mipmapped" : "No Mipmaps", font);
                paper.Box($"{id}_st_pad").Height(1).IsNotInteractable();
            }
        }

        // No meta means nothing can be applied, so don't offer settings that can't be saved.
        if (MetaPathOf(entry) is not string metaPath || !File.Exists(metaPath)) return;

        // Read once and kept live per asset by the base; edits stay put while you look at something else.
        EchoObject settings = Settings(entry);

        EditorGUI.SectionHeader(paper, $"{id}_settings_hdr", "Import Settings", first: texture == null);

        bool genMips = settings.TryGet("generateMipmaps", out var mipTag) && mipTag.BoolValue;
        EditorGUI.SettingsToggle(paper, $"{id}_mips", "Generate Mipmaps", genMips,
            v => { settings["generateMipmaps"] = new EchoObject(v); }, separator: false);

        bool minLinear = !settings.TryGet("minLinear", out var minTag) || minTag.BoolValue;
        Origami.Checkbox(paper, $"{id}_min", minLinear,
                v => settings["minLinear"] = new EchoObject(v))
            .LabelRight("Min Linear").Show();

        bool magLinear = !settings.TryGet("magLinear", out var magTag) || magTag.BoolValue;
        Origami.Checkbox(paper, $"{id}_mag", magLinear,
                v => settings["magLinear"] = new EchoObject(v))
            .LabelRight("Mag Linear").Show();

        bool mipLinear = !settings.TryGet("mipLinear", out var mipFilterTag) || mipFilterTag.BoolValue;
        Origami.Checkbox(paper, $"{id}_mip", mipLinear,
                v => settings["mipLinear"] = new EchoObject(v))
            .LabelRight("Mip Linear").Show();

        var currentWrap = settings.TryGet("wrapMode", out var wrapTag)
            ? (SamplerAddressMode)wrapTag.IntValue : SamplerAddressMode.Wrap;
        EditorGUI.Row(paper, $"{id}_wrap", "Wrap Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_wrap_v", currentWrap,
                v => { settings["wrapMode"] = new EchoObject((int)v); }).Show());

        // Sprite settings: mode + a button to open the full Sprite Editor, which edits the shared sprite
        // object. Fold any edit of it back into the compound so the inspector's diff can see it.
        var spriteTarget = Importers.SpriteEditRegistry.Get(entry.Guid);
        if (spriteTarget.Dirty) FoldSpriteSettings(entry, settings);
        Origami.Header(paper, $"{id}_sprite_hdr", "Sprite").Show();
        EditorGUI.Row(paper, $"{id}_spmode", "Sprite Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_spmode_v", spriteTarget.Settings.Mode,
                v => { spriteTarget.Settings.Mode = v; spriteTarget.Dirty = true; }).Show());

        if (spriteTarget.Settings.Mode != Importers.SpriteMode.None)
            Origami.Button(paper, $"{id}_spopen", $"{EditorIcons.PenToSquare}  Open Sprite Editor",
                () => SpriteEditorWindow.OpenFor(entry.Guid)).Width(200).Show();

        // The settings on screen are defaults, not what the meta actually holds, so saving would replace
        // the real slicing with them. Say so and keep the save locked out until the meta is fixed.
        if (spriteTarget.LoadFailed)
            Origami.Label(paper, $"{id}_sperr",
                $"{EditorIcons.TriangleExclamation}  This texture's sprite settings could not be read. Saving is disabled so the existing data isn't overwritten - see the console.")
                .Show();

        DrawApplyRevertBar(paper, id, entry, asset);
    }

}
