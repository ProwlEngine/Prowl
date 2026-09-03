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
    /// <summary> Unique identifier for this sub-asset entry. </summary>
    [SerializeField] public Guid Guid;
    /// <summary> Name of the sub-asset. </summary>
    [SerializeField] public string Name = "";
    /// <summary> Assembly-qualified type name of the sub-asset. Serialized backing for the Type property. </summary>
    [SerializeField] public string TypeName = "";  // Assembly-qualified type name
    /// <summary> GUIDs of other sub-assets or assets this sub-asset depends on. </summary>
    [SerializeField] public Guid[] Dependencies = Array.Empty<Guid>();

    /// <summary> The resolved System.Type of the sub-asset, serialized as TypeName. </summary>
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
    /// <summary> Unique identifier for this asset entry. </summary>
    [SerializeField] public Guid Guid;
    /// <summary> Path to the asset relative to the Assets/ directory. </summary>
    [SerializeField] public string Path = "";           // Relative to Assets/, e.g. "Textures/Grass.png"
    /// <summary> Assembly-qualified type name of the importer used for this asset. </summary>
    [SerializeField] public string ImporterType = "";   // e.g. "TextureImporter"
    /// <summary> Version of the importer that last processed this asset. </summary>
    [SerializeField] public int ImporterVersion;
    /// <summary> Last write time of the asset file in UTC ticks (File.GetLastWriteTimeUtc().Ticks). </summary>
    [SerializeField] public long LastModifiedTicks;     // File.GetLastWriteTimeUtc().Ticks
    /// <summary> Assembly-qualified type name of the main asset type. Serialized backing for the MainAssetType property. </summary>
    [SerializeField] public string? MainAssetTypeName;  // Assembly-qualified type name of main asset
    /// <summary> GUIDs of other assets this asset depends on. </summary>
    [SerializeField] public Guid[] Dependencies = Array.Empty<Guid>();
    /// <summary> User-defined labels or tags associated with this asset. </summary>
    [SerializeField] public string[] Labels = Array.Empty<string>();
    /// <summary> Sub-assets contained within this asset. </summary>
    [SerializeField] public SubAssetEntry[] SubAssets = Array.Empty<SubAssetEntry>();

    /// <summary> Indicates whether this asset needs to be reimported. Not serialized. </summary>
    [SerializeIgnore] public bool NeedsReimport;

    /// <summary> The resolved System.Type of the main asset, serialized as MainAssetTypeName. </summary>
    public Type? MainAssetType
    {
        get => MainAssetTypeName != null ? RuntimeUtils.ResolveType(MainAssetTypeName) : null;
        set => MainAssetTypeName = value?.AssemblyQualifiedName;
    }

    /// <summary>
    /// Generate a deterministic GUID for a sub-asset based on parent GUID + identity.
    /// Stable across reimports as long as the parent GUID and the identity don't change
    /// (see <see cref="Importers.ImportContext.AddSubAsset"/> for what makes a good identity).
    /// </summary>
    public static Guid DeriveSubAssetGuid(Guid parentGuid, string identity)
    {
        byte[] parentBytes = parentGuid.ToByteArray();
        byte[] nameBytes = Encoding.UTF8.GetBytes(identity);
        byte[] combined = new byte[parentBytes.Length + nameBytes.Length];
        parentBytes.CopyTo(combined, 0);
        nameBytes.CopyTo(combined, parentBytes.Length);
        byte[] hash = SHA256.HashData(combined);
        return new Guid(hash.AsSpan(0, 16));
    }
}
