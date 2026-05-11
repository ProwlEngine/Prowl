// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>
/// Displays a colored rectangle or a <see cref="Texture2D"/> sprite in the UI.
/// Analogous to Unity's <c>Image</c> component.
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

    [SerializeField] private Texture2D? _texture;
    public Texture2D? Texture
    {
        get => _texture;
        set { if (ReferenceEquals(_texture, value)) return; _texture = value; MarkDirty(UIDirtyFlags.Material); }
    }

    // ---- Material override ----
    [SerializeField] private Material? _material;
    public Material? Material
    {
        get => _material;
        set { if (ReferenceEquals(_material, value)) return; _material = value; MarkDirty(UIDirtyFlags.Material); }
    }

    /// <summary>The tint color of the image. Alpha is modulated by the parent <see cref="CanvasGroup"/>.</summary>
    [SerializeField] private Color _color = Color.White;
    public Color Color
    {
        get => _color;
        set { if (_color == value) return; _color = value; MarkDirty(UIDirtyFlags.Vertices); }
    }

    /// <summary>Whether the image should preserve the source texture's aspect ratio.</summary>
    [SerializeField] private bool _preserveAspect;
    public bool PreserveAspect
    {
        get => _preserveAspect;
        set { if (_preserveAspect == value) return; _preserveAspect = value; MarkDirty(UIDirtyFlags.Vertices); }
    }

    /// <summary>Corner radius for rounded rectangles (in pixels). 0 = sharp corners.</summary>
    [SerializeField] private float _cornerRadius;
    public float CornerRadius
    {
        get => _cornerRadius;
        set { if (_cornerRadius == value) return; _cornerRadius = value; MarkDirty(UIDirtyFlags.Vertices); }
    }

    /// <summary>
    /// Whether this element should block raycasts (pointer hit-testing).
    /// Affects input dispatch only — does not change rendering.
    /// </summary>
    [SerializeField] private bool _raycastTarget = true;
    public bool RaycastTarget
    {
        get => _raycastTarget;
        set { if (_raycastTarget == value) return; _raycastTarget = value; MarkDirty(UIDirtyFlags.Hierarchy); }
    }

    public override Material GetMaterial() => _material ?? base.GetMaterial();

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

        Color tinted = Color * new Color(1, 1, 1, ctx.Alpha);
        if (CornerRadius > 0)
            b.AddRoundedRect(local, CornerRadius, tinted);
        else
            b.AddQuad(local, tinted, new Float2(0,1), new Float2(1,0));
    }

    public override void PopulateProperties(PropertyState p, in UIContext _)
    {
        p.SetTexture("_MainTex", Texture ?? defaultTexture);
        p.SetColor("_MainColor", Color);   // tint already baked into vertex color
        p.SetVector("_Tiling", new Float2(1, 1));
        p.SetVector("_Offset", Float2.Zero);
    }

    public override void OnValidate()
    {
        Debug.Log("UIImage.OnValidate");
        MarkDirty(UIDirtyFlags.All);
    }
}
