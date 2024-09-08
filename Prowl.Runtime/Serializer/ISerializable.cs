// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using static Prowl.Runtime.Serializer;

namespace Prowl.Runtime
{
    public interface ISerializable
    {
        public SerializedProperty Serialize(SerializationContext ctx);
        public void Deserialize(SerializedProperty value, SerializationContext ctx);

    }
}
