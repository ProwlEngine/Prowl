// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using ScribeAlign = Prowl.Scribe.TextAlignment;
using TAlignment = Prowl.Runtime.UI.TextAlignment;

namespace Prowl.Runtime;

/// <summary>
/// Retained-mode text element. Produces one textured quad per glyph using Scribe's
/// <see cref="FontSystem"/> for shaping/atlas allocation; the resulting mesh is drawn
/// through the standard UI pipeline (the <c>Default/GameUI</c> shader sampling the
/// shared glyph atlas owned by <see cref="UIFontSystem"/>).
/// </summary>
[AddComponentMenu("UI/Text")]
[ComponentIcon("T")] // Text
public class TextComponent : UIBehaviour
{
    [SerializeField] private AssetRef<FontAsset> _font;
    public AssetRef<FontAsset> Font
    {
        get => _font;
        set => SetField(ref _font, value, UIDirtyFlags.Vertices | UIDirtyFlags.Material);
    }

    /// <summary>The assigned font, or the engine's embedded default font when none is set.</summary>
    public FontAsset? ResolvedFont => _font.Res ?? FontAsset.LoadDefault();

    [SerializeField] private Color _textColor = Color.White;
    public Color TextColor
    {
        get => _textColor;
        set => SetField(ref _textColor, value, UIDirtyFlags.Vertices);
    }

