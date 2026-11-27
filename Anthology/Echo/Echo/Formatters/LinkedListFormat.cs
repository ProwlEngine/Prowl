// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;
using System.Reflection;

namespace Prowl.Echo.Formatters;

internal sealed class LinkedListFormat : ISerializationFormat
{
    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LinkedList<>);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        EchoObject? reference = CollectionReferences.TryWriteReference(value, context, out int id);
        if (reference != null) return reference;

        Type elementType = targetType!.GetGenericArguments()[0];
        var linkedList = (IEnumerable)value;
        List<EchoObject> tags = [];

        foreach (object? item in linkedList)
        {
            tags.Add(Serializer.Serialize(elementType, item, context));
        }

        return CollectionReferences.WrapListBody(new EchoObject(tags), id);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (CollectionReferences.TryReadReference(value, context, out object? existing))
            return existing;

        Type elementType = targetType.GetGenericArguments()[0];
        object linkedList = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");
        CollectionReferences.Register(value, linkedList, context);

        // Use reflection to get the AddLast method to avoid ambiguity with null values
        MethodInfo addLastMethod = targetType.GetMethod("AddLast", new[] { elementType })
            ?? throw new InvalidOperationException($"AddLast method not found on type: {targetType}");

        foreach (EchoObject tag in CollectionReferences.ListItems(value))
        {
            object? item = Serializer.Deserialize(tag, elementType, context);
            addLastMethod.Invoke(linkedList, new[] { item });
        }

        return linkedList;
    }
}
