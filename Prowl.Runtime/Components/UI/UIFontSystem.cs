// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
        var tex = new Texture2D((uint)width, (uint)height, false, TextureImageFormat.Color4b);

        // Bilinear filtering hides single-pixel jaggies on diagonal glyph strokes; clamp
        // wrapping prevents UV bleed between adjacent atlas cells when the GPU samples at
        // tile boundaries.
        Graphics.SetTextureFilters(tex.Handle, TextureMin.Linear, TextureMag.Linear);
        Graphics.SetWrapS(tex.Handle, TextureWrap.ClampToEdge);
        Graphics.SetWrapT(tex.Handle, TextureWrap.ClampToEdge);

        return tex;
    }

    public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) return;

        // Scribe hands us RGBA data (4 bytes/pixel - the single-channel SDF replicated across the
        // channels, matching the Color4b atlas), so write it straight in like the Quill PaperRenderer
        // does. The old per-pixel repack treated `data` as one byte/pixel and read only the first
        // quarter of it, scattering garbage into the alpha channel - which is why nothing rendered.
        tex.SetData(new Memory<byte>(data), bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height);
    }

    // ---- Mesh capture -------------------------------------------------------------------------
    // A text component sets a capture target + coordinate mapping, calls Scribe's DrawLayout /
    // RichTextLayout.Draw (which drive DrawQuads below), then clears it. Single-threaded UI build,
    // so a single set of fields is fine.
    private UIMeshBuilder? _target;
    private float _originX;
    private float _baseY;
    private float _scale = 1f;

    /// <summary>
    /// Route the next <see cref="System"/>.<c>DrawLayout</c> / rich-text draw into <paramref name="builder"/>,
    /// mapping Scribe's +Y-down layout space (origin passed as (0,0)) into element-local +Y-up space:
    /// <c>x -> (originX + x) * scale</c>, <c>y -> (baseY - y) * scale</c>. UI text uses the default
    /// unit scale; the 3D <c>TextMeshComponent</c> passes a world-units-per-pixel scale so the same
    /// pixel-space layout bakes into a world-space mesh. Always pair with <see cref="EndCapture"/>.
    /// </summary>
    public void BeginCapture(UIMeshBuilder builder, float originX, float baseY, float scale = 1f)
    {
        _target = builder;
        _originX = originX;
        _baseY = baseY;
        _scale = scale;
    }

    public void EndCapture()
    {
        _target = null;
        _scale = 1f;
    }

    /// <summary>
    /// Scribe's draw callback: append its generated glyph triangles to the active capture target,
    /// transformed into element-local space, keeping per-vertex colour (so rich text works).
    /// </summary>
    public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices)
    {
        UIMeshBuilder? b = _target;
        if (b is null) return;

        uint baseIdx = b.NextVertex;
        for (int i = 0; i < vertices.Length; i++)
        {
            IFontRenderer.Vertex v = vertices[i];
            b.AddVertex(
                new Float3((_originX + v.Position.X) * _scale, (_baseY - v.Position.Y) * _scale, 0f),
                v.TextureCoordinate,
                Color32.FromArgb(v.Color.A, v.Color.R, v.Color.G, v.Color.B));
        }
        for (int i = 0; i < indices.Length; i++)
            b.AddIndex(baseIdx + (uint)indices[i]);
    }
}
