using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

using ImageMagick;

namespace Prowl.Editor;

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
            pixels = job.Asset switch
            {
                Texture2D => GenerateForTextureFile(job.SourceFilePath),
                Model model => GenerateFor3D(p => p.SetupForModel(model)),
                Material mat => GenerateFor3D(p => p.SetupForMaterial(mat)),
                Mesh mesh => GenerateFor3D(p => p.SetupForMesh(mesh)),
                _ => null
            };
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
                File.WriteAllBytes(GetThumbnailPath(job.Guid, db.ThumbnailsPath), pixels);
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

    public static byte[]? LoadThumbnail(Guid guid, string thumbnailsPath)
    {
        string path = GetThumbnailPath(guid, thumbnailsPath);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
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
    //  Generators — all produce ThumbnailSize x ThumbnailSize RGBA, top-down
    // ================================================================

    private static byte[]? GenerateForTextureFile(string? filePath)
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

    private static byte[]? GenerateFor3D(Action<PreviewRenderer> setup)
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

            // Flip vertically — OpenGL RT has Y=0 at bottom, we store top-down
            FlipVertical(pixels, w, h);

            return pixels;
        }
        catch { return null; }
    }

    /// <summary>Flip RGBA pixel data vertically in-place.</summary>
    private static void FlipVertical(byte[] pixels, int width, int height)
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
