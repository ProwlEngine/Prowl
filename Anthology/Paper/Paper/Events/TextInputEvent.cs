// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

public class TextInputEvent
{
    public Element Source { get; }
    public char Character { get; }

    public TextInputEvent(Element source, char character)
    {
        Source = source;
        Character = character;
    }
}
