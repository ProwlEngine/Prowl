// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class QueueFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Queue<>);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var elementType = targetType!.GetGenericArguments()[0];
        var queue = value as IEnumerable ?? throw new InvalidOperationException("Expected Queue type");

        List<EchoObject> tags = new();
        foreach (var item in queue)
            tags.Add(Serializer.Serialize(elementType, item, context));

        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetGenericArguments()[0];

        dynamic queue = (IEnumerable)Activator.CreateInstance(targetType)!
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        foreach (var tag in value.List)
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            queue.Enqueue((dynamic)item);
        }

        return queue;
    }
}
