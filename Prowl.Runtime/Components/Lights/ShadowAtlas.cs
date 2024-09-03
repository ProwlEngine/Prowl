// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime
{
    public static class ShadowAtlas
    {
        private static int size, freeTiles, tileSize, tileCount;
        private static int maxShadowSize;
        private static int?[,] tiles;

        private static RenderTexture? atlas;

        public static void TryInitialize()
        {
            tileSize = 32;
            maxShadowSize = 256;
            if (atlas != null) return;

            size = 4096;
            tileSize = 32;
            maxShadowSize = 1024;

            if (size % tileSize != 0)
                throw new ArgumentException("Size must be divisible by tileSize");

            tileCount = size / tileSize;
            freeTiles = tileCount * tileCount;
            tiles = new int?[tileCount, tileCount];

            atlas ??= new RenderTexture((uint)size, (uint)size, [], PixelFormat.D32_Float, true);
        }

        public static int GetAtlasWidth() => tileCount;

        public static int GetTileSize() => tileSize;
        public static int GetMaxShadowSize() => maxShadowSize;
        public static int GetSize() => size;

        public static RenderTexture? GetAtlas() => atlas;

        public static Vector2Int? ReserveTiles(int width, int height, int lightID)
        {
            int tileWidth = width / tileSize;
            int tileHeight = height / tileSize;

            for (int i = 0; i <= tileCount - tileWidth; i++)
                for (int j = 0; j <= tileCount - tileHeight; j++)
                    if (tiles[i, j] == null)
                    {
                        bool found = true;
                        for (int x = i; x < i + tileWidth && found; x++)
                            for (int y = j; y < j + tileHeight && found; y++)
                                if (tiles[x, y] != null)
                                    found = false;

                        if (found)
                        {
                            ReserveTile(i, j, tileWidth, tileHeight, lightID);
                            return new Vector2Int(i * tileSize, j * tileSize);
                        }
                    }

            return null;
        }

        private static void ReserveTile(int x, int y, int width, int height, int lightID)
        {
            if (x < 0 || y < 0 || x + width > tileCount || y + height > tileCount)
                throw new ArgumentException("Tile is out of bounds");

            for (int i = x; i < x + width; i++)
                for (int j = y; j < y + height; j++)
                {
                    if (tiles[i, j].HasValue)
                        throw new ArgumentException("Tile is already reserved");
                    tiles[i, j] = lightID;
                }

            freeTiles -= width * height;
        }


        public static void FreeTiles(int lightID)
        {
            for (int i = 0; i < tileCount; i++)
                for (int j = 0; j < tileCount; j++)
                    if (tiles[i, j] == lightID)
                    {
                        tiles[i, j] = null;
                        freeTiles++;
                    }
        }

        public static void Clear()
        {
            for (int i = 0; i < tileCount; i++)
                for (int j = 0; j < tileCount; j++)
                    tiles[i, j] = null;

            freeTiles = tileCount * tileCount;
        }
    }
}
