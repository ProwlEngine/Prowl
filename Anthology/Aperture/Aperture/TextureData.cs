// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Dds;

namespace Prowl.Aperture;

/// <summary>One surface of a texture: a level of one slice, and the bytes it is stored as.</summary>
public sealed class TextureLevel
{
    /// <summary>Mipmap level, zero being the full resolution surface.</summary>
    public required int MipLevel { get; init; }

    /// <summary>Array slice, cube map face, or depth slice of a volume. Zero for a plain 2D texture.</summary>
    public required int Slice { get; init; }

    /// <summary>Width of this surface in pixels, not in blocks.</summary>
    public required int Width { get; init; }

    /// <summary>Height of this surface in pixels, not in blocks.</summary>
    public required int Height { get; init; }

    /// <summary>
    /// The bytes exactly as the file stores them, with nothing unpacked or converted. For a block
    /// compressed layout these are the blocks, ready to hand to a graphics API as they are.
    /// </summary>
    public required ReadOnlyMemory<byte> Bytes { get; init; }
}

/// <summary>
/// A texture file read without decoding it: the layout it is stored in, and the bytes of every
/// surface, as the hardware reads them. Nothing is copied, so the array passed to
/// <see cref="TryLoad(byte[], out TextureData, out ApertureError)"/> has to outlive what comes
/// back. Only DDS is a texture container today; anything else is refused rather than repacked.
/// </summary>
public sealed class TextureData
{
    /// <summary>The container the texture was read from.</summary>
    public required ImageFormat Container { get; init; }

    /// <summary>The layout the surfaces are stored in.</summary>
    public required TextureFormat Format { get; init; }

    /// <summary>
    /// The layout as the file numbers it, for a file whose layout this library cannot name. Zero
    /// where the file states none, which an older header does.
    /// </summary>
    public required uint FormatNumber { get; init; }

    /// <summary>What the layout is called, in the words <see cref="ImageInfo.Compression"/> uses.</summary>
    public required string FormatName { get; init; }

    /// <summary>Width of the largest surface in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height of the largest surface in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Slices a volume texture is deep, one for a flat texture.</summary>
    public required int Depth { get; init; }

    /// <summary>Mip levels the file holds, the largest included.</summary>
    public required int MipCount { get; init; }

    /// <summary>Faces of a cube map or elements of an array, one for a plain texture.</summary>
    public required int SliceCount { get; init; }

    /// <summary>Whether the slices are the six faces of a cube rather than array elements.</summary>
    public required bool IsCubeMap { get; init; }

    /// <summary>Whether the surfaces are compressed into blocks rather than stored a pixel at a time.</summary>
    public bool IsBlockCompressed => BytesPerBlock > 0;

    /// <summary>Pixels one block covers across, one where the layout is not block compressed.</summary>
    public required int BlockWidth { get; init; }

    /// <summary>Pixels one block covers down, one where the layout is not block compressed.</summary>
    public required int BlockHeight { get; init; }

    /// <summary>Bytes one block occupies, zero where the layout is not block compressed.</summary>
    public required int BytesPerBlock { get; init; }

    /// <summary>
    /// Every surface, in the order the file stores them: all the levels of one slice, then the
    /// next slice, with a volume's depth slices inside its levels.
    /// </summary>
    public required IReadOnlyList<TextureLevel> Levels { get; init; }

    /// <summary>Finds one surface, or null where the file does not hold it.</summary>
    public TextureLevel? Level(int mipLevel, int slice = 0)
    {
        foreach (TextureLevel level in Levels)
        {
            if (level.MipLevel == mipLevel && level.Slice == slice)
                return level;
        }

        return null;
    }

    /// <summary>Reads a texture file, keeping the bytes it read.</summary>
    /// <exception cref="ApertureException">The file is not a texture this library can describe.</exception>
    public static TextureData Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] data = File.ReadAllBytes(path);
        if (!TryLoad(data, out TextureData? texture, out ApertureError error))
            throw new ApertureException(error, FormatDetector.Detect(data, path), path);

        return texture!;
    }

    /// <summary>
    /// Reads a texture from bytes the caller keeps. The surfaces are windows onto
    /// <paramref name="data"/> rather than copies of it.
    /// </summary>
    public static bool TryLoad(byte[] data, out TextureData? texture, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(data);
        texture = null;

        if (FormatDetector.Detect(data) != ImageFormat.Dds)
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        if (!DdsSurface.TryRead(data, out DdsSurface surface, out error))
            return false;

        List<DdsPlane> planes = DdsPlanes.Enumerate(surface, data.Length);
        if (planes.Count == 0)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        List<TextureLevel> levels = new(planes.Count);
        foreach (DdsPlane plane in planes)
        {
            levels.Add(new TextureLevel
            {
                MipLevel = plane.MipLevel,
                Slice = plane.Slice,
                Width = plane.Width,
                Height = plane.Height,
                Bytes = new ReadOnlyMemory<byte>(data, plane.Offset, plane.Length),
            });
        }

        if (!ImageDecoderRegistry.Get(ImageFormat.Dds)!.TryIdentify(data, out ImageInfo? info, out error))
            return false;

        texture = new TextureData
        {
            Container = ImageFormat.Dds,
            Format = Enum.IsDefined(typeof(TextureFormat), surface.DxgiFormat)
                ? (TextureFormat)surface.DxgiFormat
                : TextureFormat.Unknown,
            FormatNumber = surface.DxgiFormat,
            FormatName = info!.Compression ?? string.Empty,
            Width = surface.Width,
            Height = surface.Height,
            Depth = surface.Depth,
            MipCount = surface.MipLevels,
            SliceCount = surface.Slices,
            IsCubeMap = surface.Slices == 6 && surface.Depth == 1,
            BlockWidth = surface.BlockWidth,
            BlockHeight = surface.BlockHeight,
            BytesPerBlock = surface.BlockBytes,
            Levels = levels,
        };

        error = ApertureError.None;
        return true;
    }
}
