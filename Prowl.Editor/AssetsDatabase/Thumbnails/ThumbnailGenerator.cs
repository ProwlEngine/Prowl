using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

using ImageMagick;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.Editor.Projects;

namespace Prowl.Editor.Thumbnails;

/// <summary>
/// Lazily generates small thumbnail images for assets. Queues work and processes
/// one thumbnail per frame to avoid hitching. Thumbnails stored as raw RGBA bytes (top-down).
/// </summary>
public static class ThumbnailGenerator
{
    public static int ThumbnailSize => EditorSettings.Instance.ThumbnailSize;

    private static readonly Queue<ThumbnailJob> _queue = new();
    private static readonly HashSet<Guid> _queued = new();

    private struct ThumbnailJob
    {
        public Guid Guid;
        public EngineObject Asset;
        public string? SourceFilePath;
        // Stopwatch timestamp when this job first started waiting on dependencies (0 = not waiting yet).
        public long FirstWaitTimestamp;
    }

    // Wall-clock (not frame-count) cap on waiting for dependencies to stream in. Frame rate is a
    // bad unit here a fast machine on a slow disk would blow through a frame budget while the
    // assets are still loading. After this we render best-effort so a genuinely missing/broken
    // dependency can't keep a job cycling forever.
    private const double MaxDependencyWaitSeconds = 10.0;

    public static void Enqueue(Guid guid, EngineObject asset, string? sourceFilePath = null)
    {
        if (guid == Guid.Empty || asset == null) return;
        if (_queued.Contains(guid)) return;

        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        if (File.Exists(GetThumbnailPath(guid, db.ThumbnailsPath))) return;

        _queued.Add(guid);
        _queue.Enqueue(new ThumbnailJob { Guid = guid, Asset = asset, SourceFilePath = sourceFilePath });
    }

    public static void ProcessOne()
    {
        if (_queue.Count == 0) return;

        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        var job = _queue.Dequeue();
        _queued.Remove(job.Guid);

        if (job.Asset.IsDisposed) return;
        if (File.Exists(GetThumbnailPath(job.Guid, db.ThumbnailsPath))) return;

        // A thumbnail is a one-shot render + readback. With async asset loading on, the asset's
        // dependencies (a material's shader/textures, a model's materials, etc.) may not be loaded
        // yet, and we'd cache a blank thumbnail. Rather than block the main thread loading them,
        // request the missing ones in the background and re-queue this job for a later frame; only
        // render once everything is cached (its GPU create/upload commands are then already queued
        // ahead of the preview draw). Give up after a time budget so a broken dependency can't loop forever.
        if (!RequestDependencies(db, job.Guid))
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (job.FirstWaitTimestamp == 0) job.FirstWaitTimestamp = now;
            double waitedSeconds = (now - job.FirstWaitTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;

            if (waitedSeconds < MaxDependencyWaitSeconds)
            {
                _queued.Add(job.Guid);
                _queue.Enqueue(job);
                return;
            }
            // Timed out waiting render best-effort below with whatever loaded.
        }

        byte[]? pixels = null;
        try
        {
            var gen = EditorRegistries.GetThumbnailGenerator(job.Asset.GetType());
            if (gen != null) pixels = gen.Generate(job.Asset, job.SourceFilePath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Thumbnail generation failed for {job.Guid}: {ex.Message}");
        }

        if (pixels != null && pixels.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(db.ThumbnailsPath);
                string thumbPath = GetThumbnailPath(job.Guid, db.ThumbnailsPath);

                // Write with size header: [width:int32 LE][height:int32 LE][RGBA pixels]
                int size = ThumbnailSize;
                byte[] fileData = new byte[8 + pixels.Length];
                BitConverter.TryWriteBytes(fileData.AsSpan(0, 4), size);
                BitConverter.TryWriteBytes(fileData.AsSpan(4, 4), size);
                pixels.CopyTo(fileData, 8);
                File.WriteAllBytes(thumbPath, fileData);
            }
            catch { }
        }
    }

