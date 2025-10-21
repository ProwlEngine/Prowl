// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
namespace Prowl.Echo.Formatters;

internal sealed class TimeSpanFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(TimeSpan);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        if (value is TimeSpan timeSpan)
        {
            var compound = EchoObject.NewCompound();

            // Serialize the TimeSpan as ticks
            compound["ticks"] = new EchoObject(EchoType.Long, timeSpan.Ticks);

            return compound;
        }

        throw new NotSupportedException($"Type '{value.GetType()}' is not supported by TimeSpanFormat.");
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (value.TagType == EchoType.Compound)
        {
            if (value.TryGet("ticks", out var ticksTag) && ticksTag.TagType == EchoType.Long)
            {
                long ticks = ticksTag.LongValue;
                return new TimeSpan(ticks);
            }
            else
            {
                throw new InvalidOperationException("Invalid TimeSpan format.");
            }
        }
        throw new NotSupportedException($"Type '{value.TagType}' is not supported by TimeSpanFormat.");
    }
}
