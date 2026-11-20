// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>A world's identifier index lets go of what was destroyed, and answers lookups as it did around that.</summary>
public class IdentifierIndexTests : RuntimeTestBase
{
    [Fact]
    public void ADestroyedObject_IsNotKeptAliveByItsWorld()
    {
        SceneInstance world = CreateScene();
        (WeakReference go, WeakReference component) = Spawn();
        world.Flush();

        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(go.IsAlive);
        Assert.False(component.IsAlive);
        GC.KeepAlive(world);

        // Apart, so no local of this method keeps either alive.
        [MethodImpl(MethodImplOptions.NoInlining)]
        (WeakReference, WeakReference) Spawn()
        {
            GameObject spawned = world.Create("Spawned");
            PlainComponent plain = spawned.AddComponent<PlainComponent>();
            spawned.Dispose();
            return (new WeakReference(spawned), new WeakReference(plain));
        }
    }

    [Fact]
    public void AComponentDisposedOnItsOwn_IsNotKeptAliveByItsWorld()
    {
        SceneInstance world = CreateScene();
        WeakReference component = Spawn();
        world.Flush();

        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(component.IsAlive);
        GC.KeepAlive(world);

        // The second to join with an identifier, looked up within its section, is cached apart from the world wide index.
        [MethodImpl(MethodImplOptions.NoInlining)]
        WeakReference Spawn()
        {
            GameObject first = new("First"), second = new("Second");
            PlainComponent twin = second.AddComponent<PlainComponent>();
            twin.Identifier = first.AddComponent<PlainComponent>().Identifier;
            world.Add(first);
            world.Add(second, world.CreateSection("Other"));
            Assert.Same(twin, world.FindObject(second.Section!, twin.Identifier));
            twin.Dispose();
            return new WeakReference(twin);
        }
    }

    [Fact]
    public void ARemovedComponent_IsStillFound_UntilItIsDisposed()
    {
        SceneInstance world = CreateScene();
        GameObject go = world.Create("Go");
        PlainComponent removed = go.AddComponent<PlainComponent>();

        go.RemoveComponent(removed);
        Assert.False(removed.IsDisposed);
        Assert.Same(removed, world.FindObject(removed.Identifier));

        removed.Dispose();
        Assert.Null(world.FindObject(removed.Identifier));
    }

    [Fact]
    public void WithTheFirstHolderDestroyed_ALookupAnswersWithTheNextOne_AndAnotherJoinerDoesNotTakeItsPlace()
    {
        SceneInstance world = CreateScene();
        SceneSection other = world.CreateSection("Other");
        GameObject first = world.Create(other, "First");
        var second = new GameObject("Second");
        second.SetIdentifier(first.Identifier);
        world.Add(second);
        Guid shared = first.Identifier;
        Assert.Same(first, world.FindObject(shared));

        first.Dispose();
        Assert.Same(second, world.FindObject(shared));
        world.Flush();
        Assert.Same(second, world.FindObject(shared));

        var third = new GameObject("Third");
        third.SetIdentifier(shared);
        world.Add(third, other);
        Assert.Same(third, world.FindObject(shared));
        Assert.Same(second, world.FindObject(world.ActiveSection, shared));
    }
}
