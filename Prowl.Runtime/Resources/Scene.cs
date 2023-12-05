using Prowl.Runtime.Serializer;

namespace Prowl.Runtime.Resources
{
    public class Scene : EngineObject
    {
        public ListTag GameObjects;

        public GameObject[] InstantiateScene()
        {
            return TagSerializer.Deserialize<GameObject[]>(GameObjects);
        }
    }
}
