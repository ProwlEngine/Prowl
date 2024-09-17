// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime;

public class EngineObject : IDisposable
{
    private static readonly Stack<EngineObject> destroyed = new Stack<EngineObject>();

    private static readonly List<WeakReference<EngineObject>> allObjects = [];

    static int NextID = 1;

    protected readonly int _instanceID;
    public int InstanceID => _instanceID;

    // Asset path if we have one
    [HideInInspector]
    public Guid AssetID = Guid.Empty;

    // Asset path if we have one
    [HideInInspector]
    public ushort FileID = 0;

    [HideInInspector]
    public string Name;

    [HideInInspector, SerializeIgnore]
    public bool IsDestroyed = false;

    public EngineObject() : this(null) { }

    public EngineObject(string? name = "New Object")
    {
        _instanceID = NextID++;
        Name = "New" + GetType().Name;
        allObjects.Add(new(this));
        CreatedInstance();
        Name = name ?? Name;
    }

    public virtual void CreatedInstance()
    {
    }

    public virtual void OnValidate() { }

    public static T? FindObjectOfType<T>() where T : EngineObject
    {
        foreach (var obj in allObjects)
            if (obj.TryGetTarget(out var target) && target is T t)
                return t;
        return null;
    }

    public static T[] FindObjectsOfType<T>() where T : EngineObject
    {
        List<T> objects = new();
        foreach (var obj in allObjects)
            if (obj.TryGetTarget(out var target) && target is T t)
                objects.Add(t);
        return objects.ToArray();
    }
    public static T? FindObjectByID<T>(int id) where T : EngineObject
    {
        foreach (var obj in allObjects)
            if (obj.TryGetTarget(out var target) && target is T t && t.InstanceID == id)
                return t;
        return null;
    }

    public static void Foreach<T>(Action<T> action) where T : EngineObject
    {
        foreach (var obj in allObjects)
        {
            if (obj.TryGetTarget(out var target) && target is T t)
            {
                action(t);
            }
        }
    }

    public void Destroy() => Destroy(this);
    public void DestroyImmediate() => DestroyImmediate(this);

    public static void Destroy(EngineObject obj)
    {
        if (obj.IsDestroyed) throw new Exception(obj.Name + " is already destroyed.");
        obj.IsDestroyed = true;
        destroyed.Push(obj);
    }

    public static void DestroyImmediate(EngineObject obj)
    {
        if (obj.IsDestroyed) throw new Exception(obj.Name + " is already destroyed.");
        obj.IsDestroyed = true;
        obj.Destroy();
    }

    public static void HandleDestroyed()
    {
        while (destroyed.TryPop(out var obj))
        {
            if (!obj.IsDestroyed) continue;
            obj.Destroy();
        }

        CleanupAllObjects();
    }

    public static EngineObject Instantiate(EngineObject obj, bool keepAssetID = false)
    {
        if (obj.IsDestroyed) throw new Exception(obj.Name + " has been destroyed.");
        // Serialize and deserialize to get a new object
        var serialized = Serializer.Serialize(obj);
        // dont need to assign ID or add it to objects list the constructor will do that automatically
        var newObj = Serializer.Deserialize<EngineObject>(serialized);
        // Some objects might have a readonly name (like components) in that case it should remain the same, so if name is different set it
        newObj.Name = obj.Name;
        // Need to make sure to set GUID to empty so the engine knows this isn't the original Asset file
        if (!keepAssetID) newObj.AssetID = Guid.Empty;
        return newObj;
    }

    /// <summary>
    /// Force the object to dispose immediately
    /// You are advised to not use this! Use Destroy() Instead.
    /// </summary>
    [Obsolete("You are advised to not use this! Use Destroy() Instead.")]
    public void Dispose()
    {
        IsDestroyed = true;
        GC.SuppressFinalize(this);
        OnDispose();
        //AssetProvider.RemoveAsset(this, false);
        HasHadDispose = true;
    }

    static bool HasHadDispose = false;
    static void CleanupAllObjects()
    {
        if (!HasHadDispose) return;
        HasHadDispose = false;
#warning TODO: Find a faster solution to keeping allObjects clean of dead objects
        allObjects.RemoveAll(wr => !wr.TryGetTarget(out _));
    }

    public virtual void OnDispose() { }

    public override string ToString() { return Name; }

    protected void SerializeHeader(SerializedProperty compound)
    {
        compound.Add("Name", new(Name));

        if (AssetID != Guid.Empty)
        {
            compound.Add("AssetID", new SerializedProperty(AssetID.ToString()));
            if (FileID != 0)
                compound.Add("FileID", new SerializedProperty(FileID));
        }
    }

    protected void DeserializeHeader(SerializedProperty value)
    {
        Name = value.Get("Name")?.StringValue;

        if (value.TryGet("AssetID", out var assetIDTag))
        {
            AssetID = Guid.Parse(assetIDTag.StringValue);
            FileID = value.Get("FileID").UShortValue;
        }
    }
}

public static class EngineObjectExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Null(this EngineObject obj) => obj == null || obj.IsDestroyed;
}
