// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Echo.Formatters;

internal sealed class EnumFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type.IsEnum;

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is Enum e)
            return SerializeEnum(e);

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by EnumFormat.");
    }

    // Store at the enum's full underlying width so long/ulong-backed values don't overflow Int32.
    internal static EchoObject SerializeEnum(Enum e)
    {
        if (Enum.GetUnderlyingType(e.GetType()) == typeof(ulong))
            return new EchoObject(Convert.ToUInt64(e));
        return new EchoObject(Convert.ToInt64(e));
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        // ULongValue/LongValue also read older Int-tagged enum data, keeping old files loadable.
        if (Enum.GetUnderlyingType(targetType) == typeof(ulong))
            return Enum.ToObject(targetType, value.ULongValue);
        return Enum.ToObject(targetType, value.LongValue);
    }
}
