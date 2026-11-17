// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Threading;

using Xunit;

namespace Prowl.Runtime.Test;

public class PhysicsWorkerTests
{
    // Jitter starts its worker threads once and never stops them, so whatever they copied from the thread that
    // started them, the active scene included, would never be freed. The workers start once per process, so this
    // checks nothing when an earlier test already stepped physics.
    [Fact]
    public void PhysicsWorkers_DoNotKeepTheAsyncLocalsOfTheThreadThatStartedThem()
    {
        AsyncLocal<object?> local = new();
        WeakReference held = HoldInAsyncLocal(local);
        PhysicsWorld world = new();
        world.World.Step(1f / 60f, true);
        local.Value = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(held.IsAlive, "a physics worker kept an object its starter held in an AsyncLocal");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HoldInAsyncLocal(AsyncLocal<object?> local)
    {
        object value = new byte[1 << 20];
        local.Value = value;
        return new WeakReference(value);
    }
}
