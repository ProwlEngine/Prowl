// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

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
    public ScriptedImporter importer;
    public List<Guid> dependencies;

    /// <summary>Default constructor for MetaFile.</summary>
    public MetaFile() { }

    /// <summary>Constructor for MetaFile with a asset path.</summary>
    /// <param name="assetFile">The path of the asset.</param>
    public MetaFile(FileInfo assetFile)
    {
        Type? importerType = ImporterAttribute.GetImporter(assetFile.Extension);
        if (importerType == null)
            return;

        AssetPath = assetFile;
        guid = Guid.NewGuid();
        lastModified = assetFile.LastWriteTimeUtc;
        importer = (Activator.CreateInstance(importerType) as ScriptedImporter)!;
    }

    /// <summary>Save the MetaFile to a specified file or default to the associated asset file with a ".meta" extension.</summary>
    public void Save()
    {
        FileInfo file = new(AssetPath.FullName + ".meta");
        version = MetaVersion;
        EchoObject tag = Serializer.Serialize(this);
        StringTagConverter.WriteToFile(tag, file);
    }

    /// <summary>Load a MetaFile from the specified file.</summary>
    /// <param name="assetFile">The file to load the meta data from.</param>
    /// <returns>The loaded MetaFile.</returns>
    public static MetaFile? Load(FileInfo assetFile)
    {
        FileInfo file = new(assetFile + ".meta");
        if (!File.Exists(file.FullName)) return null; // Doesnt Exist
        EchoObject tag = StringTagConverter.ReadFromFile(file);
        MetaFile? meta = Serializer.Deserialize<MetaFile>(tag);
        meta!.AssetPath = assetFile;
        meta.lastModified = DateTime.UtcNow;

        return meta;
    }

    public static bool HasMeta(FileInfo assetFile) => new FileInfo(assetFile + ".meta").Exists;
}
