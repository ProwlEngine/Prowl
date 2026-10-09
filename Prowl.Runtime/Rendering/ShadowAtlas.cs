// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The depth texture every shadow map lives in, and the settings that decide how its space is shared.
/// Tiles are handed out by <see cref="Allocator"/> and stay where they are across frames, so a shadow map
/// that nothing invalidated is reused instead of redrawn.
/// </summary>
public static class ShadowAtlas
{
    private static int size;
    private static ShadowDepthPrecision precision;
    private static Texture2D? depth;
    private static GraphicsFrameBuffer? frameBuffer;

    /// <summary>Width and height of the shadow atlas every shadowed light shares, clamped to between 1024 and the
    /// largest texture the GPU supports. A change rebuilds the atlas, and every shadow map, on the next render.</summary>
    public static int RequestedSize { get; set; } = 8192;

    /// <summary>Bits per shadow map depth value. 16 halves the atlas's memory and is precise enough for shadows,
    /// since the bias is measured in texels and the cascades are fitted tight. A change rebuilds the atlas.</summary>
    public static ShadowDepthPrecision DepthPrecision { get; set; } = ShadowDepthPrecision.Bits16;

    /// <summary>Point and spot light shadows fade out over the last tenth of this distance from the camera,
    /// measured to the light's range. Lights further away are lit without shadows and take no atlas space.</summary>
    public static float LocalShadowDistance { get; set; } = 100f;

    /// <summary>Shadow map texels a point or spot light aims for per screen pixel it covers. When the atlas runs out
    /// of room every light scales down together before any loses its shadow.</summary>
    public static float TexelsPerPixel { get; set; } = 1f;

    /// <summary>Smallest tile a point light face or spot light is given. Below this a light drops its shadow.</summary>
    public static int MinTileSize { get; set; } = 64;

    /// <summary>How many local light faces may be redrawn in one frame only to change their resolution. Faces whose
    /// casters or light changed are always redrawn, this only paces sharpening and softening.</summary>
    public static int MaxResolutionChangesPerFrame { get; set; } = 32;

    /// <summary>How the shared space is split. Reset whenever the atlas is rebuilt.</summary>
    internal static ShadowTileAllocator Allocator { get; } = new(1024);

    /// <summary>Bumped every time the atlas is rebuilt, so cached tiles from before can tell they are gone.</summary>
    internal static int Generation { get; private set; }

    public static void TryInitialize()
    {
        int wanted = Maths.Clamp(RequestedSize, 1024, Graphics.MaxTextureSize);
        int pow2 = 1024;
        while (pow2 * 2 <= wanted) pow2 *= 2;
        wanted = pow2;
        if (depth.IsValid() && size == wanted && precision == DepthPrecision) return;

        frameBuffer?.Dispose();
        if (depth.IsValid()) depth.Dispose();
        size = wanted;
        precision = DepthPrecision;

        TextureImageFormat format = precision == ShadowDepthPrecision.Bits16 ? TextureImageFormat.Depth16f : TextureImageFormat.Depth24f;
        depth = new Texture2D((uint)size, (uint)size, false, format);

        // Sampled through hardware depth comparison: a sampler2DShadow then gets fixed function 2x2 PCF with
        // linear filtering instead of a manual compare per tap
        depth.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        depth.SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
        depth.SetDepthCompareMode(true);
        frameBuffer = Graphics.CreateFramebuffer([new GraphicsFrameBuffer.Attachment { Texture = depth.Handle, IsDepth = true }], (uint)size, (uint)size);

        Allocator.Reset(size);
        Generation++;
    }

    public static int GetSize() => size;

    /// <summary>The atlas depth texture the lighting shaders sample.</summary>
    public static Texture2D? DepthTexture => depth;

    internal static GraphicsFrameBuffer? FrameBuffer => frameBuffer;
}

public enum ShadowDepthPrecision
{
    Bits16,
    Bits24,
}

/// <summary>A square region of the shadow atlas, in texels.</summary>
public readonly record struct ShadowTile(int X, int Y, int Size)
{
    public bool IsValid => Size > 0;
}

/// <summary>
/// Hands out power of two squares of a power of two atlas. Each block splits into four when something smaller
/// is asked for, and a freed block merges back with its three siblings once they are all free, so space comes
/// back whole. Asked largest first, power of two squares fill the atlas with no waste at all.
/// </summary>
public sealed class ShadowTileAllocator
{
    public const int SmallestTile = 16;

    // Free blocks of each size, lowest position first so allocations pack toward one corner
    private readonly Dictionary<int, SortedSet<long>> _free = new();

    public int Size { get; private set; }
    public long UsedTexels { get; private set; }
    public long CapacityTexels => (long)Size * Size;

    public ShadowTileAllocator(int size) => Reset(size);

    public void Reset(int size)
    {
        Size = size;
        UsedTexels = 0;
        foreach (SortedSet<long> set in _free.Values) set.Clear();
        FreeSet(size).Add(Pack(0, 0));
    }

    /// <summary>The power of two tile a request of <paramref name="size"/> texels is rounded up to.</summary>
    public static int RoundUp(int size)
    {
        int s = SmallestTile;
        while (s < size) s <<= 1;
        return s;
    }

    public bool TryAllocate(int size, out ShadowTile tile)
    {
        size = RoundUp(size);
        int s = size;
        while (s <= Size && FreeSet(s).Count == 0) s <<= 1;
        if (s > Size)
        {
            tile = default;
            return false;
        }

        SortedSet<long> set = FreeSet(s);
        long key = set.Min;
        set.Remove(key);
        Unpack(key, out int x, out int y);
        while (s > size)
        {
            s >>= 1;
            SortedSet<long> half = FreeSet(s);
            half.Add(Pack(x + s, y));
            half.Add(Pack(x, y + s));
            half.Add(Pack(x + s, y + s));
        }

        UsedTexels += (long)size * size;
        tile = new ShadowTile(x, y, size);
        return true;
    }

    public void Free(ShadowTile tile)
    {
        if (!tile.IsValid) return;
        int x = tile.X, y = tile.Y, s = tile.Size;
        UsedTexels -= (long)s * s;

        while (s < Size)
        {
            int parent = s << 1;
            int px = x - x % parent, py = y - y % parent;
            SortedSet<long> set = FreeSet(s);
            bool siblingsFree = true;
            for (int i = 0; i < 4 && siblingsFree; i++)
            {
                int cx = px + (i & 1) * s, cy = py + (i >> 1) * s;
                if (cx == x && cy == y) continue;
                siblingsFree = set.Contains(Pack(cx, cy));
            }
            if (!siblingsFree) break;

            for (int i = 0; i < 4; i++)
            {
                int cx = px + (i & 1) * s, cy = py + (i >> 1) * s;
                if (cx != x || cy != y) set.Remove(Pack(cx, cy));
            }
            x = px;
            y = py;
            s = parent;
        }

        FreeSet(s).Add(Pack(x, y));
    }

    private SortedSet<long> FreeSet(int size)
    {
        if (!_free.TryGetValue(size, out SortedSet<long>? set))
            _free[size] = set = new SortedSet<long>();
        return set;
    }

    // Row major so the lowest key is the lowest row, then the leftmost block in it
    private static long Pack(int x, int y) => ((long)y << 32) | (uint)x;

    private static void Unpack(long key, out int x, out int y)
    {
        x = (int)(key & 0xFFFFFFFF);
        y = (int)(key >> 32);
    }
}
