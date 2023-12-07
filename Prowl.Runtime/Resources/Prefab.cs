namespace Prowl.Runtime
{
    public class Prefab : EngineObject
    {
        public CompoundTag GameObject;

        public GameObject Instantiate()
        {
            var go = TagSerializer.Deserialize<GameObject>(GameObject);
            go.AssetID = AssetID;
            return go;
        }
    }
}
