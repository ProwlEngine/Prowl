using Prowl.Runtime;
using Prowl.Runtime.Assets;

namespace Prowl.Editor.Assets
{
    public class EditorAssetProvider : IAssetProvider
    {
        public bool HasAsset(Guid assetID) => AssetDatabase.Contains(assetID);

        public T LoadAsset<T>(string relativeAssetPath, int fileID = 0) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            var guid = AssetDatabase.GUIDFromAssetPath(relativeAssetPath);
            if (guid == Guid.Empty)
                return null;
            var obj = AssetDatabase.LoadAsset<T>(guid, fileID);
            return obj;
        }

        public T LoadAsset<T>(Guid guid, int fileID = 0) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            var obj = AssetDatabase.LoadAsset<T>(guid, fileID);
            return obj;
        }

        public T? LoadAsset<T>(IAssetRef assetID) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            var obj = AssetDatabase.LoadAsset<T>(assetID.AssetID, assetID.FileID);
            return obj;
        }
    }
}