    /// <summary>
    /// Returns true when every asset in the thumbnail subject's forward dependency closure is
    /// already loaded. For any that aren't, kicks off a background load. Non-blocking.
    /// </summary>
    private static bool RequestDependencies(EditorAssetDatabase db, Guid guid)
    {
        bool ready = true;
        try
        {
            foreach (var dep in db.Dependencies.GetTransitiveDependencies(new[] { guid }))
            {
                if (AssetDatabase.GetCached(dep) == null)
                {
                    AssetLoader.Request(dep);
                    ready = false;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Thumbnail dependency check failed for {guid}: {ex.Message}");
        }
        return ready;
    }

    public static void EnqueueMissing()
    {
        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        foreach (var entry in db.GetAllEntries())
        {
            var asset = db.GetLoadedAsset(entry.Guid);
            if (asset != null)
            {
                string? sourceFile = entry.MainAssetType == typeof(Texture2D)
                    ? Path.Combine(Project.Current?.AssetsPath ?? "", entry.Path)
                    : null;
                Enqueue(entry.Guid, asset, sourceFile);
            }

            if (entry.SubAssets == null) continue;
            foreach (var sub in entry.SubAssets)
            {
                var subAsset = db.GetLoadedAsset(sub.Guid);
                if (subAsset != null)
                    Enqueue(sub.Guid, subAsset);
            }
        }
    }

    /// <summary>
    /// Load a thumbnail from disk. Returns (width, height, RGBA pixels) or null.
    /// The on-disk format is: [width:int32 LE][height:int32 LE][RGBA pixel data].
    /// Legacy files without the header are discarded (they will regenerate).
    /// </summary>
    public static (int width, int height, byte[] pixels)? LoadThumbnail(Guid guid, string thumbnailsPath)
    {
        string path = GetThumbnailPath(guid, thumbnailsPath);
        if (!File.Exists(path)) return null;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length <= 8) return null; // Too small to have header + any pixels

            int w = BitConverter.ToInt32(data, 0);
            int h = BitConverter.ToInt32(data, 4);

            // Sanity check: dimensions must be positive and pixel data must match
            if (w <= 0 || h <= 0 || data.Length != 8 + w * h * 4)
            {
                // Legacy or corrupt file - delete so it regenerates
                try { File.Delete(path); } catch { }
                return null;
            }

            byte[] pixels = new byte[data.Length - 8];
            Buffer.BlockCopy(data, 8, pixels, 0, pixels.Length);
            return (w, h, pixels);
        }
        catch { return null; }
    }

    public static void DeleteThumbnail(Guid guid, string thumbnailsPath)
    {
        string path = GetThumbnailPath(guid, thumbnailsPath);
        if (File.Exists(path))
            try { File.Delete(path); } catch { }
        _queued.Remove(guid);
    }

    /// <summary>Delete all thumbnail files on disk and clear the queue.</summary>
    public static void DeleteAll()
    {
        var project = Project.Current;
        if (project == null) return;

        try
        {
            foreach (var file in Directory.GetFiles(project.ThumbnailsPath, "*.thumb"))
                File.Delete(file);
        }
        catch { }

        _queue.Clear();
        _queued.Clear();
    }

    public static string GetThumbnailPath(Guid guid, string thumbnailsPath)
        => Path.Combine(thumbnailsPath, $"{guid}.thumb");

    public static int QueuedCount => _queue.Count;

    // ================================================================
    //  Generators all produce ThumbnailSize x ThumbnailSize RGBA, top-down
    // ================================================================

