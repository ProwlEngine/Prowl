// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Scribe;
using Prowl.Vector;

using ScribeAlign = Prowl.Scribe.TextAlignment;
using TAlignment = Prowl.Runtime.UI.TextAlignment;

namespace Prowl.Runtime;

/// <summary>
/// Draws text as a mesh in arbitrary 3D world space, independent of any <see cref="GameCanvas"/>.
/// The glyph geometry is generated once by Scribe (shared atlas via <see cref="UIFontSystem"/>),
/// baked into a cached <see cref="Mesh"/>, and drawn through the <c>Default/DefaultTextMesh</c> SDF
/// shader - so it stays crisp at any distance without the per-frame cost of a canvas.
///
/// The text is anchored around the transform origin (see <see cref="Anchor"/>), so a centered
/// anchor puts the middle of the text on the object's position. To make a camera-facing nameplate,
/// rotate the transform to face the camera yourself (billboard); nothing here forces an orientation.
/// </summary>
[AddComponentMenu("Rendering/Text Mesh")]
[ComponentIcon("T")] // Text
public class TextMeshComponent : MonoBehaviour
{
    [SerializeField] private AssetRef<FontAsset> _font;
    public AssetRef<FontAsset> Font { get => _font; set => SetField(ref _font, value); }

    /// <summary>The assigned font, or the engine's embedded default font when none is set.</summary>
    public FontAsset? ResolvedFont => _font.Res ?? FontAsset.LoadDefault();

    [SerializeField] private string _text = string.Empty;
    public string Text { get => _text; set => SetField(ref _text, value ?? string.Empty); }

    [SerializeField] private Color _textColor = Color.White;
    public Color TextColor { get => _textColor; set => SetField(ref _textColor, value); }

    /// <summary>Resolution the glyph SDF is rasterized at. Higher quality keeps large or close-up
    /// text crisp at the cost of atlas space.</summary>
    [SerializeField] private FontQuality _quality = FontQuality.Normal;
    public FontQuality Quality { get => _quality; set => SetField(ref _quality, value); }

    /// <summary>Pixel size the layout is shaped at. Combined with <see cref="PixelsPerUnit"/> this
    /// sets the world-space text height (height in units ~= <c>Size / PixelsPerUnit</c>).</summary>
    [SerializeField] private int _size = 32;
    public int Size { get => _size; set => SetField(ref _size, Maths.Max(1, value)); }

    /// <summary>How many layout pixels map to one world unit. Larger values make the text smaller.</summary>
    [SerializeField] private float _pixelsPerUnit = 100f;
    public float PixelsPerUnit { get => _pixelsPerUnit; set => SetField(ref _pixelsPerUnit, Maths.Max(0.0001f, value)); }

    /// <summary>Optional wrap/box width in layout pixels. <c>0</c> leaves the text un-wrapped; a
    /// positive value wraps to that width and anchors the block by that box.</summary>
    [SerializeField] private float _maxWidth;
    public float MaxWidth { get => _maxWidth; set => SetField(ref _maxWidth, Maths.Max(0f, value)); }

    /// <summary>Where the text block sits relative to the transform origin. The horizontal component
    /// also drives multi-line justification. <see cref="TAlignment.CenterMiddle"/> centers the block
    /// on the object's position.</summary>
    [SerializeField] private TAlignment _anchor = TAlignment.CenterMiddle;
    public TAlignment Anchor { get => _anchor; set => SetField(ref _anchor, value); }

    /// <summary>When true, <see cref="Text"/> is parsed for rich-text tags (color, size, bold/italic,
    /// etc.). The assigned <see cref="Font"/> is used for every style variant.</summary>
    [SerializeField] private bool _richText;
    public bool RichTextEnabled { get => _richText; set => SetField(ref _richText, value); }

    [SerializeField] private AssetRef<Material> _material;
    public AssetRef<Material> Material { get => _material; set => SetField(ref _material, value); }

    // Shared world-space text material (Default/DefaultTextMesh - SDF, tagged Transparent so the scene
    // pipeline draws it). Distinct from the canvas text material, which is UI-tagged and only drawn by
    // the canvas path. Lazy-allocated and reused across every TextMeshComponent that doesn't override it.
    private static Material? s_sharedMaterial;
    private static Material SharedMaterial => s_sharedMaterial ??= new Material(Shader.LoadDefault(DefaultShader.DefaultTextMesh));

    private Material ResolveMaterial() => _material.Res ?? SharedMaterial;

    [SerializeIgnore] private Mesh? _mesh;
    [SerializeIgnore] private PropertyState? _props;
    [SerializeIgnore] private bool _dirty = true;
    [SerializeIgnore] private bool _hasGeometry;
    [SerializeIgnore] private int _lastAtlasVersion = -1;

