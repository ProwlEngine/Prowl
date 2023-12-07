namespace Prowl.Runtime
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
