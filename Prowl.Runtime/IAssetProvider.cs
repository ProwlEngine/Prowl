using System;

namespace Prowl.Runtime
{
    public interface IAssetProvider
    {
        public bool HasAsset(Guid assetID);
        public AssetRef<T> LoadAsset<T>(string relativeAssetPath, int fileID = 0) where T : EngineObject;
        public AssetRef<T> LoadAsset<T>(Guid guid, int fileID = 0) where T : EngineObject;
        public AssetRef<T> LoadAsset<T>(IAssetRef assetID) where T : EngineObject;
    }
}
