// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;

namespace Prowl.Runtime.UI;

/// <summary>
/// Controls the alpha, interactivity, and raycast blocking of all child UI elements.
/// Analogous to Unity's <c>CanvasGroup</c>.
/// </summary>
/// <remarks>
/// Attach a <see cref="CanvasGroup"/> to any GameObject in the UI hierarchy.
/// The <see cref="GameCanvas"/> will apply this group's settings to the
/// <see cref="UIContext"/> before building child elements.
/// </remarks>
public class CanvasGroup : UIBehaviour
{
    /// <summary>
    /// Opacity multiplier applied to all child elements (0 = transparent, 1 = opaque).
    /// </summary>
    public float Alpha = 1f;

    /// <summary>
    /// When <c>false</c>, disables pointer interaction for all descendants.
    /// </summary>
    public bool Interactable = true;

    /// <summary>
    /// When <c>false</c>, raycasts pass through all descendant elements.
    /// </summary>
    public bool BlocksRaycasts = true;

    /// <summary>
    /// When <c>true</c>, this group and its children ignore parent
    /// <see cref="CanvasGroup"/> settings.
    /// </summary>
    public bool IgnoreParentGroups = false;

    /// <summary>
    /// Applies this group's settings to the given context.
    /// </summary>
    internal UIContext ApplyTo(UIContext parent)
    {
        UIContext ctx = IgnoreParentGroups ? UIContext.Default : parent;
        ctx.Alpha *= Alpha;
        ctx.Interactable = ctx.Interactable && Interactable;
        ctx.BlocksRaycasts = ctx.BlocksRaycasts && BlocksRaycasts;
        return ctx;
    }

    /// <summary>
    /// CanvasGroup itself does not emit visual elements.
    /// </summary>
    public override void BuildUI(Paper paper, UIContext context)
    {
        // Handled by GameCanvas during hierarchy traversal.
    }
}
