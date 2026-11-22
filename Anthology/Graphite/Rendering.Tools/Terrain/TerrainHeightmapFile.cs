using System;
using System.IO;

using Prowl.Aperture;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>How a heightmap file's values become terrain heights.</summary>
public sealed class TerrainHeightmapOptions
{
    /// <summary>
    /// The source values that land on the terrain's bottom and top. Integer formats are first read as 0 to 1, float
    /// formats (EXR, R32) as they are, so for a float map in metres these are its lowest and highest metres.
    /// </summary>
    public float SourceMin { get; init; }
    public float SourceMax { get; init; } = 1f;

    /// <summary>The width of a .raw or .r32 file, 0 to read it as square.</summary>
    public int RawWidth { get; init; }

    /// <summary>Whether the file's first row lies at the highest z instead of z 0.</summary>
    public bool FlipZ { get; init; }
}

/// <summary>Reads heightmap files into <see cref="TerrainHeightmap"/>s.</summary>
public static class TerrainHeightmapFile
{
    /// <summary>
    /// Loads a 16 bit little endian .raw, a 32 bit float .r32, or any image Aperture reads (16 bit PNG and TIFF,
    /// EXR and the rest), taking the first channel of colour images.
    /// </summary>
    public static TerrainHeightmap Load(string path, TerrainHeightmapOptions? options = null)
    {
        options ??= new TerrainHeightmapOptions();
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".raw" or ".r16")
        {
            byte[] bytes = File.ReadAllBytes(path);
            (int width, int height) = RawSize(bytes.Length / 2, options.RawWidth);
            return Map(width, height, options, i => BitConverter.ToUInt16(bytes, i * 2) / 65535.0);
        }
        if (extension == ".r32")
        {
            byte[] bytes = File.ReadAllBytes(path);
            (int width, int height) = RawSize(bytes.Length / 4, options.RawWidth);
            return Map(width, height, options, i => BitConverter.ToSingle(bytes, i * 4));
        }

        ImageInfo info = Image.Identify(path);
        bool isFloat = info.IsHdr || info.PreferredPixelFormat.IsFloatingPoint();
        using Image image = Image.Load(path, new DecodeOptions { TargetPixelFormat = isFloat ? Prowl.Aperture.PixelFormat.LF32 : Prowl.Aperture.PixelFormat.L16 });
        ImageFrame frame = image.RootFrame;
        if (isFloat)
        {
            float[] values = frame.AsSpan<float>().ToArray();
            return Map(frame.Width, frame.Height, options, i => values[i]);
        }
        ushort[] samples = frame.AsSpan<ushort>().ToArray();
        return Map(frame.Width, frame.Height, options, i => samples[i] / 65535.0);
    }

    /// <summary>Loads tiles laid out [row along z, column along x] and joins them, see <see cref="TerrainHeightmap.Join"/>.</summary>
    public static TerrainHeightmap LoadTiles(string[,] paths, int overlap, TerrainHeightmapOptions? options = null)
    {
        TerrainHeightmap[,] tiles = new TerrainHeightmap[paths.GetLength(0), paths.GetLength(1)];
        for (int row = 0; row < tiles.GetLength(0); row++)
            for (int column = 0; column < tiles.GetLength(1); column++)
                tiles[row, column] = Load(paths[row, column], options);
        return TerrainHeightmap.Join(tiles, overlap);
    }

    private static (int Width, int Height) RawSize(int count, int width)
    {
        if (width <= 0)
        {
            width = (int)Math.Round(Math.Sqrt(count));
            if (width * width != count)
                throw new InvalidDataException($"A raw heightmap of {count} samples is not square, give its width.");
        }
        if (count % width != 0)
            throw new InvalidDataException($"A raw heightmap of {count} samples does not split into rows of {width}.");
        return (width, count / width);
    }

    private static TerrainHeightmap Map(int width, int height, TerrainHeightmapOptions options, Func<int, double> value)
    {
        double min = options.SourceMin, scale = 1.0 / (options.SourceMax - options.SourceMin);
        ushort[] samples = new ushort[width * height];
        for (int z = 0; z < height; z++)
        {
            int row = options.FlipZ ? height - 1 - z : z;
            for (int x = 0; x < width; x++)
            {
                double t = Math.Clamp((value(row * width + x) - min) * scale, 0.0, 1.0);
                samples[z * width + x] = (ushort)Math.Round(t * 65535.0, MidpointRounding.AwayFromZero);
            }
        }
        return new TerrainHeightmap(width, height, samples);
    }
}
