// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.Events {

/// <summary>
/// Non-generic base class for delegate containers, enabling heterogeneous storage
/// within a single <see cref="Event{T}"/>. Subscribe via the typed
/// <see cref="EventDelegateContainer{T, TArgs}"/> derived class.
/// Implements <see cref="IDisposable"/> for self-unsubscription.
/// </summary>
public abstract class EventDelegateContainer<T> : IEventDelegateContainer where T : struct, Enum
{
    public EventManager<T>? EventManager => Event?.EventManager;

    private Event<T> _event;
    public Event<T> Event
    {
        get { return _event; }
        private set
        {
            _event = value;
        }
    }

    private bool _added;
    public bool Added
    {
        get { return _added; }
        internal set { _added = value; }
    }

    private bool _disposed;

    private readonly T eventType;
    public T EventType => eventType;

    private bool enabled = true;
    public bool Enabled
    {
        get => enabled;
        private set => enabled = value;
    }

    private readonly ExecutionOrder priority;
    public ExecutionOrder Priority => priority;

    /// <summary>
    /// The <c>TArgs</c> type this container was registered with.
    /// Used by <see cref="Event{T}"/> to build per-type snapshots without reflection.
    /// </summary>
    public abstract Type ArgsType { get; }

    /// <summary>
    /// Returns the underlying delegate wrapped by this container.
    /// </summary>
    public abstract Delegate? GetDelegate();

    /// <summary>
    /// Returns <c>true</c> when this container wraps the specified handler delegate.
    /// Used by the generated <c>-=</c> event accessor path.
    /// </summary>
    public abstract bool MatchesDelegate(Delegate handler);

/// <summary>
/// Source file where this handler was registered. Captured automatically
/// via <see cref="CallerFilePathAttribute"/> when EVENT_DEBUG is defined.
/// </summary>
public string? SourceFile { get; private set; }

/// <summary>
/// Source line number where this handler was registered.
/// </summary>
public int SourceLine { get; private set; }

/// <summary>
/// Name of the member that registered this handler.
/// </summary>
public string? SourceMember { get; private set; }

/// <summary>
/// Returns a compact "File:Line (Member)" string for diagnostics,
/// or <c>"unknown"</c> when source info was not captured.
/// </summary>
public string SourceDescription =>
    SourceFile is not null
        ? $"{SourceFile}:{SourceLine} ({SourceMember})"
        : "unknown";

    public void Link(Event<T> @event)
    {
        Event = @event;
    }

    public void Unlink()
    {
        Event = null;
    }

    private string[]? _tags;
    public string[]? Tags => _tags;

