using System.Diagnostics;

namespace Prowl.Wicked;

/// <summary>
/// Non-generic RPC promise for acknowledgment without data.
/// Resolves during Tick(). All callbacks execute on the calling thread.
/// Also serves as the base type for RpcPromise&lt;T&gt;, so code that needs to
/// track "any pending promise" can use RpcPromise as the common type.
/// </summary>
public class RpcPromise
{
    /// <summary>
    /// Creates a new pre-resolved, successful RpcPromise. Use as a return value from
    /// ServerRpc methods that return RpcPromise for acknowledgment-only RPCs.
    /// Each access returns a fresh instance, safe for independent callback chains.
    /// </summary>
    public static RpcPromise Completed => new() { IsCompleted = true, IsSuccess = true };

    /// <summary>True when the promise has resolved (success or failure).</summary>
    public bool IsCompleted { get; internal set; }

    /// <summary>True if the promise resolved successfully.</summary>
    public bool IsSuccess { get; internal set; }

    /// <summary>The error if the promise was rejected.</summary>
    public Exception? Error { get; internal set; }

    private readonly List<Action> _thenCallbacks = new();
    private readonly List<Action<Exception>> _catchCallbacks = new();
    private readonly List<Action> _finallyCallbacks = new();
    private float _timeoutSeconds;
    private readonly long _createdAtTicks = Stopwatch.GetTimestamp();

    /// <summary>
    /// Registers a callback for successful resolution.
    /// If the promise is already resolved successfully, the callback fires immediately.
    /// Multiple Then() calls are supported — all callbacks fire in registration order.
    /// </summary>
    public RpcPromise Then(Action callback)
    {
        if (IsCompleted && IsSuccess)
        {
            callback();
            return this;
        }
        _thenCallbacks.Add(callback);
        return this;
    }

    /// <summary>
    /// Registers a callback for rejection (error or timeout).
    /// If the promise is already rejected, the callback fires immediately.
    /// Multiple Catch() calls are supported — all callbacks fire in registration order.
    /// </summary>
    public RpcPromise Catch(Action<Exception> callback)
    {
        if (IsCompleted && !IsSuccess && Error != null)
        {
            callback(Error);
            return this;
        }
        _catchCallbacks.Add(callback);
        return this;
    }

    /// <summary>
    /// Registers a callback that fires on both success and failure.
    /// If the promise is already completed, the callback fires immediately.
    /// Multiple Finally() calls are supported — all callbacks fire in registration order.
    /// </summary>
    public RpcPromise Finally(Action callback)
    {
        if (IsCompleted)
        {
            callback();
            return this;
        }
        _finallyCallbacks.Add(callback);
        return this;
    }

    /// <summary>
    /// Rejects the promise if it doesn't resolve within the specified seconds.
    /// Uses wall-clock time via Stopwatch for accuracy under load.
    /// </summary>
    public RpcPromise Timeout(float seconds)
    {
        _timeoutSeconds = seconds;
        return this;
    }

    /// <summary>
    /// Checks if this promise has exceeded its timeout. If so, rejects it with a TimeoutException.
    /// Called by the RPC dispatch system during Tick() for all pending promises.
    /// Returns true if the promise timed out and was rejected.
    /// </summary>
    internal bool CheckTimeout()
    {
        if (IsCompleted || _timeoutSeconds <= 0)
            return false;

        var elapsed = Stopwatch.GetElapsedTime(_createdAtTicks);
        if (elapsed.TotalSeconds >= _timeoutSeconds)
        {
            Reject(new TimeoutException($"RPC promise timed out after {_timeoutSeconds} seconds."));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves the promise successfully. Fires all Then and Finally callbacks.
    /// Called internally by the RPC dispatch system.
    /// </summary>
    internal virtual void Resolve()
    {
        if (IsCompleted) return;
        IsCompleted = true;
        IsSuccess = true;
        foreach (var cb in _thenCallbacks)
            try { cb(); } catch (Exception ex) { Console.Error.WriteLine($"[RpcPromise] Then callback threw: {ex}"); }
        foreach (var cb in _finallyCallbacks)
            try { cb(); } catch (Exception ex) { Console.Error.WriteLine($"[RpcPromise] Finally callback threw: {ex}"); }
    }

    /// <summary>
    /// Rejects the promise with an error. Fires all Catch and Finally callbacks.
    /// Called internally by the RPC dispatch system.
    /// </summary>
    internal virtual void Reject(Exception error)
    {
        if (IsCompleted) return;
        IsCompleted = true;
        IsSuccess = false;
        Error = error;
        foreach (var cb in _catchCallbacks)
            try { cb(error); } catch (Exception ex) { Console.Error.WriteLine($"[RpcPromise] Catch callback threw: {ex}"); }
        foreach (var cb in _finallyCallbacks)
            try { cb(); } catch (Exception ex) { Console.Error.WriteLine($"[RpcPromise] Finally callback threw: {ex}"); }
    }
}

/// <summary>
/// Generic RPC promise that carries a return value of type T.
/// Inherits from RpcPromise so it can be tracked as "any pending promise."
/// Resolves during Tick(). All callbacks execute on the calling thread.
///
/// Callback ordering on Resolve(T): typed Then(Action&lt;T&gt;) callbacks fire first,
/// then base Then(Action) callbacks, then base Finally(Action) callbacks.
/// </summary>
public class RpcPromise<T> : RpcPromise
{
    /// <summary>The result value on successful resolution.</summary>
    public T? Result { get; internal set; }

    private readonly List<Action<T>> _typedThenCallbacks = new();

    /// <summary>
    /// Registers a callback for successful resolution with the result value.
    /// If the promise is already resolved successfully, the callback fires immediately.
    /// Multiple Then() calls are supported — all callbacks fire in registration order.
    /// </summary>
    public RpcPromise<T> Then(Action<T> callback)
    {
        if (IsCompleted && IsSuccess)
        {
            callback(Result!);
            return this;
        }
        _typedThenCallbacks.Add(callback);
        return this;
    }

    /// <summary>
    /// Registers a callback for rejection (error or timeout).
    /// </summary>
    public new RpcPromise<T> Catch(Action<Exception> callback)
    {
        base.Catch(callback);
        return this;
    }

    /// <summary>
    /// Registers a callback that fires on both success and failure.
    /// </summary>
    public new RpcPromise<T> Finally(Action callback)
    {
        base.Finally(callback);
        return this;
    }

    /// <summary>
    /// Rejects the promise if it doesn't resolve within the specified seconds.
    /// </summary>
    public new RpcPromise<T> Timeout(float seconds)
    {
        base.Timeout(seconds);
        return this;
    }

    /// <summary>
    /// Resolves the promise with a value.
    /// Callback order: typed Then(Action&lt;T&gt;) → base Then(Action) → base Finally(Action).
    /// </summary>
    internal void Resolve(T value)
    {
        if (IsCompleted) return;
        Result = value;
        // Fire typed callbacks first
        foreach (var cb in _typedThenCallbacks)
            try { cb(value); } catch (Exception ex) { Console.Error.WriteLine($"[RpcPromise] Then callback threw: {ex}"); }
        // Then fire base callbacks (sets IsCompleted/IsSuccess, fires base Then + Finally)
        base.Resolve();
    }

    /// <summary>
    /// Allows returning T directly from ServerRpc methods with RpcPromise&lt;T&gt; return types.
    /// </summary>
    public static implicit operator RpcPromise<T>(T value)
    {
        var promise = new RpcPromise<T> { Result = value, IsCompleted = true, IsSuccess = true };
        return promise;
    }
}
