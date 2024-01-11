using System;

namespace Prowl.Runtime
{
    public interface IAssetProvider
    {
        public bool HasAsset(Guid assetID);
        public T? LoadAsset<T>(string relativeAssetPath, int fileID = 0) where T : EngineObject;
        public T? LoadAsset<T>(Guid guid, int fileID = 0) where T : EngineObject;
        public T? LoadAsset<T>(IAssetRef assetID) where T : EngineObject;
    }
}
