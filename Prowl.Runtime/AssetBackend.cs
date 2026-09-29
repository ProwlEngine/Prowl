// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>An asset reachable by load path through <see cref="AssetDatabase.FindResource{T}"/>, with the type it loads as.</summary>
public readonly record struct ResourceEntry(string LoadPath, Guid Guid, string TypeName);

/// <summary>
/// Where asset content comes from: the editor's project, a built player's paks, or memory. A backend only answers
/// questions about GUIDs and reads content into a copy the database hands it. It never keeps assets itself.
/// </summary>
public abstract class AssetBackend
{
    /// <summary>The type a GUID loads as, or null when this backend does not know the GUID.</summary>
    public abstract Type? GetAssetType(Guid assetId);

    /// <summary>The asset's path, "Folder/Rock.png" or "Model.fbx#Body" for a sub asset.</summary>
    public virtual string? GetAssetPath(Guid assetId) => null;

    public virtual IReadOnlyList<Guid> GetHardDependencies(Guid assetId) => [];

    public virtual long GetEstimatedSize(Guid assetId) => 0;

    /// <summary>Every asset under a Resources folder, in the order that decides which one a shared load path picks.</summary>
    public virtual IReadOnlyList<ResourceEntry> Resources => [];

    /// <summary>
    /// Reads an asset's content into <paramref name="staging"/>, a fresh object of its type that nothing else has
    /// seen. Runs on the loader thread or the main thread. False when there is nothing to read.
    /// </summary>
    protected internal abstract bool ReadContent(Guid assetId, Asset staging, SerializationContext context);

    /// <summary>True when reading this asset first needs work only the main thread may do, such as importing it.</summary>
    protected internal virtual bool NeedsMainThread(Guid assetId) => false;

    /// <summary>Does the main thread only work <see cref="NeedsMainThread"/> asked for.</summary>
    protected internal virtual void PrepareOnMainThread(Guid assetId) { }

    /// <summary>Deserializes an Echo tree into the staging copy, whatever type envelope the tree carries. False for a tree that is no object.</summary>
    protected static bool ReadInto(EchoObject echo, Asset staging, SerializationContext context)
    {
        if (echo.TagType != EchoType.Compound) return false;
        Serializer.DeserializeInto(echo, staging, context);
        return true;
    }
}

/// <summary>Assets kept as Echo trees in memory, for tests and tools that need a database without files.</summary>
public sealed class MemoryAssetBackend : AssetBackend
{
    private readonly ConcurrentDictionary<Guid, (Type Type, EchoObject Content, string Path)> _assets = new();

    /// <summary>Stores a copy of <paramref name="asset"/> under a new GUID and returns it.</summary>
    public Guid Add(Asset asset, string? path = null) => Add(Guid.NewGuid(), asset, path);

    public Guid Add(Guid assetId, Asset asset, string? path = null)
    {
        EchoObject content = Serializer.Serialize(asset.GetType(), asset);
        _assets[assetId] = (asset.GetType(), content, path ?? asset.Name);
        return assetId;
    }

    public void Set(Guid assetId, Type type, EchoObject content, string? path = null)
        => _assets[assetId] = (type, content, path ?? assetId.ToString());

    public bool Remove(Guid assetId) => _assets.TryRemove(assetId, out _);

    /// <summary>What <see cref="AssetDatabase.FindResource{T}"/> resolves, in the order that decides a shared load path. Replaced whole, never edited.</summary>
    public IReadOnlyList<ResourceEntry> ResourceEntries { get; set; } = [];

    public override IReadOnlyList<ResourceEntry> Resources => ResourceEntries;

    /// <summary>How many times each asset was read, for tests that check what loaded.</summary>
    public ConcurrentDictionary<Guid, int> Reads { get; } = new();

    public override Type? GetAssetType(Guid assetId) => _assets.TryGetValue(assetId, out var entry) ? entry.Type : null;

    public override string? GetAssetPath(Guid assetId) => _assets.TryGetValue(assetId, out var entry) ? entry.Path : null;

    protected internal override bool ReadContent(Guid assetId, Asset staging, SerializationContext context)
    {
        Reads.AddOrUpdate(assetId, 1, (_, count) => count + 1);
        return _assets.TryGetValue(assetId, out var entry) && ReadInto(entry.Content.Clone(), staging, context);
    }
}

/// <summary>
/// A context that records every asset a read or write reaches, hard (a plain field) and soft (an <see cref="AssetRef{T}"/>)
/// apart, and can change which assets are written in full.
/// </summary>
internal sealed class DependencySerializationContext : SerializationContext
{
    public HashSet<Guid> Dependencies = new();
    public HashSet<Guid> SoftDependencies = new();

    /// <summary>Database assets written in full here anyway, such as the sub assets a source file carries inside it.</summary>
    public HashSet<Asset>? Inline;

    /// <summary>Links runtime assets by identity for the rest of the session, for copies that never leave memory.</summary>
    public bool LinkRuntimeAssets;

    /// <summary>The runtime assets linked by this write. A link is weak, so whatever keeps the copy keeps these too.</summary>
    public readonly List<Asset> LinkedAssets = [];
}

/// <summary>Names an asset by its path: the part after '#' for a sub asset, else the file name without its extension.</summary>
internal static class AssetNames
{
    public static string FromPath(string path)
    {
        int hash = path.LastIndexOf('#');
        return hash >= 0 ? path[(hash + 1)..] : Path.GetFileNameWithoutExtension(path);
    }
}
