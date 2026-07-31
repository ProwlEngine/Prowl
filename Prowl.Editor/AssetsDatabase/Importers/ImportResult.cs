using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime;

namespace Prowl.Editor.Importers;

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

    // Track used sub-asset names to ensure uniqueness
    private readonly HashSet<string> _usedNames = [];

    /// <summary>
    /// Add a sub-asset with a deterministic GUID derived from the parent GUID + unique name.
    /// The ID is assigned immediately so AssetRef serialization works correctly.
    /// </summary>
    public void AddSubAsset(string name, EngineObject asset)
    {
        // The name seeds the sub-asset's deterministic GUID and is its display name, so it must be
        // present and unique - a blank one collides and breaks the asset. Naming is the importer's job.
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                $"Importer added a sub-asset of type '{asset.GetType().Name}' with no name. " +
                "A sub-asset name is required (it seeds the sub-asset's GUID).");

        // Ensure unique name; appends _1, _2, etc. if duplicate.
        string uniqueName = Utils.UniqueNames.MakeUnique(name, n => _usedNames.Contains(n),
            openSeparator: "_", closeSeparator: "", stripExistingSuffix: false);
        _usedNames.Add(uniqueName);
        asset.Name = uniqueName;

        asset.AssetID = AssetEntry.DeriveSubAssetGuid(AssetGuid, uniqueName);
        SubAssets.Add(asset);
    }

    /// <summary>Add a dependency on another asset.</summary>
    public void AddDependency(Guid guid)
    {
        Dependencies.Add(guid);
    }
}
