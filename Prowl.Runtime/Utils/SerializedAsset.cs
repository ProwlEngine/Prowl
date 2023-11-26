using Prowl.Runtime.SceneManagement;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Prowl.Runtime.Utils
{
    public class SerializedAsset
    {
        [JsonProperty(Order = -1)]
        public EngineObject? Main;
        [JsonProperty(Order = 1)]
        public List<EngineObject> SubAssets = new();

        public bool HasMain => Main != null;

        public void SaveToFile(FileInfo file)
        {
            if (Main == null) throw new Exception("Asset does not have a main object.");

#warning TODO: Somehow record Dependencies - reflection to find all AssetRef instances and store them in a list?
            // We need a way for said Dependencies to be able to be loaded and references independently of the asset
            // so like if we delete another asset we can let the user know its in use by X object are they sure?

            file.Directory?.Create(); // Ensure the Directory exists
            JsonUtility.SerializeTo(file.FullName, this);
        }

        public void SaveToStream(StreamWriter writer)
        {
            if (Main == null) throw new Exception("Asset does not have a main object.");

            writer.Write(JsonUtility.Serialize(this));
        }

        public static SerializedAsset FromSerializedAsset(string path)
        {
            bool prev = GameObjectManager.AllowGameObjectConstruction;
            GameObjectManager.AllowGameObjectConstruction = false;
            var obj = JsonUtility.DeserializeFromPath<SerializedAsset>(path);
            GameObjectManager.AllowGameObjectConstruction = prev; // Restore state
            return obj;
        }

        public static SerializedAsset FromStream(StreamReader reader)
        {
            bool prev = GameObjectManager.AllowGameObjectConstruction;
            GameObjectManager.AllowGameObjectConstruction = false;
            var obj = JsonUtility.Deserialize<SerializedAsset>(reader);
            GameObjectManager.AllowGameObjectConstruction = prev; // Restore state
            return obj;
        }

        public void AddSubObject(EngineObject obj)
        {
            if (obj == null) throw new Exception("Asset cannot be null");
            if (SubAssets.Contains(obj) || ReferenceEquals(Main, obj)) throw new Exception("Asset already contains this object: " + obj);
            SubAssets.Add(obj);
        }

        public void SetMainObject(EngineObject obj)
        {
            if (obj == null) throw new Exception("Asset cannot be null");
            if (SubAssets.Contains(obj)) throw new Exception("Asset already contains this object: " + obj);
            Main = obj;
        }
    }
}
