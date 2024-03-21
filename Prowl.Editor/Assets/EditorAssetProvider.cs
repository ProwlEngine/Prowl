using Prowl.Runtime;

namespace Prowl.Editor.Assets
{
    public class EditorAssetProvider : IAssetProvider
    {
        public bool HasAsset(Guid assetID) => AssetDatabase.Contains(assetID);

        public AssetRef<T> LoadAsset<T>(string relativeAssetPath, int fileID = 0) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            var fileInfo = AssetDatabase.FromRelativePath(relativeAssetPath);
            return new AssetRef<T>(AssetDatabase.LoadAsset<T>(fileInfo, fileID));
        }

        public AssetRef<T> LoadAsset<T>(Guid guid, int fileID = 0) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            return new AssetRef<T>(AssetDatabase.LoadAsset<T>(guid, fileID));
        }

        public AssetRef<T> LoadAsset<T>(IAssetRef assetID) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            return new AssetRef<T>(AssetDatabase.LoadAsset<T>(assetID.AssetID, assetID.FileID));
        }
    }
}
