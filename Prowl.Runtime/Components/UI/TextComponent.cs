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
/// through the standard UI pipeline (the <c>Default/DefaultText</c> shader sampling the
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

    /// <summary>When true, <see cref="Text"/> is parsed for rich-text tags (color, size, bold/italic,
    /// etc.). The assigned <see cref="Font"/> is used for every style variant.</summary>
    [SerializeField] private bool _richText;
    public bool RichTextEnabled
    {
        get => _richText;
        set => SetField(ref _richText, value, UIDirtyFlags.Vertices);
    }

    // ---- Material override ----
    [SerializeField] private AssetRef<Material> _material;
    public AssetRef<Material> Material
    {
        get => _material;
        set => SetField(ref _material, value, UIDirtyFlags.Material);
    }

    public override Material GetMaterial() => _material.Res ?? GameCanvas.SharedTextMaterial;

    /// <summary>
    /// Atlas version recorded at the last successful bake. When Scribe grows the atlas
    /// (e.g. a new glyph or pixel-size variant is introduced) all existing AtlasGlyph
    /// UVs are recomputed against the new texture dimensions, so any cached glyph mesh
    /// is now pointing at the wrong region. We catch that in <see cref="Update"/> and
    /// force a rebake.
    /// </summary>
    [SerializeIgnore] private int _lastAtlasVersion = -1;

    // Cached rich-text layout. Reused across frames so its animation start-time survives (a fresh
    // layout each rebuild would re-anchor to "now" and freeze animated effects). Rebuilt only when the
    // text or layout-affecting settings change.
    [SerializeIgnore] private RichTextLayout? _richLayout;
    [SerializeIgnore] private int _richSig;
    [SerializeIgnore] private bool _richAnimated;

    public override void Update()
    {
        int v = UIFontSystem.Default.System.AtlasVersion;
        if (v != _lastAtlasVersion)
        {
            _lastAtlasVersion = v;
            MarkDirty(UIDirtyFlags.Vertices);
        }

        // Animated rich-text effects (wave, shake, rainbow, typewriter, ...) are time-driven, so the
        // mesh has to be rebuilt every frame to advance them.
        if (_richText && _richAnimated)
            MarkDirty(UIDirtyFlags.Vertices);
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

        // Element-local pivot-centered space (matches GameCanvas.BuildItemModel), +Y up. The rect's
        // TOP-LEFT is at (-pivot.X * w, (1 - pivot.Y) * h); Scribe's layout Y grows downward, so the
        // capture bridge maps it as y -> baseY - y.
        float w = r.Size.X;
        float h = r.Size.Y;
        Float2 pivot = rt.Pivot;
        float originX = -pivot.X * w;
        float originY = (1f - pivot.Y) * h;

        Color tinted = TextColor * new Color(1f, 1f, 1f, context.Alpha);
        FontColor color = new FontColor(tinted.R, tinted.G, tinted.B, tinted.A);

        // Scribe generates the geometry (plain or rich-tag parsed) and drives DrawQuads; the capture
        // maps it into element-local space and appends it to the mesh. Vertical alignment isn't done
        // by Scribe (its alignment is horizontal-only), so we offset the whole box by the layout height.
        if (_richText)
        {
            RichTextLayoutSettings rich = new RichTextLayoutSettings
            {
                RegularFont = fontFile, BoldFont = fontFile, ItalicFont = fontFile,
                BoldItalicFont = fontFile, MonoFont = fontFile,
                PixelSize = pixelSize,
                Quality = _quality,
                MaxWidth = w,
                WrapMode = TextWrapMode.Wrap,
                Alignment = ToScribeAlignment(_alignment),
                DefaultColor = color,
            };
            int sig = HashCode.Combine(_text, pixelSize, (int)_quality, w, (int)_alignment, tinted, context.Alpha);
            if (_richLayout == null || sig != _richSig)
            {
                if (_richLayout == null) _richLayout = new RichTextLayout(_text, rich);
                else { _richLayout.SetSource(_text); _richLayout.SetSettings(rich); }
                _richLayout.Update(fs.System);
                _richAnimated = _richLayout.Effects.Count > 0;
                _richSig = sig;
            }

            float verticalOffset = ComputeVerticalOffset(_alignment, h, _richLayout.Size.Y);
            fs.BeginCapture(builder, originX, originY - verticalOffset);
            try { _richLayout.Draw(fs.System, fs, Float2.Zero, (double)Time.TimeSinceStartup); }
            finally { fs.EndCapture(); }
        }
        else
        {
            TextLayoutSettings settings = new TextLayoutSettings
            {
                Font          = fontFile,
                PixelSize     = pixelSize,
                Quality       = _quality,
                Alignment     = ToScribeAlignment(_alignment),
                MaxWidth      = w,
                WrapMode      = TextWrapMode.Wrap,
                LineHeight    = 1.0f,
                TabSize       = 4,
                LetterSpacing = 0f,
                WordSpacing   = 0f,
            };
            TextLayout layout = fs.System.CreateLayout(_text, settings);

            float verticalOffset = ComputeVerticalOffset(_alignment, h, layout.Size.Y);
            fs.BeginCapture(builder, originX, originY - verticalOffset);
            try { fs.System.DrawLayout(layout, Float2.Zero, color); }
            finally { fs.EndCapture(); }
        }
    }

    public override void PopulateProperties(PropertyState p, in UIContext _)
    {
        // Always re-read the atlas through System.Texture - it can be replaced when the
        // atlas grows. Falls back to a 1x1 white texture if Scribe hasn't allocated yet.
        Texture2D atlas = UIFontSystem.Default.Atlas ?? Texture2D.LoadDefault(DefaultTexture.White);
        p.SetTexture("_MainTex", atlas);
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
