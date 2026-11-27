// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

/// <summary> Represents a keyboard key event, including the source element, the pressed key, and whether the event is a repeated key press. </summary>
public class KeyEvent
{
	/// <summary> The element that triggered the event. </summary>
    public ElementHandle Source { get; }
    /// <summary> Gets the key associated with this event. </summary>
    public PaperKey Key { get; }
    public bool IsRepeat { get; }

    public KeyEvent(ElementHandle source, PaperKey key, bool isRepeat)
    {
        Source = source;
        Key = key;
        IsRepeat = isRepeat;
    }
}
