// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Prowl.Runtime.Events {

/// <summary>
/// Lightweight accessor for a parameterless event slot.
/// Supports <c>+=</c> / <c>-=</c> for subscribe/unsubscribe and
/// <see cref="Invoke"/> for firing the event.
/// </summary>
/// <remarks>
/// This is a <see langword="readonly struct"/> returned by generated event
/// properties. The no-op setter on the property allows
/// <c>Domain.OnFoo += handler</c> to compile (expands to
/// <c>Domain.OnFoo = Domain.OnFoo + handler</c>).
/// </remarks>
public readonly struct EventAccessor<TEnum> where TEnum : struct, Enum
{
    private readonly EventManager<TEnum> _manager;
    private readonly TEnum _eventType;

    public EventAccessor(EventManager<TEnum> manager, TEnum eventType)
    {
        _manager = manager;
        _eventType = eventType;
    }

    /// <summary>Fire the parameterless event.</summary>
    public void Invoke() => _manager.InvokeEvent(_eventType);

    /// <summary>Asynchronously fire the parameterless event, awaiting async handlers.</summary>
    public Task InvokeAsync() => _manager.InvokeEventAsync(_eventType);

    public static EventAccessor<TEnum> operator +(EventAccessor<TEnum> accessor, Action handler)
    {
        accessor._manager.AddNewDelegate(accessor._eventType, handler, default(ExecutionOrder));
        return accessor;
    }

    public static EventAccessor<TEnum> operator -(EventAccessor<TEnum> accessor, Action handler)
    {
        accessor._manager.RemoveDelegate(accessor._eventType, handler);
        return accessor;
    }

    public static EventAccessor<TEnum> operator +(EventAccessor<TEnum> accessor, Func<Task> handler)
    {
        accessor._manager.AddNewAsyncDelegate(accessor._eventType, handler, default(ExecutionOrder));
        return accessor;
    }

    public static EventAccessor<TEnum> operator -(EventAccessor<TEnum> accessor, Func<Task> handler)
    {
        accessor._manager.RemoveDelegate(accessor._eventType, handler);
        return accessor;
    }

    /// <summary>
    /// Subscribes a parameterless handler to the event. Supports order, tags, and source location capture.
    /// Dispose the returned container to unsubscribe.
    /// </summary>
    public EventDelegateContainer<TEnum, Unit> Subscribe(
        Action handler, ExecutionOrder order = default,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null,
        string[]? tags = null)
        => _manager.AddNewDelegate(_eventType, handler, order, sourceFile, sourceLine, sourceMember, false, tags);

    /// <summary>
    /// Subscribes a parameterless async handler to the event. Supports order, tags, and source location capture.
    /// Dispose the returned container to unsubscribe.
    /// </summary>
    public AsyncEventDelegateContainer<TEnum, Unit> SubscribeAsync(
        Func<Task> handler, ExecutionOrder order = default,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null,
        string[]? tags = null)
        => _manager.AddNewAsyncDelegate(_eventType, handler, order, sourceFile, sourceLine, sourceMember, false, tags);

    /// <summary>
    /// Subscribes a parameterless handler that automatically unsubscribes after one invocation.
    /// </summary>
    public OneTimeParameterlessEventDelegateContainer<TEnum> SubscribeOnce(
        Action handler, ExecutionOrder order = default,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null,
        string[]? tags = null)
        => _manager.SubscribeOnce(_eventType, handler, order, sourceFile, sourceLine, sourceMember, tags);
}

/// <summary>
/// Lightweight accessor for a typed event slot.
/// Supports <c>+=</c> / <c>-=</c> for subscribe/unsubscribe and
/// <see cref="Invoke"/> for firing the event with arguments.
/// </summary>
public readonly struct EventAccessor<TEnum, TArgs> where TEnum : struct, Enum
{
    private readonly EventManager<TEnum> _manager;
    private readonly TEnum _eventType;

    public EventAccessor(EventManager<TEnum> manager, TEnum eventType)
    {
        _manager = manager;
        _eventType = eventType;
    }

    /// <summary>Fire the event with the given arguments.</summary>
    public void Invoke(TArgs args) => _manager.InvokeEvent<TArgs>(_eventType, args);

    /// <summary>Asynchronously fire the event with the given arguments, awaiting async handlers.</summary>
    public Task InvokeAsync(TArgs args) => _manager.InvokeEventAsync<TArgs>(_eventType, args);

    public static EventAccessor<TEnum, TArgs> operator +(EventAccessor<TEnum, TArgs> accessor, Action<TArgs> handler)
    {
        accessor._manager.AddNewDelegate<TArgs>(accessor._eventType, handler, default(ExecutionOrder));
        return accessor;
    }

    public static EventAccessor<TEnum, TArgs> operator -(EventAccessor<TEnum, TArgs> accessor, Action<TArgs> handler)
    {
        accessor._manager.RemoveDelegate(accessor._eventType, handler);
        return accessor;
    }

    public static EventAccessor<TEnum, TArgs> operator +(EventAccessor<TEnum, TArgs> accessor, Func<TArgs, Task> handler)
    {
        accessor._manager.AddNewAsyncDelegate<TArgs>(accessor._eventType, handler, default(ExecutionOrder));
        return accessor;
    }

    public static EventAccessor<TEnum, TArgs> operator -(EventAccessor<TEnum, TArgs> accessor, Func<TArgs, Task> handler)
    {
        accessor._manager.RemoveDelegate(accessor._eventType, handler);
        return accessor;
    }

    /// <summary>
    /// Subscribes a typed handler to the event. Supports order, tags, and source location capture.
    /// Dispose the returned container to unsubscribe.
    /// </summary>
    public EventDelegateContainer<TEnum, TArgs> Subscribe(
        Action<TArgs> handler, ExecutionOrder order = default,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null,
        string[]? tags = null)
        => _manager.AddNewDelegate<TArgs>(_eventType, handler, order, sourceFile, sourceLine, sourceMember, false, tags);

    /// <summary>
    /// Subscribes a typed async handler to the event. Supports order, tags, and source location capture.
    /// Dispose the returned container to unsubscribe.
    /// </summary>
    public AsyncEventDelegateContainer<TEnum, TArgs> SubscribeAsync(
        Func<TArgs, Task> handler, ExecutionOrder order = default,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null,
        string[]? tags = null)
        => _manager.AddNewAsyncDelegate<TArgs>(_eventType, handler, order, sourceFile, sourceLine, sourceMember, false, tags);

    /// <summary>
    /// Subscribes a typed handler that automatically unsubscribes after one invocation.
    /// </summary>
    public OneTimeEventDelegateContainer<TEnum, TArgs> SubscribeOnce(
        Action<TArgs> handler, ExecutionOrder order = default,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string? sourceMember = null,
        string[]? tags = null)
        => _manager.SubscribeOnce<TArgs>(_eventType, handler, order, sourceFile, sourceLine, sourceMember, tags);
}

}
