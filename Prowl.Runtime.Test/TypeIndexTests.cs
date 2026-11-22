// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>A world's per type indices, behind Get and GetAll, let go of destroyed components without waiting for the next query.</summary>
public class TypeIndexTests : RuntimeTestBase
{
    private interface IMarked;

    private sealed class Marked : Component, IMarked;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AComponentDestroyedAfterItsTypeWasQueried_IsNotKeptAliveByItsWorld(bool componentAlone)
    {
        Scene world = CreateScene();
        GameObject kept = world.Create("Kept");
        // Queried once, by class, by interface and by base class, and never again.
        Assert.Empty(world.GetAll<Marked>(includeDisabled: true));
        Assert.Empty(world.GetAll<IMarked>(includeDisabled: true));
        world.GetAll<Component>(includeDisabled: true);

        (WeakReference go, WeakReference component) = Spawn();
        world.Flush();
        CollectGarbage();

        Assert.False(component.IsAlive);
        Assert.Equal(componentAlone, go.IsAlive);
        GC.KeepAlive(kept);
        GC.KeepAlive(world);

        // Apart, so no local of this method keeps either alive.
        [MethodImpl(MethodImplOptions.NoInlining)]
        (WeakReference, WeakReference) Spawn()
        {
            GameObject spawned = componentAlone ? kept : world.Create("Spawned");
            Marked marked = spawned.AddComponent<Marked>();
            if (componentAlone) marked.Dispose();
            else spawned.Dispose();
            return (new WeakReference(spawned), new WeakReference(marked));
        }
    }

    [Fact]
    public void QueriesAnswerTheSame_BeforeAndAfterTheFlushThatDropsTheDestroyed()
    {
        Scene world = CreateScene();
        Marked first = world.Create("First").AddComponent<Marked>();
        Marked second = world.Create("Second").AddComponent<Marked>();
        Marked third = world.Create("Third").AddComponent<Marked>();
        Marked queued = world.Create("Queued").AddComponent<Marked>();
        Assert.Equal([first, second, third, queued], world.GetAll<Marked>(includeDisabled: true));
        Assert.Equal(4, world.GetAll<IMarked>(includeDisabled: true).Length);

        second.GameObject.Dispose();
        queued.GameObject.Destroy();
        Marked[] before = world.GetAll<Marked>(includeDisabled: true);
        world.Flush();

        Assert.Equal([first, third], before);
        Assert.Equal(before, world.GetAll<Marked>(includeDisabled: true));
        Assert.Equal(before, world.GetAll<IMarked>(includeDisabled: true).Cast<Marked>());

        // One that joins after the flush lands after the ones still here.
        Marked late = world.Create("Late").AddComponent<Marked>();
        Assert.Equal([first, third, late], world.GetAll<Marked>(includeDisabled: true));
    }
}
