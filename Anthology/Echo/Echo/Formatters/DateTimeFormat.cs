// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class DateTimeFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(DateTime);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is DateTime date)
        {
            var compound = EchoObject.NewCompound();

            // Serialize the DateTime properties
            compound["date"] = new EchoObject(EchoType.Long, date.ToBinary());

            return compound;
        }

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by DateTimeFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("date", out var dateTag) && dateTag.TagType == EchoType.Long)
            {
                long binary = dateTag.LongValue;
                return DateTime.FromBinary(binary);
            }
            else
            {
                throw new InvalidOperationException("Invalid DateTime format.");
            }
        }
        throw new NotSupportedException($"Type '{value.TagType}' is not supported by DateTimeFormat.");
    }
}
