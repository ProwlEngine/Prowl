using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class CompileQueueTests
{
    // The compile threads never exit, so whatever one copied from the thread that started it would never be freed.
    [Fact]
    public void CompileThreadsDoNotKeepTheAsyncLocalsOfTheThreadThatStartedThem()
    {
        AsyncLocal<object?> local = new();
        WeakReference held = HoldInAsyncLocal(local);

        // Each job is running before the next is queued, so no thread is idle and the queue starts every thread
        // it has not started yet from here.
        using ManualResetEventSlim release = new();
        List<Task> jobs = [];
        for (int i = 0; i < CompileQueue.ThreadCount; i++)
        {
            ManualResetEventSlim started = new();
            jobs.Add(CompileQueue.Enqueue(() => { started.Set(); release.Wait(); }, CompilePriority.Fallback).Done);
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)), "a queued job never started");
        }
        release.Set();
        Task.WaitAll(jobs.ToArray());

        local.Value = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(held.IsAlive, "a compile thread kept an object its starter held in an AsyncLocal");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HoldInAsyncLocal(AsyncLocal<object?> local)
    {
        object value = new byte[1 << 20];
        local.Value = value;
        return new WeakReference(value);
    }
}
