// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
