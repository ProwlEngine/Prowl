// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

using System;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>How a <see cref="UIImage"/> stretches its texture across the rect.</summary>
public enum ImageType
{
    /// <summary>The sprite is stretched to fit the whole rect.</summary>
    Simple,
    /// <summary>The sprite's four border regions stay unstretched; only the middle scales (nine-slice).</summary>
    Sliced,
    /// <summary>The sprite repeats across the rect at its native pixel size.</summary>
    Tiled,
    /// <summary>Only a portion of the texture is drawn, controlled by <see cref="UIImage.FillAmount"/>.</summary>
    Filled,
}

/// <summary>Geometry pattern used by <see cref="ImageType.Filled"/>. Origin values map per-method (see FillOrigin*).</summary>
public enum FillMethod
{
    /// <summary>Linear wipe left to right. Origin: 0=Left, 1=Right.</summary>
    Horizontal,
    /// <summary>Linear wipe bottom to top. Origin: 0=Bottom, 1=Top.</summary>
    Vertical,
    /// <summary>Quarter-circle wipe from a corner. Origin: 0=BL, 1=TL, 2=TR, 3=BR.</summary>
    Radial90,
    /// <summary>Semicircle wipe from an edge midpoint. Origin: 0=Bottom, 1=Left, 2=Top, 3=Right.</summary>
    Radial180,
    /// <summary>Full circle wipe from the rect center. Origin: 0=Bottom, 1=Right, 2=Top, 3=Left.</summary>
    Radial360,
}

/// <summary>
/// Displays a <see cref="Sprite"/> in the UI, drawn Simple / Sliced (nine-slice) / Tiled / Filled.
/// With no sprite assigned it draws nothing but still hit-tests as a raycast target.
/// </summary>
/// <remarks>
/// Expects the parent GameObject to have a <see cref="RectTransform"/>.
/// The image fills the rect computed by the <see cref="RectTransform"/>.
/// Alpha from the parent <see cref="CanvasGroup"/> is multiplied into <see cref="Color"/>.
/// </remarks>
public class UIImage : UIBehaviour
{
    [SerializeIgnore] private static Texture2D _defaultTexture;
    public static Texture2D defaultTexture
    {
        get
        {
            if (_defaultTexture != null) return _defaultTexture;
            //Texture2D.LoadDefault(DefaultTexture.White)
            var tex = new Texture2D(1, 1);
            tex.SetData(new System.Memory<byte>(new byte[] { 255, 255, 255, 255 }), 0,0,1,1);
            _defaultTexture = tex;
            return _defaultTexture;
        }
    }

    /// <summary>
    /// The sprite to display. This is the image's only source: its texture is bound, its atlas sub-rect drives
    /// the UVs, and its 9-slice border feeds <see cref="ImageType.Sliced"/>. With no sprite the image draws
    /// nothing but still hit-tests as a <see cref="RaycastTarget"/>. New images default to the built-in UI panel.
    /// </summary>
    [SerializeField] private AssetRef<Sprite> _sprite = new(BuiltInAssets.GuidFor(DefaultSprite.UIPanel));
    public AssetRef<Sprite> Sprite
    {
        get => _sprite;
        set => SetField(ref _sprite, value, UIDirtyFlags.Material | UIDirtyFlags.Vertices);
    }

    /// <summary>The resolved sprite, or null when none is assigned.</summary>
    private Sprite? Spr => _sprite.Res;

    /// <summary>The source texture bound for drawing: the sprite's texture, or null when no sprite is set.</summary>
    private Texture2D? SourceTexture => Spr?.Texture.Res;

    /// <summary>9-slice border in source pixels, taken from the sprite (zero when no sprite is set).</summary>
    private Float4 EffectiveBorder => Spr is Sprite s ? s.Border : Float4.Zero;

    /// <summary>Source-region size in pixels used for UV/border/tile math: the sprite's rect (zero when none).</summary>
    private (float w, float h) SourceSize => Spr is Sprite s ? (s.Rect.Width, s.Rect.Height) : (0f, 0f);

    // Sets the builder's UV remap to the sprite's atlas sub-rect (identity when the sprite covers the whole texture).
    private void ApplySpriteUVRect(UIMeshBuilder b)
    {
        if (Spr is Sprite s && s.Texture.Res is Texture2D st && st.Width > 0 && st.Height > 0)
        {
            float u0 = s.Rect.X / (float)st.Width, u1 = s.Rect.MaxX / (float)st.Width;
            float v0 = s.Rect.Y / (float)st.Height, v1 = s.Rect.MaxY / (float)st.Height;
            b.SetUVRect(new Float2(u0, v0), new Float2(u1 - u0, v1 - v0));
        }
        else
        {
            b.SetUVRect(Float2.Zero, Float2.One);
        }
    }

    // ---- Material override ----
    [SerializeField] private AssetRef<Material> _material;
    public AssetRef<Material> Material
    {
        get => _material;
        set => SetField(ref _material, value, UIDirtyFlags.Material);
    }

