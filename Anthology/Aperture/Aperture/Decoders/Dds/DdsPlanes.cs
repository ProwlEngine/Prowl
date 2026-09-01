// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>One surface of a texture file: a level of a slice, and where its bytes lie.</summary>
internal readonly record struct DdsPlane(int MipLevel, int Slice, int Width, int Height,
                                         int Offset, int Length);

/// <summary>
/// Where every surface of a texture file sits: each slice or cube face carries a chain of levels,
/// and each level of a volume a stack that halves with it. Worked out once here so that decoding
/// and handing back the bytes as they lie cannot disagree.
/// </summary>
internal static class DdsPlanes
{
    /// <summary>
    /// Lists the surfaces in the order the file stores them, stopping at the first that runs past
    /// the end. A file cut short still describes the surfaces that are whole.
    /// </summary>
    public static List<DdsPlane> Enumerate(in DdsSurface surface, int available)
    {
        List<DdsPlane> planes = [];
        DdsSurface level = surface;
        int at = surface.DataOffset;

        for (int slice = 0; slice < surface.Slices; slice++)
        {
            for (int mip = 0; mip < surface.MipLevels; mip++)
            {
                level.Width = Math.Max(1, surface.Width >> mip);
                level.Height = Math.Max(1, surface.Height >> mip);

                // A volume thins out with its levels the same way its other two axes do, and all
                // of a level's slices are stored before the next level begins.
                int deep = Math.Max(1, surface.Depth >> mip);
                long size = level.SurfaceBytes;

                if (size is <= 0 or > int.MaxValue)
                    return planes;

                for (int z = 0; z < deep; z++)
                {
                    if (at + size > available)
                        return planes;

                    // A file is never both a cube map and a volume, so the one index says which
                    // face or which slice without ever having to say both.
                    planes.Add(new DdsPlane(mip, surface.Depth > 1 ? z : slice,
                                            level.Width, level.Height, at, (int)size));
                    at += (int)size;
                }
            }
        }

        return planes;
    }
}
