// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Echo;

namespace Prowl.Runtime;

public abstract class EngineObject
{
    private static int s_nextID = 1;

    protected int _instanceID;
    public int InstanceID => _instanceID;

    // Asset path if we have one
    public string AssetPath = string.Empty;

    public string Name;

    [SerializeIgnore]
    public bool IsDestroyed = false;

    public EngineObject() : this(null) { }

    public EngineObject(string? name = "New Object")
    {
        _instanceID = s_nextID;
        s_nextID = Interlocked.Increment(ref s_nextID);
        Name = "New" + GetType().Name;
        CreatedInstance();
        Name = name ?? Name;
    }

    public virtual void CreatedInstance() { }

    public virtual void OnValidate() { }

    public void Destroy()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        OnDispose();
    }

    public virtual void OnDispose() { }


    public static bool operator ==(EngineObject left, EngineObject right)
    {
        if (left is null)
            return right is null || right.IsDestroyed;
        if (right is null)
            return left.IsDestroyed;
        return ReferenceEquals(left, right) || (left.IsDestroyed && right.IsDestroyed);
    }

    public static bool operator !=(EngineObject left, EngineObject right) => !(left == right);
    public override int GetHashCode() => IsDestroyed ? 0 : base.GetHashCode();
    public override bool Equals(object? obj) => this == (obj as EngineObject);


    public override string ToString() => Name;

    protected void SerializeHeader(EchoObject compound)
    {
        compound.Add("Name", new(Name));
        compound.Add("AssetPath", new(AssetPath));
    }

    protected void DeserializeHeader(EchoObject value)
    {
        Name = value.Get("Name")?.StringValue ?? string.Empty;
        AssetPath = value.Get("AssetPath")?.StringValue ?? string.Empty;
    }
}

public static class EngineObjectExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Null(this EngineObject obj) => obj == null || obj.IsDestroyed;
}
