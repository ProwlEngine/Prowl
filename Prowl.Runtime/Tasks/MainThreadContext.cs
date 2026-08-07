// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Runtime.Tasks;

/// <summary>
/// Sends async continuations back to the main thread, so <c>await</c> in game code resumes where the
/// scene actually lives.
/// </summary>
/// <remarks>
/// Without a synchronization context everything after an <c>await</c> resumes on a thread pool thread,
/// and ordinary game code ends up touching the scene from the wrong thread, which only sometimes
/// crashes. Installing one makes await behave the way the code reads.
/// </remarks>
public sealed class MainThreadContext : SynchronizationContext
{
    private readonly record struct Entry(SendOrPostCallback Callback, object? State);

    private readonly ConcurrentQueue<Entry> _queue = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    private TaskCompletionSource _nextFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The context the engine installed, or null before startup.</summary>
    public static MainThreadContext? Current { get; private set; }

    /// <summary>Whether the caller is on the thread the engine pumps.</summary>
    public bool IsMainThread => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>How much work is waiting for the next pump.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>Installs a context bound to the calling thread, which must be the one running the loop.</summary>
    public static void Install()
    {
        Current = new MainThreadContext();
        SetSynchronizationContext(Current);
    }

    /// <summary>Completes at the start of the next <see cref="Pump"/>.</summary>
    public Task NextFrame => _nextFrame.Task;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue(new Entry(d, state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        // Already here, so run it now. Queueing would deadlock a caller that then waits for the result.
        if (IsMainThread)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim(false);
        Exception? failure = null;

        _queue.Enqueue(new Entry(_ =>
        {
            try { d(state); }
            catch (Exception e) { failure = e; }
            finally { done.Set(); }
        }, null));

        done.Wait();
        if (failure != null) throw failure;
    }

    /// <summary>
    /// Runs everything queued for the main thread. Called once per frame by the game loop.
    /// </summary>
    /// <remarks>
    /// Drains a snapshot of what was already waiting rather than looping until empty, so a continuation
    /// that queues more work cannot keep the pump running and stall the frame.
    /// </remarks>
    public void Pump()
    {
        var frame = _nextFrame;
        _nextFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        frame.TrySetResult();

        int pending = _queue.Count;

        for (int i = 0; i < pending && _queue.TryDequeue(out var entry); i++)
        {
            try { entry.Callback(entry.State); }
            catch (OperationCanceledException) { /* work that ended with its play session */ }
            catch (Exception e) { Debug.LogError($"[Tasks] A queued continuation threw: {e.Message}\n{e.StackTrace}"); }
        }
    }
}
