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
/// Similar to Unity's <c>UnityEngine.EventSystems.UIBehaviour</c>.
/// </summary>
/// <remarks>
/// UI components do not draw themselves. Instead, the owning <see cref="GameCanvas"/>
/// traverses the hierarchy in depth-first order and calls
/// <see cref="GenerateMesh"/> on each <see cref="UIBehaviour"/> whose mesh is dirty,
/// then bakes the result into <see cref="CachedMesh"/> for the pipeline to consume.
/// </remarks>
public abstract class UIBehaviour : MonoBehaviour
{
    [SerializeIgnore] internal Mesh? CachedMesh;
    [SerializeIgnore] internal UIDirtyFlags DirtyFlags = UIDirtyFlags.All;
    [SerializeIgnore] internal int LastBuiltAtFrame = -1;

    public override void OnEnable()
    {
        GetCanvas()?.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    public override void OnDisable()
    {
        GetCanvas()?.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    public override void OnValidate()
    {
        GetCanvas()?.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.All);
    }

    public override void OnAddedToScene()
    {
        GetCanvas()?.MarkDirty(UIDirtyFlags.Hierarchy);
        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    public override void OnRemovedFromScene()
    {
        GetCanvas()?.MarkDirty(UIDirtyFlags.Hierarchy);

        MarkDirty(UIDirtyFlags.Hierarchy);
    }

    /// <summary>Subclasses fill <paramref name="builder"/> in canvas-local pixel space.</summary>
    public abstract void GenerateMesh(UIMeshBuilder builder, in UIContext context);

    /// <summary>Subclasses bind per-item shader properties (textures, scalars). Called every frame the item is visible.</summary>
    public virtual void PopulateProperties(PropertyState props, in UIContext context) { }

    /// <summary>The material this element draws with. Default returns the shared `GameUI` material.</summary>
    public virtual Material GetMaterial() => GameCanvas.SharedUIMaterial;

    /// <summary>True if this element should never share a draw call with siblings (e.g. text with its own font atlas).</summary>
    public virtual bool RequiresPerElementMaterial => false;

    public void MarkDirty(UIDirtyFlags flags)
    {
        DirtyFlags |= flags;
        GetCanvas()?.MarkDirty(flags);
    }

    public GameCanvas? GetCanvas() => GetComponentInParent<GameCanvas>(includeSelf: true);
}
