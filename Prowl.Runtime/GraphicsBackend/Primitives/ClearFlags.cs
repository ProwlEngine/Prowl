using System;

namespace Prowl.Runtime.GraphicsBackend.Primitives
{
    [Flags]
    public enum ClearFlags
    {
        Color = 1 << 1,
        Depth = 1 << 2,
        Stencil = 1 << 3,
    }
}
