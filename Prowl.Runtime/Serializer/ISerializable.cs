using Prowl.Runtime.Serialization;
using static Prowl.Runtime.Serialization.TagSerializer;

namespace Prowl.Runtime
{
    public interface ISerializable
    {
        public CompoundTag Serialize(string tagName, SerializationContext ctx);
        public void Deserialize(CompoundTag value, SerializationContext ctx);

    }
}
