// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Echo;
using Prowl.Runtime.Cloning;

namespace Prowl.Runtime;

[CloneBehavior(CloneBehavior.Reference)]
public abstract class EngineObject : ICloneExplicit
{
    private static readonly ConcurrentStack<EngineObject> s_destroyed = new();
    private static int s_nextID = 1;

    [CloneField(CloneFieldFlags.IdentityRelevant)]
    protected int _instanceID;
    public int InstanceID => _instanceID;

    // Asset path if we have one
    [HideInInspector]
    [CloneField(CloneFieldFlags.Skip)]
    public Guid AssetID = Guid.Empty;

    // Asset path if we have one
    [HideInInspector]
    [CloneField(CloneFieldFlags.Skip)]
    public ushort FileID = 0;

    [HideInInspector]
    public string Name;

    [HideInInspector, SerializeIgnore]
    [CloneField(CloneFieldFlags.Skip)]
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

    /// <summary>
    /// Creates a deep copy of this EngineObject.
    /// </summary>
    public EngineObject Clone() => this.DeepClone();
    /// <summary>
    /// Deep-copies this EngineObject to the specified target EngineObject. The target EngineObject's Type must
    /// match this EngineObject's Type.
    /// </summary>
    /// <param name="target">The target EngineObject to copy this EngineObject's data to</param>
    public void CopyTo(EngineObject target) => this.DeepCopyTo(target);

    public virtual void SetupCloneTargets(object target, ICloneTargetSetup setup)
    {
        setup.HandleObject(this, target);
        OnSetupCloneTargets(target, setup);
    }
    public virtual void CopyDataTo(object target, ICloneOperation operation)
    {
        operation.HandleObject(this, target);
        OnCopyDataTo(target, operation);
    }

    /// <summary>
    /// This method prepares the <see cref="CopyTo"/> operation for custom EngineObject Types.
    /// It uses reflection to prepare the cloning operation automatically, but you can implement
    /// this method in order to handle certain fields and cases manually. See <see cref="ICloneExplicit.SetupCloneTargets"/>
    /// for a more thorough explanation.
    /// </summary>
    protected virtual void OnSetupCloneTargets(object target, ICloneTargetSetup setup) { }

    /// <summary>
    /// This method performs the <see cref="CopyTo"/> operation for custom EngineObject Types.
    /// It uses reflection to perform the cloning operation automatically, but you can implement
    /// this method in order to handle certain fields and cases manually. See <see cref="ICloneExplicit.CopyDataTo"/>
    /// for a more thorough explanation.
    /// </summary>
    /// <param name="target">The target EngineObject where this EngineObjects data is copied to.</param>
    protected virtual void OnCopyDataTo(object target, ICloneOperation operation) { }

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

    internal static void HandleDestroyed()
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

        if (AssetID != Guid.Empty)
        {
            compound.Add("AssetID", new EchoObject(AssetID.ToString()));

            if (FileID != 0)
                compound.Add("FileID", new EchoObject(FileID));
        }
    }

    protected void DeserializeHeader(EchoObject value)
    {
        Name = value.Get("Name")?.StringValue;

        if (value.TryGet("AssetID", out EchoObject? assetIDTag))
        {
            AssetID = Guid.Parse(assetIDTag.StringValue);

            if (value.TryGet("FileID", out var fileIDTag))
                FileID = fileIDTag.UShortValue;
            else
                FileID = 0;
        }
    }
}

public static class EngineObjectExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Null(this EngineObject obj) => obj == null || obj.IsDestroyed;
}
