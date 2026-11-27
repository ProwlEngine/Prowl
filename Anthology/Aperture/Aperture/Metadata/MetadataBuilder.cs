// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;

namespace Prowl.Aperture.Metadata;

/// <summary>
/// Collects the ancillary blocks a container carries. Every format keeps them somewhere of its
/// own, but what comes out is the same three or four things, so only the finding differs. Nothing
/// here reads inside a block.
/// </summary>
internal sealed class MetadataBuilder
{
    /// <summary>Cap on a single block, so a header claiming a huge one costs nothing to refuse.</summary>
    private const int MaxBlock = 64 * 1024 * 1024;

    private byte[]? _exif;
    private byte[]? _xmp;
    private byte[]? _icc;
    private Dictionary<string, string>? _text;

    public void SetExif(ReadOnlySpan<byte> block)
    {
        if (_exif is null && block.Length > 0 && block.Length <= MaxBlock)
            _exif = block.ToArray();
    }

    public void SetXmp(ReadOnlySpan<byte> block)
    {
        if (_xmp is null && block.Length > 0 && block.Length <= MaxBlock)
            _xmp = block.ToArray();
    }

    public void SetProfile(ReadOnlySpan<byte> block)
    {
        if (_icc is null && block.Length > 0 && block.Length <= MaxBlock)
            _icc = block.ToArray();
    }

    /// <summary>Takes a profile the container stored compressed, as PNG does.</summary>
    public void SetDeflatedProfile(ReadOnlySpan<byte> block)
    {
        if (_icc is not null || TryInflate(block) is not { } inflated)
            return;

        _icc = inflated;
    }

    public void AddText(string key, string value)
    {
        if (key.Length == 0)
            return;

        _text ??= [];
        _text.TryAdd(key, value);
    }

    public ImageMetadata Build() =>
        _exif is null && _xmp is null && _icc is null && _text is null
            ? ImageMetadata.Empty
            : new ImageMetadata
            {
                Exif = _exif,
                Xmp = _xmp,
                IccProfile = _icc,
                TextEntries = _text ?? (IReadOnlyDictionary<string, string>)ImageMetadata.Empty.TextEntries,
            };

    /// <summary>Undoes the zlib wrapper a container may have put around a block.</summary>
    public static byte[]? TryInflate(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length is 0 or > MaxBlock)
            return null;

        try
        {
            using MemoryStream source = new(compressed.ToArray(), writable: false);
            using ZLibStream stream = new(source, CompressionMode.Decompress);
            using MemoryStream output = new();

            byte[] buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxBlock)
                    return null;

                output.Write(buffer, 0, read);
            }

            return output.Length == 0 ? null : output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The bytes up to a null, which is how these containers delimit a name.</summary>
    public static ReadOnlySpan<byte> UpToNull(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        return end < 0 ? data : data[..end];
    }
}
