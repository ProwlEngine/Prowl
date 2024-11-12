// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Serialization.Formatters;

public class EnumFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type.IsEnum;

    public SerializedProperty Serialize(object value, Serializer.SerializationContext context)
    {
        if (value is Enum e)
            return new(PropertyType.Int, Convert.ToInt32(e));

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by EnumFormat.");
    }

    public object? Deserialize(SerializedProperty value, Type targetType, Serializer.SerializationContext context)
    {
        if (value.TagType != PropertyType.Int)
            throw new Exception($"Expected Int type for Enum, got {value.TagType}");

        return Enum.ToObject(targetType, value.IntValue);
    }
}
