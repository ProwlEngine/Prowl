// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Tasks;

namespace Prowl.Runtime;

/// <summary>
/// Async helpers for game code: moving between the main thread and worker threads, and waiting on frames
/// and game time.
/// </summary>
/// <remarks>
/// The scene belongs to the main thread. Worker threads are for pure computation: read what you need on
/// the main thread, compute on a worker, then come back to apply the result.
/// <code>
/// var input = BuildInput();
/// await GameTask.WorkerThread();
/// var result = Compute(input);
/// await GameTask.MainThread();
/// Apply(result);
/// </code>
/// Awaiting in a component resumes on the main thread already. When play stops or scripts reload, the
/// session ends: <see cref="SessionToken"/> is cancelled and pending continuations are dropped.
/// <para/>
/// When no engine loop is running, such as in a test or a tool, every thread counts as the main thread.
/// </remarks>
public static class GameTask
{
    /// <summary>Whether the caller is on the main thread.</summary>
    public static bool IsMainThread => MainThreadContext.OnMainThread;

    /// <summary>
    /// Cancelled when the current session ends: play mode stopping, scripts reloading or the game quitting.
    /// Pass it to long running work on worker threads, which nothing else can stop.
    /// </summary>
    public static CancellationToken SessionToken => MainThreadContext.Current?.SessionToken ?? CancellationToken.None;

    /// <summary>Resumes on the main thread, at the next frame when coming from another thread.</summary>
    public static MainThreadAwaitable MainThread() => default;

    /// <summary>Resumes on a thread pool thread. Continues right away when already off the main thread.</summary>
    public static WorkerThreadAwaitable WorkerThread() => default;

    /// <summary>Completes at the start of the next frame.</summary>
    public static Task NextFrame(CancellationToken cancel = default)
    {
        MainThreadContext loop = MainThreadContext.Current
            ?? throw new InvalidOperationException("GameTask.NextFrame needs a running engine loop.");
        return loop.NextFrame(cancel);
    }

    /// <summary>Waits a number of frames.</summary>
    public static async Task Frames(int count, CancellationToken cancel = default)
    {
        for (int i = 0; i < count; i++) await NextFrame(cancel);
    }

    /// <summary>Waits for game time, so it slows with <see cref="Time.TimeScale"/> and stops while paused.</summary>
    public static async Task Delay(float seconds, CancellationToken cancel = default)
    {
        while (seconds > 0)
        {
            await NextFrame(cancel);
            seconds -= Time.DeltaTime;
        }
    }

    /// <summary>Waits for real time, ignoring time scale and pausing.</summary>
    public static async Task DelayRealtime(float seconds, CancellationToken cancel = default)
    {
        while (seconds > 0)
        {
            await NextFrame(cancel);
            seconds -= Time.UnscaledDeltaTime;
        }
    }

    /// <summary>Checks <paramref name="condition"/> once per frame until it holds.</summary>
    public static async Task WaitUntil(Func<bool> condition, CancellationToken cancel = default)
    {
        while (!condition()) await NextFrame(cancel);
    }

    /// <summary>
    /// Queues work for the main thread without waiting for it. Dropped if the session ends first.
    /// Runs at once when no loop is running.
    /// </summary>
    public static void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        MainThreadContext? loop = MainThreadContext.Current;
        if (loop == null) work();
        else loop.Post(static s => ((Action)s!)(), work);
    }

    /// <summary>
    /// Runs work on the main thread and blocks until it finishes, rethrowing what it threw. Runs at once
    /// when called on the main thread. Never call it from a thread the main thread is waiting on.
    /// </summary>
    public static void Run(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        MainThreadContext? loop = MainThreadContext.Current;
        if (loop == null) work();
        else loop.Send(static s => ((Action)s!)(), work);
    }

    /// <inheritdoc cref="Run(Action)"/>
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        T result = default!;
        Run(() => { result = work(); });
        return result;
    }

    public readonly struct MainThreadAwaitable : ICriticalNotifyCompletion
    {
        public MainThreadAwaitable GetAwaiter() => this;

        public bool IsCompleted => MainThreadContext.OnMainThread;

        public void OnCompleted(Action continuation) => Post(continuation);

        public void UnsafeOnCompleted(Action continuation) => Post(continuation);

        public void GetResult() { }
    }

    public readonly struct WorkerThreadAwaitable : ICriticalNotifyCompletion
    {
        public WorkerThreadAwaitable GetAwaiter() => this;

        public bool IsCompleted => MainThreadContext.Current != null && !MainThreadContext.Current.IsMainThread;

        public void OnCompleted(Action continuation)
            => ThreadPool.QueueUserWorkItem(static c => ((Action)c!)(), continuation);

        public void UnsafeOnCompleted(Action continuation)
            => ThreadPool.UnsafeQueueUserWorkItem(static c => ((Action)c!)(), continuation);

        public void GetResult() { }
    }
}
