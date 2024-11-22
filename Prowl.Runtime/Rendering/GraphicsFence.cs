// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public sealed class GraphicsFence : IDisposable
{
    public Fence Fence;

    public bool Signaled => Fence.Signaled;


    public GraphicsFence(bool signaled = false)
    {
        Fence = Graphics.Factory.CreateFence(signaled);
    }


    public void Reset()
        => Fence.Reset();


    public void Dispose()
    {
        Fence.Dispose();
        Fence = null;
    }
}
