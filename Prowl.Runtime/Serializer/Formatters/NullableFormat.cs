// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Serialization.Formatters;

public class NullableFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        Nullable.GetUnderlyingType(type) != null;

    public SerializedProperty Serialize(object value, Serializer.SerializationContext context)
    {
        var underlyingType = Nullable.GetUnderlyingType(value.GetType())
            ?? throw new InvalidOperationException("Not a nullable type");

        // Create compound to store nullable info
        var compound = SerializedProperty.NewCompound();
        compound.Add("value", Serializer.Serialize(value, context));
        return compound;
    }

    public object? Deserialize(SerializedProperty value, Type targetType, Serializer.SerializationContext context)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType)
            ?? throw new InvalidOperationException("Not a nullable type");

        if (value.TagType == PropertyType.Null)
            return null;

        // If it's a compound, get the value
        if (value.TagType == PropertyType.Compound && value.TryGet("value", out var valueTag))
        {
            if (valueTag.TagType == PropertyType.Null)
                return null;

            return Serializer.Deserialize(valueTag, underlyingType, context);
        }

        // Direct value case (for backwards compatibility or simpler cases)
        return Serializer.Deserialize(value, underlyingType, context);
    }
}
