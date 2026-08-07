using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// How a sub-asset's GUID is derived. Every saved reference to a sub-asset is keyed on this, so it has to
/// name the same sub-asset on every reimport - get it wrong and references don't break loudly, they rebind
/// to whichever sub-asset took that key instead.
/// </summary>
public readonly struct SubAssetIdentity
{
    /// <summary>The chosen key, or null for <see cref="Order"/>.</summary>
    internal string? Explicit { get; }

    private SubAssetIdentity(string key) => Explicit = key;

    /// <summary>
    /// Registration order within this import, counted per sub-asset type ("Mesh/0", "Material/1").
    /// Correct when the importer reads its source in a fixed order, and it leaves names free to change.
    /// Wrong for a list entries can be inserted into, removed from, or conditionally skipped: every
    /// sub-asset after the change shifts onto its neighbour's GUID.
    /// </summary>
    public static SubAssetIdentity Order => default;

    /// <summary>
    /// A key that survives insertion and removal, because it belongs to the sub-asset rather than to its
    /// position: an ID persisted with the source (the Sprite Editor's per-slice ID) or an intrinsic one
    /// (a mesh feature's key). Compose a child's from its parent's - see <see cref="ImportContext.AddSubAsset"/>,
    /// which returns the identity it resolved.
    /// </summary>
    public static SubAssetIdentity Key(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A sub-asset identity key must be non-empty.", nameof(key));
        return new SubAssetIdentity(key);
    }

    public override string ToString() => Explicit ?? "Order";
}

/// <summary>
/// Import context passed to importers. Holds the entry GUID so sub-assets get
/// correct deterministic IDs immediately enabling proper AssetRef serialization.
/// </summary>
public class ImportContext
{
    /// <summary>The parent entry's GUID.</summary>
    public Guid AssetGuid { get; }

    /// <summary>Absolute path to the source file.</summary>
    public string AbsolutePath { get; }

    /// <summary>Importer settings from .meta file.</summary>
    public Echo.EchoObject? Settings { get; }

    /// <summary>The source file's name without its extension - the usual choice for naming the
    /// main asset (see <see cref="SetMainAsset"/>).</summary>
    public string FileName => Path.GetFileNameWithoutExtension(AbsolutePath);

    /// <summary>The primary imported object.</summary>
    public EngineObject? MainAsset { get; private set; }

    /// <summary>All sub-assets.</summary>
    public List<EngineObject> SubAssets { get; } = [];

    /// <summary>Asset GUIDs that this asset depends on.</summary>
    public HashSet<Guid> Dependencies { get; } = [];

    public ImportContext(Guid assetGuid, string absolutePath, Echo.EchoObject? settings)
    {
        AssetGuid = assetGuid;
        AbsolutePath = absolutePath;
        Settings = settings;
    }

    /// <summary>Register the primary imported object. Naming is the importer's responsibility (use
    /// <see cref="FileName"/> for the common case); a null/blank Name is treated as an importer bug
    /// and throws, since it would otherwise surface as EngineObject's "New{TypeName}" default.</summary>
    public void SetMainAsset(EngineObject asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Name))
            throw new InvalidOperationException(
                $"Importer produced a main asset of type '{asset.GetType().Name}' with no Name. " +
                $"Assign one (e.g. ctx.{nameof(FileName)}) before calling {nameof(SetMainAsset)}.");

        asset.AssetID = AssetGuid;
        MainAsset = asset;
    }

    // Track used sub-asset names and identities to ensure uniqueness
    private readonly HashSet<string> _usedNames = [];
    private readonly HashSet<string> _usedIdentities = [];
    private readonly Dictionary<Type, int> _typeCounts = [];

    /// <summary>
    /// Add a sub-asset whose GUID is derived from the parent GUID + <paramref name="identity"/>, and return
    /// the resolved identity (for composing a child's, see <see cref="SubAssetIdentity.Key"/>). The ID is
    /// assigned immediately so AssetRef serialization works correctly.
    /// </summary>
    public string AddSubAsset(string name, EngineObject asset, SubAssetIdentity identity)
    {
        // The name is the sub-asset's display name, so it must be present and unique. Naming is the
        // importer's job.
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                $"Importer added a sub-asset of type '{asset.GetType().Name}' with no name. " +
                "A sub-asset name is required.");

        // Ensure unique name; appends _1, _2, etc. if duplicate.
        string uniqueName = Utils.UniqueNames.MakeUnique(name, n => _usedNames.Contains(n),
            openSeparator: "_", closeSeparator: "", stripExistingSuffix: false);
        _usedNames.Add(uniqueName);
        asset.Name = uniqueName;

        string uniqueIdentity = Utils.UniqueNames.MakeUnique(identity.Explicit ?? NextOrderIdentity(asset),
            i => _usedIdentities.Contains(i), openSeparator: "_", closeSeparator: "", stripExistingSuffix: false);
        _usedIdentities.Add(uniqueIdentity);

        asset.AssetID = AssetEntry.DeriveSubAssetGuid(AssetGuid, uniqueIdentity);
        SubAssets.Add(asset);
        return uniqueIdentity;
    }

    /// <summary>Registration order within the type, so adding a material can't shift every animation.</summary>
    private string NextOrderIdentity(EngineObject asset)
    {
        Type type = asset.GetType();
        int index = _typeCounts.GetValueOrDefault(type);
        _typeCounts[type] = index + 1;
        return $"{type.Name}/{index}";
    }

    /// <summary>Add a dependency on another asset.</summary>
    public void AddDependency(Guid guid)
    {
        Dependencies.Add(guid);
    }
}
