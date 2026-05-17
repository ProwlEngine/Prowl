using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

using ImageMagick;
using Prowl.Editor.GUI;
using Prowl.Editor.AssetsDatabase;
using Prowl.Editor.Theming;

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
    }

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

        byte[]? pixels = null;
        try
        {
            ThumbnailGeneratorRegistry.TryGenerate(job.Asset, job.SourceFilePath, out pixels);
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
