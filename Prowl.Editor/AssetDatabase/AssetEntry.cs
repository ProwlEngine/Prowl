using System;
using System.Collections.Generic;

using Prowl.Echo;

namespace Prowl.Editor;

/// <summary>
/// In-memory representation of a tracked asset in the database.
/// Stored in the index and serialized to metadata.db for fast startup.
/// </summary>
[Serializable]
public class AssetEntry
{
    [SerializeField] public Guid Guid;
    [SerializeField] public string Path = "";           // Relative to Assets/, e.g. "Textures/Grass.png"
    [SerializeField] public string ImporterType = "";   // e.g. "TextureImporter"
    [SerializeField] public int ImporterVersion;
    [SerializeField] public long LastModifiedTicks;     // File.GetLastWriteTimeUtc().Ticks
    [SerializeField] public string? MainAssetTypeName;  // Assembly-qualified type name of main asset
    [SerializeField] public Guid[] Dependencies = Array.Empty<Guid>();
    [SerializeField] public string[] Labels = Array.Empty<string>();

    [SerializeIgnore] public bool NeedsReimport;

    public Type? MainAssetType
    {
        get => MainAssetTypeName != null ? Type.GetType(MainAssetTypeName) : null;
        set => MainAssetTypeName = value?.AssemblyQualifiedName;
    }
}
