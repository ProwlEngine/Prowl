// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Aperture;
using Prowl.Editor.Core;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Build;

/// <summary>
/// The icon a built game carries: the project's icon texture, or the Prowl logo, shrunk to the sizes each platform
/// reads and written as the files that platform wants.
/// </summary>
public static class GameIcon
{
    // ====================
    // Data Structures
    // ====================
    /// <summary>A decoded icon source, top row first.</summary>
    public sealed record Source(int Width, int Height, byte[] Pixels);

    /// <summary>One size of the icon, top row first.</summary>
    public sealed record IconImage(int Size, byte[] Pixels);

    // ====================
    // Constants
    // ====================
    /// <summary>The window icon resource's file name beside the generated project.</summary>
    public const string WindowResourceFile = "prowl-icon.rgba";

    /// <summary>The name the window icon resource is embedded under. <c>Window</c> finds it by this ending.</summary>
    public const string WindowResourceName = "Game." + WindowResourceFile;

    /// <summary>The exe icon's file name beside the generated project.</summary>
    public const string ExeFile = "icon.ico";

    /// <summary>The Mac app icon's file name beside the generated project.</summary>
    public const string MacFile = "icon.icns";

    private const int MinSize = 16;

    private const string Missing = "The game icon texture no longer exists. Pick another or clear the Icon setting.";

    private static readonly int[] WindowsSizes = [256, 128, 64, 48, 32, 16];

    // An icns holds only these sizes, each under its own four letter type.
    private static readonly (int Size, string Type)[] MacSizes =
        [(1024, "ic10"), (512, "ic09"), (256, "ic08"), (128, "ic07"), (64, "icp6"), (32, "icp5"), (16, "icp4")];

    // The same pixels again as the double density version of a smaller size.
    private static readonly (int Size, string Type)[] MacRetinaSizes = [(32, "ic11"), (64, "ic12"), (256, "ic13"), (512, "ic14")];

    // ====================
    // Public Methods
    // ====================
    /// <summary>What is wrong with an icon of this size, or null when it is usable.</summary>
    public static string? Problem(string name, int width, int height)
        => width == height && width >= MinSize
            ? null
            : $"The game icon '{name}' is {width}x{height}. It has to be square and at least {MinSize}x{MinSize} pixels.";

    /// <summary>What stops the project's icon texture being used, or null when it is usable or none is set.</summary>
    public static string? Problem(AssetRef<Texture2D> icon)
    {
        if (icon.IsEmpty) return null;
        (string? unusable, string path, string name) = Resolve(icon);
        if (unusable != null) return unusable;

        try
        {
            ImageInfo info = Aperture.Image.Identify(path);
            return Problem(name, info.Width, info.Height);
        }
        catch (Exception e)
        {
            return $"The game icon '{name}' can't be read: {e.Message}";
        }
    }

    /// <summary>The icon texture's image, or the Prowl logo when none is set.</summary>
    public static Source Load(AssetRef<Texture2D> icon)
    {
        if (!icon.IsEmpty)
        {
            (string? unusable, string path, _) = Resolve(icon);
            if (unusable != null) throw new InvalidOperationException(unusable);
            return Decode(Aperture.Image.Load(path, DecodeOptions));
        }

        using Stream stream = EditorApplication.GetEmbeddedResource("prowl-icon.png")
            ?? throw new InvalidOperationException("The Prowl logo is missing from the editor.");
        return Decode(Aperture.Image.Load(stream, DecodeOptions));
    }

    /// <summary>
    /// The source at each Windows size, largest first: every standard size up to the source's own, never larger, and
    /// the source's own size when it is under 256 and not a standard one.
    /// </summary>
    public static List<IconImage> WindowsImages(Source source)
    {
        List<int> sizes = [.. WindowsSizes.Where(s => s <= source.Width)];
        if (source.Width < 256 && !sizes.Contains(source.Width))
        {
            sizes.Add(source.Width);
            sizes.Sort((a, b) => b.CompareTo(a));
        }
        return Shrink(source, sizes);
    }

    /// <summary>The source at each size an icns holds, largest first, never larger than the source.</summary>
    public static List<IconImage> MacImages(Source source)
        => Shrink(source, [.. MacSizes.Select(s => s.Size).Where(s => s <= source.Width)]);

