// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

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
    public Float4x4      Model;                  // canvas-local-pixel × canvas-world (see GameCanvas.BuildItemModel)
    public PropertyState Props = new();          // reused; never reallocated

    // -------- Sort + lifecycle --------
    public int    SortKey;                       // (canvasSortOrder << 24) | depthFirstIndex
    public UISurface Surface;                    // mirrors Canvas.RenderMode, frozen at rebuild time
    public uint   LastTransformVersion;          // (matrix-only refresh)
    public UIDirtyFlags PropertyCacheState;      // tracks whether Props needs repopulating

    // -------- Mask state (set by the canvas during BuildRecursive) --------
    public Float4?    ScissorPixels;             // (x, y, w, h) in framebuffer pixels — null = no scissor

    // ============================================================
    // IRenderable implementation
    // ============================================================

    public Material GetMaterial() => Material;
    public int GetLayer() => Owner.GameObject.LayerIndex;
    public int GetSubMeshIndex() => -1;

    /// <summary>
    /// Position used by the back-to-front sorter. We extract the translation from
    /// <see cref="Model"/> via <c>TransformPoint(Float3.Zero, Model)</c> because
    /// <c>Float4x4</c> in this engine has no <c>.Translation</c> property — it
    /// stores columns (c0..c3) and is row-major in API surface only.
    /// </summary>
    public Float3 GetPosition() => Float4x4.TransformPoint(Float3.Zero, Model);

    public void GetRenderingData(ViewerData v, out PropertyState p, out Mesh m, out Float4x4 model, out InstanceData[]? inst)
    {
        // Repopulate Props only when the owning behaviour reports a Material change.
        // The first call after a rebuild always writes (PropertyCacheState starts at All).
        if (true)//((PropertyCacheState & (UIDirtyFlags.Material | UIDirtyFlags.Hierarchy)) != 0)
        {
            Props.Clear();
            Props.SetInt("_ObjectID", Owner.InstanceID);
            Owner.PopulateProperties(Props, UIContext.Default);
            PropertyCacheState = UIDirtyFlags.None;
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
                             Float4x4 model, int sortKey, UISurface surface, Float4? scissor = null)
    {
        Owner = owner; Canvas = canvas;
        Mesh = mesh; Material = material;
        Model = model;
        SortKey = sortKey;
        Surface = surface;
        LastTransformVersion = owner.Transform.Version;
        PropertyCacheState = UIDirtyFlags.All;   // forces first GetRenderingData to populate
        ScissorPixels = scissor;
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
        LastTransformVersion = v;
    }
}