    private void SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _dirty = true;
    }

    /// <summary>Force the cached mesh to rebuild on the next collect (e.g. after editing the font asset).</summary>
    public void MarkDirty() => _dirty = true;

    public override void OnDisable()
    {
        _mesh?.OnDispose();
        _mesh = null;
        _hasGeometry = false;
        _dirty = true;
    }

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        // The shared atlas can grow (new glyph / pixel-size), which shifts every glyph's UVs; rebuild
        // when that happens, when a property changed, or when the mesh hasn't been built yet.
        int atlasVersion = UIFontSystem.Default.System.AtlasVersion;
        if (_dirty || _mesh == null || atlasVersion != _lastAtlasVersion)
        {
            RebuildMesh();
            _lastAtlasVersion = atlasVersion;
            _dirty = false;
        }

        if (!_hasGeometry || _mesh == null) return;

        Material mat = ResolveMaterial();
        if (mat == null) return;

        _props ??= new PropertyState();
        _props.Clear();
        Texture2D atlas = UIFontSystem.Default.Atlas ?? Texture2D.LoadDefault(DefaultTexture.White);
        _props.SetTexture("_MainTex", atlas);
        _props.SetColor("_MainColor", Color.White);
        _props.SetVector("_Tiling", new Float2(1, 1));
        _props.SetVector("_Offset", Float2.Zero);
        _props.SetInt("_ObjectID", InstanceID);

        renderables.Add(new MeshRenderable(_mesh, mat, Transform.LocalToWorldMatrix, GameObject.LayerIndex, _props));
    }

    // ============================================================
    // Mesh generation
    // ============================================================

    private void RebuildMesh()
    {
        _hasGeometry = false;

        FontAsset? font = ResolvedFont;
        if (font?.FontFile is null || string.IsNullOrEmpty(_text)) return;

        UIFontSystem fs = UIFontSystem.Default;
        FontFile fontFile = font.FontFile;
        float pixelSize = Maths.Max(1, _size);
        float scale = 1f / Maths.Max(0.0001f, _pixelsPerUnit);
        FontColor color = new FontColor(_textColor.R, _textColor.G, _textColor.B, _textColor.A);
        ScribeAlign align = ToScribeAlignment(_anchor);
        bool wrap = _maxWidth > 0f;

        UIMeshBuilder builder = UIMeshBuilder.Rent();
        try
        {
            float layoutWidth, layoutHeight;

            if (_richText)
            {
                RichTextLayoutSettings settings = new RichTextLayoutSettings
                {
                    RegularFont = fontFile, BoldFont = fontFile, ItalicFont = fontFile,
                    BoldItalicFont = fontFile, MonoFont = fontFile,
                    PixelSize = pixelSize,
                    Quality = _quality,
                    MaxWidth = _maxWidth,
                    WrapMode = wrap ? TextWrapMode.Wrap : TextWrapMode.NoWrap,
                    Alignment = align,
                    DefaultColor = color,
                };
                RichTextLayout layout = new RichTextLayout(_text, settings);
                layout.Update(fs.System);
                layoutWidth = layout.Size.X;
                layoutHeight = layout.Size.Y;

                (float ox, float oy) = AnchorOrigin(_anchor, wrap ? _maxWidth : layoutWidth, layoutHeight);
                fs.BeginCapture(builder, ox, oy, scale);
                try { layout.Draw(fs.System, fs, Float2.Zero, 0.0); }
                finally { fs.EndCapture(); }
            }
            else
            {
                TextLayoutSettings settings = new TextLayoutSettings
                {
                    Font          = fontFile,
                    PixelSize     = pixelSize,
                    Quality       = _quality,
                    Alignment     = align,
                    MaxWidth      = _maxWidth,
                    WrapMode      = wrap ? TextWrapMode.Wrap : TextWrapMode.NoWrap,
                    LineHeight    = 1.0f,
                    TabSize       = 4,
                    LetterSpacing = 0f,
                    WordSpacing   = 0f,
                };
                TextLayout layout = fs.System.CreateLayout(_text, settings);
                layoutWidth = layout.Size.X;
                layoutHeight = layout.Size.Y;

                (float ox, float oy) = AnchorOrigin(_anchor, wrap ? _maxWidth : layoutWidth, layoutHeight);
                fs.BeginCapture(builder, ox, oy, scale);
                try { fs.System.DrawLayout(layout, Float2.Zero, color); }
                finally { fs.EndCapture(); }
            }

            if (builder.IsEmpty) return;

            _mesh ??= new Mesh();
            builder.Bake(_mesh);
            _hasGeometry = true;
        }
        finally
        {
            UIMeshBuilder.Return(builder);
        }
    }

    // ============================================================
    // Anchor / alignment helpers
    // ============================================================

    private static ScribeAlign ToScribeAlignment(TAlignment a)
    {
        if ((a & TAlignment.Right)  != 0) return ScribeAlign.Right;
        if ((a & TAlignment.Middle) != 0) return ScribeAlign.Center;
        return ScribeAlign.Left;
    }

    // Returns the capture (originX, baseY) that maps the anchor point of a (boxWidth x boxHeight)
    // block - laid out from its top-left in +Y-down space - onto the transform origin (0,0). The
    // capture applies x -> (originX + x), y -> (baseY - y), so originX cancels the anchor column and
    // baseY the anchor row (see UIFontSystem.BeginCapture).
    private static (float originX, float baseY) AnchorOrigin(TAlignment a, float boxWidth, float boxHeight)
    {
        float anchorX = (a & TAlignment.Right)  != 0 ? boxWidth  : (a & TAlignment.Middle) != 0 ? boxWidth  * 0.5f : 0f;
        float anchorY = (a & TAlignment.Bottom) != 0 ? boxHeight : (a & TAlignment.Center) != 0 ? boxHeight * 0.5f : 0f;
        return (-anchorX, anchorY);
    }
}
