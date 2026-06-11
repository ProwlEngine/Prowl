// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Runtime.Events;

public partial class EventManager<T> where T : struct, Enum
{
    /// <summary>
    /// Returns a <see cref="Task{TArgs}"/> that completes the next time
    /// <paramref name="eventType"/> is invoked with arguments of type
    /// <typeparamref name="TArgs"/>. The internal subscription is removed
    /// automatically once the task transitions to a final state.
    /// </summary>
    /// <param name="eventType">The event to await.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait. When triggered, the returned task transitions to
    /// <see cref="TaskStatus.Canceled"/> and the subscription is disposed.
    /// </param>
    /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TArgs"/> does not match the contract declared by
    /// <see cref="EventArgsAttribute"/>.
    /// </exception>
    public Task<TArgs> WaitForEventAsync<TArgs>(
        T eventType,
        CancellationToken cancellationToken = default,
#if DEBUG
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null
#else
        string? sourceFile = null,
        int sourceLine = 0,
        string? sourceMember = null
#endif
    )
    {
        return WaitForEventAsync<TArgs>(eventType, predicate: null, cancellationToken, sourceFile, sourceLine, sourceMember);
    }

    /// <summary>
    /// Returns a <see cref="Task{TArgs}"/> that completes the next time
    /// <paramref name="eventType"/> is invoked with arguments matching
    /// <paramref name="predicate"/>. Invocations for which the predicate
    /// returns <c>false</c> are ignored — the wait continues until the
    /// predicate matches, the token is cancelled, or this manager is disposed.
    /// </summary>
    public Task<TArgs> WaitForEventAsync<TArgs>(
        T eventType,
        Func<TArgs, bool>? predicate,
        CancellationToken cancellationToken = default,
#if DEBUG
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null
#else
        string? sourceFile = null,
        int sourceLine = 0,
        string? sourceMember = null
#endif
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!EventArgsContract<T>.IsValid<TArgs>(eventType))
        {
            throw new InvalidOperationException(
                $"[EventSystem] Type mismatch on {typeof(T).Name}.{eventType}: " +
                $"WaitForEventAsync invoked with '{typeof(TArgs).Name}' but the event " +
                $"declares '{EventArgsContract<T>.GetDeclaredName(eventType)}' via [EventArgs].");
        }

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TArgs>(cancellationToken);

        TaskCompletionSource<TArgs> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WaitState<TArgs> state = new(tcs, predicate);

        EventDelegateContainer<T, TArgs> container = new(
            eventType, state.Handle, default, sourceFile, sourceLine, sourceMember);
        state.Subscription = container;
        AddDelegate(container);

        if (cancellationToken.CanBeCanceled)
        {
            state.Registration = cancellationToken.Register(static s =>
            {
                WaitState<TArgs> ws = (WaitState<TArgs>)s!;
                ws.Cancel();
            }, state);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Awaits the next firing of a parameterless event. Equivalent to the
    /// generic <c>WaitForEventAsync&lt;Unit&gt;</c> overload.
    /// </summary>
    public async Task WaitForEventAsync(
        T eventType,
        CancellationToken cancellationToken = default,
#if DEBUG
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null
#else
        string? sourceFile = null,
        int sourceLine = 0,
        string? sourceMember = null
#endif
    )
    {
        await WaitForEventAsync<Unit>(eventType, cancellationToken, sourceFile, sourceLine, sourceMember).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds the completion source, optional predicate, subscription, and
    /// cancellation registration for a single <see cref="WaitForEventAsync{TArgs}"/>
    /// call. Encapsulates the cleanup logic so completion, cancellation, and
    /// predicate paths all share the same teardown.
    /// </summary>
    private sealed class WaitState<TArgs>
    {
        private readonly TaskCompletionSource<TArgs> _tcs;
        private readonly Func<TArgs, bool>? _predicate;

        internal EventDelegateContainer<T, TArgs>? Subscription;
        internal CancellationTokenRegistration Registration;

        internal WaitState(TaskCompletionSource<TArgs> tcs, Func<TArgs, bool>? predicate)
        {
            _tcs = tcs;
            _predicate = predicate;
        }

        internal void Handle(TArgs args)
        {
            if (_predicate is not null && !_predicate(args))
                return;

            if (_tcs.TrySetResult(args))
                Cleanup();
        }

        internal void Cancel()
        {
            if (_tcs.TrySetCanceled())
                Cleanup();
        }

        private void Cleanup()
        {
            Subscription?.Dispose();
            Registration.Dispose();
        }
    }
}
