// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

namespace Prowl.Editor.Assets;

public class MetaFile
{
    public const int MetaVersion = 1;

    public FileInfo AssetPath { get; set; }

    public int version = MetaVersion;
    public Guid guid;

    public string[] assetNames = [];
    public string[] assetTypes = [];

    public DateTime lastModified;
    public readonly ScriptedImporter Importer;
    public List<Guid> dependencies;

    /// <summary>Default constructor for MetaFile.</summary>
    public MetaFile() { }

    /// <summary>Constructor for MetaFile with a relative asset path.</summary>
    /// <param name="relativeAssetPath">The relative path of the asset.</param>
    public MetaFile(FileInfo assetFile)
    {
        var importerType = ImporterAttribute.GetImporter(assetFile.Extension);
        if (importerType == null)
            return;

        AssetPath = assetFile;
        guid = Guid.NewGuid();
        lastModified = assetFile.LastWriteTimeUtc;
        var importer = Activator.CreateInstance(importerType) as ScriptedImporter;
        Importer = importer ?? throw new Exception();
    }

    /// <summary>Save the MetaFile to a specified file or default to the associated asset file with a ".meta" extension.</summary>
    /// <param name="file">The file to save the meta data.</param>
    public void Save()
    {
        var file = new FileInfo(AssetPath.FullName + ".meta");
        version = MetaVersion;
        var tag = Serializer.Serialize(this);
        StringTagConverter.WriteToFile(tag, file);
    }

    /// <summary>Load a MetaFile from the specified file.</summary>
    /// <param name="file">The file to load the meta data from.</param>
    /// <returns>The loaded MetaFile.</returns>
    public static MetaFile? Load(FileInfo assetFile)
    {
        var file = new FileInfo(assetFile + ".meta");
        if (!File.Exists(file.FullName)) return null; // Doesnt Exist
        var tag = StringTagConverter.ReadFromFile(file);
        var meta = Serializer.Deserialize<MetaFile>(tag);
        meta!.AssetPath = assetFile;
        meta.lastModified = DateTime.UtcNow;

        return meta;
    }

    public static bool HasMeta(FileInfo assetFile) => new FileInfo(assetFile + ".meta").Exists;
}
