// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Navigation;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshAreas"/> and <see cref="NavMeshAgentTypes"/> under concurrent access:
/// both are process-wide statics a worker thread can read mid-query (<see cref="NavMeshQuery.FindPath"/>
/// with a non-default area mask rebuilds its filter, and so its area-cost snapshot, on every call - see
/// <see cref="NavMeshQuery"/>'s own filter-building code) while the main thread edits them, the same
/// shape of access a project's navigation settings UI produces while agents are pathing. Both registries
/// publish a whole new table per write rather than mutating one in place, so neither an enumeration nor
/// a read of a single slot should ever throw or see a torn value, regardless of how the two race.
/// </summary>
public class NavMeshRegistryThreadSafetyTests
{
    public NavMeshRegistryThreadSafetyTests()
    {
        NavMeshAgentTypes.ResetDefault();
        NavMeshAreas.ResetDefault();
    }

    [Fact]
    public void ConcurrentReadsOfNavMeshAreas_WhileMainThreadWrites_NeverThrowOrTornRead()
    {
        using var cts = new CancellationTokenSource();
        Exception? readerException = null;

        Task[] readers = new Task[4];
        for (int r = 0; r < readers.Length; r++)
        {
            readers[r] = Task.Run(() =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
                        {
                            string name = NavMeshAreas.GetAreaName(i);
                            float cost = NavMeshAreas.GetAreaCost(i);
                            Assert.NotNull(name);
                            Assert.True(cost >= 1f);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref readerException, ex, null);
                }
            });
        }

        for (int i = 0; i < 500; i++)
        {
            int slot = 3 + i % (NavMeshAreas.MaxAreas - 3); // stay off the reserved 0-2 slots
            NavMeshAreas.SetAreaCost(slot, 1f + i % 10);
            NavMeshAreas.SetAreaName(slot, $"Area{i}");
        }

        cts.Cancel();
        Task.WaitAll(readers, TimeSpan.FromSeconds(10));

        Assert.Null(readerException);
    }

    [Fact]
    public void ConcurrentReadsOfNavMeshAgentTypes_WhileMainThreadAddsAndRemoves_NeverThrow()
    {
        using var cts = new CancellationTokenSource();
        Exception? readerException = null;

        Task[] readers = new Task[4];
        for (int r = 0; r < readers.Length; r++)
        {
            readers[r] = Task.Run(() =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        foreach (NavMeshAgentTypeInfo type in NavMeshAgentTypes.Types)
                            Assert.NotNull(type.Name);

                        NavMeshAgentTypes.GetById(NavMeshAgentTypes.HumanoidId);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref readerException, ex, null);
                }
            });
        }

        for (int i = 0; i < 200; i++)
        {
            int id = NavMeshAgentTypes.Add($"Type{i}", 0.5f, 2f, 45f, 0.4f);
            NavMeshAgentTypes.Remove(id);
        }

        cts.Cancel();
        Task.WaitAll(readers, TimeSpan.FromSeconds(10));

        Assert.Null(readerException);
    }
}
