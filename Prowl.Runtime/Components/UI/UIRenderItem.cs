// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

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
/// One drawable UI primitive: a <see cref="UIBehaviour"/>'s baked mesh and material, its model matrix and its
/// mask clip. <see cref="UIRendering"/> draws world space items as provider items and screen space items as an
/// overlay over each game view.
/// </summary>
/// <remarks>
/// Items are pooled via <see cref="UIRenderTree"/>. Direct construction from outside
/// the UI namespace is discouraged.
/// </remarks>
internal sealed class UIRenderItem
{
    // -------- Identity --------
    public UIBehaviour Owner = null!;
    public GameCanvas  Canvas = null!;           // owning canvas (used when refreshing model matrices)

    // -------- GPU payload --------
    public Mesh        Mesh     = null!;         // == Owner.CachedMesh after Bake
    public Material    Material = null!;         // resolved once per rebuild via Owner.GetMaterial()

    // -------- Per-frame state --------
    public Float4x4      Model;                  // canvas-local-pixel x canvas-world (see GameCanvas.BuildItemModel)

    // -------- Sort + lifecycle --------
    public long   SortKey;                       // (SortOrder << 42) | (canvasDiscriminator << 21) | depthFirstIndex
    public UISurface Surface;                    // mirrors Canvas.RenderMode, frozen at rebuild time
    public Float4x4 LastOwnerWorld;              // owner's world matrix at the last model refresh

    // -------- Clip (mask) state (set by the canvas during BuildRecursive) --------
    public bool         HasClip;
    public UIBehaviour? ClipSource;              // the mask element, for per-frame ClipToLocal refresh
    public Float4x4     ClipToLocal;             // world -> mask-local space (cancels CanvasToWorld)
    public Float4       ClipRect;                // (minX, minY, maxX, maxY) in mask-local pixels
    public float        ClipRadius;
    public float        ClipSoftness;

    /// <summary>Set when the mesh's mask space positions, which world space drawing keeps in UV1, need writing again.</summary>
    public bool         ClipCoordsDirty;

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
        LastOwnerWorld = owner.Transform.LocalToWorldMatrix;
        ClipCoordsDirty = true;

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
    /// Called once per frame, before the UI is drawn, by
    /// <see cref="UIRenderTree.RefreshTransforms"/>. Patches <see cref="Model"/>
    /// when only the transforms have changed (no mesh rebuild needed).
    /// </summary>
    /// <remarks>
    /// The model is composed from the owner's transform <b>and every ancestor's</b>: parent panels
    /// contribute rotation/scale/Z and the canvas contributes <c>CanvasToWorld</c>. The owner's world
    /// matrix already folds in that whole chain, so comparing it catches a moved canvas or a rotated
    /// parent panel - which the local-only <see cref="Transform.Version"/> does not. Reading it is a
    /// couple of version compares per ancestor while nothing has moved.
    /// </remarks>
    internal void RefreshModelIfDirty()
    {
        Float4x4 world = Owner.Transform.LocalToWorldMatrix;
        if (world == LastOwnerWorld) return;
        Model = Canvas.BuildItemModel(Owner);
        if (HasClip && ClipSource != null)
            ClipToLocal = Canvas.BuildItemModel(ClipSource).Invert();
        LastOwnerWorld = world;
        ClipCoordsDirty = true;
    }
}
