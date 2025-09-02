// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

public class FocusEvent
{
    public ElementHandle Source { get; }
    public bool IsFocused { get; }

    public FocusEvent(ElementHandle source, bool isFocused)
    {
        Source = source;
        IsFocused = isFocused;
    }
}
