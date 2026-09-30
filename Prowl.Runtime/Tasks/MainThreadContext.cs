// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Tasks;

/// <summary>
/// Sends async continuations back to the main thread, so <c>await</c> in game code resumes where the
/// scene actually lives.
/// </summary>
/// <remarks>
/// Without a synchronization context everything after an <c>await</c> resumes on a thread pool thread,
/// and ordinary game code ends up touching the scene from the wrong thread, which only sometimes
/// crashes. Installing one makes await behave the way the code reads.
/// <para/>
/// Each context is one session. The editor ends a session when play starts or stops and when scripts
/// reload: the session token is cancelled and every continuation still waiting on it is dropped, so a
/// method that awaited during play never resumes against the edit scene or runs code that was reloaded.
/// Blocking <see cref="Send"/> calls are the exception and carry over, since a thread is waiting on them.
/// </remarks>
public sealed class MainThreadContext : SynchronizationContext
{
    private readonly record struct Entry(SendOrPostCallback Callback, object? State, bool Carry);
    private readonly record struct FrameWaiter(TaskCompletionSource Completion, CancellationTokenRegistration Registration);

    private readonly ConcurrentQueue<Entry> _queue = new();
    private readonly ConcurrentQueue<FrameWaiter> _frameWaiters = new();
    private readonly CancellationTokenSource _session = new();
    private readonly object _gate = new();
    private readonly int _threadId;

    private MainThreadContext? _successor;

    // Zero when no loop runs. Kept static and separate from Current so the hot path is one field read.
    private static int s_loopThreadId;

    private MainThreadContext(int threadId) => _threadId = threadId;

    /// <summary>The context the engine installed, or null when no loop is running.</summary>
    public static MainThreadContext? Current { get; private set; }

