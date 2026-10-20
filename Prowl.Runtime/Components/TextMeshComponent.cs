// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Scribe;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;
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
public class TextMeshComponent : Component, IMaterialRenderer, IColorTint, IRenderSync
{
    int IMaterialRenderer.MaterialSlotCount => 1;
    Material? IMaterialRenderer.GetMaterial(int slot) { MaterialSlots.CheckSingle(slot); return Material; }
    void IMaterialRenderer.SetMaterial(int slot, Material? material) { MaterialSlots.CheckSingle(slot); Material = material; }
    Color IColorTint.Tint { get => TextColor; set => TextColor = value; }

    [SerializeField] private FontAsset? _font;
    public FontAsset? Font { get => _font; set => SetField(ref _font, value); }

    /// <summary>The assigned font, or the engine's embedded default font when none is set.</summary>
    public FontAsset? ResolvedFont
    {
        get
        {
            FontAsset? res = _font;
            return res.IsValid() ? res : FontAsset.LoadDefault();
        }
    }

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

    [SerializeField] private Material? _material;
    public Material? Material { get => _material; set => SetField(ref _material, value); }

    [SerializeField] private ShadowCasting _shadowCasting = ShadowCasting.Off;
    public ShadowCasting ShadowCasting { get => _shadowCasting; set => SetField(ref _shadowCasting, value); }

    // Shared world-space text material (Default/DefaultTextMesh - SDF, tagged Transparent so the scene
    // pipeline draws it). Distinct from the canvas text material, which is UI-tagged and only drawn by
    // the canvas path. Lazy-allocated and reused across every TextMeshComponent that doesn't override it.
    private static Material? s_sharedMaterial;
    private static Material SharedMaterial
    {
        get
        {
            if (s_sharedMaterial.IsNotValid())
                s_sharedMaterial = new Material(Shader.LoadDefault(DefaultShader.DefaultTextMesh));
            return s_sharedMaterial;
        }
    }

    private Material ResolveMaterial()
    {
        Material? res = _material;
        return res.IsValid() ? res : SharedMaterial;
    }

    [SerializeIgnore] private Mesh? _mesh;
    [SerializeIgnore] private bool _dirty = true;
    [SerializeIgnore] private bool _hasGeometry;
    [SerializeIgnore] private int _lastAtlasVersion = -1;
    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private readonly OwnedMaterial _ownMaterial = new();
    [System.NonSerialized, NotHeld] private readonly GMaterial[] _gpuMaterials = new GMaterial[1];

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    private void SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _dirty = true;
    }

    /// <summary>Force the cached mesh to rebuild on the next sync (e.g. after editing the font asset).</summary>
    public void MarkDirty() => _dirty = true;

    public override void OnDisable()
    {
        if (_mesh.IsValid()) _mesh.Dispose();
        _mesh = null;
        _hasGeometry = false;
        _dirty = true;
    }

    bool IRenderSync.Sync(RenderWorld world)
    {
        // The shared atlas can grow (new glyph or pixel size), which shifts every glyph's UVs, so that rebuilds too
        int atlasVersion = unchecked(UIFontSystem.Default.System.AtlasVersion * 31 + (_font is FontAsset f ? f.ContentVersion : 0));
        if (_dirty || _mesh.IsNotValid() || atlasVersion != _lastAtlasVersion)
        {
            RebuildMesh();
            _lastAtlasVersion = atlasVersion;
            _dirty = false;
        }

        Mesh? mesh = _mesh;
        Material material = ResolveMaterial();
        GMaterial? gpu = _hasGeometry && mesh.IsValid() && material.IsLoaded ? _ownMaterial.Of(material.GpuMaterial) : null;
        if (gpu == null)
        {
            if (material.State == AssetState.Unloaded) _ = material.LoadAsync();
            Detach(world);
            return false;
        }

        Texture2D atlasTex = UIFontSystem.Default.Atlas;
        _ownMaterial.SetTexture("_MainTex", atlasTex.IsValid() ? atlasTex : Texture2D.LoadDefault(DefaultTexture.White));
        _ownMaterial.SetColor("_MainColor", Color.White);
        _ownMaterial.SetVector("_Tiling", new Float2(1, 1));
        _ownMaterial.SetVector("_Offset", Float2.Zero);
        _gpuMaterials[0] = gpu;

        Float4x4 matrix = Transform.LocalToWorldMatrix;
        _renderable.Sync(world, new RenderableState.Desc
        {
            Mesh = mesh.GpuIfLoaded,
            Materials = _gpuMaterials,
            Transform = (Double4x4)matrix,
            TransformVersion = Transform.WorldVersion,
            Flags = RenderableState.FlagsFor(GameObject, false, false) | ShadowCasting.ToFlags(),
            LayerMask = RenderableState.LayerBit(GameObject.LayerIndex),
            ObjectId = (uint)InstanceID,
        });
        return false;
    }

    void IRenderSync.Detach(RenderWorld world) => Detach(world);

    private void Detach(RenderWorld world)
    {
        _renderable.Detach(world);
        _ownMaterial.Release();
    }

    // ============================================================
    // Mesh generation
    // ============================================================

    private void RebuildMesh()
    {
        _hasGeometry = false;

        FontAsset? font = ResolvedFont;
        if (font.IsNotValid() || font.FontFile is null || string.IsNullOrEmpty(_text)) return;

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
            float layoutWidth = layout.Size.X;
            float layoutHeight = layout.Size.Y;

            (float ox, float oy) = AnchorOrigin(_anchor, wrap ? _maxWidth : layoutWidth, layoutHeight);
            fs.BeginCapture(builder, ox, oy, scale);
            try { fs.System.DrawLayout(layout, Float2.Zero, color); }
            finally { fs.EndCapture(); }

            if (builder.IsEmpty) return;

            if (_mesh.IsNotValid()) _mesh = new Mesh();
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
