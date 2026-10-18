using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Aperture;
using Prowl.Aperture.Utilities;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Graphite.Rendering;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Thumbnails;

/// <summary>
/// Lazily generates small thumbnail images for assets. Queues work and generates one thumbnail at a
/// time, a rendered one finishing frames later once its readback arrives. Thumbnails stored as raw RGBA bytes (top-down).
/// </summary>
public static class ThumbnailGenerator
{
    /// <summary> Size in pixels of the square thumbnails generated and stored by this system. </summary>
    public static int ThumbnailSize => EditorSettings.Instance.ThumbnailSize;

    private static readonly Queue<ThumbnailJob> _queue = new();
    private static readonly HashSet<Guid> _queued = [];
    private static ThumbnailJob? _active;
    private static PreviewCapture? _capture;

    private sealed class ThumbnailJob
    {
        public Guid Guid;
        public Asset Asset;
        public string? SourceFilePath;
        // Stopwatch timestamp when this job first started waiting on dependencies (0 = not waiting yet).
        public long FirstWaitTimestamp;
        public bool Finished;
        public byte[]? Pixels;
        public bool Stale;
    }

    // Wall-clock (not frame-count) cap on waiting for dependencies to stream in. Frame rate is a
    // bad unit here a fast machine on a slow disk would blow through a frame budget while the
    // assets are still loading. After this we render best-effort so a genuinely missing/broken
    // dependency can't keep a job cycling forever.
    private const double MaxDependencyWaitSeconds = 10.0;

    /// <summary> Queues a thumbnail generation job for the given asset. No-op if the guid is empty, the asset is null, the guid is already queued, or a thumbnail file already exists on disk. </summary>
    public static void Enqueue(Guid guid, Asset asset, string? sourceFilePath = null)
    {
        if (guid == Guid.Empty || asset == null) return;
        if (_queued.Contains(guid)) return;

        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null) return;

        if (File.Exists(GetThumbnailPath(guid, db.ThumbnailsPath))) return;

