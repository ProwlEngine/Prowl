// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>
/// A rounded-rect clip region contributed by a mask (see <see cref="RectMask"/>). The fragment shader
/// maps each pixel into the mask's local space (via the mask's inverse model) and tests it against
/// <see cref="Rect"/> with <see cref="Radius"/> rounded corners and a <see cref="Softness"/> edge.
/// </summary>
internal readonly struct UIClip
{
    public readonly UIBehaviour Source;   // mask element - its model gives the world->local matrix
    public readonly Float4 Rect;          // (minX, minY, maxX, maxY) in mask-local pivot-centered pixels
    public readonly float Radius;
    public readonly float Softness;

    public UIClip(UIBehaviour source, Float4 rect, float radius, float softness)
    {
        Source = source; Rect = rect; Radius = radius; Softness = softness;
    }
}

/// <summary>
/// One drawable UI primitive. Wraps a <see cref="UIBehaviour"/>'s baked mesh and material
/// and exposes them through <see cref="IRenderable"/> so the existing
/// <c>DrawRenderables</c> batcher can consume them unchanged.
/// </summary>
/// <remarks>
/// Items are pooled via <see cref="UIRenderTree"/>. Direct construction from outside
/// the UI namespace is discouraged.
/// </remarks>
internal sealed class UIRenderItem : IRenderable
{
    // -------- Identity --------
    public UIBehaviour Owner = null!;            // back-reference for property population
    public GameCanvas  Canvas = null!;           // owning canvas (used when refreshing model matrices)

    // -------- GPU payload --------
    public Mesh        Mesh     = null!;         // == Owner.CachedMesh after Bake
    public Material    Material = null!;         // resolved once per rebuild via Owner.GetMaterial()

    // -------- Per-frame state --------
    public Float4x4      Model;                  // canvas-local-pixel x canvas-world (see GameCanvas.BuildItemModel)
    public PropertyState Props = new();          // reused; never reallocated

    // -------- Sort + lifecycle --------
    public long   SortKey;                       // (SortOrder << 42) | (canvasDiscriminator << 21) | depthFirstIndex
    public UISurface Surface;                    // mirrors Canvas.RenderMode, frozen at rebuild time
    public uint   LastTransformVersion;          // (matrix-only refresh)
    public UIDirtyFlags PropertyCacheState;      // tracks whether Props needs repopulating

    // -------- Clip (mask) state (set by the canvas during BuildRecursive) --------
    public bool         HasClip;
    public UIBehaviour? ClipSource;              // the mask element, for per-frame ClipToLocal refresh
    public Float4x4     ClipToLocal;             // world -> mask-local space (cancels CanvasToWorld)
    public Float4       ClipRect;                // (minX, minY, maxX, maxY) in mask-local pixels
    public float        ClipRadius;
    public float        ClipSoftness;

    // ============================================================
    // IRenderable implementation
    // ============================================================

    public Material GetMaterial() => Material;
    public int GetLayer() => Owner.GameObject.LayerIndex;
    public int GetSubMeshIndex() => -1;

    /// <summary>
    /// Position used by the back-to-front sorter. We extract the translation from
    /// <see cref="Model"/> via <c>TransformPoint(Float3.Zero, Model)</c> because
    /// <c>Float4x4</c> in this engine has no <c>.Translation</c> property - it
    /// stores columns (c0..c3) and is row-major in API surface only.
    /// </summary>
    public Float3 GetPosition() => Float4x4.TransformPoint(Float3.Zero, Model);

    public void GetRenderingData(ViewerData v, out PropertyState p, out Mesh m, out Float4x4 model, out InstanceData[]? inst)
    {
        // Repopulate Props only when the owning behaviour reports a Material/Hierarchy change.
        // The first call after a rebuild always writes (Initialize resets PropertyCacheState to All),
        // and any property edit dirties the canvas -> full rebuild -> fresh item -> repopulate, so a
        // persisted item on a static frame can safely skip this.
        if ((PropertyCacheState & (UIDirtyFlags.Material | UIDirtyFlags.Hierarchy)) != 0)
        {
            Props.Clear();
            Props.SetInt("_ObjectID", Owner.InstanceID);
            Owner.PopulateProperties(Props, UIContext.Default);
            PropertyCacheState = UIDirtyFlags.None;
        }

        // Clip uniforms are refreshed every frame (ClipToLocal follows the mask's transform via
        // RefreshModelIfDirty), so they live outside the cached-property block above.
        if (HasClip)
        {
            Props.SetFloat("_ClipEnable", 1f);
            Props.SetMatrix("_ClipToLocal", ClipToLocal);
            Props.SetVector("_ClipRect", ClipRect);
            Props.SetFloat("_ClipRadius", ClipRadius);
            Props.SetFloat("_ClipSoftness", ClipSoftness);
        }
        else
        {
            Props.SetFloat("_ClipEnable", 0f);
        }

        p = Props; m = Mesh; model = Model; inst = null;
    }

    public void GetCullingData(out bool ok, out AABB bounds)
    {
        // Mesh.bounds is the local AABB filled by RecalculateBounds() during Bake.
        // For overlay/camera surfaces this AABB is in canvas-local pixel space,
        // and `Model` resolves to that same space when the projection is the screen
        // ortho (so transforming by Model still yields a meaningful AABB).
        ok = Mesh.IsValid();
        if (!ok) { bounds = default; return; }
        bounds = Mesh.bounds.TransformBy(Model);
    }

    // ============================================================
    // Lifecycle helpers
    // ============================================================

    /// <summary>
    /// Called from <see cref="GameCanvas.RebuildIfDirty"/> to populate fields after
    /// <see cref="UIMeshBuilder.Bake"/> has produced a fresh mesh.
    /// </summary>
    internal void Initialize(UIBehaviour owner, GameCanvas canvas, Mesh mesh, Material material,
                             Float4x4 model, long sortKey, UISurface surface, UIClip? clip = null)
    {
        Owner = owner; Canvas = canvas;
        Mesh = mesh; Material = material;
        Model = model;
        SortKey = sortKey;
        Surface = surface;
        LastTransformVersion = owner.Transform.Version;
        PropertyCacheState = UIDirtyFlags.All;   // forces first GetRenderingData to populate

        if (clip is { } c)
        {
            HasClip = true;
            ClipSource = c.Source;
            ClipRect = c.Rect;
            ClipRadius = c.Radius;
            ClipSoftness = c.Softness;
            ClipToLocal = canvas.BuildItemModel(c.Source).Invert();
        }
        else
        {
            HasClip = false;
            ClipSource = null;
        }
    }

    /// <summary>
    /// Called once per frame, before the pipeline draws this surface, by
    /// <see cref="UIRenderTree.RefreshTransforms"/>. Patches <see cref="Model"/>
    /// when only the owning <see cref="Transform"/> has changed (no mesh rebuild needed).
    /// </summary>
    internal void RefreshModelIfDirty()
    {
        uint v = Owner.Transform.Version;
        if (v == LastTransformVersion) return;
        Model = Canvas.BuildItemModel(Owner);
        if (HasClip && ClipSource != null)
            ClipToLocal = Canvas.BuildItemModel(ClipSource).Invert();
        LastTransformVersion = v;
    }
}
