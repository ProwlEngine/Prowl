using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

using Prowl.Echo;

using Scene = Prowl.Runtime.Resources.Scene;

namespace Prowl.Runtime;

/// <summary>
/// Asset database for built standalone players.
/// Loads binary Echo cache files from loose files, ProwlPak archives, or embedded resources.
/// </summary>
public class PlayerAssetBackend : AssetBackendBase
{
    private readonly AssetPackagingMode _mode;
    private readonly string _basePath;
    private readonly Dictionary<Guid, string> _guidToPath = new();
    private readonly List<ZipArchive> _pakArchives = new();

    public Dictionary<string, Guid> ResourcesMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Guid DefaultSceneGuid { get; private set; }

    public PlayerAssetBackend(AssetPackagingMode mode, string basePath = "Content")
    {
        _mode = mode;
        _basePath = Path.Combine(Application.DataPath, basePath);

        switch (mode)
        {
            case AssetPackagingMode.LooseFiles:
            case AssetPackagingMode.ProwlPak:
                LoadManifestFromFile(Path.Combine(_basePath, "asset_manifest.bin"));
                if (mode == AssetPackagingMode.ProwlPak) LoadPakArchives();
                break;
            case AssetPackagingMode.Embedded:
                LoadManifestFromEmbedded();
                break;
        }
    }

    protected override EngineObject? LoadFresh(Guid assetId)
    {
        try
        {
            byte[]? data = LoadRawAsset(assetId);
            if (data == null) return null;

            var echo = ReadEchoBinary(data);
            if (echo == null) return null;

            var obj = Serializer.Deserialize<EngineObject>(echo);
            if (obj != null)
            {
                obj.AssetID = assetId;
                SetLoaded(assetId, obj);
            }
            return obj;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerAssetBackend] Failed to load asset {assetId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Load a scene by GUID.</summary>
    public Scene? LoadScene(Guid sceneGuid)
    {
        byte[]? data = LoadRawAsset(sceneGuid);
        if (data == null) { Debug.LogError($"[PlayerAssetBackend] Scene not found: {sceneGuid}"); return null; }

        try
        {
            var echo = ReadEchoBinary(data);
            if (echo == null) return null;

            return Serializer.Deserialize<Scene>(echo);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerAssetBackend] Failed to load scene {sceneGuid}: {ex.Message}");
            return null;
        }
    }

    // ================================================================
    //  Raw asset loading
    // ================================================================

    private byte[]? LoadRawAsset(Guid guid)
    {
        string fileName = $"{guid}.asset";

        return _mode switch
        {
            AssetPackagingMode.LooseFiles => LoadFromFile(Path.Combine(_basePath, fileName)),
            AssetPackagingMode.ProwlPak => LoadFromPak(fileName),
            AssetPackagingMode.Embedded => LoadFromEmbedded($"Assets.{fileName}"),
            _ => null,
        };
    }

    private static byte[]? LoadFromFile(string path)
        => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private byte[]? LoadFromPak(string entryName)
    {
        // ZipArchive is not thread-safe. Serialize all pak reads on the same lock Get() uses (it is
        // re-entrant, so Get -> LoadRawAsset -> LoadFromPak is fine) so a background Get and a
        // main-thread LoadScene can't read the shared archive/stream concurrently.
        lock (_loadLock)
        {
            foreach (var pak in _pakArchives)
            {
                var entry = pak.GetEntry(entryName);
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            return null;
        }
    }

    private static byte[]? LoadFromEmbedded(string resourceName)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ================================================================
    //  Manifest loading
    // ================================================================

    private void LoadManifestFromFile(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[PlayerAssetBackend] Manifest not found: {manifestPath}");
            return;
        }

        try
        {
            var echo = EchoObject.ReadFromBinary(new FileInfo(manifestPath));
            ParseManifest(echo);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerAssetBackend] Failed to parse manifest: {ex.Message}");
        }
    }

    private void LoadPakArchives()
    {
        if (!Directory.Exists(_basePath)) return;

        foreach (var pakFile in Directory.GetFiles(_basePath, "*.prowlpak"))
        {
            try
            {
                _pakArchives.Add(ZipFile.OpenRead(pakFile));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerAssetBackend] Failed to open pak {pakFile}: {ex.Message}");
            }
        }
    }

    private void LoadManifestFromEmbedded()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Assets._manifest.bin");
        if (stream == null) { Debug.LogError("[PlayerAssetBackend] Embedded manifest not found."); return; }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ParseManifest(ReadEchoBinary(ms.ToArray()));
    }

    private void ParseManifest(EchoObject? echo)
    {
        if (echo == null) return;

        if (echo.TryGet("defaultScene", out var dsTag) && Guid.TryParse(dsTag.StringValue, out var ds))
            DefaultSceneGuid = ds;

        if (echo.TryGet("assets", out var assetsTag) && assetsTag.TagType == EchoType.Compound)
            foreach (var kvp in assetsTag.Tags)
                if (Guid.TryParse(kvp.Key, out var guid))
                    _guidToPath[guid] = kvp.Value.StringValue;

        if (echo.TryGet("resources", out var resTag) && resTag.TagType == EchoType.Compound)
            foreach (var kvp in resTag.Tags)
                if (Guid.TryParse(kvp.Value.StringValue, out var guid))
                    ResourcesMap[kvp.Key] = guid;
    }

    /// <summary>Read Echo binary from byte array.</summary>
    private static EchoObject? ReadEchoBinary(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return EchoObject.ReadFromBinary(reader);
    }

    public void Dispose()
    {
        foreach (var pak in _pakArchives) pak.Dispose();
        _pakArchives.Clear();
        _loadedAssets.Clear();
    }
}
