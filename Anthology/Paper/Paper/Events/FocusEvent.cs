// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

/// <summary> Represents a focus change event for a UI element, indicating whether the element gained or lost focus. </summary>
public class FocusEvent
{
	/// <summary> The element that triggered the event. </summary>
    public ElementHandle Source { get; }
    /// <summary> Gets whether the element gained focus (true) or lost focus (false). </summary>
    public bool IsFocused { get; }

    /// <summary> Initializes a new instance of the FocusEvent class with the given source element and focus state. </summary>
    public FocusEvent(ElementHandle source, bool isFocused)
    {
        Source = source;
        IsFocused = isFocused;
    }
}
