// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

/// <summary> Event data for a text input action, providing the source element and the character typed. </summary>
public class TextInputEvent
{
    /// <summary> The element that received the text input. </summary>
    public ElementHandle Source { get; }
    /// <summary> Gets the character that was entered by the user. </summary>
    public char Character { get; }

    /// <summary> Initializes a new TextInputEvent with the given source element and typed character. </summary>
    public TextInputEvent(ElementHandle source, char character)
    {
        Source = source;
        Character = character;
    }
}
