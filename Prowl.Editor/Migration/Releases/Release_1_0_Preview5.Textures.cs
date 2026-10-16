// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0_Preview5
{
    internal static MigrationStep[] GraphiteTextureSteps =>
    [
        new("Texture import settings to filter and wrap per axis", ctx => ctx.RewriteMetaSettings(ConvertTextureImportSettings)),
        new("Textures used as data are linear", MarkDataTextures),
        new("Saved textures to the Graphite formats", ctx => ctx.RewriteEchoAssets(ConvertTextureData)),
    ];

    // Old TextureMin: Nearest, Linear, NearestMipmapNearest, LinearMipmapNearest, NearestMipmapLinear, LinearMipmapLinear.
    // Old TextureMag: Nearest, Linear. New FilterMode: Point, Bilinear, Trilinear.
    internal static int FilterFromGl(int min, int mag) => mag == 0 ? 0 : min is 4 or 5 ? 2 : 1;

    // Old TextureWrap: Repeat, ClampToBorder, ClampToEdge, MirroredRepeat. New WrapMode: Repeat, Clamp, Mirror, Border.
    internal static int WrapFromGl(int wrap) => wrap switch { 1 => 3, 2 => 1, 3 => 2, _ => 0 };

    internal static EchoObject? ConvertTextureImportSettings(EchoObject settings)
    {
        if (settings.TagType != EchoType.Compound) return null;
        bool hasMin = settings.TryGet("minFilter", out EchoObject? min);
        bool hasMag = settings.TryGet("magFilter", out EchoObject? mag);
        bool hasWrap = settings.TryGet("wrapMode", out EchoObject? wrap);
        if (!hasMin && !hasMag && !hasWrap) return null;

        settings["filter"] = new EchoObject(FilterFromGl(hasMin ? min!.IntValue : 5, hasMag ? mag!.IntValue : 1));
        int wrapMode = WrapFromGl(hasWrap ? wrap!.IntValue : 0);
        settings["wrapU"] = new EchoObject(wrapMode);
        settings["wrapV"] = new EchoObject(wrapMode);
        if (!settings.TryGet("anisotropy", out _)) settings["anisotropy"] = new EchoObject(0);
        settings.Remove("minFilter");
        settings.Remove("magFilter");
        settings.Remove("wrapMode");
        return settings;
    }

    // Material slots that hold data rather than colour. Shaders used to decode colour by hand, so every texture was
    // stored linear; now colour textures are sRGB, and a texture a material reads as data has to say it is not.
    private static readonly string[] s_normalSlots = ["_NormalTex", "_NormalMap", "_BumpMap"];
    private static readonly string[] s_dataSlots = ["_SurfaceTex", "_OcclusionTex", "_ParallaxMap", "_HeightTex", "_MaskTex", "_MetallicTex", "_RoughnessTex", "_DetailMask"];
    private static readonly string[] s_colourSlots = ["_MainTex", "_EmissionTex", "_DetailAlbedo"];

    // TextureImportType: Default, NormalMap, Mask.
    private const int DefaultType = 0, NormalMapType = 1, MaskType = 2;

    private static void MarkDataTextures(MigrationContext ctx)
    {
        Dictionary<string, int> uses = [];
        HashSet<string> colour = [];
        ctx.RewriteEchoAssets(echo =>
        {
            CollectTextureSlots(echo, uses, colour);
            return null;
        });

        foreach (string metaPath in Directory.EnumerateFiles(ctx.Project.AssetsPath, "*.meta", SearchOption.AllDirectories))
        {
            MetaFileData meta = MetaFile.Read(metaPath);
            if (!meta.ImporterType.EndsWith("TextureImporter", StringComparison.Ordinal) || meta.Settings == null) continue;
            if (ChooseTextureType(meta.Settings, meta.Guid.ToString(), metaPath[..^".meta".Length], uses, colour) is not int type) continue;
            meta.Settings["textureType"] = new EchoObject(type);
            MetaFile.Write(metaPath, meta);
            ctx.ChangedFiles.Add(metaPath);
        }
    }

    /// <summary>The type a texture's meta should be given from how materials use it, null to leave it to its name.</summary>
    internal static int? ChooseTextureType(EchoObject settings, string guid, string path, Dictionary<string, int> uses, HashSet<string> colour)
    {
        if (settings.TryGet("textureType", out _) || settings.TryGet("srgb", out _)) return null;
        if (colour.Contains(guid)) return Runtime.TextureRuntimeImporter.NamesData(path) ? DefaultType : null;
        return uses.TryGetValue(guid, out int type) ? type : null;
    }

    /// <summary>Finds every material texture slot in a tree: data slots into uses by GUID, colour slots into colour.</summary>
    internal static void CollectTextureSlots(EchoObject tag, Dictionary<string, int> uses, HashSet<string> colour)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                CollectTextureSlots(item, uses, colour);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (tag.TryGet("_textures", out EchoObject? textures) && textures!.TagType == EchoType.Compound
            && textures.TryGet("entries", out EchoObject? entries) && entries!.TagType == EchoType.List)
        {
            foreach (EchoObject entry in entries.List)
            {
                if (entry.TagType != EchoType.Compound || !entry.TryGet("key", out EchoObject? key) || key!.TagType != EchoType.String) continue;
                if (!entry.TryGet("value", out EchoObject? value) || value!.TagType != EchoType.Compound
                    || !value.TryGet("$asset", out EchoObject? asset) || asset!.TagType != EchoType.String) continue;
                string guid = asset.StringValue.Split('#')[0];
                if (s_colourSlots.Contains(key.StringValue)) colour.Add(guid);
                else if (s_normalSlots.Contains(key.StringValue)) uses[guid] = NormalMapType;
                else if (s_dataSlots.Contains(key.StringValue)) uses.TryAdd(guid, MaskType);
            }
        }

        foreach (string name in tag.GetNames().ToList())
            CollectTextureSlots(tag[name], uses, colour);
    }

    internal static EchoObject? ConvertTextureData(EchoObject echo)
    {
        bool changed = false;
        ConvertTextureData(echo, ref changed);
        return changed ? echo : null;
    }

    private static void ConvertTextureData(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                ConvertTextureData(item, ref changed);
            return;
        }
        if (tag.TagType != EchoType.Compound) return;

        if (tag.TryGet("ImageFormat", out _) && tag.TryGet("MinFilter", out _))
        {
            ConvertTexture(tag);
            changed = true;
        }
        else if (tag.TryGet("TextureFormats", out EchoObject? formats) && formats!.TagType == EchoType.List && tag.TryGet("HasDepthAttachment", out _))
        {
            ConvertRenderTexture(tag, formats);
            changed = true;
        }

        foreach (string name in tag.GetNames().ToList())
            ConvertTextureData(tag[name], ref changed);
    }

    // An old TextureImageFormat as the new TextureFormat, whether it was sRGB, and the bytes per channel of a three
    // channel format that gains a fourth channel (0 for none).
    private static (int Format, bool Srgb, int PadChannel) FormatFromOld(int old) => old switch
    {
        0 => (0, false, 0),
        1 => (1, false, 0),
        2 => (6, false, 0),
        3 => (7, false, 0),
        4 => (8, false, 2),
        5 => (8, false, 0),
        6 => (9, false, 0),
        7 => (10, false, 0),
        8 => (11, false, 4),
        9 => (11, false, 0),
        10 => (19, false, 0),
        11 or 12 => (20, false, 0),
        13 => (12, false, 0),
        14 => (13, false, 0),
        15 => (14, false, 4),
        16 => (14, false, 0),
        17 => (3, false, 0),
        18 => (4, false, 0),
        19 => (5, false, 2),
        20 => (5, false, 0),
        21 => (15, false, 0),
        22 => (16, false, 0),
        23 => (17, false, 4),
        24 => (17, false, 0),
        25 => (21, false, 0),
        26 => (0, true, 0),
        _ => (0, false, 0),
    };

    // The old Short formats were filled and read back through OpenGL as normalized signed shorts.
    private static bool IsOldShort(int old) => old is >= 2 and <= 5;

    private static void ConvertTexture(EchoObject tag)
    {
        int old = tag["ImageFormat"].IntValue;
        (int format, bool srgb, int pad) = FormatFromOld(old);
        int filter = FilterFromGl(tag["MinFilter"].IntValue, tag.Get("MagFilter")?.IntValue ?? 1);
        int wrap = WrapFromGl(tag.Get("Wrap")?.IntValue ?? 0);
        bool mipmapped = tag.Get("IsMipMapped")?.BoolValue ?? false;

        tag["Format"] = new EchoObject(format);
        tag["Srgb"] = new EchoObject(srgb);
        tag["Filter"] = new EchoObject(filter);

        if (tag.TryGet("Faces", out EchoObject? faces) && faces!.TagType == EchoType.List)
        {
            EchoObject converted = EchoObject.NewList();
            foreach (EchoObject face in faces.List)
                converted.ListAdd(new EchoObject(ConvertTexels(face.ByteArrayValue, old, pad)));
            tag["Faces"] = converted;
            tag.Remove("Wrap");
        }
        else
        {
            byte[] data = tag["Data"].ByteArrayValue;
            if (tag.Get("RowsTopFirst")?.BoolValue == true && !tag.TryGet("Depth", out _))
                data = FlipRows(data, tag["Width"].IntValue, tag["Height"].IntValue);

            EchoObject mips = EchoObject.NewList();
            mips.ListAdd(new EchoObject(ConvertTexels(data, old, pad)));
            tag["Mips"] = mips;
            tag["WrapU"] = new EchoObject(wrap);
            tag["WrapV"] = new EchoObject(wrap);
            if (tag.TryGet("Depth", out _)) tag["WrapW"] = new EchoObject(wrap);
            if (mipmapped) tag["GenerateMipmaps"] = new EchoObject(true);
            foreach (string key in new[] { "Data", "Wrap", "IsMipMapped", "RowsTopFirst" })
                tag.Remove(key);
        }

        tag.Remove("ImageFormat");
        tag.Remove("MinFilter");
        tag.Remove("MagFilter");
    }

    private static byte[] FlipRows(byte[] data, int width, int height)
    {
        if (height <= 0) return data;
        int row = data.Length / height;
        byte[] flipped = new byte[data.Length];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(data, y * row, flipped, (height - 1 - y) * row, row);
        return flipped;
    }

    /// <summary>Old texels as the new format holds them: snorm shorts become half floats, three channels gain a fourth of one.</summary>
    internal static byte[] ConvertTexels(byte[] data, int oldFormat, int padChannel)
    {
        if (IsOldShort(oldFormat))
        {
            byte[] halves = new byte[data.Length];
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                float value = Math.Max(-1f, BitConverter.ToInt16(data, i) / 32767f);
                BitConverter.TryWriteBytes(halves.AsSpan(i), (Half)value);
            }
            data = halves;
        }
        if (padChannel == 0) return data;

        byte[] one = padChannel == 2
            ? (IsOldShort(oldFormat) ? BitConverter.GetBytes((Half)1f) : [0xFF, 0xFF])
            : oldFormat == 8 ? BitConverter.GetBytes(1f) : BitConverter.GetBytes(1);
        int texel = padChannel * 3;
        int count = data.Length / texel;
        byte[] padded = new byte[count * padChannel * 4];
        for (int i = 0; i < count; i++)
        {
            Buffer.BlockCopy(data, i * texel, padded, i * padChannel * 4, texel);
            Buffer.BlockCopy(one, 0, padded, i * padChannel * 4 + texel, padChannel);
        }
        return padded;
    }

    private static void ConvertRenderTexture(EchoObject tag, EchoObject formats)
    {
        int count = formats.Count;
        if (tag.TryGet("NumTextures", out EchoObject? num)) count = Math.Min(count, num!.IntValue);

        EchoObject colors = EchoObject.NewList();
        bool srgb = false;
        for (int i = 0; i < count; i++)
        {
            (int format, bool isSrgb, _) = FormatFromOld(formats[i].IntValue);
            colors.ListAdd(new EchoObject(format));
            srgb |= isSrgb;
        }

        tag["ColorFormats"] = colors;
        tag["Srgb"] = new EchoObject(srgb);
        tag["HasDepthAttachment"] = new EchoObject(ReadFlag(tag, "HasDepthAttachment", true));
        tag["EnableRandomWrite"] = new EchoObject(ReadFlag(tag, "EnableRandomWrite", false));
        tag.Remove("TextureFormats");
        tag.Remove("NumTextures");
    }

    private static bool ReadFlag(EchoObject tag, string key, bool fallback)
    {
        if (!tag.TryGet(key, out EchoObject? value)) return fallback;
        return value!.TagType == EchoType.Bool ? value.BoolValue : value.IntValue != 0;
    }
}
