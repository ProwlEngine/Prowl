using System;
using System.Collections.Generic;

using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Import context passed to importers. Holds the entry GUID so sub-assets get
/// correct deterministic IDs immediately — enabling proper AssetRef serialization.
/// </summary>
public class ImportContext
{
    /// <summary>The parent entry's GUID.</summary>
    public Guid AssetGuid { get; }

    /// <summary>Absolute path to the source file.</summary>
    public string AbsolutePath { get; }

    /// <summary>Importer settings from .meta file.</summary>
    public Echo.EchoObject? Settings { get; }

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

    /// <summary>Set the main asset.</summary>
    public void SetMainAsset(EngineObject asset)
    {
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
        // Ensure unique name — append _1, _2, etc. if duplicate
        string uniqueName = name;
        int counter = 1;
        while (!_usedNames.Add(uniqueName))
            uniqueName = $"{name}_{counter++}";

        if (string.IsNullOrEmpty(asset.Name))
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
