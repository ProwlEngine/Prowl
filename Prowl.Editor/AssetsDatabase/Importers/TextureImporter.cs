// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Importers;

// Every extension Aperture decodes. Detection is by content, so an extension that turns out
// to hold something else is refused at import rather than mis-decoded.
/// <summary> Imports image files into Texture2D assets, with support for mipmap generation, texture filtering, wrap modes, and sprite sub-asset slicing. </summary>
[ImporterFor(".png", ".apng",
             ".jpg", ".jpeg", ".jpe", ".jfif",
             ".bmp", ".dib",
             ".gif",
             ".tga", ".icb", ".vda", ".vst",
             ".tif", ".tiff",
             ".webp",
             ".psd", ".psb",
             ".dds",
             ".exr",
             ".hdr", ".pic", ".rgbe",
             ".ico", ".cur",
             ".pnm", ".pbm", ".pgm", ".ppm", ".pam",
             ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw")]
public class TextureImporter : AssetImporter
{
    // 5: sprite sub-asset GUIDs derive from the slice's persistent Id instead of its name.
    // 6: decoded by Aperture rather than ImageMagick
    // 7: Graphite sampling settings, sRGB colour textures, stored mips
    // 8: block compression, Balanced by default
    // 9: texture types with their own processing, CPU mip chains, max size
    /// <summary> The import-format version. Incremented when the importer's output changes, triggering reimport of all assets using this importer. </summary>
    public override int Version => 9;

    public override bool Import(ImportContext ctx)
    {
        TextureImportSettings s = TextureImportSettings.Read(ctx.Settings, ctx.AbsolutePath);

        using Aperture.Image image = Aperture.Image.Load(ctx.AbsolutePath, Texture2D.DecodeToUploadLayout);
        int width = image.Width, height = image.Height;
        byte[] pixels = new byte[width * height * 4];
        image.RootFrame.CopyTo(pixels, width * 4);

        Process(pixels, width, height, s);
        if (s.Type is not (TextureImportType.LookupTable or TextureImportType.Lightmap))
            pixels = TextureProcessing.FitMaxSize(pixels, ref width, ref height, s.MaxSize, s.Srgb && s.Type != TextureImportType.NormalMap);
        if (s.Type == TextureImportType.NormalMap) TextureProcessing.Renormalize(pixels);

        List<byte[]> levels = s.Mipmaps
            ? TextureProcessing.BuildMipChain(pixels, width, height,
                new MipChainOptions(s.Srgb, s.Type == TextureImportType.NormalMap, s.PreserveCoverage ? s.AlphaCutoff : null))
            : [pixels];

        TextureFormat format = FormatFor(s.Format, s.Compression, s.Type, TextureProcessing.HasAlpha(pixels));
        Texture2D texture = Texture2D.FromMipChain(levels, width, height, s.Srgb, format, QualityOf(s.Compression));
        texture.Name = ctx.FileName;
        texture.Filter = s.Filter;
        texture.WrapU = s.WrapU;
        texture.WrapV = s.WrapV;
        texture.BorderColor = s.BorderColor;
        texture.Anisotropy = (uint)s.Anisotropy;

        ctx.SetMainAsset(texture);

        if (s.Type != TextureImportType.Sprite)
            return true;

        // Sprite sub-assets: the texture's sprite config lives in settings["sprite"] (edited in the Sprite Editor).
        SpriteImportSettings spriteSettings = TextureSpriteMeta.ReadFrom(ctx.Settings);
        if (spriteSettings.Mode == SpriteMode.None) spriteSettings.Mode = SpriteMode.Single;

        foreach (KeyValuePair<string, Texture2D> kv in spriteSettings.SecondaryTextures)
            if (kv.Value.IsValid()) ctx.AddDependency(kv.Value.AssetID);

        foreach ((SpriteSliceData? slice, Sprite? sprite) in SpriteBuilder.Build(texture, spriteSettings, image.Width, image.Height, pixels))
            ctx.AddSubAsset(slice.Name, sprite, SpriteBuilder.IdentityOf(spriteSettings, slice));

        return true;
    }

    /// <summary>The per type work on the source pixels: normals from height, a single channel, or an alpha source.</summary>
    private static void Process(byte[] pixels, int width, int height, TextureImportSettings s)
    {
        switch (s.Type)
        {
            case TextureImportType.NormalMap:
                if (s.CreateFromGrayscale) TextureProcessing.NormalsFromHeight(pixels, width, height, s.Bumpiness, s.BumpFiltering == NormalMapFiltering.Smooth);
                if (s.FlipGreen) TextureProcessing.FlipGreen(pixels);
                TextureProcessing.ClearAlpha(pixels);
                return;

            case TextureImportType.SingleChannel:
                TextureProcessing.ToSingleChannel(pixels, s.Channel == TextureChannel.Alpha);
                return;

            case TextureImportType.LookupTable:
            case TextureImportType.Lightmap:
                return;
        }

        if (s.AlphaSource == TextureAlphaSource.None) TextureProcessing.ClearAlpha(pixels);
        else if (s.AlphaSource == TextureAlphaSource.FromGrayscale) TextureProcessing.AlphaFromGrayscale(pixels);

        if (s.AlphaIsTransparency) TextureProcessing.DilateTransparent(pixels, width, height);
    }

    /// <summary>The settings a new texture starts with. Everything else follows its type until set.</summary>
    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["sprite"] = Serializer.Serialize(typeof(SpriteImportSettings), new SpriteImportSettings());
        return s;
    }

    /// <summary>
    /// The format a texture is stored in. Automatic keeps lookup tables exact and gives normal maps BC5, single channel
    /// textures BC4, and everything else BC1, or BC3 when any pixel is translucent. No compression stores RGBA8, or R8
    /// for a single channel.
    /// </summary>
    public static TextureFormat FormatFor(TextureImportFormat format, TextureCompression compression, TextureImportType type, bool hasAlpha)
    {
        if (compression == TextureCompression.None)
            return type == TextureImportType.SingleChannel ? TextureFormat.R8 : TextureFormat.RGBA8;

        return format switch
        {
            TextureImportFormat.Automatic => type switch
            {
                TextureImportType.LookupTable or TextureImportType.Lightmap => TextureFormat.RGBA8,
                TextureImportType.NormalMap => TextureFormat.BC5,
                TextureImportType.SingleChannel => TextureFormat.BC4,
                _ => hasAlpha ? TextureFormat.BC3 : TextureFormat.BC1,
            },
            TextureImportFormat.RGBA8 => TextureFormat.RGBA8,
            TextureImportFormat.R8 => TextureFormat.R8,
            TextureImportFormat.BC1 => TextureFormat.BC1,
            TextureImportFormat.BC3 => TextureFormat.BC3,
            TextureImportFormat.BC4 => TextureFormat.BC4,
            TextureImportFormat.BC5 => TextureFormat.BC5,
            TextureImportFormat.ASTC4x4 => TextureFormat.ASTC4x4,
            TextureImportFormat.ASTC5x5 => TextureFormat.ASTC5x5,
            TextureImportFormat.ASTC6x6 => TextureFormat.ASTC6x6,
            TextureImportFormat.ASTC8x8 => TextureFormat.ASTC8x8,
            TextureImportFormat.ASTC10x10 => TextureFormat.ASTC10x10,
            _ => TextureFormat.ASTC12x12,
        };
    }

    private static Aperture.TextureEncodeQuality QualityOf(TextureCompression compression) => compression switch
    {
        TextureCompression.Fast => Aperture.TextureEncodeQuality.Fast,
        TextureCompression.Thorough => Aperture.TextureEncodeQuality.Thorough,
        _ => Aperture.TextureEncodeQuality.Balanced,
    };

    /// <summary>The texture type the settings name, or the one the file name suggests when they name none.</summary>
    public static TextureImportType TypeOf(EchoObject? settings, string path) => TextureImportSettings.Read(settings, path).Type;

    /// <summary>Whether the texture is sRGB colour, the explicit setting or what its type and name suggest.</summary>
    public static bool IsSrgb(EchoObject? settings, string path) => TextureImportSettings.Read(settings, path).Srgb;
}

/// <summary>
/// A texture's import settings as the importer applies them. A setting missing from the meta takes the default of the
/// texture's type, so changing the type moves every untouched setting with it.
/// </summary>
public sealed class TextureImportSettings
{
    public const int DefaultMaxSize = 2048;

    /// <summary>The sizes a texture may be limited to.</summary>
    public static readonly int[] MaxSizes = [32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384];

    /// <summary>The meta keys, shared by the importer, the inspector and migration.</summary>
    public static class Keys
    {
        public const string Type = "textureType";
        public const string Srgb = "srgb";
        public const string AlphaSource = "alphaSource";
        public const string AlphaIsTransparency = "alphaIsTransparency";
        public const string CreateFromGrayscale = "createFromGrayscale";
        public const string Bumpiness = "bumpiness";
        public const string BumpFiltering = "bumpFiltering";
        public const string FlipGreen = "flipGreen";
        public const string Channel = "singleChannel";
        public const string Mipmaps = "generateMipmaps";
        public const string PreserveCoverage = "preserveCoverage";
        public const string AlphaCutoff = "alphaCutoff";
        public const string Filter = "filter";
        public const string WrapU = "wrapU";
        public const string WrapV = "wrapV";
        public const string BorderColor = "borderColor";
        public const string Anisotropy = "anisotropy";
        public const string MaxSize = "maxSize";
        public const string Compression = "compression";
        public const string Format = "format";
    }

    public TextureImportType Type { get; private init; }
    public bool Srgb { get; private init; }
    public TextureAlphaSource AlphaSource { get; private init; }
    public bool AlphaIsTransparency { get; private init; }
    public bool CreateFromGrayscale { get; private init; }
    public float Bumpiness { get; private init; }
    public NormalMapFiltering BumpFiltering { get; private init; }
    public bool FlipGreen { get; private init; }
    public TextureChannel Channel { get; private init; }
    public bool Mipmaps { get; private init; }
    public bool PreserveCoverage { get; private init; }
    public float AlphaCutoff { get; private init; }
    public FilterMode Filter { get; private init; }
    public WrapMode WrapU { get; private init; }
    public WrapMode WrapV { get; private init; }
    public TextureBorderColor BorderColor { get; private init; }
    public int Anisotropy { get; private init; }
    public int MaxSize { get; private init; }
    public TextureCompression Compression { get; private init; }
    public TextureImportFormat Format { get; private init; }

    public static TextureImportSettings Read(EchoObject? settings, string path)
    {
        settings ??= EchoObject.NewCompound();
        TextureImportType type = TypeFrom(settings, path);
        bool retiredMask = settings.TryGet(Keys.Type, out EchoObject? raw) && raw.TagType != EchoType.String && raw.IntValue == RetiredMask;
        bool flat = type is TextureImportType.Sprite or TextureImportType.Cookie or TextureImportType.LookupTable;
        bool clamped = flat || type == TextureImportType.Lightmap;
        bool colour = !retiredMask && type is TextureImportType.Default or TextureImportType.Sprite or TextureImportType.Cookie or TextureImportType.Lightmap;
        bool mipmaps = Bool(settings, Keys.Mipmaps, !flat);
        bool exact = type is TextureImportType.LookupTable or TextureImportType.Lightmap;
        bool sheet = type == TextureImportType.Sprite && SpriteModeOf(settings) == SpriteMode.Multiple;

        return new TextureImportSettings
        {
            Type = type,
            Srgb = Bool(settings, Keys.Srgb, colour && !TextureRuntimeImporter.NamesData(path)),
            AlphaSource = Enum(settings, Keys.AlphaSource, TextureAlphaSource.Input),
            AlphaIsTransparency = Bool(settings, Keys.AlphaIsTransparency, type == TextureImportType.Sprite),
            CreateFromGrayscale = Bool(settings, Keys.CreateFromGrayscale, false),
            Bumpiness = Math.Clamp(Float(settings, Keys.Bumpiness, 0.25f), 0f, 1f),
            BumpFiltering = Enum(settings, Keys.BumpFiltering, NormalMapFiltering.Smooth),
            FlipGreen = Bool(settings, Keys.FlipGreen, false),
            Channel = Enum(settings, Keys.Channel, TextureChannel.Red),
            Mipmaps = mipmaps,
            PreserveCoverage = Bool(settings, Keys.PreserveCoverage, false),
            AlphaCutoff = Math.Clamp(Float(settings, Keys.AlphaCutoff, 0.5f), 0f, 1f),
            Filter = Enum(settings, Keys.Filter, mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear),
            WrapU = Enum(settings, Keys.WrapU, clamped ? WrapMode.Clamp : WrapMode.Repeat),
            WrapV = Enum(settings, Keys.WrapV, clamped ? WrapMode.Clamp : WrapMode.Repeat),
            BorderColor = Enum(settings, Keys.BorderColor, TextureBorderColor.TransparentBlack),
            Anisotropy = Math.Clamp(Int(settings, Keys.Anisotropy, 0), 0, 16),
            MaxSize = Math.Clamp(Int(settings, Keys.MaxSize, exact ? MaxSizes[^1] : DefaultMaxSize), MaxSizes[0], MaxSizes[^1]),
            Compression = Enum(settings, Keys.Compression, exact || sheet ? TextureCompression.None : TextureCompression.Balanced),
            Format = Enum(settings, Keys.Format, TextureImportFormat.Automatic),
        };
    }

    /// <summary>The number the retired Mask type was stored as, read as a linear Default texture.</summary>
    private const int RetiredMask = 2;

    private static TextureImportType TypeFrom(EchoObject settings, string path)
    {
        if (settings.TryGet(Keys.Type, out EchoObject? tag) && tag.TagType != EchoType.String)
        {
            if (System.Enum.IsDefined((TextureImportType)tag.IntValue)) return (TextureImportType)tag.IntValue;
            if (tag.IntValue == RetiredMask) return TextureImportType.Default;
        }

        // Metas from before texture types slice sprites through the sprite mode alone.
        if (SpriteModeOf(settings) != SpriteMode.None) return TextureImportType.Sprite;
        return TextureRuntimeImporter.NamesNormalMap(path) ? TextureImportType.NormalMap : TextureImportType.Default;
    }

    private static SpriteMode SpriteModeOf(EchoObject settings)
        => settings.TryGet("sprite", out EchoObject? sprite) && sprite.TagType == EchoType.Compound
            && sprite.TryGet("Mode", out EchoObject? mode) && mode.TagType != EchoType.String
            ? (SpriteMode)mode.IntValue : SpriteMode.None;

    private static bool Bool(EchoObject settings, string key, bool fallback)
        => settings.TryGet(key, out EchoObject? tag) ? tag.BoolValue : fallback;

    private static int Int(EchoObject settings, string key, int fallback)
        => settings.TryGet(key, out EchoObject? tag) ? tag.IntValue : fallback;

    private static float Float(EchoObject settings, string key, float fallback)
        => settings.TryGet(key, out EchoObject? tag) ? tag.FloatValue : fallback;

    private static T Enum<T>(EchoObject settings, string key, T fallback) where T : struct, System.Enum
        => settings.TryGet(key, out EchoObject? tag) ? (T)System.Enum.ToObject(typeof(T), tag.IntValue) : fallback;
}

/// <summary>How hard the importer works to compress a texture. None stores it uncompressed.</summary>
public enum TextureCompression
{
    None,
    Fast,
    Balanced,
    Thorough,
}

/// <summary>The stored format of an imported texture, Automatic choosing one from its type and content.</summary>
public enum TextureImportFormat
{
    Automatic,
    RGBA8,
    BC1,
    BC3,
    BC4,
    BC5,
    ASTC4x4,
    ASTC5x5,
    ASTC6x6,
    ASTC8x8,
    ASTC10x10,
    ASTC12x12,
    R8,
}

/// <summary>What a texture holds, which decides how it is processed, stored and sampled. The numbers are stored in metas.</summary>
public enum TextureImportType
{
    /// <summary>Colour or data on a surface, sRGB unless the name says data.</summary>
    Default = 0,
    /// <summary>A tangent space normal map: linear, renormalized mips, two channels where compressed.</summary>
    NormalMap = 1,
    /// <summary>A 2D or UI image cut into one or many sprites: colour, no mips, clamped, edges bled under alpha.</summary>
    Sprite = 3,
    /// <summary>A colour lookup table: linear, no mips, clamped, never compressed.</summary>
    LookupTable = 4,
    /// <summary>A light cookie: colour, no mips, clamped.</summary>
    Cookie = 5,
    /// <summary>One channel such as a height map, a mask or an IES profile, read from red.</summary>
    SingleChannel = 6,
    /// <summary>Baked lighting written by the lightmapper: RGBM encoded, clamped, never resized or compressed.</summary>
    Lightmap = 7,
}

/// <summary>Where an imported texture's alpha comes from.</summary>
public enum TextureAlphaSource
{
    None,
    Input,
    FromGrayscale,
}

/// <summary>How a normal map made from a height field takes its slopes.</summary>
public enum NormalMapFiltering
{
    Smooth,
    Sharp,
}

/// <summary>The source channel of a single channel texture.</summary>
public enum TextureChannel
{
    Red,
    Alpha,
}

// Everything below supports the texture's sprite sub-assets (config, slicing tools, sub-asset building, and
// the editor/inspector shared-edit bridge). Kept alongside TextureImporter since that's their only purpose.

#region Sprite settings

/// <summary>Whether a texture produces sprite sub-assets, and how.</summary>
public enum SpriteMode
{
    /// <summary>No sprites (a plain texture).</summary>
    None,
    /// <summary>One sprite covering the whole texture.</summary>
    Single,
    /// <summary>Many sprites, sliced from the texture in the Sprite Editor.</summary>
    Multiple,
}

/// <summary>The auto-slicing tool the Sprite Editor last used.</summary>
public enum SpriteSlicingTool
{
    /// <summary> Automatically detect and slice individual sprites by alpha content. </summary>
    Automatic,
    /// <summary> Slice into a grid where each cell has a fixed size. </summary>
    GridBySize,
    /// <summary> Slice into a grid with a fixed number of cells per row and column. </summary>
    GridByCount,
    /// <summary> Slice into a staggered isometric grid, with alternate rows offset by half a cell. </summary>
    Isometric,
}

/// <summary>How a slice's custom pivot value is interpreted.</summary>
public enum PivotUnitMode
{
    /// <summary> Pivot values are in normalized 0-1 range relative to the sprite rect. </summary>
    Normalized,
    /// <summary> Pivot values are in absolute pixel coordinates. </summary>
    Pixels,
}

/// <summary>One sprite's authoring record.</summary>
public class SpriteSliceData
{
    /// <summary>Persistent identity, minted once when the slice is created and carried in the texture's
    /// <c>.meta</c>. This is what the sprite sub-asset's GUID is derived from, so renaming the slice or the
    /// texture leaves every reference to it intact.</summary>
    public Guid Id;

    /// <summary> The display name of the sprite slice. </summary>
    public string Name = "sprite";
    /// <summary> The rectangle defining the slice's position and size in the texture, in bottom-left UV space. </summary>
    public SpriteRect Rect;
    /// <summary> The pivot alignment preset for this slice. </summary>
    public SpriteAlignment Alignment = SpriteAlignment.Center;
    /// <summary> The custom pivot position, interpreted according to PivotUnit. </summary>
    public Float2 CustomPivot = new(0.5f, 0.5f);
    /// <summary> How CustomPivot is interpreted: Normalized (0-1) or Pixels. </summary>
    public PivotUnitMode PivotUnit = PivotUnitMode.Normalized;

    /// <summary>9-slice border in pixels (Left, Top, Right, Bottom).</summary>
    public Float4 Border = default;
}

/// <summary>
/// A texture's sprite configuration, serialized into the texture's <c>.meta</c> import settings under the
/// <c>"sprite"</c> key. The <see cref="TextureImporter"/> reads this and emits one <see cref="Sprite"/>
/// sub-asset per slice; the Sprite Editor reads/writes it.
/// </summary>
public class SpriteImportSettings
{
    /// <summary> Whether the texture produces sprite sub-assets, and how (None, Single, or Multiple). </summary>
    public SpriteMode Mode = SpriteMode.None;
    /// <summary> The number of pixels in the texture that correspond to one world-space unit. </summary>
    public float PixelsPerUnit = 100f;

    // Per-asset tight mesh.
    /// <summary> Whether to generate a mesh that follows the sprite's alpha shape rather than a full quad. </summary>
    public bool GenerateTightMesh = false;
    /// <summary> The detail level of the tight mesh, controlling how closely it follows the alpha contour. </summary>
    public float TightMeshDetail = 1.5f;
    /// <summary> Alpha values below this threshold are treated as transparent when generating a tight mesh. </summary>
    public byte TightMeshAlphaThreshold = 1;

    /// <summary>Named secondary maps (e.g. "_NormalMap") applied to every sprite in this texture.</summary>
    public Dictionary<string, Texture2D> SecondaryTextures = [];

    // Slicing-tool state (editor convenience; the importer only reads Slices).
    /// <summary> The auto-slicing tool last used in the Sprite Editor. The importer only reads Slices, not this value. </summary>
    public SpriteSlicingTool SlicingTool = SpriteSlicingTool.GridBySize;
    /// <summary> The pivot alignment applied to slices generated by the auto-slicing tools. </summary>
    public SpriteAlignment GeneratedPivot = SpriteAlignment.Center;
    /// <summary> The cell size (width, height) in pixels for GridBySize and Isometric slicing. </summary>
    public Int2 GridCellSize = new(16, 16);
    /// <summary> The number of cells (columns, rows) for GridByCount slicing. </summary>
    public Int2 GridCellCount = new(4, 4);
    /// <summary> The offset from the top-left corner where the grid starts, in pixels. </summary>
    public Int2 GridOffset = default;
    /// <summary> The padding between cells in the grid, in pixels. </summary>
    public Int2 GridPadding = default;
    /// <summary> Whether to keep grid cells that contain no opaque alpha content. </summary>
    public bool KeepEmptyRects = false;
    /// <summary> Whether the isometric grid alternates the offset direction (odd rows shift right instead of left). </summary>
    public bool IsoIsAlternate = false;

    /// <summary> The authored slice list. This is what the importer reads to build sprite sub-assets. </summary>
    public List<SpriteSliceData> Slices = [];
}

#endregion

#region Sprite meta (.meta read/write)

/// <summary>
/// Reads and writes a texture's <see cref="SpriteImportSettings"/> inside its <c>.meta</c> import settings
/// (under the <c>"sprite"</c> key). Bridges the Sprite Editor and the <see cref="TextureImporter"/>.
/// </summary>
public static class TextureSpriteMeta
{
    private const string Key = "sprite";

    /// <summary>
    /// Deserializes the sprite settings from a texture's <c>.meta</c> settings compound. An absent block
    /// legitimately means defaults; a block that is present but unreadable is reported through
    /// <paramref name="failed"/> so callers can refuse to write over it. Silently substituting defaults
    /// would present the texture as having no sprites and let the next save destroy the real slicing.
    /// </summary>
    public static SpriteImportSettings ReadFrom(EchoObject? settings, out bool failed)
    {
        failed = false;
        if (settings != null && settings.TryGet(Key, out EchoObject echo))
        {
            try
            {
                SerializationContext ctx = ImportHelper.CreateTrackingContext(out _);
                SpriteImportSettings? parsed = Serializer.Deserialize<SpriteImportSettings>(echo, ctx);
                if (parsed != null)
                {
                    // A Sprite texture always makes sprites, so one saved without a mode makes a single one.
                    if (parsed.Mode == SpriteMode.None && TextureImportSettings.Read(settings, "").Type == TextureImportType.Sprite)
                        parsed.Mode = SpriteMode.Single;
                    return parsed;
                }

                failed = true;
                Debug.LogError("[Sprite] Sprite import settings deserialized to null. Keeping the existing block rather than replacing it with defaults.");
            }
            catch (Exception ex)
            {
                failed = true;
                Debug.LogException($"[Sprite] Failed to read sprite import settings", ex);
            }
        }
        return new SpriteImportSettings();
    }

    /// <summary>Settings-only overload, for callers that just build from whatever is readable.</summary>
    public static SpriteImportSettings ReadFrom(EchoObject? settings) => ReadFrom(settings, out _);

    /// <summary>
    /// Serializes the sprite settings into a texture's <c>.meta</c> settings compound. A sprite mode makes it a Sprite
    /// texture, and no mode turns a Sprite texture back into a Default one.
    /// </summary>
    public static void WriteInto(EchoObject settings, SpriteImportSettings s)
    {
        AssignMissingSliceIds(s);
        settings[Key] = Serializer.Serialize(typeof(SpriteImportSettings), s);

        bool sprite = settings.TryGet(TextureImportSettings.Keys.Type, out EchoObject? type) && type.IntValue == (int)TextureImportType.Sprite;
        if (s.Mode != SpriteMode.None && !sprite)
            settings[TextureImportSettings.Keys.Type] = new EchoObject((int)TextureImportType.Sprite);
        else if (s.Mode == SpriteMode.None && sprite)
            settings[TextureImportSettings.Keys.Type] = new EchoObject((int)TextureImportType.Default);
    }

    /// <summary>Gives every slice a persistent ID before it reaches disk. The single choke point for
    /// slices authored anywhere other than the slicing tools (hand-edited metas, older projects).</summary>
    private static void AssignMissingSliceIds(SpriteImportSettings s)
    {
        foreach (SpriteSliceData slice in s.Slices)
            if (slice.Id == Guid.Empty)
                slice.Id = Guid.NewGuid();
    }

    /// <summary>Loads the sprite settings for a texture by GUID. <paramref name="failed"/> is true when
    /// existing settings could not be read, in which case the caller must not save over them.</summary>
    public static SpriteImportSettings Load(Guid textureGuid, out bool failed)
    {
        failed = false;
        try
        {
            string abs = AbsolutePath(textureGuid);
            string metaPath = MetaFile.GetMetaPath(abs);
            if (!File.Exists(metaPath)) return new SpriteImportSettings();
            return ReadFrom(MetaFile.Read(metaPath).Settings, out failed);
        }
        catch (Exception ex)
        {
            failed = true;
            Debug.LogException($"[Sprite] Could not read the meta for texture {textureGuid}", ex);
            return new SpriteImportSettings();
        }
    }

    /// <summary>Settings-only overload.</summary>
    public static SpriteImportSettings Load(Guid textureGuid) => Load(textureGuid, out _);

    /// <summary>Writes the sprite settings into a texture's <c>.meta</c> and reimports it.</summary>
    public static void Save(Guid textureGuid, SpriteImportSettings s)
    {
        string abs = AbsolutePath(textureGuid);
        string metaPath = MetaFile.GetMetaPath(abs);

        MetaFileData meta = File.Exists(metaPath)
            ? MetaFile.Read(metaPath)
            : MetaFile.CreateNew(nameof(TextureImporter));

        meta.Settings ??= EchoObject.NewCompound();
        WriteInto(meta.Settings, s);
        MetaFile.Write(metaPath, meta);
        EditorAssetBackend.Instance?.Reimport(textureGuid);
    }

    private static string AbsolutePath(Guid textureGuid)
    {
        string rel = EditorAssetBackend.Instance?.GuidToPath(textureGuid) ?? "";
        return Path.Combine(Project.Current.AssetsPath, rel);
    }

    /// <summary>The source image's size, which slices are authored in, or zero when it cannot be read.</summary>
    public static (int Width, int Height) SourceSize(Guid textureGuid)
    {
        try
        {
            Aperture.ImageInfo info = Aperture.Image.Identify(AbsolutePath(textureGuid));
            return (info.Width, info.Height);
        }
        catch (Exception ex)
        {
            Prowl.Runtime.Debug.LogWarning($"[Sprite] Could not read the source image of texture {textureGuid}: {ex.Message}");
            return (0, 0);
        }
    }

    /// <summary>The source image's alpha at its own size, bottom row first, for slicing. Null when it cannot be read.</summary>
    public static byte[]? SourceAlpha(Guid textureGuid)
    {
        try
        {
            using Aperture.Image image = Aperture.Image.Load(AbsolutePath(textureGuid), Texture2D.DecodeToUploadLayout);
            byte[] rgba = new byte[image.Width * image.Height * 4];
            image.RootFrame.CopyTo(rgba, image.Width * 4);
            return SpriteBuilder.AlphaOf(rgba);
        }
        catch (Exception ex)
        {
            Prowl.Runtime.Debug.LogWarning($"[Sprite] Could not read the source alpha of texture {textureGuid}: {ex.Message}");
            return null;
        }
    }
}

#endregion

#region Sprite edit registry (shared inspector / editor instance)

/// <summary>A texture's in-progress sprite settings, shared between the texture inspector and the Sprite Editor.</summary>
public sealed class SpriteEditTarget
{
    /// <summary> The GUID of the texture this target edits. </summary>
    public Guid TextureGuid;
    /// <summary> The in-memory sprite settings being edited. </summary>
    public SpriteImportSettings Settings = new();

    /// <summary>Set when <see cref="Settings"/> has been edited and not yet folded back into the
    /// texture's import-settings compound. The compound is what the inspector diffs to decide whether
    /// anything needs applying, so this only marks "needs re-serializing", not "needs saving".</summary>
    public bool Dirty;

    /// <summary>True when the texture's existing sprite settings could not be read. <see cref="Settings"/>
    /// is then defaults that do not describe what is on disk, so saving would destroy the real slicing.</summary>
    public bool LoadFailed;
}

/// <summary>
/// Holds one live <see cref="SpriteEditTarget"/> per texture so the texture inspector and the Sprite Editor
/// window edit the same <see cref="SpriteImportSettings"/> instance. The Sprite Editor just mutates it and
/// flags it dirty; the inspector owns the actual "Save &amp; Reimport" (persist to <c>.meta</c> + reimport).
/// </summary>
public static class SpriteEditRegistry
{
    private static readonly Dictionary<Guid, SpriteEditTarget> _targets = [];

    /// <summary>Gets the shared target for a texture, loading its settings from the <c>.meta</c> on first use.</summary>
    public static SpriteEditTarget Get(Guid textureGuid)
    {
        if (!_targets.TryGetValue(textureGuid, out SpriteEditTarget? t))
        {
            t = new SpriteEditTarget
            {
                TextureGuid = textureGuid,
                Settings = TextureSpriteMeta.Load(textureGuid, out bool failed),
                LoadFailed = failed,
            };
            _targets[textureGuid] = t;
        }
        return t;
    }

    /// <summary>Replaces a texture's sprite settings wholesale - used when a revert restores the meta
    /// values and the live copy has to follow.</summary>
    public static void SetSettings(Guid textureGuid, SpriteImportSettings settings)
    {
        SpriteEditTarget t = Get(textureGuid);
        t.Settings = settings;
        t.Dirty = false;
        t.LoadFailed = false;
    }

    /// <summary> Returns true if the texture's sprite settings have been modified and not yet re-serialized. </summary>
    public static bool IsDirty(Guid textureGuid) => _targets.TryGetValue(textureGuid, out SpriteEditTarget? t) && t.Dirty;

    /// <summary> Clears the dirty flag for the given texture, acknowledging that the settings have been re-serialized. </summary>
    public static void ClearDirty(Guid textureGuid)
    {
        if (_targets.TryGetValue(textureGuid, out SpriteEditTarget? t)) t.Dirty = false;
    }

    /// <summary>Reloads the target's settings from disk, discarding unsaved edits.</summary>
    public static void Reload(Guid textureGuid)
    {
        if (_targets.TryGetValue(textureGuid, out SpriteEditTarget? t))
        {
            t.Settings = TextureSpriteMeta.Load(textureGuid, out bool failed);
            t.LoadFailed = failed;
            t.Dirty = false;
        }
    }
}

#endregion

#region Sprite slicing tools

/// <summary>
/// Auto-slicing tools that generate a fresh list of <see cref="SpriteSliceData"/> rects from a texture.
/// Grid parameters use top-left display space; results are <see cref="SpriteRect"/>s in bottom-left / UV
/// space. Tools that skip empty cells (and the Automatic tool) need the texture's alpha, read via
/// <see cref="ReadAlpha"/> (shared with the importer's tight-mesh path).
/// </summary>
public static class SpriteSlicer
{
    private const byte OpaqueThreshold = 1;

    /// <summary>Runs the tool selected in <paramref name="data"/> and returns a fresh slice list.</summary>
    public static List<SpriteSliceData> Slice(SpriteImportSettings data, int textureWidth, int textureHeight, byte[]? alpha, string baseName)
    {
        Float2 pivot = Sprite.PivotFromAlignment(data.GeneratedPivot, new Float2(0.5f, 0.5f));

        List<SpriteSliceData> generated = data.SlicingTool switch
        {
            SpriteSlicingTool.Automatic => Automatic(alpha, textureWidth, textureHeight, baseName, data.GeneratedPivot, pivot),
            SpriteSlicingTool.GridByCount => GridByCount(data, textureWidth, textureHeight, alpha, baseName, pivot),
            SpriteSlicingTool.Isometric => Isometric(data, textureWidth, textureHeight, alpha, baseName, pivot),
            _ => GridBySize(data, textureWidth, textureHeight, alpha, baseName, pivot),
        };

        return SpriteSliceMatcher.CarryOverIdentities(data.Slices, generated);
    }

    private static List<SpriteSliceData> GridBySize(SpriteImportSettings d, int texW, int texH, byte[]? alpha, string baseName, Float2 pivot)
    {
        int cellW = Math.Max(1, d.GridCellSize.X), cellH = Math.Max(1, d.GridCellSize.Y);
        return GridCells(texW, texH, cellW, cellH, d.GridOffset, d.GridPadding, d.KeepEmptyRects, alpha, baseName, d.GeneratedPivot, pivot);
    }

    private static List<SpriteSliceData> GridByCount(SpriteImportSettings d, int texW, int texH, byte[]? alpha, string baseName, Float2 pivot)
    {
        int cols = Math.Max(1, d.GridCellCount.X), rows = Math.Max(1, d.GridCellCount.Y);
        int cellW = (texW - d.GridOffset.X - (cols - 1) * d.GridPadding.X) / cols;
        int cellH = (texH - d.GridOffset.Y - (rows - 1) * d.GridPadding.Y) / rows;
        if (cellW <= 0 || cellH <= 0) return [];
        return GridCells(texW, texH, cellW, cellH, d.GridOffset, d.GridPadding, d.KeepEmptyRects, alpha, baseName, d.GeneratedPivot, pivot);
    }

    private static List<SpriteSliceData> GridCells(int texW, int texH, int cellW, int cellH, Int2 offset, Int2 padding,
        bool keepEmpty, byte[]? alpha, string baseName, SpriteAlignment align, Float2 pivot)
    {
        var result = new List<SpriteSliceData>();
        for (int yTL = offset.Y; yTL + 1 <= texH; yTL += cellH + padding.Y)
        {
            int h = Math.Min(cellH, texH - yTL);
            if (h <= 0) break;
            for (int xTL = offset.X; xTL + 1 <= texW; xTL += cellW + padding.X)
            {
                int w = Math.Min(cellW, texW - xTL);
                if (w <= 0) break;
                var rect = new SpriteRect(xTL, texH - yTL - h, w, h);
                if (!keepEmpty && alpha != null && !AnyOpaque(alpha, texW, texH, rect)) continue;
                AddSlice(result, rect, baseName, align, pivot);
            }
        }
        return result;
    }

    private static List<SpriteSliceData> Isometric(SpriteImportSettings d, int texW, int texH, byte[]? alpha, string baseName, Float2 pivot)
    {
        // Staggered grid: alternate rows shift horizontally by half a cell. This is an approximation of an
        // isometric layout (no vertical diamond overlap); good enough as a starting point.
        var result = new List<SpriteSliceData>();
        int cellW = Math.Max(1, d.GridCellSize.X), cellH = Math.Max(1, d.GridCellSize.Y);
        int half = cellW / 2;

        int row = 0;
        for (int yTL = d.GridOffset.Y; yTL + 1 <= texH; yTL += cellH, row++)
        {
            int h = Math.Min(cellH, texH - yTL);
            if (h <= 0) break;
            bool shift = (row % 2 == 1) != d.IsoIsAlternate;
            int startX = d.GridOffset.X + (shift ? half : 0);
            for (int xTL = startX; xTL + 1 <= texW; xTL += cellW)
            {
                int w = Math.Min(cellW, texW - xTL);
                if (w <= 0) break;
                var rect = new SpriteRect(xTL, texH - yTL - h, w, h);
                if (!d.KeepEmptyRects && alpha != null && !AnyOpaque(alpha, texW, texH, rect)) continue;
                AddSlice(result, rect, baseName, d.GeneratedPivot, pivot);
            }
        }
        return result;
    }

    private static List<SpriteSliceData> Automatic(byte[]? alpha, int texW, int texH, string baseName, SpriteAlignment align, Float2 pivot)
    {
        var result = new List<SpriteSliceData>();
        if (alpha == null) return result;

        bool[] visited = new bool[texW * texH];
        var stack = new Stack<int>();

        for (int start = 0; start < visited.Length; start++)
        {
            if (visited[start] || alpha[start] < OpaqueThreshold) continue;

            int minX = texW, minY = texH, maxX = 0, maxY = 0;
            stack.Push(start);
            visited[start] = true;

            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                int x = idx % texW, y = idx / texW;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= texW || ny >= texH) continue;
                        int nIdx = ny * texW + nx;
                        if (visited[nIdx] || alpha[nIdx] < OpaqueThreshold) continue;
                        visited[nIdx] = true;
                        stack.Push(nIdx);
                    }
                }
            }

            AddSlice(result, new SpriteRect(minX, minY, maxX - minX + 1, maxY - minY + 1), baseName, align, pivot);
        }

        return result;
    }

    private static void AddSlice(List<SpriteSliceData> list, SpriteRect rect, string baseName, SpriteAlignment align, Float2 pivot)
    {
        list.Add(new SpriteSliceData
        {
            Id = Guid.NewGuid(),
            Name = $"{baseName}_{list.Count}",
            Rect = rect,
            Alignment = align,
            CustomPivot = pivot,
            PivotUnit = PivotUnitMode.Normalized,
        });
    }

    private static bool AnyOpaque(byte[] alpha, int texW, int texH, SpriteRect rect)
    {
        int x0 = Math.Max(0, rect.X), y0 = Math.Max(0, rect.Y);
        int x1 = Math.Min(texW, rect.MaxX), y1 = Math.Min(texH, rect.MaxY);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (alpha[y * texW + x] >= OpaqueThreshold) return true;
        return false;
    }

    /// <summary>
    /// Reads a texture's alpha channel into an 8-bit, bottom-left-origin grid (length <c>w*h</c>), or null
    /// if readback fails / the format is unsupported. Shared by the slicing tools and the importer's
    /// tight-mesh pass. Requires a GL context (returns null off the render thread).
    /// </summary>
    public static byte[]? ReadAlpha(Texture2D tex)
    {
        try
        {
            if (tex.GetMipRgba8() is byte[] expanded)
                return SpriteBuilder.AlphaOf(expanded);

            if (tex.Format != TextureFormat.RGBA16)
                return null;

            int count = (int)tex.Width * (int)tex.Height;
            ushort[] rgba = new ushort[count * 4];
            tex.GetData<ushort>(rgba.AsMemory());
            byte[] alpha = new byte[count];
            for (int i = 0; i < count; i++) alpha[i] = (byte)(rgba[i * 4 + 3] >> 8);
            return alpha;
        }
        catch (Exception ex)
        {
            Prowl.Runtime.Debug.LogWarning($"[SpriteSlicer] Could not read texture alpha: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Carries authored slice identity across a re-slice.
/// </summary>
public static class SpriteSliceMatcher
{
    /// <summary>How much of the smaller rect two slices must share to count as the same sprite moved.</summary>
    private const float MinOverlapFraction = 0.5f;

    /// <summary> Carries over slice identities (Id, Name, Border, custom pivot) from the previous slice list to the newly generated one, matching by identical rect first then by best overlap. </summary>
    public static List<SpriteSliceData> CarryOverIdentities(List<SpriteSliceData> previous, List<SpriteSliceData> generated)
    {
        if (previous.Count == 0 || generated.Count == 0) return generated;

        bool[] oldClaimed = new bool[previous.Count];
        bool[] newMatched = new bool[generated.Count];

        // Pass 1: identical rects. A cell the re-slice didn't move keeps its slice outright. Built in
        // reverse so the earliest duplicate wins, which keeps the result independent of dictionary order.
        var byRect = new Dictionary<(int, int, int, int), int>(previous.Count);
        for (int i = previous.Count - 1; i >= 0; i--)
            byRect[KeyOf(previous[i].Rect)] = i;

        for (int n = 0; n < generated.Count; n++)
        {
            if (!byRect.TryGetValue(KeyOf(generated[n].Rect), out int o) || oldClaimed[o]) continue;
            oldClaimed[o] = true;
            newMatched[n] = true;
            Adopt(generated[n], previous[o]);
        }

        // Pass 2: greedy best overlap over what's left, so nudging a grid keeps its sprites rather than
        // reissuing them. Strongest overlap wins first; ties resolve by index so the result is stable.
        var pairs = new List<(int New, int Old, long Overlap)>();
        for (int n = 0; n < generated.Count; n++)
        {
            if (newMatched[n]) continue;
            long newArea = AreaOf(generated[n].Rect);
            for (int o = 0; o < previous.Count; o++)
            {
                if (oldClaimed[o]) continue;
                long overlap = IntersectionArea(generated[n].Rect, previous[o].Rect);
                if (overlap <= 0) continue;
                long smaller = Math.Min(newArea, AreaOf(previous[o].Rect));
                if (smaller <= 0 || overlap < smaller * MinOverlapFraction) continue;
                pairs.Add((n, o, overlap));
            }
        }

        pairs.Sort((a, b) =>
        {
            int byOverlap = b.Overlap.CompareTo(a.Overlap);
            if (byOverlap != 0) return byOverlap;
            int byNew = a.New.CompareTo(b.New);
            return byNew != 0 ? byNew : a.Old.CompareTo(b.Old);
        });

        foreach ((int n, int o, _) in pairs)
        {
            if (newMatched[n] || oldClaimed[o]) continue;
            newMatched[n] = true;
            oldClaimed[o] = true;
            Adopt(generated[n], previous[o]);
        }

        return generated;
    }

    private static void Adopt(SpriteSliceData target, SpriteSliceData source)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.Border = source.Border;

        if (source.Alignment == SpriteAlignment.Custom)
        {
            target.Alignment = SpriteAlignment.Custom;
            target.CustomPivot = source.CustomPivot;
            target.PivotUnit = source.PivotUnit;
        }
    }

    private static (int, int, int, int) KeyOf(SpriteRect r) => (r.X, r.Y, r.Width, r.Height);

    private static long AreaOf(SpriteRect r) => (long)Math.Max(0, r.Width) * Math.Max(0, r.Height);

    private static long IntersectionArea(SpriteRect a, SpriteRect b)
    {
        long w = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.X, b.X);
        long h = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

#endregion

#region Sprite building (settings -> Sprite sub-assets)

/// <summary>
/// Builds a texture's <see cref="Sprite"/> sub-assets from its <see cref="SpriteImportSettings"/>. Single
/// mode yields one full-texture sprite; Multiple yields one per slice. Handles pivot-unit conversion,
/// per-sprite border, secondary maps, and optional tight-mesh tracing.
/// </summary>
public static class SpriteBuilder
{
    /// <summary>The identity of Single mode's one sprite. Fixed, because there is only ever one of them.</summary>
    private const string SingleIdentity = "single";

    /// <summary>
    /// The identity seeding a slice's sub-asset GUID. Always an explicit key, never order: slices are a
    /// user-edited list, and the grid slicer skips cells that become fully transparent, so an index here
    /// tracks neither the slice nor anything stable about it.
    /// </summary>
    public static SubAssetIdentity IdentityOf(SpriteImportSettings s, SpriteSliceData slice)
        => SubAssetIdentity.Key(s.Mode == SpriteMode.Single ? SingleIdentity : slice.Id.ToString("N"));

    /// <summary>Builds Sprite sub-assets for a texture that is its own source, reading tight mesh alpha back from it.</summary>
    public static List<(SpriteSliceData slice, Sprite sprite)> Build(Texture2D tex, SpriteImportSettings s)
        => Build(tex, s, (int)tex.Width, (int)tex.Height, s.GenerateTightMesh ? tex.GetMipRgba8() : null);

    /// <summary>
    /// Builds Sprite sub-assets from the texture and its sprite settings. Slices, borders and pivots are authored in the
    /// source image's pixels and scaled to the imported size, so a Max Size limit changes neither where a sprite cuts
    /// nor how large it is in the world. Single mode always covers the whole texture. <paramref name="pixels"/> are the
    /// imported RGBA8 texels tight meshes trace.
    /// </summary>
    public static List<(SpriteSliceData slice, Sprite sprite)> Build(Texture2D tex, SpriteImportSettings s, int sourceWidth, int sourceHeight, byte[]? pixels)
    {
        var result = new List<(SpriteSliceData, Sprite)>();
        int texW = (int)tex.Width, texH = (int)tex.Height;
        if (texW <= 0 || texH <= 0 || sourceWidth <= 0 || sourceHeight <= 0) return result;

        float sx = texW / (float)sourceWidth, sy = texH / (float)sourceHeight;
        byte[]? alpha = s.GenerateTightMesh && pixels != null ? AlphaOf(pixels) : null;
        int clampedCount = 0;

        foreach (SpriteSliceData slice in ResolveSlices(s, sourceWidth, sourceHeight, tex.Name))
        {
            SpriteRect source = ClampToTexture(slice.Rect, sourceWidth, sourceHeight);
            if (!SameRect(source, slice.Rect)) clampedCount++;
            result.Add((slice, BuildOne(tex, s, slice, source, Scale(source, sx, sy, texW, texH), sx, sy, texW, texH, alpha)));
        }

        // Replacing the source image with a smaller one leaves the stored rects describing the old
        // dimensions, which would otherwise produce UVs past 1 and sample whatever Repeat wrap lands on.
        // Only the built sprite is clamped - the authored slicing is left alone so restoring the original
        // image (or re-slicing) brings it straight back.
        if (clampedCount > 0)
            Prowl.Runtime.Debug.LogWarning(
                $"[Sprite] '{tex.Name}': {clampedCount} slice(s) fall outside the {sourceWidth}x{sourceHeight} texture and were clamped for this import. The stored slicing is unchanged.");

        return result;
    }

    /// <summary>The alpha channel of RGBA8 texels.</summary>
    public static byte[] AlphaOf(byte[] rgba)
    {
        byte[] alpha = new byte[rgba.Length / 4];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = rgba[(i * 4) + 3];
        return alpha;
    }

    /// <summary>A source pixel rect in the imported texture's pixels, never empty.</summary>
    private static SpriteRect Scale(SpriteRect r, float sx, float sy, int texW, int texH)
    {
        int x0 = Math.Clamp((int)MathF.Round(r.X * sx), 0, texW - 1);
        int y0 = Math.Clamp((int)MathF.Round(r.Y * sy), 0, texH - 1);
        int x1 = Math.Clamp((int)MathF.Round(r.MaxX * sx), x0 + 1, texW);
        int y1 = Math.Clamp((int)MathF.Round(r.MaxY * sy), y0 + 1, texH);
        return new SpriteRect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Trims a rect to the texture, coping with a negative origin and an oversized extent.</summary>
    private static SpriteRect ClampToTexture(SpriteRect r, int texW, int texH)
    {
        int x0 = Math.Clamp(r.X, 0, texW);
        int y0 = Math.Clamp(r.Y, 0, texH);
        int x1 = Math.Clamp(r.MaxX, 0, texW);
        int y1 = Math.Clamp(r.MaxY, 0, texH);
        return new SpriteRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    private static bool SameRect(SpriteRect a, SpriteRect b)
        => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    private static List<SpriteSliceData> ResolveSlices(SpriteImportSettings s, int texW, int texH, string baseName)
    {
        if (s.Mode != SpriteMode.Single)
            return s.Slices;

        // The one sprite covers the whole texture whatever rect was last stored, keeping the slice's identity and pivot.
        var full = new SpriteRect(0, 0, texW, texH);
        if (s.Slices.Count == 0)
            return [new() { Name = baseName, Rect = full, Alignment = SpriteAlignment.Center }];

        SpriteSliceData first = s.Slices[0];
        return
        [
            new()
            {
                Id = first.Id, Name = first.Name, Rect = full, Alignment = first.Alignment,
                CustomPivot = first.CustomPivot, PivotUnit = first.PivotUnit, Border = first.Border,
            },
        ];
    }

    /// <summary>
    /// <paramref name="source"/> is the slice clamped to the source image, <paramref name="rect"/> the same area in the
    /// imported texture. Pixel sizes scale with it so the sprite keeps its world size.
    /// </summary>
    private static Sprite BuildOne(Texture2D tex, SpriteImportSettings s, SpriteSliceData slice, SpriteRect source, SpriteRect rect,
        float sx, float sy, int texW, int texH, byte[]? alpha)
    {
        Float4 border = slice.Border;
        var sprite = new Sprite
        {
            Name = slice.Name,
            Texture = tex,
            Rect = rect,
            Pivot = ResolvePivot(slice, source),
            PixelsPerUnit = s.PixelsPerUnit * sx,
            Border = new Float4(border.X * sx, border.Y * sy, border.Z * sx, border.W * sy),
            SecondaryTextures = new Dictionary<string, Texture2D>(s.SecondaryTextures),
        };

        if (s.GenerateTightMesh && alpha != null && rect.Width > 0 && rect.Height > 0)
        {
            byte[] rectAlpha = ExtractRectAlpha(alpha, texW, texH, rect);
            SpriteMeshTracer.TracedMesh traced = SpriteMeshTracer.Generate(rectAlpha, rect.Width, rect.Height,
                s.TightMeshAlphaThreshold, s.TightMeshDetail);
            sprite.BuildTightGeometry(traced, texW, texH);
        }
        else
        {
            sprite.BuildQuadGeometry(texW, texH);
        }

        return sprite;
    }

    private static Float2 ResolvePivot(SpriteSliceData slice, SpriteRect rect)
    {
        if (slice.Alignment != SpriteAlignment.Custom)
            return Sprite.PivotFromAlignment(slice.Alignment);

        if (slice.PivotUnit == PivotUnitMode.Pixels)
        {
            float w = Math.Max(1, rect.Width);
            float h = Math.Max(1, rect.Height);
            return new Float2(slice.CustomPivot.X / w, slice.CustomPivot.Y / h);
        }
        return slice.CustomPivot;
    }

    private static byte[] ExtractRectAlpha(byte[] fullAlpha, int texW, int texH, SpriteRect rect)
    {
        int rw = rect.Width, rh = rect.Height;
        byte[] a = new byte[rw * rh];
        for (int ly = 0; ly < rh; ly++)
        {
            int sy = rect.Y + ly;
            if (sy < 0 || sy >= texH) continue;
            for (int lx = 0; lx < rw; lx++)
            {
                int sx = rect.X + lx;
                if (sx < 0 || sx >= texW) continue;
                a[ly * rw + lx] = fullAlpha[sy * texW + sx];
            }
        }
        return a;
    }
}

#endregion