    /// <summary>The tint color of the image. Alpha is modulated by the parent <see cref="CanvasGroup"/>.</summary>
    [SerializeField] private Color _color = Color.White;
    public Color Color
    {
        get => _color;
        set => SetField(ref _color, value, UIDirtyFlags.Vertices);
    }

    /// <summary>Whether the image should preserve the source texture's aspect ratio.</summary>
    [SerializeField] private bool _preserveAspect;
    public bool PreserveAspect
    {
        get => _preserveAspect;
        set => SetField(ref _preserveAspect, value, UIDirtyFlags.Vertices);
    }

    /// <summary>How the texture is mapped to the rect. See <see cref="ImageType"/>. Defaults to Sliced so the
    /// default nine-slice panel renders correctly; for a zero-border sprite/texture this is identical to Simple.</summary>
    [SerializeField] private ImageType _type = ImageType.Sliced;
    public ImageType Type
    {
        get => _type;
        set => SetField(ref _type, value, UIDirtyFlags.Vertices);
    }

    /// <summary>
    /// Multiplies the sprite's nine-slice border and tile size on screen (affects Sliced/Tiled only).
    /// 1 = the sprite's native pixels. This is purely a display multiplier - UI sizing comes from the
    /// RectTransform, not from pixels-per-unit (that's a world-space concept for SpriteRenderer).
    /// </summary>
    [SerializeField] private float _pixelsPerUnitMultiplier = 1f;
    public float PixelsPerUnitMultiplier
    {
        get => _pixelsPerUnitMultiplier;
        set => SetField(ref _pixelsPerUnitMultiplier, MathF.Max(0.0001f, value), UIDirtyFlags.Vertices);
    }

    /// <summary>Fill geometry used when <see cref="Type"/> is <see cref="ImageType.Filled"/>.</summary>
    [SerializeField] private FillMethod _fillMethod = FillMethod.Radial360;
    public FillMethod FillMethod
    {
        get => _fillMethod;
        set => SetField(ref _fillMethod, value, UIDirtyFlags.Vertices);
    }

    /// <summary>Origin selector for <see cref="ImageType.Filled"/>; meaning depends on <see cref="FillMethod"/>.</summary>
    [SerializeField] private int _fillOrigin;
    public int FillOrigin
    {
        get => _fillOrigin;
        set => SetField(ref _fillOrigin, value, UIDirtyFlags.Vertices);
    }

    /// <summary>How much of the rect to draw in <see cref="ImageType.Filled"/> mode (0..1).</summary>
    [SerializeField] private float _fillAmount = 1f;
    public float FillAmount
    {
        get => _fillAmount;
        set => SetField(ref _fillAmount, Maths.Clamp(value, 0f, 1f), UIDirtyFlags.Vertices);
    }

    /// <summary>For radial <see cref="FillMethod"/>s, sweep clockwise from the origin instead of the default counter-clockwise.</summary>
    [SerializeField] private bool _fillClockwise;
    public bool FillClockwise
    {
        get => _fillClockwise;
        set => SetField(ref _fillClockwise, value, UIDirtyFlags.Vertices);
    }

    /// <summary>
    /// Whether this element should block raycasts (pointer hit-testing).
    /// Affects input dispatch only - does not change rendering.
    /// </summary>
    [SerializeField] private bool _raycastTarget = true;
    public bool RaycastTarget
    {
        get => _raycastTarget;
        set => SetField(ref _raycastTarget, value, UIDirtyFlags.Hierarchy);
    }

    public override Material GetMaterial() => _material.Res ?? base.GetMaterial();

    public override void GenerateMesh(UIMeshBuilder b, in UIContext ctx)
    {
        var rt = GameObject.RectTransform;
        if (rt is null) return;
        Rect r = rt.ComputedRect;
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;

        // Nothing to draw without a sprite - the element is still a raycast target, just invisible.
        if (SourceTexture is null) return;

        // Remap generated UVs onto the sprite's atlas sub-rect (identity when it covers the whole texture).
        ApplySpriteUVRect(b);

        // Emit vertices in element-local pixel space, with the pivot at the origin.
        // GameCanvas.BuildItemModel translates this pivot to its absolute design-pixel
        // position and applies any LocalRotation / LocalScale around it.
        float w = r.Size.X;
        float h = r.Size.Y;
        Float2 pivot = rt.Pivot;
        Rect local = new Rect(
            -pivot.X * w,
            -pivot.Y * h,
            (1f - pivot.X) * w,
            (1f - pivot.Y) * h);

        // Fit the source aspect inside the rect (letterbox), centered, when requested.
        if (_preserveAspect)
        {
            var (sw, sh) = SourceSize;
            local = FitAspect(local, sw, sh);
        }

        Color tinted = Color * new Color(1, 1, 1, ctx.Alpha);

        switch (_type)
        {
            case ImageType.Sliced:
                EmitSliced(b, local, tinted);
                break;
            case ImageType.Tiled:
                EmitTiled(b, local, tinted);
                break;
            case ImageType.Filled:
                b.AddFilled(local, tinted, _fillMethod, _fillOrigin, _fillAmount, _fillClockwise);
                break;
            default:
                // UV (0,0) at the rect's bottom-left, (1,1) at its top-right (+Y up).
                b.AddQuad(local, tinted, Float2.Zero, Float2.One);
                break;
        }
    }

