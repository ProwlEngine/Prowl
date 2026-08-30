// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// A decoded image and the entry point to the library. The Try members never throw for malformed
/// input; prefer them for files you did not produce. A decoded image owns its pixel memory, which
/// is pooled by default, so a span taken from a frame must not outlive the image.
/// </summary>
public sealed class Image : IDisposable
{
    private ImageFrame[] _frames = [];
    private bool _disposed;

    /// <summary>The container this image was decoded from.</summary>
    public required ImageFormat Format { get; init; }

    /// <summary>Canvas width in pixels. Frames may be smaller and offset within it.</summary>
    public required int Width { get; init; }

    /// <summary>Canvas height in pixels. Frames may be smaller and offset within it.</summary>
    public required int Height { get; init; }

    /// <summary>Layout every frame in <see cref="Frames"/> uses.</summary>
    public required PixelFormat PixelFormat { get; init; }

    /// <summary>
    /// The decoded rasters, always at least one. Ordered as the container stored them: animation
    /// order for GIF, WebP and APNG, largest first for ICO, and mip level major for DDS.
    /// </summary>
    public required IReadOnlyList<ImageFrame> Frames
    {
        get => _frames;
        init => _frames = value as ImageFrame[] ?? [.. value];
    }

    /// <summary>Header facts about the source file, as <see cref="Identify(string)"/> would report them.</summary>
    public required ImageInfo Info { get; init; }

    /// <summary>Exif, XMP and ICC blocks lifted from the file, empty when none were present or requested.</summary>
    public ImageMetadata Metadata { get; internal set; } = ImageMetadata.Empty;

    /// <summary>The first and, for a still image, only frame.</summary>
    public ImageFrame RootFrame => Frames[0];

    /// <summary>Pixel data of <see cref="RootFrame"/>.</summary>
    public Span<byte> Pixels => _frames[0].Pixels;

    /// <summary>Whether the frames form an animation rather than independent images or mip levels.</summary>
    public bool IsAnimated => Info.IsAnimated;

