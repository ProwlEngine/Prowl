// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Formatters;

internal sealed class NullableFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        Nullable.GetUnderlyingType(type) != null;

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var underlyingType = Nullable.GetUnderlyingType(value.GetType())
            ?? throw new InvalidOperationException("Not a nullable type");

        // Create compound to store nullable info
        var compound = EchoObject.NewCompound();
        compound.Add("value", Serializer.Serialize(underlyingType, value, context));
        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType)
            ?? throw new InvalidOperationException("Not a nullable type");

        if (value.TagType == EchoType.Null)
            return null;

        // If it's a compound, get the value
        if (value.TagType == EchoType.Compound && value.TryGet("value", out var valueTag))
        {
            if (valueTag.TagType == EchoType.Null)
                return null;

            return Serializer.Deserialize(valueTag, underlyingType, context);
        }

        // Direct value case (for backwards compatibility or simpler cases)
        return Serializer.Deserialize(value, underlyingType, context);
    }
}
