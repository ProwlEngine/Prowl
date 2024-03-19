using static Prowl.Runtime.TagSerializer;

namespace Prowl.Runtime
{
    public interface ISerializable
    {
        public SerializedProperty Serialize(SerializationContext ctx);
        public void Deserialize(SerializedProperty value, SerializationContext ctx);

    }
}
