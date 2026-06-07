// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Runtime.UI;

namespace Prowl.Runtime;

/// <summary>
/// Coordinates a UI hierarchy: owns layout, scale, and the destination surface.
/// Does not render anything itself — visible elements are <see cref="UIBehaviour"/>s
/// that produce <see cref="UIRenderItem"/>s into <see cref="Tree"/>, which the
/// pipeline then draws via <see cref="UIRenderTree.CollectFor"/>.
/// </summary>
[AddComponentMenu("UI/Game Canvas")]
[ExecuteAlways]
[ComponentIcon("")] // Image
public class GameCanvas : MonoBehaviour
{
    // ============================================================
    // REMOVED:
    //   private RenderTexture? _renderTexture;     // dead — never used
    //   private bool _initialized = false;          // dead — never read
    //   public  void DrawGUI() { ... }              // replaced by RebuildIfDirty
    //   private void BuildChildren(...)             // replaced by BuildRecursive
    // ============================================================

    /// <summary>When set, GameCanvas uses this size instead of the window framebuffer size.
    /// Used by the editor to render into off-screen render textures.</summary>
    public static Float2? ScreenSizeOverride { get; set; }

    // ----------------------------------------------------------------
    // CHANGED: every public field that affects layout is now a property
    //          with a backing field and dirty-marking setter.
    // ----------------------------------------------------------------

    [SerializeField] private RenderMode _renderMode = RenderMode.ScreenSpaceOverlay;
    /// <summary>The rendering mode of this GameCanvas.</summary>
    public RenderMode RenderMode
    {
        get => _renderMode;
        set => SetField(ref _renderMode, value, UIDirtyFlags.Layout | UIDirtyFlags.Hierarchy);
    }

    [SerializeField] private float _scaleFactor = 1f;
    /// <summary>Global scale factor applied to all elements.</summary>
    public float ScaleFactor
    {
        get => _scaleFactor;
        set => SetField(ref _scaleFactor, value, UIDirtyFlags.Layout);
    }

