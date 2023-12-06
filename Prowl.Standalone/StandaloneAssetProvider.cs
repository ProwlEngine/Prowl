using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.IO.Compression;

namespace Prowl.Standalone;

public class StandaloneAssetProvider : IAssetProvider
{
    public DirectoryInfo Data => new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData"));

    AssetBuildPackage[] packages;

    public StandaloneAssetProvider()
    {
        int packageIndex = 0;
        FileInfo firstPackage = new FileInfo(Path.Combine(Data.FullName, $"Data{packageIndex++}.prowl"));
        List<AssetBuildPackage> packages = new();
        while (firstPackage.Exists)
        {
            packages.Add(new AssetBuildPackage(firstPackage.OpenRead(), ZipArchiveMode.Read));
            firstPackage = new FileInfo(Path.Combine(Data.FullName, $"Data{packageIndex++}.prowl"));
        }
        this.packages = packages.ToArray();
    }

    Dictionary<Guid, SerializedAsset> _loaded = [];

    public bool HasAsset(Guid assetID) => _loaded.ContainsKey(assetID);

    public T LoadAsset<T>(string relativeAssetPath) where T : EngineObject
    {
        foreach (AssetBuildPackage package in packages)
            if (package.TryGetAsset(relativeAssetPath, out var asset))
                return (T)asset.Main;
        throw new FileNotFoundException($"Asset with path {relativeAssetPath} not found.");
    }

    public T LoadAsset<T>(Guid guid) where T : EngineObject
    {
        foreach (AssetBuildPackage package in packages)
            if (package.TryGetAsset(guid, out var asset))
                return (T)asset.Main;
        throw new FileNotFoundException($"Asset with GUID {guid} not found.");
    }
}
