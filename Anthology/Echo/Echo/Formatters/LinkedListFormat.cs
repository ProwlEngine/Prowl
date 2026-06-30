// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;

namespace Prowl.Echo.Formatters;

internal sealed class LinkedListFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LinkedList<>);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var elementType = targetType!.GetGenericArguments()[0];
        var linkedList = (IEnumerable)value;
        List<EchoObject> tags = new();

        foreach (var item in linkedList)
        {
            tags.Add(Serializer.Serialize(elementType, item, context));
        }

        return new EchoObject(tags);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        Type elementType = targetType.GetGenericArguments()[0];
        var linkedList = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");

        // Use reflection to get the AddLast method to avoid ambiguity with null values
        var addLastMethod = targetType.GetMethod("AddLast", new[] { elementType })
            ?? throw new InvalidOperationException($"AddLast method not found on type: {targetType}");

        foreach (var tag in value.List)
        {
            var item = Serializer.Deserialize(tag, elementType, context);
            addLastMethod.Invoke(linkedList, new[] { item });
        }

        return linkedList;
    }
}