    public bool HasTag(string tag)
    {
        if (_tags == null) return false;
        for (int i = 0; i < _tags.Length; i++)
        {
            if (string.Equals(_tags[i], tag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    protected EventDelegateContainer(T eventType, ExecutionOrder priority, string[]? tags = null)
    {
        this.priority = priority;
        this.eventType = eventType;
        this._tags = tags;
    }

    protected EventDelegateContainer(T eventType, ExecutionOrder priority, string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
        : this(eventType, priority, tags)
    {
#if EVENT_DEBUG
        SourceFile = sourceFile is not null ? System.IO.Path.GetFileName(sourceFile) : null;
        SourceLine = sourceLine;
        SourceMember = sourceMember;
#endif
    }

    public void Enable()
    {
        Enabled = true;
    }
    public void Disable()
    {
        Enabled = false;
    }

    /// <summary>
    /// Removes this delegate from its parent event, enabling <c>using</c> patterns
    /// and preventing leaks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Event?.Remove(this);
    }
}

/// <summary>
/// Typed delegate container wrapping an <see cref="Action{TArgs}"/>.
/// </summary>
public class EventDelegateContainer<T, TArgs> : EventDelegateContainer<T>, IInvocable<TArgs> where T : struct, Enum
{
    public override Type ArgsType => typeof(TArgs);

    /// <inheritdoc />
    public override bool MatchesDelegate(Delegate handler) => handler != null && handler.Equals(eventDelegate);

    private readonly Action<TArgs>? eventDelegate;

    public override Delegate? GetDelegate() => eventDelegate;

    public EventDelegateContainer(T eventType, Action<TArgs> eventDelegate, ExecutionOrder priority = default, string[]? tags = null)
        : base(eventType, priority, tags)
    {
        this.eventDelegate = eventDelegate;
    }

public EventDelegateContainer(T eventType, Action<TArgs> eventDelegate, ExecutionOrder priority,
    string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
    : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
{
    this.eventDelegate = eventDelegate;
}

    /// <summary>
    /// Protected constructor for subclasses that provide their own invocation
    /// logic and do not use the <see cref="eventDelegate"/> field.
    /// </summary>
    protected EventDelegateContainer(T eventType, ExecutionOrder priority, string[]? tags = null)
        : base(eventType, priority, tags)
    {
    }

protected EventDelegateContainer(T eventType, ExecutionOrder priority,
    string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
    : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
{
}

public virtual void Invoke(TArgs args)
    {
        if (!Enabled) return;
        eventDelegate?.Invoke(args);
    }
}

/// <summary>
/// Specialized container for parameterless events that stores an <see cref="Action"/>
/// directly, avoiding the closure allocation that wrapping in an
/// <see cref="Action{Unit}"/> would incur.
/// </summary>
public sealed class ParameterlessEventDelegateContainer<T> : EventDelegateContainer<T, Unit> where T : struct, Enum
{
    /// <inheritdoc />
    public override bool MatchesDelegate(Delegate handler) => handler != null && handler.Equals(_action);

    private readonly Action _action;

    public override Delegate? GetDelegate() => _action;

    public ParameterlessEventDelegateContainer(T eventType, Action action, ExecutionOrder priority = default, string[]? tags = null)
        : base(eventType, priority, tags)
    {
        _action = action;
    }

public ParameterlessEventDelegateContainer(T eventType, Action action, ExecutionOrder priority,
    string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
    : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
{
    _action = action;
}

    public override void Invoke(Unit args)
    {
        if (!Enabled) return;
        _action?.Invoke();
    }
}

/// <summary>
/// A typed delegate container that automatically disposes itself after a single invocation.
/// </summary>
public sealed class OneTimeEventDelegateContainer<T, TArgs> : EventDelegateContainer<T, TArgs> where T : struct, Enum
{
    private readonly Action<TArgs>? _eventDelegate;

    public override Delegate? GetDelegate() => _eventDelegate;

    public override Type ArgsType => typeof(TArgs);
    public override bool MatchesDelegate(Delegate handler) => handler != null && handler.Equals(_eventDelegate);

    public OneTimeEventDelegateContainer(T eventType, Action<TArgs> eventDelegate, ExecutionOrder priority,
        string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
        : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
    {
        _eventDelegate = eventDelegate;
    }

    public override void Invoke(TArgs args)
    {
        if (!Enabled) return;
        _eventDelegate?.Invoke(args);
        Dispose();
    }
}

/// <summary>
/// A parameterless delegate container that automatically disposes itself after a single invocation.
/// </summary>
public sealed class OneTimeParameterlessEventDelegateContainer<T> : EventDelegateContainer<T, Unit> where T : struct, Enum
{
    private readonly Action _action;

    public override Delegate? GetDelegate() => _action;

    public override bool MatchesDelegate(Delegate handler) => handler != null && handler.Equals(_action);

    public OneTimeParameterlessEventDelegateContainer(T eventType, Action action, ExecutionOrder priority,
        string? sourceFile, int sourceLine, string? sourceMember, string[]? tags = null)
        : base(eventType, priority, sourceFile, sourceLine, sourceMember, tags)
    {
        _action = action;
    }

    public override void Invoke(Unit args)
    {
        if (!Enabled) return;
        _action?.Invoke();
        Dispose();
    }
}

}
