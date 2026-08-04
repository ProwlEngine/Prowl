// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.UI;

/// <summary>
/// Abstract base class for every retained-mode UI component.
/// </summary>
/// <remarks>
/// UI components do not draw themselves. Instead, the owning <see cref="GameCanvas"/>
/// traverses the hierarchy in depth-first order and calls
/// <see cref="GenerateMesh"/> on each <see cref="UIBehaviour"/> whose mesh is dirty,
/// then bakes the result into <see cref="CachedMesh"/> for the pipeline to consume.
/// </remarks>
[RequireComponent(typeof(RectTransform))]
public abstract class UIBehaviour : MonoBehaviour
{
    [SerializeIgnore] internal Mesh? CachedMesh;
    [SerializeIgnore] internal UIDirtyFlags DirtyFlags = UIDirtyFlags.All;

    // The rect size and inherited alpha the CachedMesh was last baked from. The baked geometry
    // depends on both (size drives the quad extents; alpha is baked into vertex colors), yet neither
    // is covered by the Vertices dirty flag: a parent resize or an ancestor CanvasGroup alpha change
    // reflows/recolors this element without touching its own flags. EnsureBaked compares these to
    // force a re-bake when they drift (otherwise a stretched child renders at its old size).
    [SerializeIgnore] internal Float2 LastBakeSize = new(float.NaN, float.NaN);
    [SerializeIgnore] internal float LastBakeAlpha = float.NaN;

    public override void OnEnable()
    {
        var canvas = GetCanvas();
        if (canvas.IsValid()) canvas.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    public override void OnDisable()
    {
        var canvas = GetCanvas();
        if (canvas.IsValid()) canvas.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    public override void OnValidate()
    {
        // Run the OnValide only in the editor- During runtime, dirtying should be driven by the fields themselves.
        if (!Application.IsPlaying)
        {
            var canvas = GetCanvas();
            if (canvas.IsValid()) canvas.MarkDirty(UIDirtyFlags.All);
            MarkDirty(UIDirtyFlags.All);
        }
    }

    public override void OnAddedToScene()
    {
        var canvas = GetCanvas();
        if (canvas.IsValid()) canvas.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    public override void OnRemovedFromScene()
    {
        var canvas = GetCanvas();
        if (canvas.IsValid()) canvas.MarkDirty(UIDirtyFlags.Hierarchy);

        MarkDirty(UIDirtyFlags.Hierarchy);

        // Free the baked GPU buffers - the canvas will re-bake from scratch if this element
        // is ever re-added. Without this every created/destroyed UI element leaks its mesh.
        if (CachedMesh.IsValid()) CachedMesh.Dispose();
        CachedMesh = null;
        DirtyFlags |= UIDirtyFlags.Vertices;
    }

    /// <summary>Subclasses fill <paramref name="builder"/> in canvas-local pixel space.</summary>
    public abstract void GenerateMesh(UIMeshBuilder builder, in UIContext context);

    /// <summary>
    /// True while this element's geometry is built from an asset that hasn't streamed in yet, so what it
    /// baked is missing or a placeholder.
    /// <para>
    /// The canvas stays dirty while any element reports this. Nothing else would bring it back: a clean
    /// canvas skips the whole rebuild walk, so an element that baked nothing on frame one is never asked
    /// again, and the asset arriving changes nothing on screen until something unrelated (a window
    /// resize) happens to dirty the canvas.
    /// </para>
    /// </summary>
    public virtual bool IsContentPending => false;

    /// <summary>Subclasses bind per-item shader properties (textures, scalars). Called every frame the item is visible.</summary>
    public virtual void PopulateProperties(PropertyState props, in UIContext context) { }

    /// <summary>The material this element draws with. Default returns the shared `DefaultUI` material.</summary>
    public virtual Material GetMaterial() => GameCanvas.SharedUIMaterial;

    public void MarkDirty(UIDirtyFlags flags)
    {
        DirtyFlags |= flags;
        var canvas = GetCanvas();
        if (canvas.IsValid()) canvas.MarkDirty(flags);
    }

    /// <summary>
    /// Backing-field setter used by every <see cref="UIBehaviour"/> property. Assigns
    /// <paramref name="value"/> only when it differs from <paramref name="field"/>, and
    /// marks <paramref name="flags"/> dirty when it does. Returns <c>true</c> if the value
    /// changed - callers can branch on it for additional work (see <c>CanvasGroup.Alpha</c>).
    /// </summary>
    /// <remarks>
    /// This is the single place the UI's value-change check lives. <see cref="EqualityComparer{T}.Default"/>
    /// resolves correctly for every property type in use: structs (<c>Color</c>, <c>Float2</c>)
    /// compare by value, and <see cref="EngineObject"/> references (<c>Texture2D</c>, <c>Material</c>,
    /// <c>FontAsset</c>) compare by reference via <see cref="EngineObject"/>'s overridden equality.
    /// </remarks>
    protected bool SetField<T>(ref T field, T value, UIDirtyFlags flags)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        MarkDirty(flags);
        return true;
    }

    public GameCanvas? GetCanvas() => GetComponentInParent<GameCanvas>(includeSelf: true);

    // ============================================================
    // Scene-view gizmos (RectTransform / UI gizmos)
    // ============================================================

    /// <summary>Faint rect outline while not selected, so authors can see where UI lives in the scene.</summary>
    public override void DrawGizmos()
    {
        UIGizmos.DrawRect(this, UIGizmos.UnselectedColor, drawPivot: false, drawAnchors: false);
    }

    /// <summary>Bold rect outline plus pivot + anchor handles when this element is selected.</summary>
    public override void DrawGizmosSelected()
    {
        UIGizmos.DrawRect(this, UIGizmos.SelectedColor, drawPivot: true, drawAnchors: true);
    }
}
