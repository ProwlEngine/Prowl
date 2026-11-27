// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>How hard an encoder works to make the output small.</summary>
public enum CompressionEffort
{
    /// <summary>No compression at all, for a file that is about to be thrown away.</summary>
    None,

    /// <summary>Compresses, but spends as little time as possible doing it.</summary>
    Fastest,

    /// <summary>The balance an encoder would pick if it were not asked.</summary>
    Balanced,

    /// <summary>Spends noticeably longer for a noticeably smaller file.</summary>
    Smallest,
}

/// <summary>
/// What an encoder should write. The shared settings live here and each format adds its own; a
/// setting a format has no answer for is ignored rather than refused.
/// </summary>
public sealed class EncodeOptions
{
    /// <summary>The settings an encoder uses when a caller passes none.</summary>
    public static EncodeOptions Default { get; } = new();

    /// <summary>
    /// The container to write. <see cref="ImageFormat.Unknown"/> asks the entry point to choose,
    /// which the path taking overloads do from the file extension.
    /// </summary>
    public ImageFormat Format { get; set; } = ImageFormat.Unknown;

    /// <summary>How hard to work at making the file small.</summary>
    public CompressionEffort Effort { get; set; } = CompressionEffort.Balanced;

    /// <summary>
    /// Convert the pixels to this layout before writing. Null writes the layout the image already
    /// holds, narrowed only where the format cannot represent it.
    /// </summary>
    public PixelFormat? TargetPixelFormat { get; set; }

    /// <summary>
    /// Reads the bottom row of the image first, for a caller whose pixels are held the way a
    /// lower left texture origin wants them.
    /// </summary>
    public bool FlipVertically { get; set; }

    /// <summary>Settings only the PNG encoder reads.</summary>
    public PngEncodeOptions Png { get; set; } = new();

    /// <summary>Returns a copy, so a caller can adjust one setting without disturbing the original.</summary>
    public EncodeOptions Clone() => new()
    {
        Format = Format,
        Effort = Effort,
        TargetPixelFormat = TargetPixelFormat,
        FlipVertically = FlipVertically,
        Png = Png.Clone(),
    };
}

/// <summary>Which per row filter a PNG encoder applies before compressing.</summary>
public enum PngFilterStrategy
{
    /// <summary>Picks the filter that makes each row compress best, tried row by row.</summary>
    Adaptive,

    /// <summary>Stores every row as it lies, which is fastest and largest.</summary>
    None,

    /// <summary>Differences each byte against the pixel to its left.</summary>
    Sub,

    /// <summary>Differences each byte against the byte above it.</summary>
    Up,

    /// <summary>Differences against the average of left and above.</summary>
    Average,

    /// <summary>Differences against whichever of the three neighbours the predictor picks.</summary>
    Paeth,
}

/// <summary>Settings the PNG encoder reads.</summary>
public sealed class PngEncodeOptions
{
    /// <summary>
    /// Which row filter to apply. Adaptive tries all five on every row and keeps the one whose
    /// output has the smallest total deviation, which is what makes a photograph compress.
    /// </summary>
    public PngFilterStrategy Filter { get; set; } = PngFilterStrategy.Adaptive;

    /// <summary>Text to write as tEXt chunks, keyed by keyword. Keywords are Latin-1, 1 to 79 characters.</summary>
    public IReadOnlyDictionary<string, string>? TextEntries { get; set; }

    /// <summary>Returns a copy.</summary>
    public PngEncodeOptions Clone() => new()
    {
        Filter = Filter,
        TextEntries = TextEntries,
    };
}