        // The job holds what it renders until it is done, since nothing else may.
        var job = new ThumbnailJob { Guid = guid, Asset = asset, SourceFilePath = sourceFilePath };
        AssetDatabase.Hold(asset, job);
        _queued.Add(guid);
        _queue.Enqueue(job);
    }

    /// <summary> Advances thumbnail generation by one frame. Finishes the job in flight once its pixels arrive, otherwise starts the next queued job, waiting for its asset dependencies to load (up to a time budget). Call once per frame. </summary>
    public static void ProcessOne()
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null) return;

        if (_active != null)
        {
            _capture?.Step();
            if (_active.Finished) Finish(db);
            return;
        }

        if (_queue.Count == 0) return;

        ThumbnailJob job = _queue.Dequeue();
        _queued.Remove(job.Guid);

        if (job.Asset.IsDisposed || File.Exists(GetThumbnailPath(job.Guid, db.ThumbnailsPath)))
        {
            AssetDatabase.ReleaseAll(job);
            return;
        }

        // A thumbnail is a one-shot render + readback. With async asset loading on, the asset's
        // dependencies (a material's shader/textures, a model's materials, etc.) may not be loaded
        // yet, and we'd cache a blank thumbnail. Rather than block the main thread loading them,
        // request the missing ones in the background and re-queue this job for a later frame; only
        // render once everything is cached. Give up after a time budget so a broken dependency can't loop forever.
        if (!RequestDependencies(db, job))
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

        _active = job;
        try
        {
            IThumbnailGenerator? gen = EditorRegistries.GetThumbnailGenerator(job.Asset.GetType());
            if (gen == null) job.Finished = true;
            else gen.Generate(job.Asset, job.SourceFilePath, pixels => { job.Pixels = pixels; job.Finished = true; });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Thumbnail generation failed for {job.Guid}: {ex.Message}");
            job.Finished = true;
        }

        if (job.Finished) Finish(db);
    }

    private static void Finish(EditorAssetBackend db)
    {
        ThumbnailJob job = _active!;
        _active = null;
        AssetDatabase.ReleaseAll(job);

        byte[]? pixels = job.Pixels;
        if (job.Stale || pixels == null || pixels.Length == 0) return;

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

    /// <summary>
    /// Returns true when every asset in the thumbnail subject's forward dependency closure is
    /// already loaded. For any that aren't, kicks off a background load. Non-blocking.
    /// </summary>
    private static bool RequestDependencies(EditorAssetBackend db, ThumbnailJob job)
    {
        bool ready = true;
        try
        {
            foreach (Guid dep in db.Dependencies.GetTransitiveDependencies(new[] { job.Guid }))
            {
                if (AssetDatabase.Get(dep) is not { } asset) continue;
                AssetDatabase.Hold(asset, job);
                if (asset.State is AssetState.Unloaded or AssetState.Loading) ready = false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Thumbnail dependency check failed for {job.Guid}: {ex.Message}");
        }
        return ready;
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

    /// <summary> Deletes the thumbnail file for the given guid from disk and removes the guid from the queue. </summary>
    public static void DeleteThumbnail(Guid guid, string thumbnailsPath)
    {
        string path = GetThumbnailPath(guid, thumbnailsPath);
        if (File.Exists(path))
            try { File.Delete(path); } catch { }
        _queued.Remove(guid);
        if (_active != null && _active.Guid == guid) _active.Stale = true;
    }

    /// <summary>Delete all thumbnail files on disk and clear the queue.</summary>
    public static void DeleteAll()
    {
        Project? project = Project.Current;
        if (project == null) return;

        try
        {
            foreach (string file in Directory.GetFiles(project.ThumbnailsPath, "*.thumb"))
                File.Delete(file);
        }
        catch { }

        _queue.Clear();
        _queued.Clear();
        _capture?.Abandon();
        _capture = null;
        if (_active != null) AssetDatabase.ReleaseAll(_active);
        _active = null;
    }

    /// <summary> Returns the full file path for a thumbnail with the given guid, using the .thumb extension. </summary>
    public static string GetThumbnailPath(Guid guid, string thumbnailsPath)
        => Path.Combine(thumbnailsPath, $"{guid}.thumb");

    /// <summary> Gets the number of thumbnail generation jobs currently queued and awaiting processing. </summary>
    public static int QueuedCount => _queue.Count;

    // ================================================================
    //  Generators all produce ThumbnailSize x ThumbnailSize RGBA, top-down
    // ================================================================

    internal static byte[]? GenerateForTextureFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        try
        {
            using Aperture.Image image = Aperture.Image.Load(filePath,
                new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8 });

            // Scaled to fit, then centred on a square of transparent padding.
            using Aperture.Image thumbnail = ImageResize.FitAndPad(
                image, ThumbnailSize, ThumbnailSize, stackalloc byte[] { 0, 0, 0, 0 });

            return thumbnail.RootFrame.Pixels.ToArray();
        }
        catch { return null; }
    }

    internal static void GenerateFor3D(Action<PreviewRenderer> setup, Action<byte[]?> done)
    {
        if (Graphics.Renderer is not { } renderer)
        {
            done(null);
            return;
        }

        var preview = new PreviewRenderer(ThumbnailSize, ThumbnailSize) { TransparentBackground = true };
        try
        {
            setup(preview);
        }
        catch
        {
            preview.Dispose();
            throw;
        }

        _capture = new PreviewCapture(preview, renderer, done);
        _capture.Step();
    }

    /// <summary>
    /// Draws a preview each frame until its shaders are compiled, then reads it back after the frame that draws it.
    /// Camera.Render only adds a view to the frame, so the pixels exist once the renderer has run that frame.
    /// </summary>
    private sealed class PreviewCapture(PreviewRenderer preview, Renderer renderer, Action<byte[]?> done)
    {
        // A frame's draws are prepared while the next one is built, so its shader compiles show two frames later
        private const int MinFrames = 3;

        private readonly long _started = System.Diagnostics.Stopwatch.GetTimestamp();
        private int _frames;
        private bool _requested;
        private TextureReadback? _result;

        public void Step()
        {
            try
            {
                if (_result is { } result)
                {
                    Complete(result.Failed ? null : result.Data);
                    return;
                }
                if (_requested) return;

                preview.Render();
                _frames++;

                bool compiling = _frames < MinFrames || renderer.ShaderCompilesPending > 0;
                if (compiling && System.Diagnostics.Stopwatch.GetElapsedTime(_started).TotalSeconds < MaxDependencyWaitSeconds) return;

                if (preview.Result.IsNotValid() || preview.Result!.MainTexture.GpuTexture is not { } texture)
                {
                    Complete(null);
                    return;
                }
                renderer.Readback(texture, r => _result = r);
                _requested = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Thumbnail render failed: {ex.Message}");
                Complete(null);
            }
        }

        public void Abandon() => preview.Dispose();

        private void Complete(byte[]? pixels)
        {
            int w = preview.Width, h = preview.Height;
            _capture = null;
            preview.Dispose();

            if (pixels == null || pixels.Length != w * h * 4)
            {
                done(null);
                return;
            }

            // Render targets hold their bottom row first, thumbnails top first
            FlipVertical(pixels, w, h);
            done(pixels);
        }
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
            Texture2D? tex = sprite.Texture;
            if (tex == null) return null;
            int tw = (int)tex.Width, th = (int)tex.Height;
            if (tw <= 0 || th <= 0) return null;

            byte[]? src = ReadTextureRgba(tex, tw, th); // bottom-left origin
            if (src == null) return null;

            int rx = Math.Clamp(sprite.Rect.X, 0, tw - 1), ry = Math.Clamp(sprite.Rect.Y, 0, th - 1);
            int rw = Math.Clamp(sprite.Rect.Width, 1, tw - rx), rh = Math.Clamp(sprite.Rect.Height, 1, th - ry);

            int size = ThumbnailSize;
            byte[] dst = new byte[size * size * 4]; // transparent by default

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
        byte[] outp = new byte[count * 4];
        if (tex.Format == Runtime.TextureFormat.RGBA8)
        {
            tex.GetData<byte>(outp.AsMemory());
        }
        else if (tex.Format == Runtime.TextureFormat.RGBA16)
        {
            ushort[] s = new ushort[count * 4];
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
