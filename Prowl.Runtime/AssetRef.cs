// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// A reference to an asset that neither loads nor holds it, for things needed maybe later: a list of levels, an
/// optional high resolution texture. A build still ships what it points at. Use a plain field for everything else.
/// </summary>
public struct AssetRef<T> : IAssetRef, IEquatable<AssetRef<T>>, ISerializable where T : Asset
{
    private const string Key = "$assetRef";

    private Guid _assetId;

    public AssetRef(Guid assetId) => _assetId = assetId;

    public AssetRef(T? asset)
    {
        _assetId = asset is null ? Guid.Empty : asset.AssetID;
        if (asset is not null && _assetId == Guid.Empty)
            Debug.LogWarningOnce($"AssetRef.Runtime.{asset.InstanceID}", $"'{asset.Name}' ({typeof(T).Name}) is not in the asset database, so an AssetRef can not point at it.");
    }

    public Guid AssetID => _assetId;
    public bool IsEmpty => _assetId == Guid.Empty;
    public Type AssetType => typeof(T);

    /// <summary>The asset, loaded or not. Never loads.</summary>
    public T? Get() => AssetDatabase.Get<T>(_assetId);

    /// <summary>The asset, loaded. Blocks.</summary>
    public T? Load() => AssetDatabase.Load<T>(_assetId);

    public Task<T?> LoadAsync(CancellationToken cancel = default) => AssetDatabase.LoadAsync<T>(_assetId, cancel);

    public static implicit operator AssetRef<T>(T? asset) => new(asset);

    public bool Equals(AssetRef<T> other) => _assetId == other._assetId;
    public override bool Equals(object? obj) => obj is AssetRef<T> other && Equals(other);
    public override int GetHashCode() => _assetId.GetHashCode();
    public static bool operator ==(AssetRef<T> a, AssetRef<T> b) => a._assetId == b._assetId;
    public static bool operator !=(AssetRef<T> a, AssetRef<T> b) => a._assetId != b._assetId;

    public override string ToString() => IsEmpty ? $"None ({typeof(T).Name})" : $"{typeof(T).Name} {_assetId}";

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        compound.Add(Key, new EchoObject(_assetId.ToString()));
        if (!IsEmpty && ctx is DependencySerializationContext tracker) tracker.SoftDependencies.Add(_assetId);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Guid id = value.TryGet(Key, out EchoObject? tag) && Guid.TryParse(tag!.StringValue, out Guid parsed) ? parsed : Guid.Empty;
        if (id != Guid.Empty && ctx is DependencySerializationContext tracker) tracker.SoftDependencies.Add(id);
        _assetId = id;
    }
}

/// <summary>An <see cref="AssetRef{T}"/> of any type, for inspectors and tools.</summary>
public interface IAssetRef
{
    Guid AssetID { get; }
    bool IsEmpty { get; }
    Type AssetType { get; }
}