    /// <summary>An ico holding each image as a PNG.</summary>
    public static byte[] ToIco(IReadOnlyList<IconImage> images)
    {
        byte[][] pngs = [.. images.Select(ToPng)];
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        int offset = 6 + (16 * images.Count);
        for (int i = 0; i < images.Count; i++)
        {
            // A side of 256 is stored as 0, since the field is one byte.
            byte side = (byte)(images[i].Size == 256 ? 0 : images[i].Size);
            writer.Write(side);
            writer.Write(side);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (byte[] png in pngs) writer.Write(png);
        return stream.ToArray();
    }

    /// <summary>An icns holding each image as a PNG, and again as the double density image of the size below it.</summary>
    public static byte[] ToIcns(IReadOnlyList<IconImage> images)
    {
        Dictionary<int, byte[]> pngs = images.ToDictionary(i => i.Size, ToPng);
        List<(string Type, byte[] Png)> chunks =
        [
            .. MacSizes.Concat(MacRetinaSizes).Where(s => pngs.ContainsKey(s.Size)).Select(s => (s.Type, pngs[s.Size])),
        ];

        using MemoryStream stream = new();
        WriteChunk(stream, "icns", 8 + chunks.Sum(c => 8 + c.Png.Length));
        foreach ((string type, byte[] png) in chunks)
        {
            WriteChunk(stream, type, 8 + png.Length);
            stream.Write(png);
        }

        return stream.ToArray();
    }

    /// <summary>The window icon resource <c>Window</c> reads: a count, then each image's width, height and RGBA pixels.</summary>
    public static byte[] ToWindowResource(IReadOnlyList<IconImage> images)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(images.Count);
        foreach (IconImage image in images)
        {
            writer.Write(image.Size);
            writer.Write(image.Size);
            writer.Write(image.Pixels);
        }

        return stream.ToArray();
    }

    // ====================
    // Internal Helpers
    // ====================
    private static DecodeOptions DecodeOptions => new() { TargetPixelFormat = PixelFormat.Rgba8 };

    private static Source Decode(Aperture.Image image)
    {
        using (image)
        {
            byte[] pixels = new byte[image.Width * image.Height * 4];
            image.RootFrame.CopyTo(pixels, image.Width * 4);
            return new Source(image.Width, image.Height, pixels);
        }
    }

    // The icon texture's file on disk and its path under Assets, or the error when it can't be read as an image file.
    private static (string? Error, string Path, string Relative) Resolve(AssetRef<Texture2D> icon)
    {
        EditorAssetBackend? assets = EditorAssetBackend.Instance;
        if (assets?.GuidToPathIncludingSubAssets(icon.AssetID) is not { } relative) return (Missing, "", "");

        if (assets.TryGetParentGuid(icon.AssetID, out _))
        {
            Asset? sub = AssetDatabase.Get(icon.AssetID);
            return ($"The game icon '{(sub.IsValid() ? sub.Name : icon.AssetID.ToString())}' is inside '{relative}'. Pick an image file.", "", relative);
        }

        string path = TextureSpriteMeta.AbsolutePath(icon.AssetID);
        return (File.Exists(path) ? null : Missing, path, relative);
    }

    // A size that divides the largest is made from it, so a big source is read once; any other comes from the source, since resizing twice through misaligned pixels blurs it
    private static List<IconImage> Shrink(Source source, List<int> sizes)
    {
        if (sizes.Count == 0) return [];

        byte[] largest = TextureProcessing.Resize(source.Pixels, source.Width, source.Height, sizes[0], sizes[0], srgb: true);
        return [.. sizes.Select(size => new IconImage(size, sizes[0] % size == 0
            ? TextureProcessing.Resize(largest, sizes[0], sizes[0], size, size, srgb: true)
            :TextureProcessing.Resize(source.Pixels, source.Width, source.Height, size, size, srgb: true)))];
    }

    private static byte[] ToPng(IconImage image)
    {
        using Aperture.Image png = Aperture.Image.FromPixels(image.Pixels, image.Size, image.Size, PixelFormat.Rgba8);
        return png.Encode(new EncodeOptions { Format = ImageFormat.Png });
    }

    private static void WriteChunk(Stream stream, string type, int length)
    {
        Span<byte> header = stackalloc byte[8];
        System.Text.Encoding.ASCII.GetBytes(type, header);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], length);
        stream.Write(header);
    }
}
