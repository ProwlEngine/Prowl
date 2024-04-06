using System;

namespace Prowl.Runtime.Rendering
{
    [Flags]
    public enum ClearFlags
    {
        Color = 1 << 1,
        Depth = 1 << 2,
        Stencil = 1 << 3,
    }
}
