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
        if (spriteTarget.Dirty) FoldSpriteSettings(entry, settings);

        TextureImportSettings current = TextureImportSettings.Read(settings, texturePath);
        TextureImportType type = current.Type;

        EditorGUI.Row(paper, $"{id}_type", "Texture Type", () =>
            Origami.EnumDropdown(paper, $"{id}_type_v", type, v => ApplyType(settings, spriteTarget, v)).Show());

        switch (type)
        {
            case TextureImportType.NormalMap:
                Toggle(paper, $"{id}_fromgray", "Create From Grayscale", settings, TextureImportSettings.Keys.CreateFromGrayscale, current.CreateFromGrayscale);
                if (current.CreateFromGrayscale)
                {
                    EditorGUI.SliderRow(paper, $"{id}_bump", "Bumpiness", current.Bumpiness, 0f, 1f,
                        v => { settings[TextureImportSettings.Keys.Bumpiness] = new EchoObject(v); });
                    EnumRow(paper, $"{id}_bumpfilter", "Filtering", settings, TextureImportSettings.Keys.BumpFiltering, current.BumpFiltering);
                }
                Toggle(paper, $"{id}_flipg", "Flip Green Channel", settings, TextureImportSettings.Keys.FlipGreen, current.FlipGreen);
                break;

            case TextureImportType.SingleChannel:
                EnumRow(paper, $"{id}_channel", "Channel", settings, TextureImportSettings.Keys.Channel, current.Channel);
                break;

            case TextureImportType.LookupTable:
                break;

            case TextureImportType.Lightmap:
                EditorGUI.Note(paper, $"{id}_lightmap",
                    "Baked lighting written by the lightmapper. It stays at full size and uncompressed, since its RGBM encoding magnifies any error, and it is replaced on the next bake.");
                break;

            default:
                if (type == TextureImportType.Sprite)
                    SpriteRows(paper, id, entry, spriteTarget);

                Toggle(paper, $"{id}_srgb", "sRGB (Colour Texture)", settings, TextureImportSettings.Keys.Srgb, current.Srgb);
                EnumRow(paper, $"{id}_alphasrc", "Alpha Source", settings, TextureImportSettings.Keys.AlphaSource, current.AlphaSource);
                if (current.AlphaSource != TextureAlphaSource.None)
                    Toggle(paper, $"{id}_alphatr", "Alpha Is Transparency", settings, TextureImportSettings.Keys.AlphaIsTransparency, current.AlphaIsTransparency);
                break;
        }

        Origami.Header(paper, $"{id}_adv_hdr", "Advanced").Show();
        Toggle(paper, $"{id}_mips", "Generate Mipmaps", settings, TextureImportSettings.Keys.Mipmaps, current.Mipmaps);
        if (current.Mipmaps && type is TextureImportType.Default or TextureImportType.Sprite or TextureImportType.Cookie)
        {
            Toggle(paper, $"{id}_coverage", "Mipmaps Preserve Coverage", settings, TextureImportSettings.Keys.PreserveCoverage, current.PreserveCoverage);
            if (current.PreserveCoverage)
                EditorGUI.SliderRow(paper, $"{id}_cutoff", "Alpha Cutoff Value", current.AlphaCutoff, 0f, 1f,
                    v => { settings[TextureImportSettings.Keys.AlphaCutoff] = new EchoObject(v); });
        }

        EnumRow(paper, $"{id}_wrapu", "Wrap U", settings, TextureImportSettings.Keys.WrapU, current.WrapU);
        EnumRow(paper, $"{id}_wrapv", "Wrap V", settings, TextureImportSettings.Keys.WrapV, current.WrapV);
        if (current.WrapU == WrapMode.Border || current.WrapV == WrapMode.Border)
            EnumRow(paper, $"{id}_border", "Border Colour", settings, TextureImportSettings.Keys.BorderColor, current.BorderColor);

        EnumRow(paper, $"{id}_filter", "Filter Mode", settings, TextureImportSettings.Keys.Filter, current.Filter);
        if (current.Filter != FilterMode.Point)
            EditorGUI.Row(paper, $"{id}_aniso", "Aniso Level", () =>
                Origami.IntSlider(paper, $"{id}_aniso_v", current.Anisotropy,
                    v => { settings[TextureImportSettings.Keys.Anisotropy] = new EchoObject(v); }, 0, 16).Show());

        Origami.Header(paper, $"{id}_platform_hdr", "Default Platform").Show();
        EditorGUI.Row(paper, $"{id}_maxsize", "Max Size", () =>
            Origami.Dropdown(paper, $"{id}_maxsize_v", current.MaxSize,
                    v => { settings[TextureImportSettings.Keys.MaxSize] = new EchoObject(v); }, TextureImportSettings.MaxSizes)
                .Display(v => v.ToString()).Show());
        EnumRow(paper, $"{id}_compression", "Compression", settings, TextureImportSettings.Keys.Compression, current.Compression);
        if (current.Compression != TextureCompression.None)
            EnumRow(paper, $"{id}_format", "Format", settings, TextureImportSettings.Keys.Format, current.Format);

        if (type == TextureImportType.Sprite && current.Compression != TextureCompression.None
            && spriteTarget.Settings.Mode == SpriteMode.Multiple && MisalignedSlices(spriteTarget.Settings, current) is int misaligned and > 0)
            EditorGUI.Note(paper, $"{id}_bleed",
                $"{EditorIcons.TriangleExclamation}  {misaligned} sprite(s) do not line up with the compression blocks, so colour can bleed between neighbouring sprites. Use Compression None, or slice on the block grid.");

        // The settings on screen are defaults, not what the meta actually holds, so saving would replace
        // the real slicing with them. Say so and keep the save locked out until the meta is fixed.
        if (spriteTarget.LoadFailed)
            Origami.Label(paper, $"{id}_sperr",
                $"{EditorIcons.TriangleExclamation}  This texture's sprite settings could not be read. Saving is disabled so the existing data isn't overwritten - see the console.")
                .Show();

        DrawApplyRevertBar(paper, id, entry, asset);
    }

    /// <summary>Sprite mode, pixels per unit, mesh type and the Sprite Editor, which all edit the shared sprite settings.</summary>
    private static void SpriteRows(Paper paper, string id, AssetEntry entry, SpriteEditTarget sprite)
    {
        SpriteImportSettings s = sprite.Settings;
        SpriteMode mode = s.Mode == SpriteMode.Multiple ? SpriteMode.Multiple : SpriteMode.Single;

        EditorGUI.Row(paper, $"{id}_spmode", "Sprite Mode", () =>
            Origami.Dropdown(paper, $"{id}_spmode_v", mode, v => { s.Mode = v; sprite.Dirty = true; }, s_spriteModes).Show());

        EditorGUI.Row(paper, $"{id}_ppu", "Pixels Per Unit", () =>
            Origami.NumericField<float>(paper, $"{id}_ppu_v", s.PixelsPerUnit,
                v => { s.PixelsPerUnit = MathF.Max(0.01f, v); sprite.Dirty = true; }).Show());

        EditorGUI.Row(paper, $"{id}_mesh", "Mesh Type", () =>
            Origami.Dropdown(paper, $"{id}_mesh_v", s.GenerateTightMesh ? 1 : 0,
                v => { s.GenerateTightMesh = v == 1; sprite.Dirty = true; }, s_meshTypes).Show());

        Origami.Button(paper, $"{id}_spopen", $"{EditorIcons.PenToSquare}  Open Sprite Editor",
            () => SpriteEditorWindow.OpenFor(entry.Guid)).Width(200).Show();
    }

    /// <summary>Slices whose edges fall inside a compression block of the format the sheet will be stored in.</summary>
    private static int MisalignedSlices(SpriteImportSettings sprite, TextureImportSettings settings)
    {
        TextureFormat format = TextureImporter.FormatFor(settings.Format, settings.Compression, settings.Type, hasAlpha: true);
        if (!GraphicsFormats.IsCompressed(format)) return 0;

        (uint blockWidth, uint blockHeight) = Graphite.FormatSizeHelpers.GetBlockDimensions(GraphicsFormats.ToPixelFormat(format));
        int count = 0;
        foreach (SpriteSliceData slice in sprite.Slices)
        {
            SpriteRect r = slice.Rect;
            if (r.X % blockWidth != 0 || r.Y % blockHeight != 0 || r.Width % blockWidth != 0 || r.Height % blockHeight != 0)
                count++;
        }
        return count;
    }

    private static readonly SpriteMode[] s_spriteModes = [SpriteMode.Single, SpriteMode.Multiple];
    private static readonly string[] s_meshTypes = ["Full Rect", "Tight"];

    // Settings that follow the type are cleared, so the new type's defaults apply until set by hand.
    private static void ApplyType(EchoObject settings, SpriteEditTarget sprite, TextureImportType type)
    {
        settings[TextureImportSettings.Keys.Type] = new EchoObject((int)type);
        foreach (string key in s_typeDefaults)
            settings.Remove(key);

        SpriteMode mode = type != TextureImportType.Sprite ? SpriteMode.None
            : sprite.Settings.Mode == SpriteMode.None ? SpriteMode.Single : sprite.Settings.Mode;
        if (sprite.Settings.Mode != mode)
        {
            sprite.Settings.Mode = mode;
            sprite.Dirty = true;
        }
    }

    private static readonly string[] s_typeDefaults =
    [
        TextureImportSettings.Keys.Srgb, TextureImportSettings.Keys.Mipmaps, TextureImportSettings.Keys.Filter,
        TextureImportSettings.Keys.WrapU, TextureImportSettings.Keys.WrapV, TextureImportSettings.Keys.AlphaIsTransparency,
    ];

    private static void Toggle(Paper paper, string id, string label, EchoObject settings, string key, bool value)
        => EditorGUI.SettingsToggle(paper, id, label, value, v => { settings[key] = new EchoObject(v); });

    private static void EnumRow<T>(Paper paper, string id, string label, EchoObject settings, string key, T value) where T : struct, Enum
        => EditorGUI.Row(paper, id, label, () =>
            Origami.EnumDropdown(paper, $"{id}_v", value,
                v => { settings[key] = new EchoObject(Convert.ToInt32(v)); }).Show());

}
