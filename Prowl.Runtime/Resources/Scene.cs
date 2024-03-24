using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime
{
    public class Scene : EngineObject
    {
        public SerializedProperty GameObjects;

        public static Scene Create(GameObject[] all)
        {
            Scene scene = new Scene();
            Serializer.SerializationContext ctx = new();
            scene.GameObjects = Serializer.Serialize(all, ctx);
            return scene;
        }

        public GameObject[] InstantiateScene()
        {
            return Serializer.Deserialize<GameObject[]>(GameObjects);
        }
    }
}
