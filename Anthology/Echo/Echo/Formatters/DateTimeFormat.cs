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

            // Handle type information based on TypeMode
            bool shouldIncludeType = context.TypeMode switch {
                TypeMode.Aggressive => true, // Always include type information
                TypeMode.None => false, // Never include type information
                TypeMode.Auto => targetType == typeof(object) || targetType != typeof(DateTime), // Include type information if target is object or actual type is different
                _ => true // Default to aggressive for safety
            };
            if (shouldIncludeType)
                compound["$type"] = new(EchoType.String, typeof(DateTime).FullName);

            // Serialize the DateTime properties
            compound["value"] = new EchoObject(EchoType.Long, date.ToBinary());

            return compound;
        }

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by DateTimeFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("value", out var dateTag) && dateTag.TagType == EchoType.Long)
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
