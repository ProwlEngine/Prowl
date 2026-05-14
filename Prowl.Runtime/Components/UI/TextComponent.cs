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
    [SerializeField] private FontAsset _font = null!;
    public FontAsset Font
    {
        get => _font;
        set => SetField(ref _font, value, UIDirtyFlags.Vertices | UIDirtyFlags.Material);
    }

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

    // ---- Material override ----
    [SerializeField] private Material? _material;
    public Material? Material
    {
        get => _material;
        set => SetField(ref _material, value, UIDirtyFlags.Material);
    }

    public override Material GetMaterial() => _material ?? base.GetMaterial();

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
        if (Font?.FontFile is null || string.IsNullOrEmpty(Text)) return;

        var rt = GameObject.RectTransform;
        if (rt is null) return;
        Rect r = rt.ComputedRect;
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;

        UIFontSystem fs = UIFontSystem.Default;
        FontFile fontFile = Font.FontFile;
        float pixelSize = Maths.Max(1, _size);

        // Ask Scribe to lay out the text. CreateLayout populates AtlasGlyph references
        // (calling GetOrCreateGlyph internally), which in turn pushes any not-yet-rasterized
        // glyphs into the atlas via UIFontSystem.UpdateTextureRegion.
        TextLayoutSettings settings = new TextLayoutSettings
        {
            Font          = fontFile,
            PixelSize     = pixelSize,
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

        // Element-local pivot-centered space (matches GameCanvas.BuildItemModel).
        // Inside this space the rect's TOP-LEFT is at (-pivot.X * w, -pivot.Y * h);
        // we lay glyphs out relative to that point.
        float w = r.Size.X;
        float h = r.Size.Y;
        Float2 pivot = rt.Pivot;
        float originX = -pivot.X * w;
        float originY = -pivot.Y * h;

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

                GlyphMetrics m = ag.Metrics;
                if (m.Width <= 0 || m.Height <= 0) continue; // whitespace / invisible

                // Glyph quad in canvas-design pixels, top-left origin.
                float x0 = originX + line.Position.X + inst.Position.X;
                float y0 = originY + verticalOffset + line.Position.Y + inst.Position.Y;
                float x1 = x0 + m.Width;
                float y1 = y0 + m.Height;

                builder.AddQuad(
                    new Rect(x0, y0, x1, y1),
                    tinted,
                    new Float2(ag.U0, ag.V0),
                    new Float2(ag.U1, ag.V1));
            }
        }
    }

    public override void PopulateProperties(PropertyState p, in UIContext _)
    {
        // Always re-read the atlas through System.Texture — it can be replaced when the
        // atlas grows. Falls back to a 1×1 white texture if Scribe hasn't allocated yet.
        Texture2D atlas = UIFontSystem.Default.Atlas ?? Texture2D.LoadDefault(DefaultTexture.White);
        p.SetTexture("_MainTex", atlas);
        p.SetColor("_MainColor", Color.White);
        p.SetVector("_Tiling", new Float2(1, 1));
        p.SetVector("_Offset", Float2.Zero);
    }

    // ============================================================
    // Alignment helpers
    // ============================================================

    private static ScribeAlign ToScribeAlignment(TAlignment a) => a switch
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
