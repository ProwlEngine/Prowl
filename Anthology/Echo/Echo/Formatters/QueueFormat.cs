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
        var reference = CollectionReferences.TryWriteReference(value, context, out int id);
        if (reference != null) return reference;

        var elementType = targetType!.GetGenericArguments()[0];
        var queue = value as IEnumerable ?? throw new InvalidOperationException("Expected Queue type");

        List<EchoObject> tags = new();
        foreach (var item in queue)
            tags.Add(Serializer.Serialize(elementType, item, context));

        return CollectionReferences.WrapListBody(new EchoObject(tags), id);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (CollectionReferences.TryReadReference(value, context, out var existing))
            return existing;

        Type elementType = targetType.GetGenericArguments()[0];

        dynamic queue = (IEnumerable)Activator.CreateInstance(targetType)!
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");
        CollectionReferences.Register(value, queue, context);

        foreach (var tag in CollectionReferences.ListItems(value))
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            queue.Enqueue((dynamic)item);
        }

        return queue;
    }
}
