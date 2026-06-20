using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Prowl.Graphite.Compiler.Tests;


// Slang creates a single process-wide global session on the thread that first touches it, and its
// child sessions are affine to that thread. xUnit's executor can resume sequential tests on
// different thread-pool threads, which would use the global session off its owning thread and crash
// the native layer. Routing every Slang interaction (including compiler construction, which calls
// GlobalSession.FindProfile) through this one dedicated thread keeps all access single-threaded.
internal static class SlangThread
{
    static readonly BlockingCollection<Action> s_work = new();

    static SlangThread()
    {
        Thread thread = new(() =>
        {
            foreach (Action work in s_work.GetConsumingEnumerable())
                work();
        })
        {
            IsBackground = true,
            Name = "Slang",
        };

        thread.Start();
    }

    // Runs func on the dedicated Slang thread and blocks until it completes, propagating exceptions.
    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        ExceptionDispatchInfo error = null;
        using ManualResetEventSlim done = new(false);

        s_work.Add(() =>
        {
            try { result = func(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
            finally { done.Set(); }
        });

        done.Wait();
        error?.Throw();
        return result;
    }
}
