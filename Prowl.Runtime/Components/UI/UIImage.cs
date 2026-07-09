// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
    /// <summary>The texture is stretched to fit the whole rect (the default).</summary>
    Simple,
    /// <summary>The four <see cref="UIImage.Border"/> regions stay unstretched; only the middle scales.</summary>
    Sliced,
    /// <summary>The texture repeats across the rect at its native size (scaled by <see cref="UIImage.PixelsPerUnit"/>).</summary>
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
/// Displays a colored rectangle or a <see cref="Texture2D"/> sprite in the UI.
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

    [SerializeField] private AssetRef<Texture2D> _texture;
    public AssetRef<Texture2D> Texture
    {
        get => _texture;
        set => SetField(ref _texture, value, UIDirtyFlags.Material);
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

    /// <summary>Corner radius for rounded rectangles (in pixels). 0 = sharp corners. Only honored when <see cref="Type"/> is <see cref="ImageType.Simple"/>.</summary>
    [SerializeField] private float _cornerRadius;
    public float CornerRadius
    {
        get => _cornerRadius;
        set => SetField(ref _cornerRadius, value, UIDirtyFlags.Vertices);
    }

    /// <summary>How the texture is mapped to the rect. See <see cref="ImageType"/>.</summary>
    [SerializeField] private ImageType _type = ImageType.Simple;
    public ImageType Type
    {
        get => _type;
        set => SetField(ref _type, value, UIDirtyFlags.Vertices);
    }

    /// <summary>Source-texture border in pixels (L, T, R, B). Used by <see cref="ImageType.Sliced"/> and <see cref="ImageType.Tiled"/> to keep edges un-stretched.</summary>
    [SerializeField] private Float4 _border;
    public Float4 Border
    {
        get => _border;
        set => SetField(ref _border, value, UIDirtyFlags.Vertices);
    }

    /// <summary>Pixels-per-unit divisor applied to <see cref="Border"/> and tile size. Higher values shrink borders/tiles on-screen.</summary>
    [SerializeField] private float _pixelsPerUnit = 1f;
    public float PixelsPerUnit
    {
        get => _pixelsPerUnit;
        set => SetField(ref _pixelsPerUnit, value, UIDirtyFlags.Vertices);
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

        // Fit the texture's aspect inside the rect (letterbox), centered, when requested.
        if (_preserveAspect)
            local = FitAspect(local, _texture.Res);

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
                if (CornerRadius > 0)
                    b.AddRoundedRect(local, CornerRadius, tinted);
                else
                    // UV (0,0) at the rect's bottom-left, (1,1) at its top-right (+Y up).
                    b.AddQuad(local, tinted, Float2.Zero, Float2.One);
                return;
        }

        // Simple already emits a rounded mesh directly via AddRoundedRect; other fill modes need a
        // post-process clip pass to round their corners.
        if (CornerRadius > 0)
            b.ClipToRoundedRect(local, CornerRadius);
    }

    private void EmitSliced(UIMeshBuilder b, Rect local, Color tinted)
    {
        float ppu = _pixelsPerUnit > 0 ? _pixelsPerUnit : 1f;
        // Pixel borders are in source-texture pixels; divide by PPU to get screen pixels and
        // clamp so opposite borders never overlap (which would invert the center quad).
        float bl = _border.X / ppu, bt = _border.Y / ppu, br = _border.Z / ppu, bb = _border.W / ppu;
        float maxX = local.Size.X * 0.5f, maxY = local.Size.Y * 0.5f;
        bl = MathF.Min(bl, maxX); br = MathF.Min(br, maxX);
        bt = MathF.Min(bt, maxY); bb = MathF.Min(bb, maxY);

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
        // Tile size in screen pixels: source texture native size scaled by 1/PPU. If no texture
        // is set we fall back to the rect itself so the image just stretches like Simple.
        // NOTE: Border-aware tiling (keeping the sprite edges un-tiled) needs real Sprite borders,
        // so it's deferred until the Sprite feature lands; for now Tiled always tiles the whole texture.
        Texture2D? tex = _texture.Res;
        if (tex is null || tex.Width == 0 || tex.Height == 0)
        {
            b.AddQuad(local, tinted, Float2.Zero, Float2.One);
            return;
        }

        float ppu = _pixelsPerUnit > 0 ? _pixelsPerUnit : 1f;
        Float2 tileSize = new Float2(tex.Width / ppu, tex.Height / ppu);
        b.AddTiled(local, tileSize, tinted);
    }

    // Fits a texture's aspect ratio inside `local`, centered (letterbox). No-op without a valid texture.
    private static Rect FitAspect(Rect local, Texture2D? tex)
    {
        if (tex is null || tex.Width <= 0 || tex.Height <= 0) return local;
        float rw = local.Size.X, rh = local.Size.Y;
        if (rw <= 0f || rh <= 0f) return local;

        float texAspect = (float)tex.Width / tex.Height;
        float nw = rw, nh = rh;
        if (rw / rh > texAspect) nw = rh * texAspect;  // rect wider than the texture -> shrink width
        else                     nh = rw / texAspect;  // rect taller -> shrink height

        float cx = (local.Min.X + local.Max.X) * 0.5f;
        float cy = (local.Min.Y + local.Max.Y) * 0.5f;
        return new Rect(cx - nw * 0.5f, cy - nh * 0.5f, cx + nw * 0.5f, cy + nh * 0.5f);
    }

    private Float4 ComputeUVBorder()
    {
        Texture2D? tex = _texture.Res;
        if (tex is null || tex.Width == 0 || tex.Height == 0) return Float4.Zero;
        return new Float4(
            _border.X / tex.Width,
            _border.Y / tex.Height,
            _border.Z / tex.Width,
            _border.W / tex.Height);
    }

    public override void PopulateProperties(PropertyState p, in UIContext _)
    {
        p.SetTexture("_MainTex", _texture.Res ?? defaultTexture);
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
