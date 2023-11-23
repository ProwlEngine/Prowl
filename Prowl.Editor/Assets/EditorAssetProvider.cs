using Prowl.Runtime;
using Prowl.Runtime.Assets;

namespace Prowl.Editor.Assets
{
    public class EditorAssetProvider : IAssetProvider
    {
        public bool HasAsset(Guid assetID) => AssetDatabase.Contains(assetID);

        public T LoadAsset<T>(string relativeAssetPath) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            var guid = AssetDatabase.GUIDFromAssetPath(relativeAssetPath);
            if (guid == Guid.Empty)
                return null;
            var obj = AssetDatabase.LoadAsset<T>(guid);
            return obj;
        }

        public T LoadAsset<T>(Guid guid) where T : EngineObject
        {
            // The Editor is a special case, its just a wrapper around the AssetDatabase
            var obj = AssetDatabase.LoadAsset<T>(guid);
            return obj;
        }
    }
}
