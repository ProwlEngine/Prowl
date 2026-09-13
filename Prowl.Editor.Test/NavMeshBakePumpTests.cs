// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading.Tasks;

using Prowl.Editor.Inspector;
using Prowl.Runtime;
using Prowl.Runtime.Navigation;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Exercises <see cref="NavMeshBakePump"/>'s queue/drain logic in isolation, using a manually
/// completed <see cref="TaskCompletionSource{T}"/> in place of a real <see cref="NavMeshBakeJob"/> - a
/// pending bake stays pending until picked up, a completed one fires its callback exactly once and is
/// then gone, and a faulted one is dropped without ever calling its callback or throwing.
/// </summary>
public class NavMeshBakePumpTests : EditorTestHarness
{
    private static NavMeshSurface NewSurface(string name) => new GameObject(name).AddComponent<NavMeshSurface>();

    [Fact]
    public void PendingBake_IsBakingTrue_ProcessCompletedDoesNotFireYet()
    {
        NavMeshSurface surface = NewSurface("Pending");
        var source = new TaskCompletionSource<NavMeshBuildResult>();
        bool fired = false;

        NavMeshBakePump.Enqueue(surface, source.Task, _ => fired = true);
        Assert.True(NavMeshBakePump.IsBaking(surface));

        NavMeshBakePump.ProcessCompleted();

        Assert.True(NavMeshBakePump.IsBaking(surface));
        Assert.False(fired);

        source.SetResult(NavMeshBuildResult.Failed("cleanup"));
        NavMeshBakePump.ProcessCompleted();
    }

    [Fact]
    public void CompletedBake_FiresCallbackOnceAndStopsBeingReportedAsBaking()
    {
        NavMeshSurface surface = NewSurface("Completed");
        var source = new TaskCompletionSource<NavMeshBuildResult>();
        var expected = new NavMeshBuildResult { Success = true };

        int callCount = 0;
        NavMeshBuildResult? received = null;
        NavMeshBakePump.Enqueue(surface, source.Task, result => { callCount++; received = result; });

        source.SetResult(expected);
        NavMeshBakePump.ProcessCompleted();

        Assert.Equal(1, callCount);
        Assert.Same(expected, received);
        Assert.False(NavMeshBakePump.IsBaking(surface));

        // A second drain after the entry is already gone must not fire the callback again.
        NavMeshBakePump.ProcessCompleted();
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void FaultedBake_IsDroppedWithoutFiringItsCallbackOrThrowing()
    {
        NavMeshSurface surface = NewSurface("Faulted");
        var source = new TaskCompletionSource<NavMeshBuildResult>();
        bool fired = false;

        NavMeshBakePump.Enqueue(surface, source.Task, _ => fired = true);
        source.SetException(new InvalidOperationException("simulated bake failure"));

        NavMeshBakePump.ProcessCompleted(); // must not throw

        Assert.False(fired);
        Assert.False(NavMeshBakePump.IsBaking(surface));
    }

    [Fact]
    public void EnqueueingASecondBakeForTheSameSurface_ReplacesThePendingOne()
    {
        NavMeshSurface surface = NewSurface("Replaced");
        var firstSource = new TaskCompletionSource<NavMeshBuildResult>();
        var secondSource = new TaskCompletionSource<NavMeshBuildResult>();

        bool firstFired = false, secondFired = false;
        NavMeshBakePump.Enqueue(surface, firstSource.Task, _ => firstFired = true);
        NavMeshBakePump.Enqueue(surface, secondSource.Task, _ => secondFired = true); // e.g. a rebake started before the first landed

        firstSource.SetResult(new NavMeshBuildResult { Success = true });
        secondSource.SetResult(new NavMeshBuildResult { Success = true });
        NavMeshBakePump.ProcessCompleted();

        Assert.False(firstFired, "The replaced bake's callback should never fire.");
        Assert.True(secondFired);
    }
}
