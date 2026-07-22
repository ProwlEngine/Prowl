// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.PaperUI.LayoutEngine;

internal class ChildElementInfo
{
    public ElementHandle Element;
    public float CrossBefore;
    public float Cross;
    public float CrossAfter;
    public float MainBefore;
    public float Main;
    public float MainAfter;

    public ChildElementInfo(ElementHandle element)
    {
        Element = element;
    }
}
