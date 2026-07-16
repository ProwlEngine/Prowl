// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

using Prowl.Graphite;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Loads the two precomputed SMAA lookup textures (AreaTex and SearchTex) from
/// embedded resources, caching one shared GPU texture each.
///
/// Both are stored as raw RGBA8 (the reference RG8/R8 data expanded into the R/G
/// channels: AreaTex = [R,G,0,255], SearchTex = [R,0,0,255]) in the <b>upstream</b>
/// row order. The reference data comes from https://github.com/iryoku/smaa
/// (Textures/AreaTex.h, Textures/SearchTex.h), MIT licensed.
///
/// Note: unlike turol/smaaDemo's raw-GL path (which vertically flips these textures
/// under RENDERER_OPENGL via flipSMAATextures), Prowl must NOT flip them - its blit /
/// render-target sampling already presents them in the upstream orientation. Flipping
/// them blends diagonals on the wrong side (verified against a diagonal line in
/// ProwlMapDemo: the flipped orientation produces dark fringing along diagonals).
///
/// Loaded raw rather than as PNG because <see cref="Texture2D.FromImage"/> forces
/// sRGB + a vertical flip, which would corrupt these data textures - the same
/// reason <see cref="BRDFLutGenerator"/> loads a raw .brdf.
/// </summary>
public static class SMAALookupTextures
{
    private const int AreaWidth = 160;
    private const int AreaHeight = 560;
    private const int SearchWidth = 64;
    private const int SearchHeight = 16;

    private static Texture2D? _areaTex;
    private static Texture2D? _searchTex;

    /// <summary>The SMAA precomputed area lookup texture (160x560, RGBA8).</summary>
    public static Texture2D AreaTex
    {
        get
        {
            if (_areaTex != null && _areaTex.IsValid())
                return _areaTex;
            _areaTex = Load("Assets/Defaults/SMAAAreaTex.bin", AreaWidth, AreaHeight, "SMAA Area LUT");
            return _areaTex;
        }
    }

    /// <summary>The SMAA precomputed search lookup texture (64x16, RGBA8).</summary>
    public static Texture2D SearchTex
    {
        get
        {
            if (_searchTex != null && _searchTex.IsValid())
                return _searchTex;
            _searchTex = Load("Assets/Defaults/SMAASearchTex.bin", SearchWidth, SearchHeight, "SMAA Search LUT");
            return _searchTex;
        }
    }

    private static Texture2D Load(string resourcePath, int width, int height, string name)
    {
        using Stream stream = EmbeddedResources.GetStream(resourcePath);

        int expectedSize = width * height * 4; // RGBA8
        byte[] pixels = new byte[expectedSize];

        int bytesRead = stream.Read(pixels, 0, expectedSize);
        if (bytesRead != expectedSize)
            throw new InvalidDataException($"SMAA lookup texture '{resourcePath}' size mismatch. Expected {expectedSize} bytes, got {bytesRead}.");

        var tex = new Texture2D((uint)width, (uint)height, false, PixelFormat.R8_G8_B8_A8_UNorm);
        tex.SetData<byte>(pixels);
        tex.SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipPoint);
        tex.SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
        tex.Name = name;
        return tex;
    }
}
