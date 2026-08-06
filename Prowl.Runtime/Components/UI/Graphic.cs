// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Base for every UI element that actually draws something. Owns the tint, the optional material
/// override, and whether the element takes part in pointer hit-testing.
/// </summary>
/// <remarks>
/// Behaviours that produce no geometry (layout groups, <see cref="CanvasGroup"/>, <see cref="RectMask"/>,
/// <see cref="Selectable"/>) derive from <see cref="UIBehaviour"/> directly and are never raycast targets,
/// so a bare layout panel no longer swallows clicks meant for what is behind it.
/// </remarks>
public abstract class Graphic : UIBehaviour
{
    /// <summary>
    /// Whether this element blocks pointer hit-testing. Affects input dispatch only, not rendering.
    /// </summary>
    [SerializeField] private bool _raycastTarget = true;
    public bool RaycastTarget
    {
        get => _raycastTarget;
        set => SetField(ref _raycastTarget, value, UIDirtyFlags.Hierarchy);
    }

    /// <summary>Material override. When unset the graphic draws with <see cref="DefaultMaterial"/>.</summary>
    [SerializeField] private AssetRef<Material> _material;
    public AssetRef<Material> Material
    {
        get => _material;
        set => SetField(ref _material, value, UIDirtyFlags.Material);
    }

    /// <summary>The tint applied to this graphic. Its alpha is multiplied by the inherited
    /// <see cref="CanvasGroup"/> alpha when the mesh is baked.</summary>
    [SerializeField] private Color _color = Color.White;
    public Color Color
    {
        get => _color;
        set => SetField(ref _color, value, UIDirtyFlags.Vertices);
    }

    /// <summary>The material used when no override is assigned.</summary>
    protected virtual Material DefaultMaterial => GameCanvas.SharedUIMaterial;

    public override Material GetMaterial()
    {
        Material? m = _material.Res;
        return m.IsValid() ? m : DefaultMaterial;
    }
}
