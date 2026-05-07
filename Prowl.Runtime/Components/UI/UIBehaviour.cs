// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;

namespace Prowl.Runtime.UI;

/// <summary>
/// Abstract base class for all UI components in the Prowl.Paper UI system.
/// Similar to Unity's <c>UnityEngine.EventSystems.UIBehaviour</c>.
/// </summary>
/// <remarks>
/// UI components do not build Paper elements in their own <see cref="MonoBehaviour.OnGui"/>
/// method. Instead, the parent <see cref="GameCanvas"/> traverses the hierarchy and calls
/// <see cref="BuildUI"/> on each <see cref="UIBehaviour"/> in depth-first order.
/// </remarks>
public abstract class UIBehaviour : MonoBehaviour
{
    /// <summary>
    /// Called by the owning <see cref="GameCanvas"/> during UI construction.
    /// Subclasses should use <paramref name="paper"/> to emit Paper elements.
    /// </summary>
    /// <param name="paper">The Paper instance provided by the owning GameCanvas.</param>
    /// <param name="context">
    /// Inherited rendering context (e.g. group alpha, interactivity)
    /// accumulated from parent <see cref="CanvasGroup"/> components.
    /// </param>
    public abstract void BuildUI(Paper paper, UIContext context);

    /// <summary>
    /// Finds the nearest <see cref="GameCanvas"/> in the parent hierarchy.
    /// </summary>
    public GameCanvas? GetCanvas()
    {
        return GetComponentInParent<GameCanvas>(includeSelf: true);
    }
}
