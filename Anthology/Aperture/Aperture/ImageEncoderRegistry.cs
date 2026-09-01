// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Encoders;

namespace Prowl.Aperture;

/// <summary>
/// Maps an <see cref="ImageFormat"/> to the encoder that writes it. PNG is the only format that
/// can be written today; <see cref="Register"/> adds one of your own or replaces a built in.
/// </summary>
public static class ImageEncoderRegistry
{
    private static readonly Dictionary<ImageFormat, IImageEncoder> Encoders = new()
    {
        [ImageFormat.Png] = new PngEncoder(),
    };

    private static readonly object Gate = new();

    /// <summary>Every format that currently has an encoder.</summary>
    public static IReadOnlyCollection<ImageFormat> RegisteredFormats
    {
        get
        {
            lock (Gate)
                return Encoders.Keys.ToArray();
        }
    }

    /// <summary>Installs an encoder, replacing any existing one for the same format.</summary>
    public static void Register(IImageEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        lock (Gate)
            Encoders[encoder.Format] = encoder;
    }

    /// <summary>Removes the encoder for a format, so saves of it fail with <see cref="ApertureError.NotSupported"/>.</summary>
    public static bool Unregister(ImageFormat format)
    {
        lock (Gate)
            return Encoders.Remove(format);
    }

    /// <summary>Looks up the encoder for a format.</summary>
    public static bool TryGet(ImageFormat format, out IImageEncoder? encoder)
    {
        lock (Gate)
            return Encoders.TryGetValue(format, out encoder);
    }

    /// <summary>Looks up the encoder for a format, or null when none is registered.</summary>
    public static IImageEncoder? Get(ImageFormat format)
    {
        TryGet(format, out IImageEncoder? encoder);
        return encoder;
    }
}
