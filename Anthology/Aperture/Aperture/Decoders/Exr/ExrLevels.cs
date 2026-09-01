// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>One resolution of a tiled file, and where its tiles sit in the chunk table.</summary>
internal readonly record struct ExrLevel(int X, int Y, int Width, int Height,
                                         int Across, int Down, int FirstChunk)
{
    public int Chunks => Across * Down;
}

/// <summary>
/// The resolutions a tiled file holds: the picture once, a chain of halvings, or a grid whose two
/// axes halve independently. The chunk table covers every tile of every level in order.
/// </summary>
internal static class ExrLevels
{
    /// <summary>Cap on levels, which is past what any picture a machine can hold would need.</summary>
    private const int MaxLevels = 32;

    /// <summary>The size of one axis at a level, rounded the way the file says to round it.</summary>
    public static int SizeAt(int size, int level, bool roundUp) =>
        roundUp ? Math.Max(1, (size + (1 << level) - 1) >> level) : Math.Max(1, size >> level);

    /// <summary>How many halvings an axis of this size takes before it reaches one.</summary>
    public static int CountFor(int size, bool roundUp)
    {
        int count = 1;
        int at = size;

        while (at > 1 && count < MaxLevels)
        {
            at = roundUp ? (at + 1) / 2 : at / 2;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Lists the levels in the order the chunk table holds them: one after another for a chain,
    /// and rows of the grid for the other kind.
    /// </summary>
    public static List<ExrLevel> Enumerate(ExrHeader header)
    {
        List<ExrLevel> levels = [];
        if (header.TileWidth <= 0 || header.TileHeight <= 0)
            return levels;

        bool roundUp = header.LevelRounding == 1;
        int width = header.Width;
        int height = header.Height;
        int at = 0;

        void Add(int x, int y)
        {
            int levelWidth = SizeAt(width, x, roundUp);
            int levelHeight = SizeAt(height, y, roundUp);
            int across = (levelWidth + header.TileWidth - 1) / header.TileWidth;
            int down = (levelHeight + header.TileHeight - 1) / header.TileHeight;

            levels.Add(new ExrLevel(x, y, levelWidth, levelHeight, across, down, at));
            at += across * down;
        }

        switch (header.LevelMode)
        {
            case 1:
            {
                int count = Math.Max(CountFor(width, roundUp), CountFor(height, roundUp));
                for (int level = 0; level < count; level++)
                    Add(level, level);

                break;
            }

            case 2:
            {
                int acrossLevels = CountFor(width, roundUp);
                int downLevels = CountFor(height, roundUp);
                for (int y = 0; y < downLevels; y++)
                {
                    for (int x = 0; x < acrossLevels; x++)
                        Add(x, y);
                }

                break;
            }

            default:
                Add(0, 0);
                break;
        }

        return levels;
    }
}
