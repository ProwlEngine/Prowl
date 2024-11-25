// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public interface ISerializable
{
    public Echo Serialize(SerializationContext ctx);
    public void Deserialize(Echo value, SerializationContext ctx);

}
