// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Events;

public class KeyEvent
{
    public ElementHandle Source { get; }
    public PaperKey Key { get; }
    public bool IsRepeat { get; }

    public KeyEvent(ElementHandle source, PaperKey key, bool isRepeat)
    {
        Source = source;
        Key = key;
        IsRepeat = isRepeat;
    }
}
