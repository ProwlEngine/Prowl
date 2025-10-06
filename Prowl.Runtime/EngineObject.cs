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
    private static readonly ConcurrentStack<EngineObject> s_destroyed = new();
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

    public static T?[] FindObjectsOfType<T>() where T : EngineObject
    {
        List<T> objects = [];
        foreach (GameObject go in SceneManagement.SceneManager.Current.Res!.AllObjects)
        {
            if (go is T t)
                objects.Add(t);

            foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
                if (comp is T t2)
                    objects.Add(t2);
        }
        return objects.ToArray();
    }

    public static T? FindObjectByID<T>(int id) where T : EngineObject
    {
        foreach (GameObject go in SceneManagement.SceneManager.Current.Res!.AllObjects)
        {
            if (go.InstanceID == id)
                return go as T;
            foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
                if (comp.InstanceID == id)
                    return comp as T;
        }
        return null;
    }

    public static T? FindObjectByIdentifier<T>(Guid identifier) where T : EngineObject
    {
        foreach (GameObject go in SceneManagement.SceneManager.Current.Res!.AllObjects)
        {
            if (go.Identifier == identifier)
                return go as T;
            foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
                if (comp.Identifier == identifier)
                    return comp as T;
        }
        return null;
    }

    public void DestroyLater()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        s_destroyed.Push(this);
    }

    public void DestroyImmediate()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        OnDispose();
    }

    public static void HandleDestroyed()
    {
        while (s_destroyed.TryPop(out EngineObject? obj))
        {
            if (!obj.IsDestroyed) throw new Exception(obj.Name + " is not destroyed yet exists in the destroyed stack, this should not happen.");
            obj.OnDispose();
        }
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
