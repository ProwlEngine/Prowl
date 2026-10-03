// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Engine objects are shared rather than duplicated when a copy reaches one through a field. An asset referenced
/// by two components stays one asset. Copying an engine object directly still copies it, see <see cref="ObjectCopy"/>.
/// </summary>
public abstract class EngineObject : IDisposable
{
    private static int s_nextID = 1;

    protected int _instanceID;
    public int InstanceID => _instanceID;

    [HideInInspector] public string Name;

    // Interlocked-guarded rather than a plain bool: a finalizer can now race an explicit Dispose()
    // call from another thread (the finalizer thread runs independently of everything else), so the
    // check-and-set must be atomic or both could pass the guard and double-run OnDispose.
    private int _disposed;
    public bool IsDisposed => _disposed != 0;

    public EngineObject() : this(null) { }

    public EngineObject(string? name = "New Object")
    {
        _instanceID = Interlocked.Increment(ref s_nextID);
        Name = "New" + GetType().Name;
        CreatedInstance();
        Name = name ?? Name;
    }

    public virtual void CreatedInstance() { }

    public virtual void OnValidate() { }

    public void Dispose()
    {
        if (this is Asset { Registered: true })
            throw new InvalidOperationException($"'{Name}' ({GetType().Name}) belongs to the asset database. Use AssetDatabase.Unload to free its memory.");

        if (IsDisposed) return;
        AssertCanDispose();

        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        // Explicit disposal means a finalizer (if this type has one) has nothing left to do.
        GC.SuppressFinalize(this);
        try { OnDispose(); }
        finally { OnDisposed(); }
    }

    /// <summary>Runs after <see cref="OnDispose"/> even when an override skips base or throws.</summary>
    private protected virtual void OnDisposed() { }

    /// <summary>Throws before anything is torn down when this may not be disposed from the calling thread.</summary>
    private protected virtual void AssertCanDispose() { }

    private static readonly List<EngineObject> s_destroyQueue = [];

    /// <summary>
    /// Queues this object to be disposed at the end of the frame, once every callback has finished.
    /// It stays fully usable until then, so anything still holding it this frame keeps working, and
    /// teardown never lands in the middle of an Update, a render or a physics callback.
    /// <para/>
    /// A destroyed GameObject still ticks and still collides for the rest of the frame. Set
    /// <c>Enabled = false</c> alongside this if that matters, or call <see cref="Dispose"/> to tear
    /// down right now and deal with the consequences.
    /// </summary>
    public void Destroy()
    {
        if (this is Asset { Registered: true })
            throw new InvalidOperationException($"'{Name}' ({GetType().Name}) belongs to the asset database. Use AssetDatabase.Unload to free its memory.");

        if (IsDisposed) return;
        lock (s_destroyQueue) s_destroyQueue.Add(this);
    }

    /// <summary>
    /// Disposes everything <see cref="Destroy"/> queued. Driven once per frame by the game loop,
    /// after rendering. Anything queued while this runs waits for the next frame.
    /// </summary>
    public static void ProcessDestroyed()
    {
        EngineObject[] queued;
        lock (s_destroyQueue)
        {
            if (s_destroyQueue.Count == 0) return;
            queued = [.. s_destroyQueue];
            s_destroyQueue.Clear();
        }

        foreach (EngineObject obj in queued)
        {
            if (obj.IsDisposed) continue; // disposed by hand, or by an owner that went first

            try { obj.Dispose(); }
            catch (Exception ex) { Debug.LogError($"[{obj.Name}/{obj.GetType().Name}] Dispose() threw while being destroyed: {ex.Message}\n{ex.StackTrace}"); }
        }
    }


    public static bool operator ==(EngineObject left, EngineObject right)
    {
        return ReferenceEquals(left, right);
    }
    public static bool operator !=(EngineObject left, EngineObject right) => !(left == right);
    public override bool Equals(object? obj) => this == (obj as EngineObject);
    public override int GetHashCode() => _instanceID;

    protected virtual void OnDispose() { }

    /// <summary>Call at the top of any accessor that must not run on a disposed object.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void EnsureNotDisposed([CallerMemberName] string? member = null)
    {
        if (IsDisposed)
            ThrowDisposed(member);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowDisposed(string? member)
        => throw new ObjectDisposedException(Name, $"'{Name}' ({GetType().Name}) was already disposed when '{member}' was accessed.");

    public override string ToString() => Name;

    protected void SerializeHeader(EchoObject compound) => compound.Add("Name", new(Name));

    protected void DeserializeHeader(EchoObject value) => Name = value.Get("Name")?.StringValue ?? Name;
}

public static class EngineObjectExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotValid([NotNullWhen(false)] this EngineObject? obj) => obj is null || obj.IsDisposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValid([NotNullWhen(true)] this EngineObject? obj) => obj is not null && !obj.IsDisposed;
}
