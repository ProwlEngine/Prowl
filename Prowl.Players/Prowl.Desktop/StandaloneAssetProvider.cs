using System.IO.Compression;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

public class StandaloneAssetProvider : IAssetProvider
{
    readonly AssetBundle[] packages;

    public StandaloneAssetProvider()
    {
        int packageIndex = 0;
        FileInfo firstPackage = new FileInfo(Path.Combine(Program.Data.FullName, $"Data{packageIndex++}.prowl"));
        List<AssetBundle> assetBundles = [];
        while (File.Exists(firstPackage.FullName))
        {
            assetBundles.Add(new AssetBundle(firstPackage.OpenRead(), ZipArchiveMode.Read));
            firstPackage = new FileInfo(Path.Combine(Program.Data.FullName, $"Data{packageIndex++}.prowl"));
        }
        packages = assetBundles.ToArray();
    }

    readonly Dictionary<Guid, SerializedAsset> _loaded = [];

    public bool HasAsset(Guid assetID) => _loaded.ContainsKey(assetID);

    public AssetRef<T> LoadAsset<T>(string relativeAssetPath, ushort fileID = 0) where T : EngineObject
    {
        Guid guid = GetGuidFromPath(relativeAssetPath);
        if (_loaded.ContainsKey(guid))
            return (T)(fileID == 0 ? _loaded[guid].Main : _loaded[guid].SubAssets[fileID - 1]);

        foreach (AssetBundle package in packages)
            if (package.TryGetAsset(relativeAssetPath, out var asset) && asset is not null)
            {
                _loaded[guid] = asset;
                return (T)(fileID == 0 ? asset.Main : asset.SubAssets[fileID - 1]);
            }
        throw new FileNotFoundException($"Asset with path {relativeAssetPath} not found.");
    }

    public AssetRef<T> LoadAsset<T>(Guid guid, ushort fileID = 0) where T : EngineObject
    {
        if (_loaded.ContainsKey(guid))
            return (T)(fileID == 0 ? _loaded[guid].Main : _loaded[guid].SubAssets[fileID - 1]);

        foreach (AssetBundle package in packages)
            if (package.TryGetAsset(guid, out var asset) && asset is not null)
            {
                _loaded[guid] = asset;
                return (T)(fileID == 0 ? asset.Main : asset.SubAssets[fileID - 1]);
            }
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
    }

    public AssetRef<T> LoadAsset<T>(IAssetRef? assetID) where T : EngineObject
    {
        if (assetID == null) return null;
        return LoadAsset<T>(assetID.AssetID, assetID.FileID);
    }

    public SerializedAsset? LoadAsset(Guid guid)
    {
        if (_loaded.TryGetValue(guid, out SerializedAsset? loadAsset))
            return loadAsset;

        foreach (AssetBundle package in packages)
            if (package.TryGetAsset(guid, out var asset) && asset is not null)
            {
                _loaded[guid] = asset;
                return asset;
            }
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
    }

    public string? GetPathFromGUID(Guid guid)
    {
        foreach (AssetBundle package in packages)
            if (package.TryGetPath(guid, out var path))
                return path;
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
    }

    public Guid GetGuidFromPath(string relativeAssetPath)
    {
        foreach (AssetBundle package in packages)
            if (package.TryGetGuid(relativeAssetPath, out var guid))
                return guid;
        throw new FileNotFoundException($"Asset with path {relativeAssetPath} not found.");
    }
}
