using Prowl.Runtime.Serializer;
using static Prowl.Runtime.Serializer.TagSerializer;

namespace Prowl.Runtime
{
    public interface ISerializable
    {
        public CompoundTag Serialize(SerializationContext ctx);
        public void Deserialize(CompoundTag value, SerializationContext ctx);

    }
}
