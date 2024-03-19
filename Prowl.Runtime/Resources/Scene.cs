namespace Prowl.Runtime
{
    public class Scene : EngineObject
    {
        public SerializedProperty GameObjects;

        public GameObject[] InstantiateScene()
        {
            return Serializer.Deserialize<GameObject[]>(GameObjects);
        }
    }
}
