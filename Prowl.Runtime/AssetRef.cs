// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;

namespace Prowl.Runtime;

internal sealed class DependencySerializationContext : SerializationContext
{
    public HashSet<Guid> Dependencies = new HashSet<Guid>();
}

/// <summary>
/// A serializable reference to an asset. Stores a GUID for persistent identification
/// and caches the resolved instance. When the asset is needed, it's loaded via AssetDatabase.Get().
/// This solves the "stale reference" problem — all references to the same asset share the same
/// resolved instance, and re-resolving always gets the latest version.
/// </summary>
public struct AssetRef<T> : IAssetRef, ISerializable where T : EngineObject
{
    private T? instance;
    private Guid assetID;

    /// <summary>
    /// The resolved asset. Lazily loaded via AssetDatabase.Get() if not cached.
    /// Returns null if the asset is missing or this ref is explicitly null.
    /// </summary>
    public T? Res
    {
        get
        {
            if (instance.IsNotValid())
                RetrieveInstance();
            return instance;
        }
        set
        {
            instance = value;
            assetID = value?.AssetID ?? Guid.Empty;
        }
    }

    /// <summary>Returns the cached instance without attempting to load. May be null.</summary>
    public T? ResWeak => instance.IsValid() ? instance : null;

    /// <summary>The asset GUID. Setting this clears the cached instance if it doesn't match.</summary>
    public Guid AssetID
    {
        get => assetID;
        set
        {
            assetID = value;
            if (instance != null && instance.AssetID != value)
                instance = null;
        }
    }

    public bool IsExplicitNull => instance == null && assetID == Guid.Empty;
    public bool IsRuntimeResource => instance != null && assetID == Guid.Empty;
    public string Name => instance != null ? (instance.IsNotValid() ? "DESTROYED" : instance.Name) : "None";
    public Type InstanceType => typeof(T);

    public AssetRef(T? res)
    {
        instance = res;
        assetID = res?.AssetID ?? Guid.Empty;
    }

    public AssetRef(Guid id)
    {
        instance = null;
        assetID = id;
    }

    public object? GetInstance() => Res;

    public void SetInstance(object? obj)
    {
        if (obj is T res)
            Res = res;
        else
            Res = null;
    }

    /// <summary>Force a reload from the asset database.</summary>
    public void EnsureLoaded()
    {
        if (instance.IsNotValid())
            RetrieveInstance();
    }

    /// <summary>Clear the cached instance. Next access will re-resolve from the database.</summary>
    public void Detach() => instance = null;

    private void RetrieveInstance()
    {
        if (assetID != Guid.Empty)
            instance = AssetDatabase.Get(assetID) as T;
        else
            instance = null;
    }

    // ================================================================
    //  Serialization — stores AssetID + inline instance for runtime resources
    // ================================================================

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        compound.Add("AssetID", new EchoObject(assetID.ToString()));

        if (assetID != Guid.Empty && ctx is DependencySerializationContext tracker)
        {
            tracker.Dependencies.Add(assetID);
        }

        // Only serialize the instance inline if it's a runtime resource (no asset ID)
        if (assetID == Guid.Empty && instance != null)
            compound.Add("Instance", Echo.Serializer.Serialize(typeof(T), instance, ctx));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        if (value.TryGet("AssetID", out var idTag))
            assetID = Guid.Parse(idTag.StringValue);
        else
            assetID = Guid.Empty;

        if (assetID != Guid.Empty && ctx is DependencySerializationContext tracker)
        {
            tracker.Dependencies.Add(assetID);
        }

        // If no asset ID, try to deserialize an inline instance
        if (assetID == Guid.Empty && value.TryGet("Instance", out EchoObject instTag))
            instance = Echo.Serializer.Deserialize<T?>(instTag, ctx);
        else
            instance = null; // Will be resolved lazily via Res property
    }

    // ================================================================
    //  Operators
    // ================================================================

    public static implicit operator AssetRef<T>(T? res) => new(res);

    public override bool Equals(object? obj) => obj is AssetRef<T> other && this == other;

    public override int GetHashCode() => assetID != Guid.Empty ? assetID.GetHashCode() : (instance?.GetHashCode() ?? 0);

    public static bool operator ==(AssetRef<T> a, AssetRef<T> b)
    {
        if (a.instance != null && b.instance != null)
            return a.instance == b.instance;
        if (a.IsExplicitNull && b.IsExplicitNull)
            return true;
        return a.assetID == b.assetID && a.assetID != Guid.Empty;
    }

    public static bool operator !=(AssetRef<T> a, AssetRef<T> b) => !(a == b);

    public override string ToString()
    {
        char state = IsRuntimeResource ? 'R' : IsExplicitNull ? 'N' : instance.IsValid() ? 'L' : '_';
        return $"[{state}] {typeof(T).Name}";
    }
}
