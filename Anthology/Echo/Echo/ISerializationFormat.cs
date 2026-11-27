// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public interface ISerializationFormat
{
    bool CanHandle(Type type);
    EchoObject Serialize(Type targetType, object value, SerializationContext context);
    object? Deserialize(EchoObject value, Type targetType, SerializationContext context);
}
