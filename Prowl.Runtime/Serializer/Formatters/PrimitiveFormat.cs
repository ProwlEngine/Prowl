// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;

namespace Prowl.Runtime.Serialization.Formatters;

public class PrimitiveFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsPrimitive ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(byte[]);

    public SerializedProperty Serialize(object value, Serializer.SerializationContext context)
    {
        return value switch
        {
            char c => new(PropertyType.Byte, (byte)c), // Char is serialized as a byte
            byte b => new(PropertyType.Byte, b),
            sbyte sb => new(PropertyType.sByte, sb),
            short s => new(PropertyType.Short, s),
            int i => new(PropertyType.Int, i),
            long l => new(PropertyType.Long, l),
            uint ui => new(PropertyType.UInt, ui),
            ulong ul => new(PropertyType.ULong, ul),
            ushort us => new(PropertyType.UShort, us),
            float f => new(PropertyType.Float, f),
            double d => new(PropertyType.Double, d),
            decimal dec => new(PropertyType.Decimal, dec),
            string str => new(PropertyType.String, str),
            byte[] bArr => new(PropertyType.ByteArray, bArr),
            bool bo => new(PropertyType.Bool, bo),
            _ => throw new NotSupportedException($"Type '{value.GetType()}' is not supported by PrimitiveFormat.")
        };
    }

    public object? Deserialize(SerializedProperty value, Type targetType, Serializer.SerializationContext context)
    {
        try
        {
            if (value.TagType == PropertyType.ByteArray && targetType == typeof(byte[]))
                return value.Value;

            return Convert.ChangeType(value.Value, targetType);
        }
        catch
        {
            throw new Exception($"Failed to deserialize primitive '{targetType}' with value: {value.Value}");
        }
    }
}
