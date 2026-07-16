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

    public static MetaFileData Read(string metaFilePath)
    {
        string text = File.ReadAllText(metaFilePath);
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
    public static MetaFileData EnsureMeta(string absoluteAssetPath, string importerTypeName, int importerVersion = 1, EchoObject? defaultSettings = null, Guid? forcedGuid = null)
    {
        string metaPath = GetMetaPath(absoluteAssetPath);
        if (File.Exists(metaPath))
        {
            try
            {
                var existing = Read(metaPath);
                if (forcedGuid.HasValue && existing.Guid != forcedGuid.Value)
                {
                    existing.Guid = forcedGuid.Value;
                    Write(metaPath, existing);
                }

                if (existing.Guid != Guid.Empty)
                    return existing;
            }
            catch { /* corrupted meta, recreate */ }
        }

        var data = CreateNew(importerTypeName, importerVersion, defaultSettings);
        if (forcedGuid.HasValue)
            data.Guid = forcedGuid.Value;
        Write(metaPath, data);
        return data;
    }
}
