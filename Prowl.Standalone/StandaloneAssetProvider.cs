using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.IO.Compression;

namespace Prowl.Standalone;

public class StandaloneAssetProvider : IAssetProvider
{
    AssetBuildPackage[] packages;

    public StandaloneAssetProvider()
    {
        int packageIndex = 0;
        FileInfo firstPackage = new FileInfo(Path.Combine(Program.Data.FullName, $"Data{packageIndex++}.prowl"));
        List<AssetBuildPackage> packages = new();
        while (File.Exists(firstPackage.FullName)) {
            packages.Add(new AssetBuildPackage(firstPackage.OpenRead(), ZipArchiveMode.Read));
            firstPackage = new FileInfo(Path.Combine(Program.Data.FullName, $"Data{packageIndex++}.prowl"));
        }
        this.packages = packages.ToArray();
    }

    Dictionary<Guid, SerializedAsset> _loaded = [];

    public bool HasAsset(Guid assetID) => _loaded.ContainsKey(assetID);

    public AssetRef<T> LoadAsset<T>(string relativeAssetPath, int fileID = 0) where T : EngineObject
    {
        Guid guid = GetGuidFromPath(relativeAssetPath);
        if (_loaded.ContainsKey(guid))
            return (T)(fileID == 0 ? _loaded[guid].Main : _loaded[guid].SubAssets[fileID]);

        foreach (AssetBuildPackage package in packages)
            if (package.TryGetAsset(relativeAssetPath, out var asset)) {
                _loaded[guid] = asset;
                return (T)(fileID == 0 ? asset.Main : asset.SubAssets[fileID]);
            }
        throw new FileNotFoundException($"Asset with path {relativeAssetPath} not found.");
    }

    public AssetRef<T> LoadAsset<T>(Guid guid, int fileID = 0) where T : EngineObject
    {
        if (_loaded.ContainsKey(guid))
            return (T)(fileID == 0 ? _loaded[guid].Main : _loaded[guid].SubAssets[fileID]);

        foreach (AssetBuildPackage package in packages)
            if (package.TryGetAsset(guid, out var asset)) {
                _loaded[guid] = asset;
                return (T)(fileID == 0 ? asset.Main : asset.SubAssets[fileID]);
            }
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
    }

    public AssetRef<T> LoadAsset<T>(IAssetRef assetID) where T : EngineObject
    {
        if (assetID == null) return null;
        return LoadAsset<T>(assetID.AssetID, assetID.FileID);
    }

    public string GetPathFromGUID(Guid guid)
    {
        foreach (AssetBuildPackage package in packages)
            if (package.TryGetPath(guid, out var path))
                return path;
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
    }

    public Guid GetGuidFromPath(string relativeAssetPath)
    {
        foreach (AssetBuildPackage package in packages)
            if (package.TryGetGuid(relativeAssetPath, out var guid))
                return guid;
        throw new FileNotFoundException($"Asset with path {relativeAssetPath} not found.");
    }
}
