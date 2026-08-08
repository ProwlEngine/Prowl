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

[ImporterFor(".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".hdr", ".dds", ".exr")]
public class TextureImporter : AssetImporter
{
    // 5: sprite sub-asset GUIDs derive from the slice's persistent Id instead of its name.
    public override int Version => 5;

    public override bool Import(ImportContext ctx)
    {
        // Settings are guaranteed to have defaults merged by EditorAssetDatabase.RunImport
        bool generateMipmaps = ctx.Settings?.TryGet("generateMipmaps", out var mipTag) == true && mipTag.BoolValue;

        // Load texture WITHOUT mipmaps first we'll generate them after applying settings
        var texture = Texture2D.FromFile(ctx.AbsolutePath, false);
        texture.Name = ctx.FileName;

        // Read filter/wrap settings (defaults merged by RunImport)
        var minFilter = ctx.Settings?.TryGet("minFilter", out var minTag2) == true
            ? (TextureMin)minTag2.IntValue : (generateMipmaps ? TextureMin.LinearMipmapLinear : TextureMin.Linear);
        var magFilter = ctx.Settings?.TryGet("magFilter", out var magTag) == true
            ? (TextureMag)magTag.IntValue : TextureMag.Linear;
        var wrapMode = ctx.Settings?.TryGet("wrapMode", out var wrapTag) == true
            ? (TextureWrap)wrapTag.IntValue : TextureWrap.Repeat;

        // Generate mipmaps if requested (must happen before setting mipmap filters)
        if (generateMipmaps)
            texture.GenerateMipmaps();

        // Downgrade mipmap filters if no mipmaps
        if (!generateMipmaps)
        {
            minFilter = minFilter switch
            {
                TextureMin.NearestMipmapNearest or TextureMin.NearestMipmapLinear => TextureMin.Nearest,
                TextureMin.LinearMipmapNearest or TextureMin.LinearMipmapLinear => TextureMin.Linear,
                _ => minFilter
            };
        }

        texture.SetTextureFilters(minFilter, magFilter);
        texture.SetWrapModes(wrapMode, wrapMode);

        ctx.SetMainAsset(texture);

        // Sprite sub-assets: the texture's sprite config lives in settings["sprite"] (edited in the Sprite Editor).
        SpriteImportSettings spriteSettings = TextureSpriteMeta.ReadFrom(ctx.Settings);
        if (spriteSettings.Mode != SpriteMode.None)
        {
            foreach (var kv in spriteSettings.SecondaryTextures)
                if (!kv.Value.IsExplicitNull) ctx.AddDependency(kv.Value.AssetID);

            foreach (var (slice, sprite) in SpriteBuilder.Build(texture, spriteSettings))
                ctx.AddSubAsset(slice.Name, sprite, SpriteBuilder.IdentityOf(spriteSettings, slice));
        }

        return true;
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateMipmaps"] = new EchoObject(true);
        s["minFilter"] = new EchoObject((int)TextureMin.LinearMipmapLinear);
        s["magFilter"] = new EchoObject((int)TextureMag.Linear);
        s["wrapMode"] = new EchoObject((int)TextureWrap.Repeat);
        s["sprite"] = Serializer.Serialize(typeof(SpriteImportSettings), new SpriteImportSettings());
        return s;
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
    /// <summary>Persistent identity, minted once when the slice is created and carried in the texture's
    /// <c>.meta</c>. This is what the sprite sub-asset's GUID is derived from, so renaming the slice or the
    /// texture leaves every reference to it intact.</summary>
    public Guid Id;

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
                var ctx = ImportHelper.CreateTrackingContext(out _);
                SpriteImportSettings? parsed = Serializer.Deserialize<SpriteImportSettings>(echo, ctx);
                if (parsed != null) return parsed;

                failed = true;
                Prowl.Runtime.Debug.LogError("[Sprite] Sprite import settings deserialized to null. Keeping the existing block rather than replacing it with defaults.");
            }
            catch (Exception ex)
            {
                failed = true;
                Prowl.Runtime.Debug.LogError($"[Sprite] Failed to read sprite import settings: {ex.Message}\n{ex.StackTrace}");
            }
        }
        return new SpriteImportSettings();
    }

    /// <summary>Settings-only overload, for callers that just build from whatever is readable.</summary>
    public static SpriteImportSettings ReadFrom(EchoObject? settings) => ReadFrom(settings, out _);

    /// <summary>Serializes the sprite settings into a texture's <c>.meta</c> settings compound.</summary>
    public static void WriteInto(EchoObject settings, SpriteImportSettings s)
    {
        AssignMissingSliceIds(s);
        settings[Key] = Serializer.Serialize(typeof(SpriteImportSettings), s);
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
            Prowl.Runtime.Debug.LogError($"[Sprite] Could not read the meta for texture {textureGuid}: {ex.Message}\n{ex.StackTrace}");
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
}

#endregion

#region Sprite edit registry (shared inspector / editor instance)

/// <summary>A texture's in-progress sprite settings, shared between the texture inspector and the Sprite Editor.</summary>
public sealed class SpriteEditTarget
{
    public Guid TextureGuid;
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
    private static readonly Dictionary<Guid, SpriteEditTarget> _targets = new();

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
            int w = (int)tex.Width, h = (int)tex.Height;
            int count = w * h;
            var alpha = new byte[count];

            if (tex.ImageFormat == TextureImageFormat.Color4b)
            {
                var rgba = new byte[count * 4];
                tex.GetData<byte>(rgba.AsMemory());
                for (int i = 0; i < count; i++) alpha[i] = rgba[i * 4 + 3];
            }
            else if (tex.ImageFormat == TextureImageFormat.UnsignedShort4)
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

/// <summary>
/// Carries authored slice identity across a re-slice.
/// </summary>
public static class SpriteSliceMatcher
{
    /// <summary>How much of the smaller rect two slices must share to count as the same sprite moved.</summary>
    private const float MinOverlapFraction = 0.5f;

    public static List<SpriteSliceData> CarryOverIdentities(List<SpriteSliceData> previous, List<SpriteSliceData> generated)
    {
        if (previous.Count == 0 || generated.Count == 0) return generated;

        var oldClaimed = new bool[previous.Count];
        var newMatched = new bool[generated.Count];

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

    public static List<(SpriteSliceData slice, Sprite sprite)> Build(Texture2D tex, SpriteImportSettings s)
    {
        var result = new List<(SpriteSliceData, Sprite)>();
        int texW = (int)tex.Width, texH = (int)tex.Height;
        if (texW <= 0 || texH <= 0) return result;

        List<SpriteSliceData> slices = ResolveSlices(s, texW, texH, tex.Name);
        byte[]? alpha = s.GenerateTightMesh ? SpriteSlicer.ReadAlpha(tex) : null;

        int clampedCount = 0;
        foreach (SpriteSliceData slice in slices)
        {
            SpriteRect rect = ClampToTexture(slice.Rect, texW, texH);
            if (!SameRect(rect, slice.Rect)) clampedCount++;
            result.Add((slice, BuildOne(tex, s, slice, rect, texW, texH, alpha)));
        }

        // Replacing the source image with a smaller one leaves the stored rects describing the old
        // dimensions, which would otherwise produce UVs past 1 and sample whatever Repeat wrap lands on.
        // Only the built sprite is clamped - the authored slicing is left alone so restoring the original
        // image (or re-slicing) brings it straight back.
        if (clampedCount > 0)
            Prowl.Runtime.Debug.LogWarning(
                $"[Sprite] '{tex.Name}': {clampedCount} slice(s) fall outside the {texW}x{texH} texture and were clamped for this import. The stored slicing is unchanged.");

        return result;
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

    /// <summary><paramref name="rect"/> is the slice's rect after clamping to the texture, which is what
    /// the sprite is actually built from.</summary>
    private static Sprite BuildOne(Texture2D tex, SpriteImportSettings s, SpriteSliceData slice, SpriteRect rect, int texW, int texH, byte[]? alpha)
    {
        var sprite = new Sprite
        {
            Name = slice.Name,
            Texture = tex, // implicit AssetRef<Texture2D>; carries the texture's AssetID
            Rect = rect,
            Pivot = ResolvePivot(slice, rect),
            PixelsPerUnit = s.PixelsPerUnit,
            Border = slice.Border,
            SecondaryTextures = new Dictionary<string, AssetRef<Texture2D>>(s.SecondaryTextures),
        };

        if (s.GenerateTightMesh && alpha != null && rect.Width > 0 && rect.Height > 0)
        {
            byte[] rectAlpha = ExtractRectAlpha(alpha, texW, texH, rect);
            var traced = SpriteMeshTracer.Generate(rectAlpha, rect.Width, rect.Height,
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
