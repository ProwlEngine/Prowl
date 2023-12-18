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
        while (firstPackage.Exists) {
            packages.Add(new AssetBuildPackage(firstPackage.OpenRead(), ZipArchiveMode.Read));
            firstPackage = new FileInfo(Path.Combine(Program.Data.FullName, $"Data{packageIndex++}.prowl"));
        }
        this.packages = packages.ToArray();
    }

    Dictionary<Guid, SerializedAsset> _loaded = [];

    public bool HasAsset(Guid assetID) => _loaded.ContainsKey(assetID);

    public T LoadAsset<T>(string relativeAssetPath) where T : EngineObject
    {
        Guid guid = GetGuidFromPath(relativeAssetPath);
        if (_loaded.ContainsKey(guid))
            return (T)_loaded[guid].Main;

        foreach (AssetBuildPackage package in packages)
            if (package.TryGetAsset(relativeAssetPath, out var asset)) {
                _loaded[guid] = asset;
                return (T)asset.Main;
            }
        throw new FileNotFoundException($"Asset with path {relativeAssetPath} not found.");
    }

    public T LoadAsset<T>(Guid guid) where T : EngineObject
    {
        if (_loaded.ContainsKey(guid))
            return (T)_loaded[guid].Main;

        foreach (AssetBuildPackage package in packages)
            if (package.TryGetAsset(guid, out var asset)) {
                _loaded[guid] = asset;
                return (T)asset.Main;
            }
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
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
