using Prowl.Runtime.Serialization;
using static Prowl.Runtime.Serialization.TagSerializer;

namespace Prowl.Runtime
{
    public interface ISerializable
    {
        public CompoundTag Serialize(SerializationContext ctx);
        public void Deserialize(CompoundTag value, SerializationContext ctx);

    }
}
