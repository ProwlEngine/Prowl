using static Prowl.Runtime.TagSerializer;

namespace Prowl.Runtime
{
    public interface ISerializable
    {
        public CompoundTag Serialize(SerializationContext ctx);
        public void Deserialize(CompoundTag value, SerializationContext ctx);

    }
}
