// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

/// <summary>
/// Inherited rendering context passed down the UI hierarchy during
/// <see cref="GameCanvas"/> tree construction.
/// Accumulates values from <see cref="CanvasGroup"/> components.
/// </summary>
public struct UIContext
{
    /// <summary>
    /// Accumulated alpha multiplier (1 = fully opaque, 0 = fully transparent).
    /// Each <see cref="CanvasGroup"/> multiplies its own alpha into this value.
    /// </summary>
    public float Alpha;

    /// <summary>
    /// Whether pointer events should be processed for elements under this context.
    /// A <see cref="CanvasGroup"/> with <see cref="CanvasGroup.Interactable"/> set to
    /// <c>false</c> disables interaction for all descendants.
    /// </summary>
    public bool Interactable;

    /// <summary>
    /// Whether raycasts (pointer hit-testing) are blocked by elements in this context.
    /// </summary>
    public bool BlocksRaycasts;

    /// <summary>
    /// Creates a default context with full opacity and all interaction enabled.
    /// </summary>
    public static UIContext Default => new()
    {
        Alpha = 1f,
        Interactable = true,
        BlocksRaycasts = true,
    };
}
