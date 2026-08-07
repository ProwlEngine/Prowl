using System;
using System.IO;

using Prowl.Echo;

namespace Prowl.Editor;

/// <summary>
/// Data stored in a .meta companion file.
/// </summary>
public class MetaFileData
{
    public Guid Guid;
    public string ImporterType = "";
    public int ImporterVersion;
    public EchoObject? Settings;
}

/// <summary>
/// Reads and writes .meta companion files in Echo string format (human-readable).
/// Every asset file in Assets/ gets a .meta companion containing its stable GUID and import settings.
/// </summary>
public static class MetaFile
{
    public static string GetMetaPath(string assetPath) => assetPath + ".meta";

    public static bool Exists(string assetPath) => File.Exists(GetMetaPath(assetPath));

    public static MetaFileData Read(string metaFilePath) => Parse(File.ReadAllText(metaFilePath));

    private static MetaFileData Parse(string text)
    {
        var echo = EchoObject.ReadFromString(text);

        var data = new MetaFileData();

        if (echo.TryGet("guid", out var guidTag))
            Guid.TryParse(guidTag.StringValue, out data.Guid);

        if (echo.TryGet("importer", out var importerTag))
            data.ImporterType = importerTag.StringValue;

        if (echo.TryGet("importerVersion", out var versionTag))
            data.ImporterVersion = versionTag.IntValue;

        if (echo.TryGet("settings", out var settingsTag))
            data.Settings = settingsTag;

        return data;
    }

    public static void Write(string metaFilePath, MetaFileData data)
    {
        var echo = EchoObject.NewCompound();
        echo["guid"] = new EchoObject(data.Guid.ToString());
        echo["importer"] = new EchoObject(data.ImporterType);
        echo["importerVersion"] = new EchoObject(data.ImporterVersion);

        if (data.Settings != null)
            echo["settings"] = data.Settings.Clone();

        // Write to a temp file and rename into place so a crash/power-loss mid-write can't
        // leave a truncated .meta file EnsureMeta would otherwise mint a new GUID for it,
        // permanently breaking every reference to the asset.
        string tempPath = metaFilePath + ".tmp";
        File.WriteAllText(tempPath, echo.WriteToString());
        File.Move(tempPath, metaFilePath, overwrite: true);
    }

    public static MetaFileData CreateNew(string importerTypeName, int importerVersion = 1, EchoObject? defaultSettings = null)
    {
        return new MetaFileData
        {
            Guid = Guid.NewGuid(),
            ImporterType = importerTypeName,
            ImporterVersion = importerVersion,
            Settings = defaultSettings
        };
    }

    /// <summary>
    /// Ensure a .meta file exists for the given asset. Creates one if missing.
    /// Returns the meta data.
    /// </summary>
    public static MetaFileData EnsureMeta(string absoluteAssetPath, string importerTypeName, int importerVersion = 1, EchoObject? defaultSettings = null)
    {
        string metaPath = GetMetaPath(absoluteAssetPath);
        if (File.Exists(metaPath))
        {
            // Reading the bytes and understanding them are separate failures and must stay that way.
            // A file we cannot read at all is this asset's identity temporarily out of reach - a lock
            // held by a sync client, scanner, or editor - and minting a fresh GUID there would orphan
            // every reference to the asset and its sub-assets, silently and permanently. Refuse, and
            // let the caller skip the file until it can be read.
            string text;
            try
            {
                text = File.ReadAllText(metaPath);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Could not read '{metaPath}'. Refusing to regenerate it, which would break every " +
                    "existing reference to this asset. Restore or delete the .meta file.", ex);
            }

            // Content we can read but not parse (or that carries no GUID) has no identity left to
            // preserve, so minting one is the only way forward - but say so, because any existing
            // reference to this asset is about to stop resolving.
            MetaFileData? existing = null;
            try { existing = Parse(text); }
            catch { /* unparseable */ }

            if (existing != null && existing.Guid != Guid.Empty)
                return existing;

            Runtime.Debug.LogError(
                $"'{metaPath}' is corrupt or carries no GUID. Assigning a new one - existing references " +
                "to this asset will no longer resolve. Restore the .meta from source control to recover them.");
        }

        var data = CreateNew(importerTypeName, importerVersion, defaultSettings);
        Write(metaPath, data);
        return data;
    }
}
