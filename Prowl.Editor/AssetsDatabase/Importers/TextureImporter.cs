// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Importers;

[ImporterFor(".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".hdr", ".dds", ".exr")]
public class TextureImporter : AssetImporter
{
    public override int Version => 4; // Bumped: min/mag/mip filter booleans

    public override bool Import(ImportContext ctx)
    {
        // Settings are guaranteed to have defaults merged by EditorAssetDatabase.RunImport
        bool generateMipmaps = ctx.Settings?.TryGet("generateMipmaps", out var mipTag) == true && mipTag.BoolValue;

        // Allocate mip storage up front so GenerateMipmaps (inside FromFile) has somewhere to write
        var texture = Texture2D.FromFile(ctx.AbsolutePath, generateMipmaps);
        texture.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);

        // Read filter/wrap settings (defaults merged by RunImport)
        bool minLinear = ctx.Settings?.TryGet("minLinear", out EchoObject? minTag) != true || minTag.BoolValue;
        bool magLinear = ctx.Settings?.TryGet("magLinear", out EchoObject? magTag) != true || magTag.BoolValue;
        bool mipLinear = ctx.Settings?.TryGet("mipLinear", out EchoObject? mipFilterTag) != true || mipFilterTag.BoolValue;
        SamplerAddressMode wrapMode = ctx.Settings?.TryGet("wrapMode", out EchoObject? wrapTag) == true
            ? (SamplerAddressMode)wrapTag.IntValue : SamplerAddressMode.Wrap;

        texture.SetTextureFilters(CombineFilters(minLinear, magLinear, mipLinear && generateMipmaps));
        texture.SetWrapModes(wrapMode, wrapMode);

        ctx.SetMainAsset(texture);

        // Sprite sub-assets: the texture's sprite config lives in settings["sprite"] (edited in the Sprite Editor).
        SpriteImportSettings spriteSettings = TextureSpriteMeta.ReadFrom(ctx.Settings);
        if (spriteSettings.Mode != SpriteMode.None)
        {
            foreach (var kv in spriteSettings.SecondaryTextures)
                if (!kv.Value.IsExplicitNull) ctx.AddDependency(kv.Value.AssetID);

            foreach (var (name, sprite) in SpriteBuilder.Build(texture, spriteSettings))
                ctx.AddSubAsset(name, sprite);
        }

        return true;
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateMipmaps"] = new EchoObject(true);
        s["sRGB"] = new EchoObject(true);
        s["minLinear"] = new EchoObject(true);
        s["magLinear"] = new EchoObject(true);
        s["mipLinear"] = new EchoObject(true);
        s["wrapMode"] = new EchoObject((int)SamplerAddressMode.Wrap);
        s["sprite"] = Serializer.Serialize(typeof(SpriteImportSettings), new SpriteImportSettings());
        return s;
    }

    private static SamplerFilter CombineFilters(bool minLinear, bool magLinear, bool mipLinear)
    {
        // SamplerFilter packs three flags: min (bit 2), mag (bit 1), mip (bit 0).
        int value = (minLinear ? 0b100 : 0) | (magLinear ? 0b010 : 0) | (mipLinear ? 0b001 : 0);
        return (SamplerFilter)value;
    }
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
    Automatic,
    GridBySize,
    GridByCount,
    Isometric,
}

/// <summary>How a slice's custom pivot value is interpreted.</summary>
public enum PivotUnitMode
{
    Normalized,
    Pixels,
}

/// <summary>One sprite's authoring record.</summary>
public class SpriteSliceData
{
    public string Name = "sprite";
    public SpriteRect Rect;
    public SpriteAlignment Alignment = SpriteAlignment.Center;
    public Float2 CustomPivot = new(0.5f, 0.5f);
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
    public SpriteMode Mode = SpriteMode.None;
    public float PixelsPerUnit = 100f;

    // Per-asset tight mesh.
    public bool GenerateTightMesh = false;
    public float TightMeshDetail = 1.5f;
    public byte TightMeshAlphaThreshold = 1;

    /// <summary>Named secondary maps (e.g. "_NormalMap") applied to every sprite in this texture.</summary>
    public Dictionary<string, AssetRef<Texture2D>> SecondaryTextures = new();

    // Slicing-tool state (editor convenience; the importer only reads Slices).
    public SpriteSlicingTool SlicingTool = SpriteSlicingTool.GridBySize;
    public SpriteAlignment GeneratedPivot = SpriteAlignment.Center;
    public Int2 GridCellSize = new(16, 16);
    public Int2 GridCellCount = new(4, 4);
    public Int2 GridOffset = default;
    public Int2 GridPadding = default;
    public bool KeepEmptyRects = false;
    public bool IsoIsAlternate = false;

    public List<SpriteSliceData> Slices = new();
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

    /// <summary>Deserializes the sprite settings from a texture's <c>.meta</c> settings compound (defaults if absent).</summary>
    public static SpriteImportSettings ReadFrom(EchoObject? settings)
    {
        if (settings != null && settings.TryGet(Key, out EchoObject echo))
        {
            try
            {
                var ctx = ImportHelper.CreateTrackingContext(out _);
                return Serializer.Deserialize<SpriteImportSettings>(echo, ctx) ?? new SpriteImportSettings();
            }
            catch { /* fall through to defaults */ }
        }
        return new SpriteImportSettings();
    }

    /// <summary>Serializes the sprite settings into a texture's <c>.meta</c> settings compound.</summary>
    public static void WriteInto(EchoObject settings, SpriteImportSettings s)
    {
        settings[Key] = Serializer.Serialize(typeof(SpriteImportSettings), s);
    }

    /// <summary>Loads the sprite settings for a texture by GUID.</summary>
    public static SpriteImportSettings Load(Guid textureGuid)
    {
        try
        {
            string abs = AbsolutePath(textureGuid);
            string metaPath = MetaFile.GetMetaPath(abs);
            if (!File.Exists(metaPath)) return new SpriteImportSettings();
            return ReadFrom(MetaFile.Read(metaPath).Settings);
        }
        catch
        {
            return new SpriteImportSettings();
        }
    }

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
}

#endregion

#region Sprite edit registry (shared inspector / editor instance)

/// <summary>A texture's in-progress sprite settings, shared between the texture inspector and the Sprite Editor.</summary>
public sealed class SpriteEditTarget
{
    public Guid TextureGuid;
    public SpriteImportSettings Settings = new();
    /// <summary>True when the settings differ from what's on disk (i.e. a Save &amp; Reimport is pending).</summary>
    public bool Dirty;
}

/// <summary>
/// Holds one live <see cref="SpriteEditTarget"/> per texture so the texture inspector and the Sprite Editor
/// window edit the same <see cref="SpriteImportSettings"/> instance. The Sprite Editor just mutates it and
/// flags it dirty; the inspector owns the actual "Save &amp; Reimport" (persist to <c>.meta</c> + reimport).
/// </summary>
public static class SpriteEditRegistry
{
    private static readonly Dictionary<Guid, SpriteEditTarget> _targets = new();

    /// <summary>Gets the shared target for a texture, loading its settings from the <c>.meta</c> on first use.</summary>
    public static SpriteEditTarget Get(Guid textureGuid)
    {
        if (!_targets.TryGetValue(textureGuid, out SpriteEditTarget? t))
        {
            t = new SpriteEditTarget
            {
                TextureGuid = textureGuid,
                Settings = TextureSpriteMeta.Load(textureGuid),
            };
            _targets[textureGuid] = t;
        }
        return t;
    }

    public static bool IsDirty(Guid textureGuid) => _targets.TryGetValue(textureGuid, out SpriteEditTarget? t) && t.Dirty;

    public static void ClearDirty(Guid textureGuid)
    {
        if (_targets.TryGetValue(textureGuid, out SpriteEditTarget? t)) t.Dirty = false;
    }

    /// <summary>Reloads the target's settings from disk, discarding unsaved edits.</summary>
    public static void Reload(Guid textureGuid)
    {
        if (_targets.TryGetValue(textureGuid, out SpriteEditTarget? t))
        {
            t.Settings = TextureSpriteMeta.Load(textureGuid);
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

        return data.SlicingTool switch
        {
            SpriteSlicingTool.Automatic => Automatic(alpha, textureWidth, textureHeight, baseName, data.GeneratedPivot, pivot),
            SpriteSlicingTool.GridByCount => GridByCount(data, textureWidth, textureHeight, alpha, baseName, pivot),
            SpriteSlicingTool.Isometric => Isometric(data, textureWidth, textureHeight, alpha, baseName, pivot),
            _ => GridBySize(data, textureWidth, textureHeight, alpha, baseName, pivot),
        };
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
        if (cellW <= 0 || cellH <= 0) return new List<SpriteSliceData>();
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

        var visited = new bool[texW * texH];
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
            int w = (int)tex.Width, h = (int)tex.Height;
            int count = w * h;
            var alpha = new byte[count];

            if (tex.ImageFormat == PixelFormat.R8_G8_B8_A8_UNorm)
            {
                var rgba = new byte[count * 4];
                tex.GetData<byte>(rgba.AsMemory());
                for (int i = 0; i < count; i++) alpha[i] = rgba[i * 4 + 3];
            }
            else if (tex.ImageFormat == PixelFormat.R16_G16_B16_A16_UNorm)
            {
                var rgba = new ushort[count * 4];
                tex.GetData<ushort>(rgba.AsMemory());
                for (int i = 0; i < count; i++) alpha[i] = (byte)(rgba[i * 4 + 3] >> 8);
            }
            else
            {
                return null;
            }

            return alpha;
        }
        catch (Exception ex)
        {
            Prowl.Runtime.Debug.LogWarning($"[SpriteSlicer] Could not read texture alpha: {ex.Message}");
            return null;
        }
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
    public static List<(string name, Sprite sprite)> Build(Texture2D tex, SpriteImportSettings s)
    {
        var result = new List<(string, Sprite)>();
        int texW = (int)tex.Width, texH = (int)tex.Height;
        if (texW <= 0 || texH <= 0) return result;

        List<SpriteSliceData> slices = ResolveSlices(s, texW, texH, tex.Name);
        byte[]? alpha = s.GenerateTightMesh ? SpriteSlicer.ReadAlpha(tex) : null;

        foreach (SpriteSliceData slice in slices)
            result.Add((slice.Name, BuildOne(tex, s, slice, texW, texH, alpha)));

        return result;
    }

    private static List<SpriteSliceData> ResolveSlices(SpriteImportSettings s, int texW, int texH, string baseName)
    {
        if (s.Mode == SpriteMode.Single)
        {
            if (s.Slices.Count > 0) return new List<SpriteSliceData> { s.Slices[0] };
            return new List<SpriteSliceData>
            {
                new() { Name = baseName, Rect = new SpriteRect(0, 0, texW, texH), Alignment = SpriteAlignment.Center }
            };
        }
        return s.Slices;
    }

    private static Sprite BuildOne(Texture2D tex, SpriteImportSettings s, SpriteSliceData slice, int texW, int texH, byte[]? alpha)
    {
        var sprite = new Sprite
        {
            Name = slice.Name,
            Texture = tex, // implicit AssetRef<Texture2D>; carries the texture's AssetID
            Rect = slice.Rect,
            Pivot = ResolvePivot(slice),
            PixelsPerUnit = s.PixelsPerUnit,
            Border = slice.Border,
            SecondaryTextures = new Dictionary<string, AssetRef<Texture2D>>(s.SecondaryTextures),
        };

        if (s.GenerateTightMesh && alpha != null)
        {
            byte[] rectAlpha = ExtractRectAlpha(alpha, texW, texH, slice.Rect);
            var traced = SpriteMeshTracer.Generate(rectAlpha, slice.Rect.Width, slice.Rect.Height,
                s.TightMeshAlphaThreshold, s.TightMeshDetail);
            sprite.BuildTightGeometry(traced, texW, texH);
        }
        else
        {
            sprite.BuildQuadGeometry(texW, texH);
        }

        return sprite;
    }

    private static Float2 ResolvePivot(SpriteSliceData slice)
    {
        if (slice.Alignment != SpriteAlignment.Custom)
            return Sprite.PivotFromAlignment(slice.Alignment);

        if (slice.PivotUnit == PivotUnitMode.Pixels)
        {
            float w = Math.Max(1, slice.Rect.Width);
            float h = Math.Max(1, slice.Rect.Height);
            return new Float2(slice.CustomPivot.X / w, slice.CustomPivot.Y / h);
        }
        return slice.CustomPivot;
    }

    private static byte[] ExtractRectAlpha(byte[] fullAlpha, int texW, int texH, SpriteRect rect)
    {
        int rw = rect.Width, rh = rect.Height;
        var a = new byte[rw * rh];
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