    internal static byte[]? GenerateForTextureFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        try
        {
            using var image = new MagickImage(filePath);

            // Resize maintaining aspect ratio, then extent to square with transparent padding
            var size = (uint)ThumbnailSize;
            var geo = new MagickGeometry(size, size)
            {
                IgnoreAspectRatio = false,
                FillArea = false
            };
            image.Resize(geo);

            // Center in a square canvas
            image.BackgroundColor = MagickColors.Transparent;
            image.Extent(size, size, Gravity.Center);

            // Output as RGBA
            var pixels = image.GetPixels();
            return pixels.ToByteArray(PixelMapping.RGBA);
        }
        catch { return null; }
    }

    internal static byte[]? GenerateFor3D(Action<PreviewRenderer> setup)
    {
        try
        {
            using var preview = new PreviewRenderer(ThumbnailSize, ThumbnailSize);
            setup(preview);
            preview.Render();

            var rt = preview.Result;
            if (rt == null || rt.MainTexture == null) return null;

            int w = rt.Width;
            int h = rt.Height;
            byte[] pixels = new byte[w * h * 4];
            rt.MainTexture.GetData<byte>(pixels);

            // Flip vertically OpenGL RT has Y=0 at bottom, we store top-down
            FlipVertical(pixels, w, h);

            return pixels;
        }
        catch { return null; }
    }

    /// <summary>
    /// Renders a <see cref="Sprite"/>'s sub-rect (of its source texture) into a thumbnail. Reads the texture
    /// pixels directly (sub-assets have no source file), crops the sprite rect, flips to top-down, and
    /// letterboxes it into the square with transparent padding.
    /// </summary>
    internal static byte[]? GenerateForSprite(Sprite sprite)
    {
        try
        {
            var tex = sprite.Texture.Res;
            if (tex == null) return null;
            int tw = (int)tex.Width, th = (int)tex.Height;
            if (tw <= 0 || th <= 0) return null;

            byte[]? src = ReadTextureRgba(tex, tw, th); // bottom-left origin
            if (src == null) return null;

            int rx = Math.Clamp(sprite.Rect.X, 0, tw - 1), ry = Math.Clamp(sprite.Rect.Y, 0, th - 1);
            int rw = Math.Clamp(sprite.Rect.Width, 1, tw - rx), rh = Math.Clamp(sprite.Rect.Height, 1, th - ry);

            int size = ThumbnailSize;
            var dst = new byte[size * size * 4]; // transparent by default

            // Letterbox the rect's aspect into the square.
            float aspect = rw / (float)rh;
            int dw = size, dh = size;
            if (aspect >= 1f) dh = Math.Max(1, (int)MathF.Round(size / aspect));
            else dw = Math.Max(1, (int)MathF.Round(size * aspect));
            int ox = (size - dw) / 2, oy = (size - dh) / 2;

            for (int y = 0; y < dh; y++)
            {
                int spriteRowFromTop = Math.Min(rh - 1, (int)((y + 0.5f) / dh * rh));
                int srcY = ry + (rh - 1 - spriteRowFromTop); // texture is bottom-up
                for (int x = 0; x < dw; x++)
                {
                    int srcX = rx + Math.Min(rw - 1, (int)((x + 0.5f) / dw * rw));
                    int si = (srcY * tw + srcX) * 4;
                    int di = ((oy + y) * size + (ox + x)) * 4;
                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }
        catch { return null; }
    }

    private static byte[]? ReadTextureRgba(Texture2D tex, int tw, int th)
    {
        int count = tw * th;
        var outp = new byte[count * 4];
        if (tex.ImageFormat == TextureImageFormat.Color4b)
        {
            tex.GetData<byte>(outp.AsMemory());
        }
        else if (tex.ImageFormat == TextureImageFormat.UnsignedShort4)
        {
            var s = new ushort[count * 4];
            tex.GetData<ushort>(s.AsMemory());
            for (int i = 0; i < count * 4; i++) outp[i] = (byte)(s[i] >> 8);
        }
        else
        {
            return null;
        }
        return outp;
    }

    /// <summary>Flip RGBA pixel data vertically in-place.</summary>
    internal static void FlipVertical(byte[] pixels, int width, int height)
    {
        int stride = width * 4;
        byte[] row = new byte[stride];
        for (int y = 0; y < height / 2; y++)
        {
            int topOffset = y * stride;
            int botOffset = (height - 1 - y) * stride;
            // Swap rows
            Buffer.BlockCopy(pixels, topOffset, row, 0, stride);
            Buffer.BlockCopy(pixels, botOffset, pixels, topOffset, stride);
            Buffer.BlockCopy(row, 0, pixels, botOffset, stride);
        }
    }
}
