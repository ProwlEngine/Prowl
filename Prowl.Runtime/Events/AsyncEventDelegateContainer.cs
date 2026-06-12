// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading.Tasks;

namespace Prowl.Runtime.Events {

/// <summary>
/// Typed delegate container wrapping a <see cref="Func{TArgs, Task}"/> for async
/// event handlers. Participates in the same priority-sorted snapshot as synchronous
/// containers, enabling mixed sync/async handler chains.
/// <para>
/// When invoked via the synchronous <see cref="Invoke"/> path, the returned
/// <see cref="Task"/> is observed but not awaited. Use
/// <see cref="Event{T}.InvokeAsync{TArgs}"/> or the generated
/// <c>InvokeXxxAsync</c> methods to properly await async handlers.
/// </para>
/// </summary>
public class AsyncEventDelegateContainer<T, TArgs> : EventDelegateContainer<T, TArgs>, IAsyncInvocable<TArgs>
    where T : struct, Enum
{
    /// <inheritdoc />
    public override bool MatchesDelegate(Delegate handler) => handler != null && handler.Equals(_asyncDelegate);

    private readonly Func<TArgs, Task>? _asyncDelegate;

    public override Delegate? GetDelegate() => _asyncDelegate;

    public AsyncEventDelegateContainer(T eventType, Func<TArgs, Task> asyncDelegate, ExecutionOrder priority = default, string[]? tags = null)
        : base(eventType, priority, tags)
    {
        _asyncDelegate = asyncDelegate;
    }

public AsyncEventDelegateContainer(T eventType, Func<TArgs, Task> asyncDelegate, ExecutionOrder priority,
    string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
    : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
{
    _asyncDelegate = asyncDelegate;
}

    /// <summary>
    /// Protected constructor for subclasses that provide their own invocation
    /// logic and do not use the <see cref="_asyncDelegate"/> field.
    /// </summary>
    protected AsyncEventDelegateContainer(T eventType, ExecutionOrder priority, string[]? tags = null)
        : base(eventType, priority, tags)
    {
    }

protected AsyncEventDelegateContainer(T eventType, ExecutionOrder priority,
    string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
    : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
{
}

    /// <summary>
    /// Synchronous invocation fallback. Fires the async handler without awaiting.
    /// When EVENT_DEBUG is defined, a warning is logged — prefer <see cref="InvokeAsync"/> instead.
    /// </summary>
    public override void Invoke(TArgs args)
    {
        if (!Enabled) return;
#if EVENT_DEBUG
        EventSystemDiagnostics.LogWarning?.Invoke(
            $"[EventSystem] Async handler on {typeof(T).Name} invoked synchronously. " +
            $"Use InvokeAsync/InvokeEventAsync for proper async execution. " +
            $"Handler: {SourceDescription}");
#endif
        _asyncDelegate?.Invoke(args);
    }

    /// <summary>
    /// Asynchronously invokes the handler and returns the resulting <see cref="Task"/>.
    /// </summary>
    public virtual Task InvokeAsync(TArgs args)
    {
        if (!Enabled) return Task.CompletedTask;
        return _asyncDelegate?.Invoke(args) ?? Task.CompletedTask;
    }
}

/// <summary>
/// Specialized container for parameterless async events that stores a
/// <see cref="Func{Task}"/> directly, avoiding the closure allocation that
/// wrapping in a <see cref="Func{Unit, Task}"/> would incur.
/// </summary>
public sealed class ParameterlessAsyncEventDelegateContainer<T> : AsyncEventDelegateContainer<T, Unit>
    where T : struct, Enum
{
    /// <inheritdoc />
    public override bool MatchesDelegate(Delegate handler) => handler != null && handler.Equals(_asyncAction);

    private readonly Func<Task> _asyncAction;

    public override Delegate? GetDelegate() => _asyncAction;

    public ParameterlessAsyncEventDelegateContainer(T eventType, Func<Task> asyncAction, ExecutionOrder priority = default, string[]? tags = null)
        : base(eventType, priority, tags)
    {
        _asyncAction = asyncAction;
    }

public ParameterlessAsyncEventDelegateContainer(T eventType, Func<Task> asyncAction, ExecutionOrder priority,
    string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
    : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
{
    _asyncAction = asyncAction;
}

    public override void Invoke(Unit args)
    {
        if (!Enabled) return;
#if EVENT_DEBUG
        EventSystemDiagnostics.LogWarning?.Invoke(
            $"[EventSystem] Async handler on {typeof(T).Name} invoked synchronously. " +
            $"Use InvokeAsync/InvokeEventAsync for proper async execution. " +
            $"Handler: {SourceDescription}");
#endif
        _asyncAction?.Invoke();
    }

    public override Task InvokeAsync(Unit args)
    {
        if (!Enabled) return Task.CompletedTask;
        return _asyncAction?.Invoke() ?? Task.CompletedTask;
    }
}

}
