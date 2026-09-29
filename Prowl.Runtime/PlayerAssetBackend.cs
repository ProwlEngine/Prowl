using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Asset content for built standalone players, from loose files, ProwlPak archives or embedded resources. The
/// manifest names every shipped asset's file, type, size and dependencies, so nothing is opened until it loads.
/// </summary>
public class PlayerAssetBackend : AssetBackend, IDisposable
{
    /// <summary>The manifest layout this build writes and reads.</summary>
    public const int ManifestFormat = 2;

    private sealed class Entry
    {
        public string File = "";
        public string Path = "";
        public string TypeName = "";
        public long Size;
        public Guid[] Hard = [];
        public Guid[] Soft = [];
        public Type? Type;
    }

    private readonly AssetPackagingMode _mode;
    private readonly string _basePath;
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly List<ZipArchive> _pakArchives = new();
    private readonly object _pakLock = new();
    private readonly List<ResourceEntry> _resources = new();

    public Guid DefaultSceneGuid { get; private set; }

    public override IReadOnlyList<ResourceEntry> Resources => _resources;

    public PlayerAssetBackend(AssetPackagingMode mode, string basePath = "Content")
    {
        _mode = mode;
        _basePath = System.IO.Path.Combine(Application.DataPath, basePath);

        switch (mode)
        {
            case AssetPackagingMode.LooseFiles:
            case AssetPackagingMode.ProwlPak:
                LoadManifestFromFile(System.IO.Path.Combine(_basePath, "asset_manifest.bin"));
                if (mode == AssetPackagingMode.ProwlPak) LoadPakArchives();
                break;
            case AssetPackagingMode.Embedded:
                LoadManifestFromEmbedded();
                break;
        }
    }

    public override Type? GetAssetType(Guid assetId)
    {
        if (!_entries.TryGetValue(assetId, out Entry? entry)) return null;
        return entry.Type ??= RuntimeUtils.ResolveType(entry.TypeName);
    }

    public override string? GetAssetPath(Guid assetId) => _entries.TryGetValue(assetId, out Entry? entry) ? entry.Path : null;

    public override IReadOnlyList<Guid> GetHardDependencies(Guid assetId) => _entries.TryGetValue(assetId, out Entry? entry) ? entry.Hard : [];

    public override long GetEstimatedSize(Guid assetId) => _entries.TryGetValue(assetId, out Entry? entry) ? entry.Size : 0;

    protected internal override bool ReadContent(Guid assetId, Asset staging, SerializationContext context)
    {
        if (!_entries.TryGetValue(assetId, out Entry? entry))
        {
            Debug.LogWarning($"[PlayerAssetBackend] {assetId} is not in the asset manifest. It may not have shipped.");
            return false;
        }

        byte[]? data = _mode switch
        {
            AssetPackagingMode.LooseFiles => LoadFromFile(System.IO.Path.Combine(_basePath, entry.File)),
            AssetPackagingMode.ProwlPak => LoadFromPak(entry.File),
            AssetPackagingMode.Embedded => LoadFromEmbedded($"Assets.{entry.File}"),
            _ => null,
        };
        if (data == null) return false;

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return ReadInto(EchoObject.ReadFromBinary(reader), staging, context);
    }

    private static byte[]? LoadFromFile(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    // ZipArchive is not thread safe, so every read of a pak takes the same lock.
    private byte[]? LoadFromPak(string entryName)
    {
        lock (_pakLock)
        {
            foreach (var pak in _pakArchives)
            {
                var entry = pak.GetEntry(entryName);
                if (entry == null) continue;

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
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

    private void LoadManifestFromFile(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[PlayerAssetBackend] Manifest not found: {manifestPath}");
            return;
        }

        try
        {
            ParseManifest(EchoObject.ReadFromBinary(new FileInfo(manifestPath)));
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
            try { _pakArchives.Add(ZipFile.OpenRead(pakFile)); }
            catch (Exception ex) { Debug.LogError($"[PlayerAssetBackend] Failed to open pak {pakFile}: {ex.Message}"); }
        }
    }

    private void LoadManifestFromEmbedded()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Assets._manifest.bin");
        if (stream == null) { Debug.LogError("[PlayerAssetBackend] Embedded manifest not found."); return; }

        using var reader = new BinaryReader(stream);
        ParseManifest(EchoObject.ReadFromBinary(reader));
    }

    private void ParseManifest(EchoObject echo)
    {
        int format = echo.Get("format")?.IntValue ?? 1;
        if (format != ManifestFormat)
        {
            Debug.LogError($"[PlayerAssetBackend] The asset manifest is format {format}, this player reads format {ManifestFormat}. Rebuild the game.");
            return;
        }

        if (echo.TryGet("defaultScene", out var dsTag) && Guid.TryParse(dsTag!.StringValue, out var ds))
            DefaultSceneGuid = ds;

        if (echo.TryGet("assets", out var assetsTag) && assetsTag!.TagType == EchoType.Compound)
            foreach (var (key, tag) in assetsTag.Tags)
                if (Guid.TryParse(key, out var guid) && tag.TagType == EchoType.Compound)
                    _entries[guid] = new Entry
                    {
                        File = tag.Get("file")?.StringValue ?? $"{guid}.asset",
                        Path = tag.Get("path")?.StringValue ?? "",
                        TypeName = tag.Get("type")?.StringValue ?? "",
                        Size = tag.Get("size")?.LongValue ?? 0,
                        Hard = ReadGuids(tag.Get("hard")),
                        Soft = ReadGuids(tag.Get("soft")),
                    };

        if (echo.TryGet("resources", out var resTag) && resTag!.TagType == EchoType.List)
            foreach (var item in resTag.List)
                if (item.TryGet("path", out var path) && item.TryGet("guid", out var guidTag) && Guid.TryParse(guidTag!.StringValue, out var guid))
                    _resources.Add(new ResourceEntry(path!.StringValue, guid, item.TryGet("type", out var type) ? type!.StringValue : ""));
    }

    private static Guid[] ReadGuids(EchoObject? list)
    {
        if (list is not { TagType: EchoType.List }) return [];
        var guids = new List<Guid>(list.List.Count);
        foreach (EchoObject item in list.List)
            if (Guid.TryParse(item.StringValue, out Guid guid)) guids.Add(guid);
        return guids.ToArray();
    }

    public void Dispose()
    {
        lock (_pakLock)
        {
            foreach (var pak in _pakArchives) pak.Dispose();
            _pakArchives.Clear();
        }
    }
}
