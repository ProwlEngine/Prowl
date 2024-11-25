// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class DateTimeFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(DateTime);

    public EchoObject Serialize(object value, SerializationContext context)
    {
        if (value is DateTime date)
            return new(PropertyType.Long, date.ToBinary());

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by DateTimeFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType != PropertyType.Long)
            throw new Exception($"Expected Long type for DateTime, got {value.TagType}");

        return DateTime.FromBinary(value.LongValue);
    }
}