    [SerializeField] private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set => SetField(ref _text, value ?? string.Empty, UIDirtyFlags.Vertices);
    }

    [SerializeField] private TAlignment _alignment = TAlignment.CenterMiddle;
    public TAlignment Alignment
    {
        get => _alignment;
        set => SetField(ref _alignment, value, UIDirtyFlags.Vertices);
    }

    [SerializeField] private int _size = 20;
    public int Size
    {
        get => _size;
        set => SetField(ref _size, Maths.Max(1, value), UIDirtyFlags.Vertices);
    }

    /// <summary>Resolution the glyph SDF is rasterized at (see <see cref="FontQuality"/>). Higher
    /// quality keeps large text crisp at the cost of atlas space; the default is fine for body text.</summary>
    [SerializeField] private FontQuality _quality = FontQuality.Normal;
    public FontQuality Quality
    {
        get => _quality;
        set => SetField(ref _quality, value, UIDirtyFlags.Vertices | UIDirtyFlags.Material);
    }

    // ---- Material override ----
    [SerializeField] private AssetRef<Material> _material;
    public AssetRef<Material> Material
    {
        get => _material;
        set => SetField(ref _material, value, UIDirtyFlags.Material);
    }

    public override Material GetMaterial() => _material.Res ?? base.GetMaterial();

    /// <summary>Each TextComponent samples its font's glyph atlas; that atlas is unique
    /// per font / pixel size, so siblings can't share a draw call with non-text elements.</summary>
    public override bool RequiresPerElementMaterial => true;

    /// <summary>
    /// Atlas version recorded at the last successful bake. When Scribe grows the atlas
    /// (e.g. a new glyph or pixel-size variant is introduced) all existing AtlasGlyph
    /// UVs are recomputed against the new texture dimensions, so any cached glyph mesh
    /// is now pointing at the wrong region. We catch that in <see cref="Update"/> and
    /// force a rebake.
    /// </summary>
    [SerializeIgnore] private int _lastAtlasVersion = -1;

    public override void Update()
    {
        int v = UIFontSystem.Default.System.AtlasVersion;
        if (v != _lastAtlasVersion)
        {
            _lastAtlasVersion = v;
            MarkDirty(UIDirtyFlags.Vertices);
        }
    }

    // ============================================================
    // Mesh generation
    // ============================================================

    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context)
    {
        FontAsset? font = ResolvedFont;
        if (font?.FontFile is null || string.IsNullOrEmpty(Text)) return;

        var rt = GameObject.RectTransform;
        if (rt is null) return;
        Rect r = rt.ComputedRect;
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;

        UIFontSystem fs = UIFontSystem.Default;
        FontFile fontFile = font.FontFile;
        float pixelSize = Maths.Max(1, _size);

        // Ask Scribe to lay out the text. CreateLayout populates AtlasGlyph references
        // (calling GetOrCreateGlyph internally), which in turn pushes any not-yet-rasterized
        // glyphs into the atlas via UIFontSystem.UpdateTextureRegion.
        TextLayoutSettings settings = new TextLayoutSettings
        {
            Font          = fontFile,
            PixelSize     = pixelSize,
            Quality       = _quality,
            Alignment     = ToScribeAlignment(_alignment),
            MaxWidth      = r.Size.X,
            WrapMode      = TextWrapMode.Wrap,
            LineHeight    = 1.0f,
            TabSize       = 4,
            LetterSpacing = 0f,
            WordSpacing   = 0f,
        };

        TextLayout layout = fs.System.CreateLayout(_text, settings);
        layout.EnsureUpToDate(fs.System);

        // Element-local pivot-centered space (matches GameCanvas.BuildItemModel), +Y up.
        // Inside this space the rect's TOP-LEFT is at (-pivot.X * w, (1 - pivot.Y) * h);
        // Scribe's layout Y grows downward, so we subtract it from this origin.
        float w = r.Size.X;
        float h = r.Size.Y;
        Float2 pivot = rt.Pivot;
        float originX = -pivot.X * w;
        float originY = (1f - pivot.Y) * h;

        // Vertical alignment is not handled by Scribe (its TextAlignment is horizontal-only),
        // so we shift the whole layout box ourselves.
        float verticalOffset = ComputeVerticalOffset(_alignment, h, layout.Size.Y);

        Color tinted = TextColor * new Color(1f, 1f, 1f, context.Alpha);

        var lines = layout.Lines;
        for (int li = 0; li < lines.Count; li++)
        {
            Line line = lines[li];
            for (int gi = 0; gi < line.Glyphs.Count; gi++)
            {
                GlyphInstance inst = line.Glyphs[gi];
                AtlasGlyph ag = inst.Glyph;
                if (ag is null || !ag.IsInAtlas) continue;

                // The atlas glyph is resolution independent now: metrics come from the font at the
                // display size, and the quad is the glyph's padded distance-field region scaled to
                // that size (matching FontSystem.DrawLayout). Look metrics up by GLYPH INDEX, not
                // codepoint: shaped/substituted glyphs (ligatures, and anything through the shaper)
                // carry Codepoint == 0, which FindGlyphIndex maps to the .notdef glyph, so a
                // codepoint lookup returns null and every glyph gets skipped.
                GlyphMetrics m = fs.System.GetGlyphMetricsByIndex(ag.Font, ag.GlyphIndex, pixelSize) ?? default;
                if (m.Width <= 0 || m.Height <= 0) continue; // whitespace / invisible

                float sc = ag.Font.ScaleForPixelHeight(pixelSize);

                // Recover the pen origin (inst.Position bakes in the glyph offset), then place the
                // region. Scribe lays out downward, so its Y is subtracted from the origin (+Y up).
                // The atlas V axis runs top-to-bottom (V0 = region top), hence V1 maps to the quad's
                // bottom-left corner and V0 to its top-right.
                float penX = originX + line.Position.X + inst.Position.X - m.OffsetX;
                float penY = originY - verticalOffset - line.Position.Y - inst.Position.Y + m.OffsetY;
                float x0 = penX + (float)(ag.RegionX0 * sc);
                float x1 = penX + (float)(ag.RegionX1 * sc);
                float yTop = penY + (float)(ag.RegionY1 * sc);
                float yBottom = penY + (float)(ag.RegionY0 * sc);

                builder.AddQuad(
                    new Rect(x0, yBottom, x1, yTop),
                    tinted,
                    new Float2(ag.U0, ag.V1),
                    new Float2(ag.U1, ag.V0));
            }
        }
    }

    public override void PopulateProperties(PropertyState p, in UIContext _)
    {
        // Always re-read the atlas through System.Texture - it can be replaced when the
        // atlas grows. Falls back to a 1x1 white texture if Scribe hasn't allocated yet.
        Texture2D atlas = UIFontSystem.Default.Atlas ?? Texture2D.LoadDefault(DefaultTexture.White);
        p.SetTexture("_MainTex", atlas);
        p.SetFloat("_SdfText", 1.0f); // atlas is a single-channel SDF; the GameUI shader reconstructs coverage
        p.SetColor("_MainColor", Color.White);
        p.SetVector("_Tiling", new Float2(1, 1));
        p.SetVector("_Offset", Float2.Zero);
    }

    // ============================================================
    // Alignment helpers
    // ============================================================

    // TAlignment is a [Flags] enum with one bit per axis position, so both helpers test the
    // individual axis flags rather than switching on the nine named combinations - that way
    // every combination (including TopCenter / TopRight) resolves correctly.

    private static ScribeAlign ToScribeAlignment(TAlignment a)
    {
        if ((a & TAlignment.Right)  != 0) return ScribeAlign.Right;
        if ((a & TAlignment.Middle) != 0) return ScribeAlign.Center;
        return ScribeAlign.Left;
    }

    private static float ComputeVerticalOffset(TAlignment a, float boxHeight, float layoutHeight)
    {
        if ((a & TAlignment.Bottom) != 0) return Maths.Max(0f, boxHeight - layoutHeight);
        if ((a & TAlignment.Center) != 0) return Maths.Max(0f, (boxHeight - layoutHeight) * 0.5f);
        return 0f; // Top (default)
    }
}
