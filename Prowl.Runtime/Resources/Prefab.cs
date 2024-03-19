namespace Prowl.Runtime
{
    public class Prefab : EngineObject
    {
        public SerializedProperty GameObject;

        public GameObject Instantiate()
        {
            var go = Serializer.Deserialize<GameObject>(GameObject);
            go.AssetID = AssetID;
            return go;
        }
    }
}
