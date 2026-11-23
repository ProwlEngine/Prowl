using System;

namespace Prowl.Graphite.Rendering;

/// <summary>A grid of heights over the terrain's range, 0 at its bottom and 65535 at its top, row by row from z 0.</summary>
public sealed class TerrainHeightmap
{
    public TerrainHeightmap(int width, int height, ushort[] samples)
    {
        if (width < 2 || height < 2 || samples.Length != width * height)
            throw new ArgumentException("A heightmap needs at least 2 by 2 samples, one per texel.");
        Width = width;
        Height = height;
        Samples = samples;
    }

    public int Width { get; }
    public int Height { get; }
    public ushort[] Samples { get; }

    /// <summary>Cut out quads, one per sample for the quad whose lowest corner it is, or null for none.</summary>
    public bool[]? Holes { get; set; }

    /// <summary>
    /// Joins tiles laid out [row along z, column along x] into one map, each sharing <paramref name="overlap"/> samples
    /// with its neighbours, 1 for exports whose tiles repeat their seam.
    /// </summary>
    public static TerrainHeightmap Join(TerrainHeightmap[,] tiles, int overlap)
    {
        int rows = tiles.GetLength(0), columns = tiles.GetLength(1);
        int width = overlap, height = overlap;
        for (int column = 0; column < columns; column++)
            width += tiles[0, column].Width - overlap;
        for (int row = 0; row < rows; row++)
            height += tiles[row, 0].Height - overlap;

        ushort[] samples = new ushort[width * height];
        bool[]? holes = null;
        int z0 = 0;
        for (int row = 0; row < rows; row++)
        {
            int x0 = 0;
            for (int column = 0; column < columns; column++)
            {
                TerrainHeightmap tile = tiles[row, column];
                if (tile.Height != tiles[row, 0].Height || tile.Width != tiles[0, column].Width)
                    throw new ArgumentException("Tiles in a row must share a height and tiles in a column a width.");
                for (int z = 0; z < tile.Height; z++)
                {
                    tile.Samples.AsSpan(z * tile.Width, tile.Width).CopyTo(samples.AsSpan((z0 + z) * width + x0));
                    if (tile.Holes != null)
                    {
                        holes ??= new bool[width * height];
                        tile.Holes.AsSpan(z * tile.Width, tile.Width).CopyTo(holes.AsSpan((z0 + z) * width + x0));
                    }
                }
                x0 += tile.Width - overlap;
            }
            z0 += tiles[row, 0].Height - overlap;
        }
        return new TerrainHeightmap(width, height, samples) { Holes = holes };
    }
}
