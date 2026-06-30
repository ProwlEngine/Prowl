// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class DateTimeOffsetFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(DateTimeOffset);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is DateTimeOffset dateTimeOffset)
        {
            var compound = EchoObject.NewCompound();

            // Serialize the DateTimeOffset components (using local ticks, not UTC)
            compound["ticks"] = new EchoObject(EchoType.Long, dateTimeOffset.Ticks);
            compound["offset"] = new EchoObject(EchoType.Long, dateTimeOffset.Offset.Ticks);

            return compound;
        }

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by DateTimeOffsetFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("ticks", out var ticksTag) && ticksTag.TagType == EchoType.Long &&
                value.TryGet("offset", out var offsetTag) && offsetTag.TagType == EchoType.Long)
            {
                long ticks = ticksTag.LongValue;
                long offsetTicks = offsetTag.LongValue;

                var offset = new TimeSpan(offsetTicks);
                return new DateTimeOffset(ticks, offset);
            }
            else
            {
                throw new InvalidOperationException("Invalid DateTimeOffset format.");
            }
        }
        throw new NotSupportedException($"Type '{value.TagType}' is not supported by DateTimeOffsetFormat.");
    }
}
