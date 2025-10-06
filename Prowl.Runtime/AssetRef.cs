// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime;

// Taken and modified from Duality's ContentRef.cs
// https://github.com/AdamsLair/duality/blob/master/Source/Core/Duality/ContentRef.cs

public struct AssetRef<T> : IAssetRef, ISerializable where T : EngineObject
{
    private T? instance;
    private string _assetPath = string.Empty;
    private string assetPath
    {
        get { return _assetPath ??= string.Empty; }
        set { _assetPath = value ?? string.Empty; }
    }

    /// <summary>
    /// The actual <see cref="EngineObject"/>. If currently unavailable, it is loaded and then returned.
    /// Because of that, this Property is only null if the references Resource is missing, invalid, or
    /// this content reference has been explicitly set to null. Never returns disposed Resources.
    /// </summary>
    public T? Res
    {
        get
        {
            if (instance == null || instance.IsDestroyed) RetrieveInstance();
            return instance;
        }
        set
        {
            assetPath = value == null ? string.Empty : value.AssetPath;
            instance = value;
        }
    }

    /// <summary>
    /// Returns the current reference to the Resource that is stored locally. No attemp is made to load or reload
    /// the Resource if currently unavailable.
    /// </summary>
    public T? ResWeak
    {
        get { return instance == null || instance.IsDestroyed ? null : instance; }
    }

    /// <summary>
    /// The path where to look for the Resource, if it is currently unavailable.
    /// </summary>
    public string AssetPath
    {
        get { return assetPath; }
        set
        {
            assetPath = value;
            if (instance != null && instance.AssetPath != value)
                instance = null;
        }
    }

    /// <summary>
    /// Returns whether this content reference has been explicitly set to null.
    /// </summary>
    public bool IsExplicitNull
    {
        get
        {
            return instance == null && string.IsNullOrEmpty(assetPath);
        }
    }

    /// <summary>
    /// Returns whether this content reference is available in general. This may trigger loading it, if currently unavailable.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (instance != null && !instance.IsDestroyed) return true;
            RetrieveInstance();
            return instance != null;
        }
    }

    /// <summary>
    /// Returns whether the referenced Resource is currently loaded.
    /// </summary>
    public bool IsLoaded
    {
        get
        {
            if (instance != null && !instance.IsDestroyed) return true;
            return Game.AssetProvider.HasAsset(assetPath);
        }
    }

    /// <summary>
    /// Returns whether the Resource has been generated at runtime and cannot be retrieved via content path.
    /// </summary>
    public bool IsRuntimeResource
    {
        get { return instance != null && string.IsNullOrEmpty(assetPath); }
    }

    public string Name
    {
        get
        {
            if (instance != null) return instance.IsDestroyed ? "DESTROYED_" + instance.Name : instance.Name;
            return "No Instance";
        }
    }

    public Type InstanceType => typeof(T);

    /// <summary>
    /// Creates a AssetRef pointing to the <see cref="EngineObject"/> at the specified id / using 
    /// the specified alias.
    /// </summary>
    /// <param name="id"></param>
    public AssetRef(string id)
    {
        instance = null;
        assetPath = id;
    }
    /// <summary>
    /// Creates a AssetRef pointing to the specified <see cref="EngineObject"/>.
    /// </summary>
    /// <param name="res">The Resource to reference.</param>
    public AssetRef(T res)
    {
        instance = res;
        assetPath = res != null ? res.AssetPath : string.Empty;
    }

    public object? GetInstance()
    {
        return Res;
    }

    public void SetInstance(object? obj)
    {
        if (obj is T res)
            Res = res;
        else
            Res = null;
    }

    /// <summary>
    /// Loads the associated content as if it was accessed now.
    /// You don't usually need to call this method. It is invoked implicitly by trying to
    /// access the <see cref="AssetRef{T}"/>.
    /// </summary>
    public void EnsureLoaded()
    {
        if (instance == null || instance.IsDestroyed)
            RetrieveInstance();
    }
    /// <summary>
    /// Discards the resolved content reference cache to allow garbage-collecting the Resource
    /// without losing its reference. Accessing it will result in reloading the Resource.
    /// </summary>
    public void Detach()
    {
        instance = null;
    }

    private void RetrieveInstance()
    {
        if (!string.IsNullOrEmpty(assetPath))
            instance = Game.AssetProvider.LoadAsset<T>(assetPath);
        else if (instance != null && instance.AssetPath != string.Empty)
            instance = Game.AssetProvider.LoadAsset<T>(instance.AssetPath);
        else
            instance = null;
    }

    public override string ToString()
    {
        Type resType = typeof(T);

        char stateChar;
        if (IsRuntimeResource)
            stateChar = 'R';
        else if (IsExplicitNull)
            stateChar = 'N';
        else if (IsLoaded)
            stateChar = 'L';
        else
            stateChar = '_';

        return $"[{stateChar}] {resType.Name}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is AssetRef<T> @ref)
            return this == @ref;
        else
            return base.Equals(obj);
    }

    public override int GetHashCode()
    {
        if (assetPath != string.Empty) return assetPath.GetHashCode();
        else if (instance != null) return instance.GetHashCode();
        else return 0;
    }

    public bool Equals(AssetRef<T> other)
    {
        return this == other;
    }

    public static implicit operator AssetRef<T>(T res)
    {
        return new AssetRef<T>(res);
    }
    public static explicit operator T(AssetRef<T> res)
    {
        return res.Res;
    }

    /// <summary>
    /// Compares two AssetRefs for equality.
    /// </summary>
    /// <param name="first"></param>
    /// <param name="second"></param>
    /// <remarks>
    /// This is a two-step comparison. First, their actual Resources references are compared.
    /// If they're both not null and equal, true is returned. Otherwise, their AssetID's are compared for equality
    /// </remarks>
    public static bool operator ==(AssetRef<T> first, AssetRef<T> second)
    {
        // Old check, didn't work for XY == null when XY was a Resource created at runtime
        //if (first.instance != null && second.instance != null)
        //    return first.instance == second.instance;
        //else
        //    return first.assetID == second.assetID;

        // Completely identical
        if (first.instance == second.instance && first.assetPath == second.assetPath)
            return true;
        // Same instances
        else if (first.instance != null && second.instance != null)
            return first.instance == second.instance;
        // Null checks
        else if (first.IsExplicitNull) return second.IsExplicitNull;
        else if (second.IsExplicitNull) return first.IsExplicitNull;
        // Path comparison
        else
        {
            string? firstPath = first.instance != null ? first.instance.AssetPath : first.assetPath;
            string? secondPath = second.instance != null ? second.instance.AssetPath : second.assetPath;
            return firstPath == secondPath;
        }
    }
    /// <summary>
    /// Compares two AssetRefs for inequality.
    /// </summary>
    /// <param name="first"></param>
    /// <param name="second"></param>
    public static bool operator !=(AssetRef<T> first, AssetRef<T> second)
    {
        return !(first == second);
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("AssetID", new EchoObject(assetPath.ToString()));
        if (IsRuntimeResource)
            compoundTag.Add("Instance", Serializer.Serialize(instance, ctx));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        assetPath = value["AssetID"].StringValue;
        if (value.TryGet("Instance", out EchoObject tag))
            instance = Serializer.Deserialize<T?>(tag, ctx);
    }
}