    /// <summary>Whether the caller is on the thread the engine pumps.</summary>
    public bool IsMainThread => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>
    /// Whether the caller is on the engine's thread, for main thread only APIs to check themselves.
    /// True when no context has been installed (tests, tools, early startup), since there is no engine
    /// thread to be off yet and refusing to run would be worse than the race being guarded against.
    /// </summary>
    public static bool OnMainThread
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            int loop = s_loopThreadId;
            return loop == 0 || Environment.CurrentManagedThreadId == loop;
        }
    }

    /// <summary>Cancelled when this session ends.</summary>
    public CancellationToken SessionToken => _session.Token;

    /// <summary>Whether a newer session has replaced this one.</summary>
    public bool IsEnded => Volatile.Read(ref _successor) != null;

    /// <summary>How much work is waiting for the next pump.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>Completes at the start of the next <see cref="Pump"/>, or is cancelled if the session ends first.</summary>
    public Task NextFrame(CancellationToken cancel = default)
    {
        if (cancel.IsCancellationRequested) return Task.FromCanceled(cancel);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = cancel.CanBeCanceled
            ? cancel.Register(static (s, token) => ((TaskCompletionSource)s!).TrySetCanceled(token), completion)
            : default;

        lock (_gate)
        {
            if (_successor == null)
            {
                _frameWaiters.Enqueue(new FrameWaiter(completion, registration));
                return completion.Task;
            }
        }

        registration.Dispose();
        return Task.FromCanceled(_session.Token);
    }

    /// <summary>Installs a context bound to the calling thread, which must be the one running the loop.</summary>
    public static void Install()
    {
        Current?.End(null);
        Bind(new MainThreadContext(Environment.CurrentManagedThreadId));
    }

    /// <summary>
    /// Ends the current session and starts a new one on the same thread. Must be called on the main thread.
    /// </summary>
    public static void Restart()
    {
        MainThreadContext? old = Current;
        if (old == null) return;
        if (!old.IsMainThread)
            throw new InvalidOperationException("MainThreadContext.Restart must be called on the main thread.");

        var next = new MainThreadContext(old._threadId);
        Bind(next);
        old.End(next);
    }

    /// <summary>Ends the session and removes the context, for when the loop stops.</summary>
    public static void Uninstall()
    {
        MainThreadContext? old = Current;
        if (old == null) return;

        Current = null;
        s_loopThreadId = 0;
        if (ReferenceEquals(SynchronizationContext.Current, old)) SetSynchronizationContext(null);
        old.End(null);
    }

    private static void Bind(MainThreadContext context)
    {
        Current = context;
        s_loopThreadId = context._threadId;
        SetSynchronizationContext(context);
    }

    private void End(MainThreadContext? successor)
    {
        lock (_gate)
        {
            _successor = successor ?? this;

            // Whoever is blocked in Send is waiting on these, so they move to the new session or, with no
            // loop left to run them, run here.
            while (_queue.TryDequeue(out Entry entry))
            {
                if (!entry.Carry) continue;
                if (successor != null) successor._queue.Enqueue(entry);
                else entry.Callback(entry.State);
            }
        }

        try { _session.Cancel(); }
        catch (AggregateException e) { Debug.LogError($"[Tasks] A session cancellation callback threw: {e.InnerException?.Message}\n{e.InnerException?.StackTrace}"); }

        while (_frameWaiters.TryDequeue(out FrameWaiter waiter))
        {
            waiter.Registration.Dispose();
            waiter.Completion.TrySetCanceled(_session.Token);
        }
    }

    // Follows the chain of sessions to the live one, for work that must run whichever session it was queued in.
    private MainThreadContext Live()
    {
        MainThreadContext context = this;
        while (Volatile.Read(ref context._successor) is { } next && !ReferenceEquals(next, context))
            context = next;
        return context;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_gate)
        {
            if (_successor != null) return;
            _queue.Enqueue(new Entry(d, state, false));
        }
    }

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

        var entry = new Entry(_ =>
        {
            try { d(state); }
            catch (Exception e) { failure = e; }
            finally { done.Set(); }
        }, null, true);

        MainThreadContext target = Live();
        while (true)
        {
            lock (target._gate)
            {
                if (target._successor == null)
                {
                    target._queue.Enqueue(entry);
                    break;
                }
            }

            MainThreadContext next = target.Live();
            if (ReferenceEquals(next, target))
            {
                // Uninstalled: no loop will ever pump again, so run it on this thread.
                entry.Callback(null);
                break;
            }
            target = next;
        }

        done.Wait();
        if (failure != null) throw failure;
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Runs everything queued for the main thread. Called once per frame by the game loop.
    /// </summary>
    /// <remarks>
    /// Drains a snapshot of what was already waiting rather than looping until empty, so a continuation
    /// that queues more work cannot keep the pump running and stall the frame.
    /// </remarks>
    public void Pump()
    {
        // First, so a continuation waiting on the frame is queued in time to run in this same pump.
        int waiting = _frameWaiters.Count;
        for (int i = 0; i < waiting && _frameWaiters.TryDequeue(out FrameWaiter waiter); i++)
        {
            waiter.Registration.Dispose();
            waiter.Completion.TrySetResult();
        }

        int pending = _queue.Count;

        for (int i = 0; i < pending && !IsEnded && _queue.TryDequeue(out var entry); i++)
        {
            try { entry.Callback(entry.State); }
            catch (OperationCanceledException) { /* work that ended with its session */ }
            catch (Exception e) { Debug.LogError($"[Tasks] A queued continuation threw: {e.Message}\n{e.StackTrace}"); }
        }
    }

    /// <summary>
    /// Throws when a live scene is touched from a thread other than the main one while a loop is running.
    /// Objects outside a running scene are free to build on any thread, which is how background loading works.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AssertOwner(GameObject? go, [CallerMemberName] string member = "")
    {
        if (OnMainThread) return;
        if (go is not null && go.Scene is { IsDisposed: false, IsActive: true }) ThrowOffThread(member);
    }

    /// <inheritdoc cref="AssertOwner(GameObject?, string)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AssertOwner(Scene? scene, [CallerMemberName] string member = "")
    {
        if (OnMainThread) return;
        if (scene is { IsDisposed: false, IsActive: true }) ThrowOffThread(member);
    }

    /// <summary>Throws when called off the main thread while a loop is running, whatever is being touched.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AssertMainThread([CallerMemberName] string member = "")
    {
        if (!OnMainThread) ThrowOffThread(member);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOffThread(string member)
        => throw new InvalidOperationException(
            $"{member} must be called on the main thread, and was called from thread {Environment.CurrentManagedThreadId}. " +
            "Use 'await GameTask.MainThread()' or GameTask.Post to get back there.");
}
