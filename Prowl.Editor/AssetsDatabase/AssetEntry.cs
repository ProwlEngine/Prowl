using System;
using System.Security.Cryptography;
using System.Text;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Represents a sub-asset inside a parent asset (e.g. a Mesh inside a Model file).
/// </summary>
[Serializable]
public class SubAssetEntry
{
    [SerializeField] public Guid Guid;
    [SerializeField] public string Name = "";
    [SerializeField] public string TypeName = "";  // Assembly-qualified type name
    [SerializeField] public Guid[] Dependencies = Array.Empty<Guid>();

    public Type? Type
    {
        get => !string.IsNullOrEmpty(TypeName) ? RuntimeUtils.ResolveType(TypeName) : null;
        set => TypeName = value?.AssemblyQualifiedName ?? "";
    }
}

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
    [SerializeField] public SubAssetEntry[] SubAssets = Array.Empty<SubAssetEntry>();

    [SerializeIgnore] public bool NeedsReimport;

    public Type? MainAssetType
    {
        get => MainAssetTypeName != null ? RuntimeUtils.ResolveType(MainAssetTypeName) : null;
        set => MainAssetTypeName = value?.AssemblyQualifiedName;
    }

    /// <summary>
    /// Generate a deterministic GUID for a sub-asset based on parent GUID + name.
    /// Stable across reimports as long as parent GUID and sub-asset name don't change.
    /// </summary>
    public static Guid DeriveSubAssetGuid(Guid parentGuid, string subAssetName)
    {
        byte[] parentBytes = parentGuid.ToByteArray();
        byte[] nameBytes = Encoding.UTF8.GetBytes(subAssetName);
        byte[] combined = new byte[parentBytes.Length + nameBytes.Length];
        parentBytes.CopyTo(combined, 0);
        nameBytes.CopyTo(combined, parentBytes.Length);
        byte[] hash = SHA256.HashData(combined);
        return new Guid(hash.AsSpan(0, 16));
    }
}