    private void EmitSliced(UIMeshBuilder b, Rect local, Color tinted)
    {
        // Borders are in source pixels, scaled by the display multiplier and drawn in the RectTransform's
        // pixel space.
        float m = _pixelsPerUnitMultiplier > 0 ? _pixelsPerUnitMultiplier : 1f;
        Float4 border = EffectiveBorder;
        float bl = border.X * m, bt = border.Y * m, br = border.Z * m, bb = border.W * m;

        // When the rect is too small to fit the borders, scale ALL of them by a single factor rather than
        // clamping each axis on its own. Independent clamping squishes a square corner into a rectangle
        // (a thin element keeps full-height side corners but tiny top/bottom ones); a uniform scale keeps
        // every corner at its native aspect - square corners stay square - while still guaranteeing
        // opposite borders never overlap (which would invert the center quad).
        float w = local.Size.X, h = local.Size.Y;
        float scale = 1f;
        if (bl + br > w && bl + br > 0f) scale = MathF.Min(scale, w / (bl + br));
        if (bt + bb > h && bt + bb > 0f) scale = MathF.Min(scale, h / (bt + bb));
        if (scale < 1f) { bl *= scale; br *= scale; bt *= scale; bb *= scale; }

        Float4 uv = ComputeUVBorder();
        // Clamp opposite UV borders the same way as the pixel borders: if left+right (or top+bottom)
        // exceed the full 0..1 range, the center slice's UV would invert and sample garbage. Scale each
        // overflowing pair down proportionally so the borders just meet. (uv is L, T, R, B.)
        float uh = uv.X + uv.Z;
        if (uh > 1f) { uv.X /= uh; uv.Z /= uh; }
        float uvv = uv.Y + uv.W;
        if (uvv > 1f) { uv.Y /= uvv; uv.W /= uvv; }

        b.AddNineSlice(local, new Float4(bl, bt, br, bb), uv, tinted);
    }

    private void EmitTiled(UIMeshBuilder b, Rect local, Color tinted)
    {
        // Tile at the sprite's native pixel size in the RectTransform's pixel space.
        // NOTE: Border-aware tiling (keeping the sprite's 9-slice edges un-tiled) is not done yet;
        // Tiled currently repeats the whole sprite rect.
        var (sw, sh) = SourceSize;
        if (sw <= 0f || sh <= 0f)
        {
            b.AddQuad(local, tinted, Float2.Zero, Float2.One);
            return;
        }

        float m = _pixelsPerUnitMultiplier > 0 ? _pixelsPerUnitMultiplier : 1f;
        b.AddTiled(local, new Float2(sw * m, sh * m), tinted);
    }

    // Fits a source region's aspect ratio inside `local`, centered (letterbox). No-op without a valid size.
    private static Rect FitAspect(Rect local, float texW, float texH)
    {
        if (texW <= 0f || texH <= 0f) return local;
        float rw = local.Size.X, rh = local.Size.Y;
        if (rw <= 0f || rh <= 0f) return local;

        float texAspect = texW / texH;
        float nw = rw, nh = rh;
        if (rw / rh > texAspect) nw = rh * texAspect;  // rect wider than the texture -> shrink width
        else                     nh = rw / texAspect;  // rect taller -> shrink height

        float cx = (local.Min.X + local.Max.X) * 0.5f;
        float cy = (local.Min.Y + local.Max.Y) * 0.5f;
        return new Rect(cx - nw * 0.5f, cy - nh * 0.5f, cx + nw * 0.5f, cy + nh * 0.5f);
    }

    private Float4 ComputeUVBorder()
    {
        var (sw, sh) = SourceSize;
        if (sw <= 0f || sh <= 0f) return Float4.Zero;
        Float4 border = EffectiveBorder;
        return new Float4(
            border.X / sw,
            border.Y / sh,
            border.Z / sw,
            border.W / sh);
    }

    public override void PopulateProperties(PropertySet p, in UIContext _)
    {
        p.SetTexture("_MainTex", SourceTexture ?? defaultTexture);
        // The tint (and CanvasGroup alpha) is already baked into the vertex color in GenerateMesh,
        // and the shader computes texture * vColor * _MainColor - so _MainColor must stay white or
        // the color/alpha would be applied twice (a 50% tint would render at 25%).
        p.SetColor("_MainColor", Color.White);
        p.SetVector("_Tiling", new Float2(1, 1));
        p.SetVector("_Offset", Float2.Zero);
    }

    // No OnValidate override: every property routes through SetField (for code) or its
    // public setter (for inspector edits / undo, see PropertyGrid.ApplyFieldValue), so the
    // precise dirty flags are always marked. UIBehaviour.OnValidate remains the catch-all.
}
