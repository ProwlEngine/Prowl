using System;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime
{
    public interface IAssetProvider
    {
        public bool HasAsset(Guid assetID);
        public AssetRef<T> LoadAsset<T>(string relativeAssetPath, ushort fileID = 0) where T : EngineObject;
        public AssetRef<T> LoadAsset<T>(Guid guid, ushort fileID = 0) where T : EngineObject;
        public AssetRef<T> LoadAsset<T>(IAssetRef assetID) where T : EngineObject;
        public SerializedAsset? LoadAsset(Guid guid);
    }
}
