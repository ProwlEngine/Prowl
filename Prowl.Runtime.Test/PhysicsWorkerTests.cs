// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Threading;

using Xunit;

using JitterParallel = Jitter2.Parallelization.Parallel;
using JitterThreadPool = Jitter2.Parallelization.ThreadPool;

namespace Prowl.Runtime.Test;

public class PhysicsWorkerTests
{
    // Jitter starts its worker threads once and never stops them, so whatever they copied from the thread that
    // started them, the active scene included, would never be freed.
    [Fact]
    public void PhysicsWorkers_RunWithoutTheExecutionContextOfTheThreadThatStartedThem()
    {
        AsyncLocal<object?> local = new() { Value = new object() };
        new PhysicsWorld().Update();
        local.Value = null;

        ExecutionContext? none = null;
        Thread bare = new(() => none = ExecutionContext.Capture());
        bare.UnsafeStart();
        bare.Join();

        // One batch per thread of the pool, each held until all have one, so every worker reports its context.
        int threads = JitterThreadPool.Instance.ThreadCount;
        int caller = Environment.CurrentManagedThreadId, reported = 0, inherited = 0;
        using Barrier all = new(threads);
        JitterParallel.ForBatch(0, threads, threads, _ =>
        {
            if (Environment.CurrentManagedThreadId != caller)
            {
                Interlocked.Increment(ref reported);
                if (!ReferenceEquals(ExecutionContext.Capture(), none)) Interlocked.Increment(ref inherited);
            }
            all.SignalAndWait(TimeSpan.FromSeconds(10));
        });

        Assert.Equal(threads - 1, reported);
        Assert.Equal(0, inherited);
    }
}