    [SerializeField] private int _sortOrder;
    /// <summary>Sort order relative to other screen-space canvases. Higher = on top.</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetField(ref _sortOrder, value, UIDirtyFlags.Hierarchy);
    }

    [SerializeField] private ScaleMode _uiScaleMode = ScaleMode.ConstantPixelSize;
    public ScaleMode UIScaleMode { get => _uiScaleMode; set => SetField(ref _uiScaleMode, value, UIDirtyFlags.Layout); }

    [SerializeField] private Float2 _referenceResolution = new(1920f, 1080f);
    public Float2 ReferenceResolution { get => _referenceResolution; set => SetField(ref _referenceResolution, value, UIDirtyFlags.Layout); }

    [SerializeField] private float _matchWidthOrHeight = 0.5f;
    public float MatchWidthOrHeight { get => _matchWidthOrHeight; set => SetField(ref _matchWidthOrHeight, value, UIDirtyFlags.Layout); }

    [SerializeField] private ScreenMatchMode _screenMatchMode = ScreenMatchMode.MatchWidthOrHeight;
    public ScreenMatchMode ScreenMatchMode { get => _screenMatchMode; set => SetField(ref _screenMatchMode, value, UIDirtyFlags.Layout); }

    // ----------------------------------------------------------------
    // NEW: WorldSpace-only configuration
    // ----------------------------------------------------------------

    /// <summary>How many canvas pixels equal one world unit when <see cref="RenderMode"/>
    /// is <see cref="RenderMode.WorldSpace"/>. 100 ⇒ a 100×100 px button is 1×1 m.</summary>
    [SerializeField] private float _referencePixelsPerUnit = 100f;
    public float ReferencePixelsPerUnit
    {
        get => _referencePixelsPerUnit;
        set => SetField(ref _referencePixelsPerUnit, Maths.Max(value, 0.001f), UIDirtyFlags.Layout);
    }

    // ----------------------------------------------------------------
    // NEW: shared default UI material — referenced by UIBehaviour.GetMaterial
    // ----------------------------------------------------------------

    private static Material? s_sharedUIMaterial;
    /// <summary>Shared <see cref="Material"/> using the <c>Default/GameUI</c> shader.
    /// Lazy-allocated; reused across every UI element that doesn't override
    /// <see cref="UIBehaviour.GetMaterial"/>.</summary>
    public static Material SharedUIMaterial
        => s_sharedUIMaterial ??= new Material(Shader.LoadDefault(DefaultShader.GameUI));

    // ----------------------------------------------------------------
    // NEW: render tree + dirty state
    // ----------------------------------------------------------------

    // Read-only auto-property: not serialized by Prowl.Echo.
    internal UIRenderTree Tree { get; } = new();
    [SerializeIgnore] private bool _isDirty = true;
    [SerializeIgnore] private UIDirtyFlags _aggregateDirty = UIDirtyFlags.All;

    /// <summary>
    /// Size of the surface this canvas was last built against (in real pixels).
    /// Tracked so that a resolution change — typical when the editor's game viewport resizes
    /// or when the rendering camera switches between targets — automatically forces a rebuild.
    /// Without this, layout uses the previous frame's resolution while the projection uses
    /// the current one, putting the UI off-center and at the wrong scale.
    /// </summary>
    [SerializeIgnore] private Float2 _lastBuildSize = Float2.Zero;

    /// <summary>The canvas's root rect (design pixels) from the last rebuild. The canvas itself has no
    /// RectTransform; this is its layout extent, used by children and the bounds gizmo.</summary>
    [SerializeIgnore] private Rect _rootRect;
    public Rect RootRect => _rootRect;

    /// <summary>Called by descendants (or by property setters above) to request a rebuild.</summary>
    public void MarkDirty(UIDirtyFlags flags)
    {
        _aggregateDirty |= flags;
        _isDirty = true;
    }

    /// <summary>
    /// Backing-field setter for this canvas's properties: assigns only on a real change and
    /// marks <paramref name="flags"/> dirty when it does. Mirrors <see cref="UIBehaviour.SetField{T}"/>
    /// so code, inspector edits, and undo all funnel value-change detection through one place.
    /// </summary>
    private bool SetField<T>(ref T field, T value, UIDirtyFlags flags)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        MarkDirty(flags);
        return true;
    }

    /// <summary>
    /// Float overload that compares with an epsilon. Layout-driving values (scale factor,
    /// pixels-per-unit) are recomputed every rebuild from float math; an exact compare would
    /// let sub-ULP jitter re-trigger rebuilds, so we treat near-equal values as unchanged.
    /// </summary>
    private bool SetField(ref float field, float value, UIDirtyFlags flags, float epsilon = 1e-6f)
    {
        if (Maths.Abs(field - value) < epsilon) return false;
        field = value;
        MarkDirty(flags);
        return true;
    }

    // ============================================================
    // Lifecycle (mostly unchanged from the original)
    // ============================================================

    /// <summary>Ensures this GameCanvas's GameObject and all descendants use RectTransform.</summary>
    public override void OnAddedToScene()
    {
        // The canvas is the layout root and needs no RectTransform itself; its children do.
        EnsureChildRectTransforms(GameObject);
        MarkDirty(UIDirtyFlags.All);   // first build is always dirty
    }

    // CHANGED: OnEnable / OnDisable bodies — both were empty in the original
    public override void OnEnable()  => MarkDirty(UIDirtyFlags.All);
    public override void OnDisable() { /* tree retained but no canvas walk picks us up */ }

    // Update() is intentionally not overridden. ScaleFactor must be computed against the
    // active render-target size, which is only known inside RebuildIfDirty (where the
    // pipeline has pushed GameCanvas.ScreenSizeOverride). Computing it here would use the
    // stale framebuffer size and fight the per-camera value.

    /// <summary>Resolves the effective screen-pixel size for this canvas, in real pixels.</summary>
    private static Float2 ResolveScreenSize() => new Float2(
        ScreenSizeOverride?.X ?? Window.InternalWindow.FramebufferSize.X,
        ScreenSizeOverride?.Y ?? Window.InternalWindow.FramebufferSize.Y);

    // ============================================================
    // NEW: WorldSpace IRenderable plumbing
    // ============================================================

    /// <summary>
    /// For <see cref="RenderMode.WorldSpace"/> canvases, adds every item in <see cref="Tree"/>
    /// to the scene's main renderable list so they participate in #13 Transparent + UI passes.
    /// Overlay/Camera canvases ignore this hook — they are pulled by
    /// <see cref="UIRenderTree.CollectFor"/> from the pipeline directly.
    /// </summary>
    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> _)
    {
        if (RenderMode != RenderMode.WorldSpace) return;
        RebuildIfDirty();
        Tree.RefreshTransforms();
        foreach (UIRenderItem it in Tree.Items)
            renderables.Add(it);
    }

    // ============================================================
    // NEW: rebuild driver — replaces the old DrawGUI
    // ============================================================

    /// <summary>
    /// Walks the hierarchy under this canvas, computes layout, generates meshes for any
    /// <see cref="UIBehaviour"/> with dirty vertices, and produces a fresh <see cref="Tree"/>.
    /// Cheap when <see cref="_isDirty"/> is false (single boolean check + return).
    /// </summary>
    public void RebuildIfDirty()
    {
        // Detect a render-target size change (window resize, editor viewport change, switch
        // between cameras with different RT sizes). The pipeline pushes the active surface size
        // via GameCanvas.ScreenSizeOverride before calling here, so a mismatch forces a rebuild.
        Float2 currentSize = ResolveScreenSize();
        if (!_lastBuildSize.Equals(currentSize))
        {
            _isDirty = true;
            _lastBuildSize = currentSize;
        }

        if (!_isDirty) return;

        // Recompute scale factor against the current screen size *before* layout — Update()
        // can't do this reliably because it runs without ScreenSizeOverride set.
        ScaleFactor = ComputeScaleFactor();

        Tree.Clear();

        Rect rootRect = ComputeRootRect();
        _rootRect = rootRect; // the canvas has no RectTransform; children lay out against this directly

        int dfs = 0;
        BuildRecursive(GameObject, rootRect, UIContext.Default, canvasScissor: null, ref dfs);
        Tree.SortHierarchical();

        _isDirty = false;
        _aggregateDirty = UIDirtyFlags.None;
    }

    private Rect ComputeRootRect()
    {
        // World-space canvases have a fixed design size (their ReferenceResolution) and don't track the
        // screen. Screen-space canvases fill the surface (in design pixels, after the scale factor).
        if (RenderMode == RenderMode.WorldSpace)
            return new Rect(0, 0, Maths.Max(ReferenceResolution.X, 1f), Maths.Max(ReferenceResolution.Y, 1f));

        float rawW = ScreenSizeOverride?.X ?? Window.InternalWindow.FramebufferSize.X;
        float rawH = ScreenSizeOverride?.Y ?? Window.InternalWindow.FramebufferSize.Y;
        float screenW = rawW / Maths.Max(ScaleFactor, 0.001f);
        float screenH = rawH / Maths.Max(ScaleFactor, 0.001f);
        return new Rect(0, 0, screenW, screenH);
    }

    private void BuildRecursive(GameObject parent, Rect parentRect, UIContext ctx, Rect? canvasScissor, ref int dfsIndex)
    {
        foreach (GameObject child in parent.Children)
        {
            if (!child.EnabledInHierarchy) continue;

            // Nested GameCanvas — skip; it manages its own tree.
            GameCanvas? nested = child.GetComponent<GameCanvas>();
            if (nested != null && nested != this) continue;

            // Apply CanvasGroup to the inherited context.
            UIContext childCtx = ctx;
            CanvasGroup? grp = child.GetComponent<CanvasGroup>();
            if (grp != null && grp.EnabledInHierarchy)
                childCtx = grp.ApplyTo(ctx);

            // Lay out this child's RectTransform.
            Rect childRect = parentRect;
            if (child.RectTransform is { } rt)
                childRect = rt.ComputeRect(parentRect);

            // ---- RectMask: intersect parent scissor with this rect, drop if fully outside. ----
            Rect? childScissor = canvasScissor;
            RectMask? rectMask = child.GetComponent<RectMask>();
            if (rectMask != null && rectMask.EnabledInHierarchy)
            {
                Rect mr = rectMask.GetClipRectInCanvasPixels();
                childScissor = canvasScissor is null ? mr : IntersectRect(canvasScissor.Value, mr);
                // Empty scissor → whole subtree contributes nothing. Skip it.
                if (childScissor!.Value.Size.X <= 0f || childScissor.Value.Size.Y <= 0f)
                    continue;
            }

            // Per-item scissor culling: if the layout rect doesn't intersect the active scissor, skip the GameObject.
            if (childScissor is { } cs && !RectsIntersect(cs, childRect))
            {
                // Children sit inside this rect by construction, so we can prune the whole subtree.
                continue;
            }

            bool wroteMask = false;


            // (Re)bake every UIBehaviour that produces geometry, then add a UIRenderItem.
            foreach (UIBehaviour ui in child.GetComponents<UIBehaviour>())
            {
                if (!ui.EnabledInHierarchy) continue;

                EnsureBaked(ui, childCtx);
                if (ui.CachedMesh is { } mesh)
                {
                    EmitItem(ui, mesh, dfsIndex++, childScissor);
                }
            }

            BuildRecursive(child, childRect, childCtx, childScissor, ref dfsIndex);

        }
    }

    private static void EnsureBaked(UIBehaviour ui, in UIContext childCtx)
    {
        bool needsBake = ui.CachedMesh is null
                      || (ui.DirtyFlags & UIDirtyFlags.Vertices) != 0;
        if (!needsBake) return;

        ui.CachedMesh ??= new Mesh();
        UIMeshBuilder builder = UIMeshBuilder.Rent();
        try
        {
            ui.GenerateMesh(builder, childCtx);
            if (builder.IsEmpty)      ui.CachedMesh = null;
            else                      builder.Bake(ui.CachedMesh);
        }
        finally { UIMeshBuilder.Return(builder); }
        ui.DirtyFlags &= ~UIDirtyFlags.Vertices;
    }

    private void EmitItem(UIBehaviour ui, Mesh mesh, int dfsIndex, Rect? canvasScissor)
    {
        UIRenderItem item = Tree.RentItem();
        item.Initialize(
            owner:    ui,
            canvas:   this,
            mesh:     mesh,
            material: ui.GetMaterial(),
            model:    BuildItemModel(ui),
            sortKey:  (SortOrder << 24) | dfsIndex,
            surface:  UIRenderTree.ToSurface(RenderMode),
            scissor:  ConvertScissorToFramebufferPixels(canvasScissor));
        Tree.Add(item);
    }

    /// <summary>
    /// Converts a canvas-design-pixel rect into GL framebuffer pixels (origin at bottom-left,
    /// pixel units). For Overlay/Camera modes the conversion is just a uniform scale by
    /// <see cref="ScaleFactor"/>; for WorldSpace there's no meaningful screen-space scissor
    /// so we return null.
    /// </summary>
    private Float4? ConvertScissorToFramebufferPixels(Rect? canvasRect)
    {
        if (canvasRect is null || RenderMode == RenderMode.WorldSpace) return null;
        Rect r = canvasRect.Value;
        float s = Maths.Max(ScaleFactor, 0.001f);
        float x = r.Min.X * s;
        float y = r.Min.Y * s;
        float w = r.Size.X * s;
        float h = r.Size.Y * s;
        return new Float4(x, y, w, h);
    }

    private static Rect IntersectRect(Rect a, Rect b)
    {
        float minX = MathF.Max(a.Min.X, b.Min.X);
        float minY = MathF.Max(a.Min.Y, b.Min.Y);
        float maxX = MathF.Min(a.Max.X, b.Max.X);
        float maxY = MathF.Min(a.Max.Y, b.Max.Y);
        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;
        return new Rect(minX, minY, maxX, maxY);
    }

    private static bool RectsIntersect(Rect a, Rect b)
        => a.Max.X > b.Min.X && b.Max.X > a.Min.X && a.Max.Y > b.Min.Y && b.Max.Y > a.Min.Y;

    /// <summary>
    /// Base canvas → world transform without per-element pivot / rotation / scale. Per-mode:
    ///   • WorldSpace → <see cref="Transform.LocalToWorldMatrix"/> × scale(1 / <see cref="ReferencePixelsPerUnit"/>)
    ///   • ScreenSpaceOverlay / ScreenSpaceCamera → scale(<see cref="ScaleFactor"/>)
    /// </summary>
    /// <remarks>
    /// Exposed so the scene-view editor and gizmos stay in sync with the actual rendering by
    /// consuming the same matrix.
    /// </remarks>
    public Float4x4 CanvasToWorld => RenderMode switch
    {
        // World-space canvases map design pixels 1:1 to world units (through the transform), so the canvas
        // occupies its ReferenceResolution in world space and doesn't shrink when the mode is toggled.
        // Author the world size via the GameObject's Transform scale.
        RenderMode.WorldSpace => Transform.LocalToWorldMatrix,
        _ => Float4x4.CreateScale(Maths.Max(ScaleFactor, 0.001f)),
    };

    /// <summary>
    /// Returns the model matrix for a UI element under this canvas. Equivalent to
    /// <see cref="CanvasToWorld"/> × <see cref="BuildRectModel(RectTransform)"/>.
    /// </summary>
    internal Float4x4 BuildItemModel(UIBehaviour b)
        => CanvasToWorld * BuildRectModel(b.GameObject.RectTransform!);

    /// <summary>
    /// Builds the canvas-design-pixel space matrix that places a RectTransform's pivot-centered
    /// mesh into the canvas frame, threading <b>parent rotation and scale</b> down the chain.
    /// </summary>
    internal Float4x4 BuildRectModel(RectTransform rt)
    {
        Rect cr = rt.ComputedRect;
        Float2 pivot = rt.Pivot;

        // Element's pivot in canvas-axis-aligned design pixels.
        float pivotX = cr.Min.X + pivot.X * cr.Size.X;
        float pivotY = cr.Min.Y + pivot.Y * cr.Size.Y;

        // Element TRS: rotation/scale apply around the pivot (mesh is pivot-centered),
        // then the pivot is placed at its layout position.
        Float4x4 model = Float4x4.CreateTRS(
            new Float3(pivotX, pivotY, 0),
            rt.LocalRotation,
            rt.LocalScale);

        RectTransform? canvasRT = GameObject.RectTransform;
        GameObject? cur = rt.GameObject.Parent;
        while (cur != null)
        {
            RectTransform? prt = cur.RectTransform;
            if (prt == null || prt == canvasRT) break;

            Rect pcr = prt.ComputedRect;
            float pPivotX = pcr.Min.X + prt.Pivot.X * pcr.Size.X;
            float pPivotY = pcr.Min.Y + prt.Pivot.Y * pcr.Size.Y;
            Float3 pPivot = new Float3(pPivotX, pPivotY, 0);

            Float4x4 wrap = Float4x4.CreateTRS(pPivot, prt.LocalRotation, prt.LocalScale)
                          * Float4x4.CreateTranslation(-pPivot);
            model = wrap * model;

            cur = cur.Parent;
        }

        return model;
    }

    // ============================================================
    // Unchanged from the original — preserved verbatim
    // ============================================================

    private static void EnsureChildRectTransforms(GameObject parent)
    {
        foreach (GameObject child in parent.Children)
        {
            child.EnsureRectTransform();
            EnsureChildRectTransforms(child);
        }
    }

    private float ComputeScaleFactor()
    {
        switch (UIScaleMode)
        {
            case ScaleMode.ConstantPixelSize:    return Maths.Max(ScaleFactor, 0.001f);
            case ScaleMode.ScaleWithScreenSize:  return ComputeScreenSizeScale();
            default:                              return 1f;
        }
    }

    // ============================================================
    // Scene-view gizmos
    // ============================================================

    public override void DrawGizmos()
    {
        UIGizmos.DrawCanvasRect(this, UIGizmos.UnselectedColor);
    }

    public override void DrawGizmosSelected()
    {
        UIGizmos.DrawCanvasRect(this, UIGizmos.SelectedColor);
    }

    private float ComputeScreenSizeScale()
    {
        return ComputeScreenSizeScale(ScreenSizeOverride);
    }

    public float ComputeScreenSizeScale(Float2? screenSizeOverride)
    {
        float screenW = screenSizeOverride?.X ?? Window.InternalWindow.FramebufferSize.X;
        float screenH = screenSizeOverride?.Y ?? Window.InternalWindow.FramebufferSize.Y;
        if (screenW <= 0 || screenH <= 0) return 1f;

        float refW = Maths.Max(ReferenceResolution.X, 1f);
        float refH = Maths.Max(ReferenceResolution.Y, 1f);

        float logWidth  = MathF.Log2(screenW / refW);
        float logHeight = MathF.Log2(screenH / refH);

        float logScale = ScreenMatchMode switch
        {
            ScreenMatchMode.MatchWidthOrHeight =>
                Maths.Lerp(logWidth, logHeight, Maths.Clamp(MatchWidthOrHeight, 0f, 1f)),
            ScreenMatchMode.Expand => Maths.Min(logWidth, logHeight),
            ScreenMatchMode.Shrink => Maths.Max(logWidth, logHeight),
            _ => 0f,
        };
        return Maths.Max(Maths.Pow(2f, logScale), 0.001f);
    }
}