    /// <summary>
    /// Returns the pixel memory of every frame to the pool. Safe to call more than once; the
    /// frames are left empty rather than dangling.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ImageFrame[] frames = _frames;
        for (int i = 0; i < frames.Length; i++)
            frames[i].Release();
    }

    // ---- Detection -------------------------------------------------------------------

    /// <summary>
    /// Identifies the container from a leading chunk of a file. Needs at most
    /// <see cref="FormatDetector.MaxSignatureLength"/> bytes and never reads pixel data.
    /// </summary>
    public static ImageFormat DetectFormat(ReadOnlySpan<byte> data) => FormatDetector.Detect(data);

    /// <summary>
    /// Identifies the container of a file, reading only the head and tail. A camera raw whose
    /// marking directory sits past the probe comes back as <see cref="ImageFormat.Tiff"/>;
    /// <see cref="Identify(string)"/> reads the whole file and still reports it as raw.
    /// </summary>
    public static ImageFormat DetectFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[]? probe = TryReadProbe(path, out _);
        return probe is null ? ImageFormat.Unknown : FormatDetector.Detect(probe, path);
    }

    // ---- Identify --------------------------------------------------------------------

    /// <summary>Reads header facts without decoding pixels.</summary>
    /// <exception cref="ApertureException">The data is not a recognised or well formed image.</exception>
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        if (!TryIdentify(data, out ImageInfo? info, out ApertureError error))
            throw ApertureException.For(error, FormatDetector.Detect(data));
        return info!;
    }

    /// <summary>Reads header facts from a file without decoding pixels.</summary>
    /// <exception cref="ApertureException">The file is not a recognised or well formed image.</exception>
    public static ImageInfo Identify(string path)
    {
        if (!TryIdentify(path, out ImageInfo? info, out ApertureError error))
            throw ApertureException.For(error, DetectFormat(path));
        return info!;
    }

    /// <summary>Reads header facts without decoding pixels, reporting failure instead of throwing.</summary>
    public static bool TryIdentify(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error) =>
        TryIdentify(data, null, out info, out error);

    /// <summary>
    /// Reads header facts without decoding pixels, using <paramref name="fileName"/> only to
    /// settle formats that carry no signature.
    /// </summary>
    public static bool TryIdentify(ReadOnlySpan<byte> data, string? fileName,
                                   out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (!Resolve(data, fileName, out IImageDecoder? decoder, out error))
            return false;

        return decoder!.TryIdentify(data, out info, out error);
    }

    /// <summary>Reads header facts from a file, reporting failure instead of throwing.</summary>
    public static bool TryIdentify(string path, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        byte[]? data = TryReadAllBytes(path, out error);
        return data is not null && TryIdentify(data, path, out info, out error);
    }

    // ---- Load ------------------------------------------------------------------------

    /// <summary>Decodes an image from memory.</summary>
    /// <exception cref="ApertureException">The data could not be decoded.</exception>
    public static Image Load(ReadOnlySpan<byte> data, DecodeOptions? options = null)
    {
        if (!TryLoad(data, options, out Image? image, out ApertureError error))
            throw ApertureException.For(error, FormatDetector.Detect(data));
        return image!;
    }

    /// <summary>Decodes an image from a file.</summary>
    /// <exception cref="ApertureException">The file could not be read or decoded.</exception>
    public static Image Load(string path, DecodeOptions? options = null)
    {
        if (!TryLoad(path, options, out Image? image, out ApertureError error))
            throw ApertureException.For(error, DetectFormat(path));
        return image!;
    }

    /// <summary>Decodes an image from a stream, reading it to the end.</summary>
    /// <exception cref="ApertureException">The stream could not be read or decoded.</exception>
    public static Image Load(Stream stream, DecodeOptions? options = null)
    {
        if (!TryLoad(stream, options, out Image? image, out ApertureError error))
            throw ApertureException.For(error, ImageFormat.Unknown);
        return image!;
    }

    /// <summary>Decodes an image from memory, reporting failure instead of throwing.</summary>
    public static bool TryLoad(ReadOnlySpan<byte> data, DecodeOptions? options,
                               out Image? image, out ApertureError error) =>
        TryLoad(data, null, options, out image, out error);

    /// <summary>
    /// Decodes an image from memory, using <paramref name="fileName"/> only to settle formats
    /// that carry no signature.
    /// </summary>
    public static bool TryLoad(ReadOnlySpan<byte> data, string? fileName, DecodeOptions? options,
                               out Image? image, out ApertureError error)
    {
        image = null;
        if (!Resolve(data, fileName, out IImageDecoder? decoder, out error))
            return false;

        return decoder!.TryDecode(data, options ?? DecodeOptions.Default, out image, out error);
    }

    /// <summary>Decodes an image from a file, reporting failure instead of throwing.</summary>
    public static bool TryLoad(string path, DecodeOptions? options, out Image? image, out ApertureError error)
    {
        image = null;
        byte[]? data = TryReadAllBytes(path, out error);
        return data is not null && TryLoad(data, path, options, out image, out error);
    }

    /// <summary>Decodes an image from a stream, reporting failure instead of throwing.</summary>
    public static bool TryLoad(Stream stream, DecodeOptions? options, out Image? image, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(stream);
        image = null;

        byte[] data;
        try
        {
            if (stream is MemoryStream seekable && seekable.TryGetBuffer(out ArraySegment<byte> segment))
                return TryLoad(segment.AsSpan(), options, out image, out error);

            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            data = buffer.ToArray();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException)
        {
            error = ApertureError.IoError;
            return false;
        }

        return TryLoad(data, options, out image, out error);
    }

    // ---- Helpers ---------------------------------------------------------------------

    private static bool Resolve(ReadOnlySpan<byte> data, string? fileName,
                                out IImageDecoder? decoder, out ApertureError error)
    {
        decoder = null;
        ImageFormat format = FormatDetector.Detect(data, fileName);
        if (format == ImageFormat.Unknown)
        {
            error = ApertureError.UnknownFormat;
            return false;
        }

        if (!ImageDecoderRegistry.TryGet(format, out decoder))
        {
            error = ApertureError.NotSupported;
            return false;
        }

        error = ApertureError.None;
        return true;
    }

    private static byte[]? TryReadAllBytes(string path, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            error = ApertureError.None;
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ApertureError.IoError;
            return null;
        }
    }

    /// <summary>
    /// Reads the head and, where the file has one, the TGA footer, joined so the detector sees
    /// both without paging in the whole file. The head is large because the maker string that
    /// separates a camera raw from a TIFF can sit several kilobytes in.
    /// </summary>
    private static byte[]? TryReadProbe(string path, out ApertureError error)
    {
        const int HeadLength = 64 * 1024;
        const int TailLength = 26;
        try
        {
            error = ApertureError.None;
            using FileStream fs = File.OpenRead(path);
            long length = fs.Length;
            if (length <= HeadLength + TailLength)
            {
                byte[] whole = new byte[length];
                fs.ReadExactly(whole);
                return whole;
            }

            byte[] probe = new byte[HeadLength + TailLength];
            fs.ReadExactly(probe, 0, HeadLength);
            fs.Seek(-TailLength, SeekOrigin.End);
            fs.ReadExactly(probe, HeadLength, TailLength);
            return probe;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ApertureError.IoError;
            return null;
        }
    }
}
