// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public sealed class GraphicsFence : IDisposable
{
    public Fence Fence;

    internal GraphicsFence(bool signaled = false)
    {
        Fence = Graphics.Factory.CreateFence(signaled);
    }

    public void Dispose()
    {
        Fence.Dispose();
        Fence = null;
    }
}
