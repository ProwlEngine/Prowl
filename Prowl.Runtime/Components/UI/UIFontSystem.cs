// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Runtime.Rendering;
using System;

using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Owns the Scribe <see cref="FontSystem"/> and its glyph atlas <see cref="Texture2D"/>
/// for the retained-mode UI framework. There is one instance per process - UI text from
/// every <see cref="GameCanvas"/> shares it (matching how most engines treat font
/// atlases).
/// </summary>
/// <remarks>
/// Implements <see cref="IFontRenderer"/>.
///
/// The retained-mode pipeline does NOT call <see cref="DrawQuads"/>; instead,
/// <see cref="TextComponent.GenerateMesh"/> walks <see cref="TextLayout"/> and emits
/// per-glyph quads via <see cref="UIMeshBuilder"/>, sampling the atlas texture in the
/// fragment shader.
///
/// Atlas growth: when Scribe runs out of room it allocates a new (larger) texture via
/// <see cref="CreateTexture"/>. The previous texture is released by Scribe; consumers
/// must re-read <see cref="Atlas"/> every frame and not cache the reference past a
/// possible re-allocation. <see cref="System"/>.<c>AtlasVersion</c> increments on any
/// mutation (new glyph or grow), giving consumers a cheap way to detect staleness.
/// </remarks>
internal sealed class UIFontSystem : IFontRenderer
{
    private static UIFontSystem? s_default;
    public static UIFontSystem Default => s_default ??= new UIFontSystem();

    /// <summary>The Scribe font system.</summary>
    public FontSystem System { get; }

    /// <summary>The current glyph atlas texture. May be replaced when the atlas grows;
    /// always read this freshly through <see cref="System"/>.</summary>
    public Texture2D Atlas => (Texture2D)System.Texture;



    private UIFontSystem(int initialWidth = 1024, int initialHeight = 1024)
    {
        // FontSystem will call back into our IFontRenderer.CreateTexture during construction
        // (because includeWhiteRect: true forces an immediate atlas allocation + write).
        System = new FontSystem(this, initialWidth, initialHeight, includeWhiteRect: true);
    }

    // ============================================================
    // IFontRenderer implementation
    // ============================================================

    /// <summary>Scribe asks for a fresh atlas backing texture. Called once at startup
    /// and again every time the atlas grows past <see cref="FontSystem"/>.<c>MaxAtlasSize</c>.
    /// </summary>
    public object CreateTexture(int width, int height)
    {
        var tex = new Texture2D((uint)width, (uint)height, false, PixelFormat.R8_G8_B8_A8_UNorm);

        // Bilinear filtering hides single-pixel jaggies on diagonal glyph strokes; clamp
        // wrapping prevents UV bleed between adjacent atlas cells when the GPU samples at
        // tile boundaries.
        tex.SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipPoint);
        tex.SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);

        return tex;
    }

    public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) return;

        int pixelCount = bounds.Width * bounds.Height;
        byte[] rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            byte a = i < data.Length ? data[i] : (byte)0;
            rgba[i * 4 + 0] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = a;
        }

        tex.SetData(new Memory<byte>(rgba), bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height);
    }

    /// <summary>
    /// Internal DrawQuads is not used- Instead <see cref="TextComponent"/> walks the
    /// <see cref="TextLayout"/> itself and emits quads into <see cref="UIMeshBuilder"/>.
    /// </summary>
    public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
    {
        // Intentionally empty.
    }
}
