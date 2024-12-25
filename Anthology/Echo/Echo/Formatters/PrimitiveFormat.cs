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

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var typeCode = Type.GetTypeCode(value.GetType());
        return typeCode switch 
        {
            // Ordered by rough frequency of use, to reduce conditional checks
            TypeCode.Single => new((float)value),
            TypeCode.Int32 => new((int)value),
            TypeCode.Boolean => new((bool)value),
            TypeCode.String => new((string)value),
            TypeCode.Int64 => new((long)value),
            TypeCode.Byte => new((byte)value),
            TypeCode.Char => new((byte)(char)value),
            TypeCode.Double => new((double)value),
            TypeCode.UInt32 => new((uint)value),
            TypeCode.Int16 => new((short)value),
            TypeCode.Object when value is byte[] bArr => new(EchoType.ByteArray, bArr),
            TypeCode.UInt64 => new((ulong)value),
            TypeCode.UInt16 => new((ushort)value),
            TypeCode.SByte => new((sbyte)value),
            TypeCode.Decimal => new((decimal)value),
            _ => throw new NotSupportedException($"Type '{value.GetType()}' is not supported by PrimitiveFormat.")
        };

        //return value switch 
        //{
        //    string str => new(EchoType.String, str),
        //    int i => new(EchoType.Int, i),
        //    bool bo => new(EchoType.Bool, bo),
        //    float f => new(EchoType.Float, f),
        //    double d => new(EchoType.Double, d),
        //    byte b => new(EchoType.Byte, b),
        //    char c => new(EchoType.Byte, (byte)c),
        //    byte[] bArr => new(EchoType.ByteArray, bArr),
        //    long l => new(EchoType.Long, l),
        //    decimal dec => new(EchoType.Decimal, dec),
        //    uint ui => new(EchoType.UInt, ui),
        //    ulong ul => new(EchoType.ULong, ul),
        //    short s => new(EchoType.Short, s),
        //    ushort us => new(EchoType.UShort, us),
        //    sbyte sb => new(EchoType.sByte, sb),
        //    _ => throw new NotSupportedException($"Type '{value.GetType()}' is not supported by PrimitiveFormat.")
        //};
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
