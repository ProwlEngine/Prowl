// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture;

namespace Prowl.Editor.Test;

/// <summary>Small real image files for the tests that need something importable on disk.</summary>
internal static class TestImages
{
    /// <summary>Writes a PNG of one colour, creating the directory if it is not there yet.</summary>
    public static void WriteSolidPng(string absolutePath, int size, byte r, byte g, byte b, byte a = 255)
    {
        string? directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] pixels = new byte[size * size * 4];
        for (int at = 0; at < pixels.Length; at += 4)
        {
            pixels[at] = r;
            pixels[at + 1] = g;
            pixels[at + 2] = b;
            pixels[at + 3] = a;
        }

        using Image image = Image.FromPixels(pixels, size, size, PixelFormat.Rgba8);
        image.Save(absolutePath);
    }
}
