// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

public class FocusEvent
{
    public Element Source { get; }
    public bool IsFocused { get; }

    public FocusEvent(Element source, bool isFocused)
    {
        Source = source;
        IsFocused = isFocused;
    }
}
