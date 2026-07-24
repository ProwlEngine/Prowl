// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections;
using System.Collections.Concurrent;

namespace Prowl.Echo.Formatters;

internal sealed class ListFormat : ISerializationFormat
{
    private static readonly ConcurrentDictionary<Type, Type> _elementTypeCache = new();

    public bool CanHandle(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    private static Type GetElementType(Type listType)
    {
        if (_elementTypeCache.TryGetValue(listType, out var cached))
            return cached;
        var elementType = listType.GetGenericArguments()[0];
        _elementTypeCache.TryAdd(listType, elementType);
        return elementType;
    }

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var reference = CollectionReferences.TryWriteReference(value, context, out int id);
        if (reference != null) return reference;

        var elementType = GetElementType(targetType!);
        var list = value as IList ?? throw new InvalidOperationException("Expected IList type");

        List<EchoObject> tags = new(list.Count);
        for (int i = 0; i < list.Count; i++)
            tags.Add(Serializer.Serialize(elementType, list[i], context));
        return CollectionReferences.WrapListBody(new EchoObject(tags), id);
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        if (CollectionReferences.TryReadReference(value, context, out var existing))
            return existing;

        Type elementType = GetElementType(targetType);
        var list = Activator.CreateInstance(targetType) as IList
            ?? throw new InvalidOperationException($"Failed to create instance of type: {targetType}");
        CollectionReferences.Register(value, list, context);

        foreach (var tag in CollectionReferences.ListItems(value))
            list.Add(Serializer.Deserialize(tag, elementType, context));
        return list;
    }
}
