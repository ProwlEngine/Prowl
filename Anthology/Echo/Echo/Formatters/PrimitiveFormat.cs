// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class PrimitiveFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsPrimitive ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(byte[]);

    public EchoObject Serialize(object value, SerializationContext context)
    {
        return value switch
        {
            char c => new(EchoType.Byte, (byte)c), // Char is serialized as a byte
            byte b => new(EchoType.Byte, b),
            sbyte sb => new(EchoType.sByte, sb),
            short s => new(EchoType.Short, s),
            int i => new(EchoType.Int, i),
            long l => new(EchoType.Long, l),
            uint ui => new(EchoType.UInt, ui),
            ulong ul => new(EchoType.ULong, ul),
            ushort us => new(EchoType.UShort, us),
            float f => new(EchoType.Float, f),
            double d => new(EchoType.Double, d),
            decimal dec => new(EchoType.Decimal, dec),
            string str => new(EchoType.String, str),
            byte[] bArr => new(EchoType.ByteArray, bArr),
            bool bo => new(EchoType.Bool, bo),
            _ => throw new NotSupportedException($"Type '{value.GetType()}' is not supported by PrimitiveFormat.")
        };
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        try
        {
            if (value.TagType == EchoType.ByteArray && targetType == typeof(byte[]))
                return value.Value;

            return Convert.ChangeType(value.Value, targetType);
        }
        catch
        {
            throw new Exception($"Failed to deserialize primitive '{targetType}' with value: {value.Value}");
        }
    }
}
