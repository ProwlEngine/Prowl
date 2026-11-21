// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.OrigamiUI;

/// <summary>Shared pieces of arrow key navigation for widgets that list items.</summary>
internal static class KeyNav
{
    private const string PressedKey = "keynav:pressed";

    /// <summary>Whether focus sits on the owner or inside it, with no text input holding the keyboard.</summary>
    public static bool FocusWithin(Paper paper, ElementHandle owner) =>
        paper.IsElementFocusWithin(owner.Data.ID) && !paper.WantsCaptureKeyboard;

    /// <summary>Notes that one of the owner's rows was pressed, for <see cref="TakeFocus"/> to act on next frame.</summary>
    public static void MarkPressed(Paper paper, ElementHandle owner) => paper.SetElementStorage(owner, PressedKey, true);

    /// <summary>
    /// Moves the focus from a just pressed row to the owner, which stays built when rows scroll out of view.
    /// Returns whether the owner holds the focus.
    /// </summary>
    public static bool TakeFocus(Paper paper, ElementHandle owner)
    {
        if (paper.GetElementStorage(owner, PressedKey, false))
        {
            paper.SetElementStorage(owner, PressedKey, false);
            if (!paper.WantsCaptureKeyboard) paper.SetFocus(owner);
        }
        return paper.FocusedElementId == owner.Data.ID;
    }

    /// <summary>-1 for a press of the previous arrow, 1 for the next one, 0 for neither.</summary>
    public static int Step(Paper paper, bool vertical = true, bool horizontal = false)
    {
        if (vertical && paper.IsKeyPressedOrRepeating(PaperKey.Down) || horizontal && paper.IsKeyPressedOrRepeating(PaperKey.Right)) return 1;
        if (vertical && paper.IsKeyPressedOrRepeating(PaperKey.Up) || horizontal && paper.IsKeyPressedOrRepeating(PaperKey.Left)) return -1;
        return 0;
    }

    /// <summary>The next usable index from <paramref name="current"/> in the step's direction, or <paramref name="current"/> when there is none.</summary>
    public static int Next(int current, int count, int step, Func<int, bool>? usable = null)
    {
        int start = current < 0 ? (step > 0 ? 0 : count - 1) : current + step;
        for (int i = start; i >= 0 && i < count; i += step)
            if (usable == null || usable(i)) return i;
        return current;
    }

    /// <summary>The first or last usable index, for Home and End.</summary>
    public static int Edge(int count, bool last, Func<int, bool>? usable = null) =>
        Next(-1, count, last ? -1 : 1, usable);
}
