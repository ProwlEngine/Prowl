// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders;

namespace Prowl.Aperture;

/// <summary>
/// Maps a detected <see cref="ImageFormat"/> to the decoder that handles it. Pre-populated with
/// every built in decoder; <see cref="Register"/> replaces one or adds a format of your own.
/// </summary>
public static class ImageDecoderRegistry
{
    private static readonly Dictionary<ImageFormat, IImageDecoder> Decoders = new()
    {
        [ImageFormat.Bmp] = new BmpDecoder(),
        [ImageFormat.Dds] = new DdsDecoder(),
        [ImageFormat.Exr] = new ExrDecoder(),
        [ImageFormat.Gif] = new GifDecoder(),
        [ImageFormat.Hdr] = new HdrDecoder(),
        [ImageFormat.Ico] = new IcoDecoder(),
        [ImageFormat.Jpeg] = new JpegDecoder(),
        [ImageFormat.Png] = new PngDecoder(),
        [ImageFormat.Pnm] = new PnmDecoder(),
        [ImageFormat.Tga] = new TgaDecoder(),
        [ImageFormat.Tiff] = new TiffDecoder(),
        [ImageFormat.Webp] = new WebpDecoder(),
        [ImageFormat.Psd] = new PsdDecoder(),
        [ImageFormat.Raw] = new RawDecoder(),
    };

    private static readonly object Gate = new();

    /// <summary>Every format that currently has a decoder.</summary>
    public static IReadOnlyCollection<ImageFormat> RegisteredFormats
    {
        get
        {
            lock (Gate)
                return Decoders.Keys.ToArray();
        }
    }

    /// <summary>Installs a decoder, replacing any existing one for the same format.</summary>
    public static void Register(IImageDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        lock (Gate)
            Decoders[decoder.Format] = decoder;
    }

    /// <summary>Removes the decoder for a format, so loads of it fail with <see cref="ApertureError.NotSupported"/>.</summary>
    public static bool Unregister(ImageFormat format)
    {
        lock (Gate)
            return Decoders.Remove(format);
    }

    /// <summary>Looks up the decoder for a format.</summary>
    public static bool TryGet(ImageFormat format, out IImageDecoder? decoder)
    {
        lock (Gate)
            return Decoders.TryGetValue(format, out decoder);
    }

    /// <summary>Looks up the decoder for a format, or null when none is registered.</summary>
    public static IImageDecoder? Get(ImageFormat format)
    {
        TryGet(format, out IImageDecoder? decoder);
        return decoder;
    }
}
