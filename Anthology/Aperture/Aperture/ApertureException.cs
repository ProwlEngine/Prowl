// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// Thrown by the throwing overloads of <see cref="Image.Load(string, DecodeOptions?)"/> and
/// <see cref="Image.Identify(string)"/>. The Try variants report the same
/// <see cref="ApertureError"/> without throwing.
/// </summary>
public sealed class ApertureException : Exception
{
    /// <summary>The failure category.</summary>
    public ApertureError Error { get; }

    /// <summary>The container the failure was attributed to, if one was identified.</summary>
    public ImageFormat Format { get; }

    /// <summary>Creates an exception describing a decode or identify failure.</summary>
    public ApertureException(ApertureError error, ImageFormat format, string message)
        : base(message)
    {
        Error = error;
        Format = format;
    }

    internal static ApertureException For(ApertureError error, ImageFormat format) =>
        new(error, format, Describe(error, format));

    /// <summary>The same failures worded for a write rather than a read.</summary>
    internal static ApertureException ForSave(ApertureError error, ImageFormat format)
    {
        string what = format == ImageFormat.Unknown ? "image" : format.ToString();
        string message = error switch
        {
            ApertureError.UnknownFormat => "No format was named and the file extension does not imply one.",
            ApertureError.NotSupported => $"No encoder is registered for {what}.",
            ApertureError.UnsupportedFeature => $"The {what} encoder cannot write that pixel layout.",
            ApertureError.InvalidDimensions => "An image is at least one pixel each way.",
            ApertureError.IoError => $"The {what} could not be written.",
            _ => Describe(error, format),
        };

        return new ApertureException(error, format, message);
    }

    private static string Describe(ApertureError error, ImageFormat format)
    {
        string what = format == ImageFormat.Unknown ? "image" : format.ToString();
        return error switch
        {
            ApertureError.UnknownFormat => "The data does not match any container signature Aperture knows.",
            ApertureError.NotSupported => $"No decoder is registered for {what}.",
            ApertureError.UnsupportedFeature => $"The {what} uses a feature this decoder does not implement.",
            ApertureError.InvalidHeader => $"The {what} header is malformed.",
            ApertureError.InvalidBitDepth => $"The {what} declares a bit depth the format does not allow.",
            ApertureError.InvalidColorType => $"The {what} declares an undefined colour type.",
            ApertureError.InvalidDimensions => $"The {what} declares unusable dimensions.",
            ApertureError.InvalidData => $"The {what} payload is corrupt.",
            ApertureError.UnexpectedEndOfData => $"The {what} ended before the header said it would.",
            ApertureError.ChecksumMismatch => $"A checksum inside the {what} does not match its data.",
            ApertureError.DecompressionFailed => $"A compressed stream inside the {what} could not be unpacked.",
            ApertureError.LimitExceeded => $"The {what} exceeds the limits set in DecodeOptions.",
            ApertureError.OutOfMemory => $"Decoding the {what} ran out of memory.",
            ApertureError.IoError => $"The {what} could not be read.",
            ApertureError.NoImageData => $"The {what} contains no image.",
            _ => $"The {what} could not be decoded.",
        };
    }
}
