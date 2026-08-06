// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

/// <summary>How a <see cref="Selectable"/> shows its current <see cref="SelectionState"/>.</summary>
public enum SelectableTransition
{
    /// <summary>No visual feedback; the widget still tracks state for its own logic.</summary>
    None,

    /// <summary>Lerps the target graphic's color across the states. The default.</summary>
    ColorTint,

    /// <summary>Swaps the target <see cref="UIImage"/>'s sprite per state (see <see cref="SpriteState"/>).</summary>
    SpriteSwap,
}
