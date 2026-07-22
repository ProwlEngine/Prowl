// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.PaperUI.LayoutEngine;

internal struct UISize
{
    public float Main;
    public float Cross;

    public UISize(float main, float cross)
    {
        Main = main;
        Cross = cross;
    }
}
